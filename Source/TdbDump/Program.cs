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

            // Write TSectionDat to separate file
            try
            {
                using (var writer = new STFWriter(tsectionPath))
                {
                    TSectionWriter.UpdateTSectionDat(writer, track.Primitives.ToArray());
                    writer.WriteBlockEnd();
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

            // Scenario files still use the first feature only until path
            // stitching across the network exists.
            try
            {
                FeatureChain firstChain = track.Chains[0];
                int vectorId = firstChain.VectorNodeId;
                var related = allNodes
                    .Where(node =>
                        (node is TrackNode tn && tn.Id == vectorId)
                        || (node is TrEndNode en && en.Pins.Any(p => p.Node == vectorId)))
                    .ToList();

                if (related.OfType<TrEndNode>().Count() < 2)
                {
                    Console.WriteLine(
                        "Skipping scenario files: first feature has no free end nodes after snapping.");
                }
                else
                {
                    ScenarioWriter.Write(basePath, firstChain.Sections, related);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error writing scenario files: " + ex.Message);
                return 1;
            }

            // Write DynamicTracks to World Files
            try
            {
                var dynamicTracks = DynamicTrack.MakeDynamicTrackObjects(
                    track.Chains,
                    track.Primitives);
                WorldWriter.WriteWorldFiles(dynamicTracks);
                Console.WriteLine(dynamicTracks.Count + " dynamic tracks written");

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error writing world files: " + ex.Message);
                return 1;
            }
        }
    }
}
