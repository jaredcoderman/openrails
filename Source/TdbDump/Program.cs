using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Orts.Parsers.Msts;

namespace TdbDump
{
    internal class Program
    {
        private const string DefaultRoute =
            @"C:\Users\jared\ORRoutes\BNSF Starter Route - Copy\ROUTES\BNSF_Scenic";

        static int Main(string[] args)
        {
            if (!TryParseArgs(args, out CliOptions cli, out string error))
            {
                Console.WriteLine(error);
                PrintUsage();
                return 1;
            }

            string routeDirectory = cli.RouteDirectory ?? DefaultRoute;
            string networkPath = cli.NetworkPath; // may be null → TrackBuilder default search
            string tsectionPath = Path.Combine(routeDirectory, "tsection.dat");
            string routeName = Path.GetFileName(
                routeDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            string tdbPath = Path.Combine(routeDirectory, routeName + ".tdb");

            TrackBuilder track;
            try
            {
                track = string.IsNullOrWhiteSpace(networkPath)
                    ? new TrackBuilder()
                    : new TrackBuilder(networkPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error loading track network: " + ex.Message);
                return 1;
            }

            List<object> allNodes;
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

            var pathOptions = BuildPathOptions(cli, routeName);

            if (cli.PathOnly)
            {
                try
                {
                    ScenarioWriter.Write(routeDirectory, track.Chains, allNodes, pathOptions);
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error writing scenario files: " + ex.Message);
                    return 1;
                }
            }

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

            // Player path only when start/end were requested (or --path-only above).
            if (cli.StartObjectId.HasValue && cli.GoalObjectId.HasValue)
            {
                try
                {
                    ScenarioWriter.Write(routeDirectory, track.Chains, allNodes, pathOptions);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error writing scenario files: " + ex.Message);
                    return 1;
                }
            }
            else
            {
                Console.WriteLine("Skipping .pat/.srv/.act (no --start/--end).");
            }

            // Write DynamicTracks to World Files
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

                int worldFiles = WorldWriter.WriteWorldFiles(routeDirectory, dynamicTracks);
                Console.WriteLine(
                    "World sync: " + tdbSectionCount + " TDB sections, "
                    + dynamicTracks.Count + " DynTracks, "
                    + worldFiles + " world file(s)");

                TerrainStamper.StampFlatTiles(
                    routeDirectory,
                    TerrainStamper.CollectTilesFromChains(track.Chains),
                    borderTiles: 1);

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error writing world files: " + ex.Message);
                return 1;
            }
        }

        private sealed class CliOptions
        {
            public bool PathOnly;
            public string NetworkPath;
            public string RouteDirectory;
            public int? StartObjectId;
            public bool StartIsStart = true;
            public int? GoalObjectId;
            public bool GoalIsStart = true;
            public string PathId;
            public string PathName;
            public string StartLabel;
            public string EndLabel;
            public string Consist;
            public string RouteId;
        }

        private static ScenarioPathOptions BuildPathOptions(CliOptions cli, string routeName)
        {
            var options = new ScenarioPathOptions
            {
                RouteId = string.IsNullOrWhiteSpace(cli.RouteId) ? routeName : cli.RouteId,
            };

            if (cli.StartObjectId.HasValue && cli.GoalObjectId.HasValue)
            {
                options.StartObjectId = cli.StartObjectId;
                options.StartIsStart = cli.StartIsStart;
                options.GoalObjectId = cli.GoalObjectId;
                options.GoalIsStart = cli.GoalIsStart;
            }

            if (!string.IsNullOrWhiteSpace(cli.PathId))
                options.PathId = cli.PathId;
            if (!string.IsNullOrWhiteSpace(cli.PathName))
                options.PathName = cli.PathName;
            if (!string.IsNullOrWhiteSpace(cli.StartLabel))
                options.StartLabel = cli.StartLabel;
            if (!string.IsNullOrWhiteSpace(cli.EndLabel))
                options.EndLabel = cli.EndLabel;
            if (!string.IsNullOrWhiteSpace(cli.Consist))
                options.Consist = cli.Consist;

            return options;
        }

        private static bool TryParseArgs(string[] args, out CliOptions cli, out string error)
        {
            cli = new CliOptions();
            error = null;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg == "--path-only")
                {
                    cli.PathOnly = true;
                    continue;
                }
                if (arg == "--help" || arg == "-h" || arg == "/?")
                {
                    error = "Usage:";
                    return false;
                }

                if (!TryTakeValue(args, ref i, out string value, out error))
                    return false;

                switch (arg)
                {
                    case "--network":
                        cli.NetworkPath = value;
                        break;
                    case "--route":
                        cli.RouteDirectory = value;
                        break;
                    case "--start":
                        if (!TryParseEndRef(value, out int startOid, out bool startIsStart))
                        {
                            error = "Invalid --start value '" + value + "' (expected e.g. 151:S or 151:E).";
                            return false;
                        }
                        cli.StartObjectId = startOid;
                        cli.StartIsStart = startIsStart;
                        break;
                    case "--end":
                        if (!TryParseEndRef(value, out int endOid, out bool endIsStart))
                        {
                            error = "Invalid --end value '" + value + "' (expected e.g. 1101:E).";
                            return false;
                        }
                        cli.GoalObjectId = endOid;
                        cli.GoalIsStart = endIsStart;
                        break;
                    case "--path-id":
                        cli.PathId = value;
                        break;
                    case "--name":
                        cli.PathName = value;
                        break;
                    case "--start-label":
                        cli.StartLabel = value;
                        break;
                    case "--end-label":
                        cli.EndLabel = value;
                        break;
                    case "--consist":
                        cli.Consist = value;
                        break;
                    case "--route-id":
                        cli.RouteId = value;
                        break;
                    default:
                        error = "Unknown argument: " + arg;
                        return false;
                }
            }

            if (cli.PathOnly
                && (!cli.StartObjectId.HasValue || !cli.GoalObjectId.HasValue))
            {
                error = "--path-only requires both --start and --end.";
                return false;
            }

            if (cli.StartObjectId.HasValue != cli.GoalObjectId.HasValue)
            {
                error = "Provide both --start and --end, or neither.";
                return false;
            }

            return true;
        }

        private static bool TryTakeValue(
            string[] args, ref int i, out string value, out string error)
        {
            value = null;
            error = null;
            if (i + 1 >= args.Length)
            {
                error = "Missing value after " + args[i];
                return false;
            }
            i++;
            value = args[i];
            return true;
        }

        /// <summary>
        /// Accepts "151:S", "151S", "151:s", "151:E", "151E".
        /// </summary>
        private static bool TryParseEndRef(string text, out int objectId, out bool isStart)
        {
            objectId = 0;
            isStart = true;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            text = text.Trim();
            char side = '\0';
            string numberPart = text;

            if (text.Length >= 2)
            {
                char last = char.ToUpperInvariant(text[text.Length - 1]);
                if (last == 'S' || last == 'E')
                {
                    side = last;
                    numberPart = text.Substring(0, text.Length - 1).TrimEnd(':');
                }
            }

            if (side == '\0')
                return false;
            if (!int.TryParse(numberPart, out objectId))
                return false;

            isStart = side == 'S';
            return true;
        }

        private static void PrintUsage()
        {
            Console.WriteLine(
                "TdbDump [--path-only] [--network file] [--route dir]"
                + " [--start oid:S|E] [--end oid:S|E]"
                + " [--path-id id] [--name name]"
                + " [--start-label label] [--end-label label]"
                + " [--consist name] [--route-id id]");
            Console.WriteLine(
                "  --path-only   Build in-memory graph and write .pat/.srv/.act only"
                + " (requires --start and --end).");
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
