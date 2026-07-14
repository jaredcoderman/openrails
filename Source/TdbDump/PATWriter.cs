using System;
using System.Collections.Generic;
using System.Globalization;
using Orts.Parsers.Msts;

namespace TdbDump
{
    public static class PATWriter
    {
        private const uint NoNextNode = uint.MaxValue;

        public static void Write(
            string filePath,
            IReadOnlyList<TrackNode> sectionNodes,
            TrEndNode endNode,
            string pathId = "TestPat",
            string pathName = "Test Track",
            string startName = "Start",
            string endName = "End")
        {
            if (sectionNodes == null)
                throw new ArgumentNullException(nameof(sectionNodes));
            if (endNode == null)
                throw new ArgumentNullException(nameof(endNode));
            if (sectionNodes.Count == 0)
                throw new ArgumentException("At least one track section is required.", nameof(sectionNodes));

            using (var writer = new STFWriter(filePath, "P0t"))
            {
                writer.WriteProperty("Serial", 1);

                writer.WriteBlockStart("TrackPDPs");
                foreach (var node in sectionNodes)
                {
                    if (node != null && node.Section != null)
                        WriteTrackPdp(writer, node.Section, 2, 0);
                }

                // Section nodes describe section starts. Add the final end
                // node so the path reaches the end of the generated track.
                // The reference path uses 2 0 for the endpoint as well.
                WriteTrackPdp(writer, endNode, 2, 0);
                writer.WriteBlockEnd();

                writer.WriteBlockStart("TrackPath");
                writer.WriteProperty("TrPathName", Quote(pathId));
                writer.WriteProperty("Name", Quote(pathName));
                writer.WriteProperty("TrPathStart", startName);
                writer.WriteProperty("TrPathEnd", endName);

                int waypointCount = sectionNodes.Count + 1;
                writer.WriteBlockStart("TrPathNodes", waypointCount);
                for (int i = 0; i < waypointCount; i++)
                {
                    uint nextMainNode = i == waypointCount - 1
                        ? NoNextNode
                        : (uint)(i + 1);

                    writer.WriteNoLabel(string.Format(
                        CultureInfo.InvariantCulture,
                        "TrPathNode ( 00000000 {0} {1} {2} )",
                        nextMainNode,
                        NoNextNode,
                        i));
                }
                writer.WriteBlockEnd();
                writer.WriteBlockEnd();
            }
        }

        private static void WriteTrackPdp(
            STFWriter writer,
            TrVectorSection section,
            int flag1,
            int flag2)
        {
            writer.WriteNoLabel(string.Format(
                CultureInfo.InvariantCulture,
                "TrackPDP ( {0} {1} {2} {3} {4} {5} {6} )",
                section.TileX,
                section.TileZ,
                section.X,
                section.Y,
                section.Z,
                flag1,
                flag2));
        }

        private static void WriteTrackPdp(
            STFWriter writer,
            TrEndNode node,
            int flag1,
            int flag2)
        {
            writer.WriteNoLabel(string.Format(
                CultureInfo.InvariantCulture,
                "TrackPDP ( {0} {1} {2} {3} {4} {5} {6} )",
                node.TileX,
                node.TileZ,
                node.X,
                node.Y,
                node.Z,
                flag1,
                flag2));
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
