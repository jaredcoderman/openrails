using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace TdbDump
{
    public class WorldWriter
    {
        /// <summary>
        /// Write one world file per tile. Each Dyntrack stays in its section's
        /// TileX/TileZ with tile-local Position so TDB WFName+UiD lookup hits
        /// the same object Open Rails loads for that section.
        /// </summary>
        /// <returns>Number of world files written.</returns>
        public static int WriteWorldFiles(
            string routeDirectory,
            List<DynamicTrack> tracks,
            float defaultY = TerrainStamper.FlatTerrainY)
        {
            if (string.IsNullOrWhiteSpace(routeDirectory))
                throw new ArgumentException("Route directory is required.", nameof(routeDirectory));

            string worldDir = Path.Combine(routeDirectory, "WORLD");
            Directory.CreateDirectory(worldDir);

            if (tracks == null || tracks.Count == 0)
            {
                Console.WriteLine("No dynamic tracks to write.");
                return 0;
            }

            var byTile = tracks
                .GroupBy(t => (t.TileX, t.TileZ))
                .OrderBy(g => g.Key.TileX)
                .ThenBy(g => g.Key.TileZ)
                .ToList();

            int filesWritten = 0;
            foreach (var group in byTile)
            {
                int tileX = group.Key.TileX;
                int tileZ = group.Key.TileZ;
                var tileTracks = group
                    .OrderBy(t => t.UiD)
                    .ToList();

                // UiDs must be unique within this world file (OR lookup key).
                var seenUiDs = new HashSet<uint>();
                foreach (var track in tileTracks)
                {
                    if (!seenUiDs.Add(track.UiD))
                    {
                        throw new InvalidOperationException(
                            "Duplicate Dyntrack UiD " + track.UiD
                            + " in world tile (" + tileX + "," + tileZ + ").");
                    }
                }

                string fileName = WorldFileName(tileX, tileZ);
                string filePath = Path.Combine(worldDir, fileName);
                string tempPath = filePath + ".tmp";

                using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var sw = new StreamWriter(fs, System.Text.Encoding.Unicode))
                {
                    sw.WriteLine("SIMISA@@@@@@@@@@JINX0w0t______");
                    sw.WriteLine();
                    sw.WriteLine("Tr_Worldfile (");
                    sw.WriteLine("  VDbIdCount ( 0 )");

                    foreach (var track in tileTracks)
                        WriteDyntrack(sw, track, defaultY);

                    sw.WriteLine(")");
                    sw.Flush();
                }

                if (File.Exists(filePath))
                    File.Replace(tempPath, filePath, null);
                else
                    File.Move(tempPath, filePath);

                Console.WriteLine(
                    "Wrote world file: " + filePath
                    + " (" + tileTracks.Count + " Dyntracks)");
                filesWritten++;
            }

            return filesWritten;
        }

        private static void WriteDyntrack(StreamWriter sw, DynamicTrack track, float defaultY)
        {
            // Same tile-local X/Z as TDB. OR/TSRE negate Position.Z (and Qz) on
            // load — do not negate X or the mesh mirrors across the tile origin.
            float posX = track.X;
            // Prefer the TDB section Y (flat terrain); fall back only if unset.
            float posY = Math.Abs(track.Y) > 1e-3f ? track.Y : defaultY;
            float posZ = track.Z;

            // Match TrackObj / WorldObj save convention: store −Qz in the file.
            float resultQx = track.Qx;
            float resultQy = track.Qy;
            float resultQz = -track.Qz;
            float resultQw = track.Qw;

            sw.WriteLine("  Dyntrack (");
            sw.WriteLine("    UiD ( " + ((int)track.UiD).ToString(CultureInfo.InvariantCulture) + " )");
            sw.WriteLine("    SectionIdx ( " + ((int)track.SectionIdx).ToString(CultureInfo.InvariantCulture) + " )");
            sw.WriteLine("    Elevation ( " + ((int)track.Elevation).ToString(CultureInfo.InvariantCulture) + " )");
            sw.WriteLine("    CollideFlags ( " + ((int)track.CollideFlags).ToString(CultureInfo.InvariantCulture) + " )");
            sw.WriteLine("    StaticFlags ( " + ((int)track.StaticFlags).ToString(CultureInfo.InvariantCulture) + " )");
            sw.WriteLine(
                "    Position ( "
                + posX.ToString(CultureInfo.InvariantCulture) + " "
                + posY.ToString(CultureInfo.InvariantCulture) + " "
                + posZ.ToString(CultureInfo.InvariantCulture) + " )");
            sw.WriteLine(
                "    QDirection ( "
                + resultQx.ToString(CultureInfo.InvariantCulture) + " "
                + resultQy.ToString(CultureInfo.InvariantCulture) + " "
                + resultQz.ToString(CultureInfo.InvariantCulture) + " "
                + resultQw.ToString(CultureInfo.InvariantCulture) + " )");
            sw.WriteLine("    VDbId ( " + ((int)track.VdbId).ToString(CultureInfo.InvariantCulture) + " )");

            sw.WriteLine("    TrackSections (");
            foreach (var section in track.TrackSections)
            {
                int curveFlag = section.IsCurve ? 1 : 0;
                float param1 = section.IsCurve ? section.SignedAngle : section.Length;
                float param2 = section.IsCurve ? section.Radius : 0f;
                sw.WriteLine("      TrackSection (");
                sw.WriteLine(
                    "        SectionCurve ( "
                    + curveFlag.ToString(CultureInfo.InvariantCulture) + " )");
                sw.WriteLine(
                    "        "
                    + section.SectionIndex.ToString(CultureInfo.InvariantCulture) + " "
                    + param1.ToString(CultureInfo.InvariantCulture) + " "
                    + param2.ToString(CultureInfo.InvariantCulture));
                sw.WriteLine("      )");
            }
            sw.WriteLine("    )");
            sw.WriteLine("  )");
        }

        public static string WorldFileName(int tileX, int tileZ)
        {
            string signX = tileX < 0 ? "-" : "+";
            string signZ = tileZ < 0 ? "-" : "+";
            return string.Format(
                CultureInfo.InvariantCulture,
                "w{0}{1:000000}{2}{3:000000}.w",
                signX, Math.Abs(tileX),
                signZ, Math.Abs(tileZ));
        }
    }
}
