using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Orts.Parsers.Msts;

namespace TdbDump
{
    internal class Program
    {
        static int Main(string[] args)
        {
            string basePath = @"C:\Users\jared\ORRoutes\BNSF Starter Route - Copy\ROUTES\BNSF_Scenic";
            string tsectionPath = Path.Combine(basePath, "tsection.dat");
            string tdbPath = Path.Combine(basePath, "BNSF_Scenic.tdb");

            TrackBuilder track;
            try
            {
                track = new TrackBuilder();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error loading track network: " + ex.Message);
                return 1;
            }

            List<object> allNodes = null;

            // Build TDB graph first so endpoint fillers are included in primitives.
            try
            {
                allNodes = track.BuildAllNodes();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error building track nodes: " + ex.Message);
                return 1;
            }

            int pinErrors = ValidatePinLinks(allNodes);
            if (pinErrors > 0)
            {
                Console.WriteLine("ERROR: " + pinErrors + " invalid TrPin link(s); aborting write.");
                return 1;
            }
            Console.WriteLine("TrPin link check: OK");

            // Write TSectionDat to separate file
            try
            {
                using (var writer = new STFWriter(tsectionPath))
                {
                    TSectionWriter.UpdateTSectionDat(writer, track.Primitives.ToArray());
                }
                Console.WriteLine("Wrote TrackSections to: " + tsectionPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error writing tsection.dat: " + ex.Message);
                return 1;
            }

            // Write TrackNodes to TDB file
            try
            {
                using (var writer = new STFWriter(tdbPath))
                {
                    writer.WriteBlockStart("trackdb");
                    writer.WriteBlockStart("tracknodes", allNodes.Count);

                    foreach (object node in allNodes)
                    {
                        if (node is TrEndNode endNode)
                        {
                            TDBWriter.WriteEndNode(writer, endNode);
                        }
                        else if (node is TrJunctionNode junctionNode)
                        {
                            TDBWriter.WriteJunctionNode(writer, junctionNode);
                        }
                        else if (node is TrackNode vectorNode)
                        {
                            TDBWriter.WriteVectorNode(writer, vectorNode);
                        }
                    }

                    writer.WriteBlockEnd();

                    writer.WriteBlockStart("tritemtable", 0);
                    writer.WriteBlockEnd();

                    writer.WriteBlockEnd();
                }

                Console.WriteLine(
                    "Wrote TrackNodes to: " + tdbPath
                    + " (" + track.Chains.Count + " features, "
                    + allNodes.Count + " TDB nodes)");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error writing tdb file: " + ex.Message);
                return 1;
            }

            // Player path across the snapped network (any two free ends).
            try
            {
                ScenarioWriter.Write(basePath, track.Chains, allNodes);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error writing scenario files: " + ex.Message);
                return 1;
            }

            // Write DynamicTracks to World Files (one DynTrack per TDB section,
            // in the world file named by that section's WFName / TileX/Z).
            try
            {
                int tdbSectionCount = track.Chains.Sum(c => c.Sections.Count);
                var dynamicTracks = DynamicTrack.MakeDynamicTrackObjects(
                    track.Chains,
                    track.Primitives);

                if (dynamicTracks.Count != tdbSectionCount)
                {
                    throw new InvalidOperationException(
                        "DynTrack count " + dynamicTracks.Count
                        + " != TDB section count " + tdbSectionCount);
                }

                // Spot-check: every DynTrack UiD/tile matches a chain section,
                // and curve params match the shared primitive.
                var sectionByKey = new Dictionary<(int TileX, int TileZ, int UiD), TrVectorSection>();
                foreach (var chain in track.Chains)
                {
                    foreach (var node in chain.Sections)
                    {
                        var s = node.Section;
                        sectionByKey[(s.TileX, s.TileZ, s.WorldFileUiD)] = s;
                    }
                }

                var primitivesByIndex = track.Primitives.ToDictionary(p => p.SectionIndex);
                foreach (var dyn in dynamicTracks)
                {
                    if (!sectionByKey.TryGetValue(
                            ((int)dyn.TileX, (int)dyn.TileZ, (int)dyn.UiD),
                            out TrVectorSection tdbSection))
                    {
                        throw new InvalidOperationException(
                            "DynTrack UiD " + dyn.UiD + " tile ("
                            + dyn.TileX + "," + dyn.TileZ
                            + ") has no matching TDB TrVectorSection.");
                    }

                    if (tdbSection.SectionIndex != dyn.SectionIdx)
                    {
                        throw new InvalidOperationException(
                            "SectionIndex mismatch UiD " + dyn.UiD
                            + ": TDB " + tdbSection.SectionIndex
                            + " vs DynTrack " + dyn.SectionIdx);
                    }

                    if (!primitivesByIndex.TryGetValue(dyn.SectionIdx, out TrackPrimitive prim))
                        continue;

                    var live = dyn.TrackSections[0];
                    float expectP1 = prim.IsCurve ? prim.SignedAngle : prim.Length;
                    float expectP2 = prim.IsCurve ? prim.Radius : 0f;
                    float gotP1 = live.IsCurve ? live.SignedAngle : live.Length;
                    float gotP2 = live.IsCurve ? live.Radius : 0f;
                    if (Math.Abs(expectP1 - gotP1) > 0.01f || Math.Abs(expectP2 - gotP2) > 0.01f)
                    {
                        throw new InvalidOperationException(
                            "Geometry mismatch SectionIndex " + dyn.SectionIdx
                            + ": primitive (" + expectP1 + "," + expectP2
                            + ") vs DynTrack (" + gotP1 + "," + gotP2 + ")");
                    }
                }

                int worldFiles = WorldWriter.WriteWorldFiles(dynamicTracks);
                Console.WriteLine(
                    "World sync: " + tdbSectionCount + " TDB sections, "
                    + dynamicTracks.Count + " DynTracks, "
                    + worldFiles + " world file(s)");

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error writing world files: " + ex.Message);
                return 1;
            }
        }

        /// <summary>
        /// Mirrors OR Signals.performLinkTest: each pin's Direction selects the
        /// opposite side of the linked node for the reciprocal back-link.
        /// </summary>
        private static int ValidatePinLinks(IReadOnlyList<object> allNodes)
        {
            var byId = new Dictionary<int, (string Kind, List<TrPin> Pins, int Inpins)>();
            foreach (object node in allNodes)
            {
                if (node is TrackNode vector)
                    byId[vector.Id] = ("vector", vector.Pins, 1);
                else if (node is TrEndNode end)
                    byId[end.Id] = ("end", end.Pins, 1);
                else if (node is TrJunctionNode junction)
                    byId[junction.Id] = ("junction", junction.Pins, 1);
            }

            int errors = 0;
            foreach (var kv in byId)
            {
                int id = kv.Key;
                var pins = kv.Value.Pins;
                int inpins = kv.Value.Inpins;
                for (int i = 0; i < pins.Count; i++)
                {
                    int direction = i < inpins ? 0 : 1;
                    // For junctions, pin index 0 is in; 1 and 2 are outs (direction 1).
                    if (kv.Value.Kind == "junction")
                        direction = i == 0 ? 0 : 1;

                    TrPin pin = pins[i];
                    if (!byId.TryGetValue(pin.Node, out var linked))
                    {
                        Console.WriteLine(
                            "  pin error: node " + id + " -> missing node " + pin.Node);
                        errors++;
                        continue;
                    }

                    int linkedDirection = pin.Pin == 0 ? 1 : 0;
                    bool found = false;
                    for (int j = 0; j < linked.Pins.Count; j++)
                    {
                        int otherDir;
                        if (linked.Kind == "junction")
                            otherDir = j == 0 ? 0 : 1;
                        else if (linked.Kind == "end")
                            otherDir = 0;
                        else
                            otherDir = j < 1 ? 0 : 1;

                        if (otherDir != linkedDirection)
                            continue;
                        if (linked.Pins[j].Node == id)
                        {
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        Console.WriteLine(
                            "  pin error: node " + id + " side " + direction
                            + " TrPin(" + pin.Node + "," + pin.Pin + ") has no reciprocal on "
                            + pin.Node + " side " + linkedDirection);
                        errors++;
                    }
                }
            }

            return errors;
        }
    }
}
