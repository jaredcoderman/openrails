using System;
using System.Collections.Generic;
using System.Globalization;
using Orts.Parsers.Msts;

namespace TdbDump
{
    public sealed class PathWaypoint
    {
        public int TileX;
        public int TileZ;
        public float X;
        public float Y;
        public float Z;
        /// <summary>2 = junction, 1 = start/end/intermediate vector point.</summary>
        public int JunctionFlag;
        /// <summary>0 = normal; 1 = intermediate (non start/end).</summary>
        public int InvalidFlag;
        /// <summary>Optional TrPathNode hex flags (e.g. 4 for intermediate).</summary>
        public uint PathFlags;
    }

    public static class PATWriter
    {
        private const uint NoNextNode = uint.MaxValue;

        public static void Write(
            string filePath,
            IReadOnlyList<PathWaypoint> waypoints,
            string pathId = "GeneratedTrack",
            string pathName = "Generated Track",
            string startName = "Start",
            string endName = "End")
        {
            if (waypoints == null)
                throw new ArgumentNullException(nameof(waypoints));
            if (waypoints.Count < 2)
                throw new ArgumentException("At least two waypoints are required.", nameof(waypoints));

            using (var writer = new STFWriter(filePath, "P0t"))
            {
                writer.WriteProperty("Serial", 1);

                writer.WriteBlockStart("TrackPDPs");
                foreach (var wp in waypoints)
                {
                    writer.WriteNoLabel(string.Format(
                        CultureInfo.InvariantCulture,
                        "TrackPDP ( {0} {1} {2} {3} {4} {5} {6} )",
                        wp.TileX,
                        wp.TileZ,
                        wp.X.ToString(CultureInfo.InvariantCulture),
                        wp.Y.ToString(CultureInfo.InvariantCulture),
                        wp.Z.ToString(CultureInfo.InvariantCulture),
                        wp.JunctionFlag,
                        wp.InvalidFlag));
                }
                writer.WriteBlockEnd();

                writer.WriteBlockStart("TrackPath");
                writer.WriteProperty("TrPathName", Quote(pathId));
                writer.WriteProperty("Name", Quote(pathName));
                writer.WriteProperty("TrPathStart", startName);
                writer.WriteProperty("TrPathEnd", endName);

                writer.WriteBlockStart("TrPathNodes", waypoints.Count);
                for (int i = 0; i < waypoints.Count; i++)
                {
                    uint nextMainNode = i == waypoints.Count - 1
                        ? NoNextNode
                        : (uint)(i + 1);

                    writer.WriteNoLabel(string.Format(
                        CultureInfo.InvariantCulture,
                        "TrPathNode ( {0:X8} {1} {2} {3} )",
                        waypoints[i].PathFlags,
                        nextMainNode,
                        NoNextNode,
                        i));
                }
                writer.WriteBlockEnd();
                writer.WriteBlockEnd();
            }
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
