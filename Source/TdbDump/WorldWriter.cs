using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Globalization;
using Newtonsoft.Json;
using Orts.Formats.Msts;
using Orts.Parsers.Msts;

namespace TdbDump
{
    public class WorldWriter
    {
        public static void WriteWorldFiles(List<DynamicTrack> tracks, float defaultY = 1000f)
        {
            // Build output path matching existing world file layout
            string basePath = @"C:\Users\jared\ORRoutes\BNSF Starter Route - Copy\ROUTES\BNSF_Scenic";
            string worldDir = Path.Combine(basePath, "WORLD");
            Directory.CreateDirectory(worldDir);

            // Write one .w file per tile (group tracks that share the same world tile)
            const int baseX = -12842;
            const int baseZ = 14734;

            // For now, write ALL tracks to the base tile with transformed local coordinates
            // This avoids tile-mismatch issues
            const int targetWorldTileX = baseX;
            const int targetWorldTileZ = baseZ;

            string signX = targetWorldTileX < 0 ? "-" : "+";
            string signZ = targetWorldTileZ < 0 ? "-" : "+";
            int absX = Math.Abs(targetWorldTileX);
            int absZ = Math.Abs(targetWorldTileZ);

            string fileName = string.Format(System.Globalization.CultureInfo.InvariantCulture, "w{0}{1:000000}{2}{3:000000}.w", signX, absX, signZ, absZ);
            string filePath = Path.Combine(worldDir, fileName);

            // Write file atomically: write to temp then move
            string tempPath = filePath + ".tmp";
            using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var sw = new StreamWriter(fs, System.Text.Encoding.Unicode))
            {
                // Exact ASCII header for uncompressed world text files
                sw.WriteLine("SIMISA@@@@@@@@@@JINX0w0t______");
                sw.WriteLine();

                // Begin Tr_Worldfile block
                sw.WriteLine("Tr_Worldfile (");
                sw.WriteLine("  VDbIdCount ( 0 )");

                foreach (var track in tracks)
                {
                    // Convert tile coordinates to local coordinates within the base tile
                    // If a track is at tile (-12843, 14733), that's 1 tile west and 1 tile south of base (-12842, 14734)
                    int tileOffsetX = track.TileX - targetWorldTileX;  // Number of tiles away in X
                    int tileOffsetZ = track.TileZ - targetWorldTileZ;  // Number of tiles away in Z
                    
                    // Convert to world coordinates
                    float worldX = tileOffsetX * 2048f + track.X;
                    float worldY = defaultY;
                    float worldZ = tileOffsetZ * 2048f + track.Z;

                    // Apply coordinate transformation (negate X, keep Z positive)
                    float posX = -worldX;
                    float posY = worldY;
                    float posZ = worldZ;
                    
                    float resultQx = track.Qx;
                    float resultQy = track.Qy;
                    float resultQz = -track.Qz;  // Negate Z component of quaternion
                    float resultQw = track.Qw;

                    sw.WriteLine("  Dyntrack (");
                    sw.WriteLine("    UiD ( " + ((int)track.UiD).ToString(System.Globalization.CultureInfo.InvariantCulture) + " )");
                    sw.WriteLine("    SectionIdx ( " + ((int)track.SectionIdx).ToString(System.Globalization.CultureInfo.InvariantCulture) + " )");
                    sw.WriteLine("    Elevation ( " + ((int)track.Elevation).ToString(System.Globalization.CultureInfo.InvariantCulture) + " )");
                    sw.WriteLine("    CollideFlags ( " + ((int)track.CollideFlags).ToString(System.Globalization.CultureInfo.InvariantCulture) + " )");
                    sw.WriteLine("    StaticFlags ( " + ((int)track.StaticFlags).ToString(System.Globalization.CultureInfo.InvariantCulture) + " )");
                    sw.WriteLine("    Position ( " + posX.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + posY.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + posZ.ToString(System.Globalization.CultureInfo.InvariantCulture) + " )");
                    sw.WriteLine("    QDirection ( " + resultQx.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + resultQy.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + resultQz.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + resultQw.ToString(System.Globalization.CultureInfo.InvariantCulture) + " )");
                    sw.WriteLine("    VDbId ( " + ((int)track.VdbId).ToString(System.Globalization.CultureInfo.InvariantCulture) + " )");

                    sw.WriteLine("    TrackSections (");
                    foreach (var section in track.TrackSections)
                    {
                        int curveFlag = section.IsCurve ? 1 : 0;
                        float param1 = section.IsCurve ? section.SignedAngle : section.Length;
                        float param2 = section.IsCurve ? section.Radius : 0f;
                        sw.WriteLine("      TrackSection (");
                        sw.WriteLine("        SectionCurve ( " + curveFlag.ToString(System.Globalization.CultureInfo.InvariantCulture) + " )");
                        sw.WriteLine("        " + section.SectionIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + param1.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + param2.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        sw.WriteLine("      )");
                    }
                    sw.WriteLine("    )"); // TrackSections

                    sw.WriteLine("  )"); // Dyntrack
                }

                sw.WriteLine(")"); // Tr_Worldfile
                sw.Flush();
            }

            // Replace destination file atomically
            if (File.Exists(filePath))
                File.Replace(tempPath, filePath, null);
            else
                File.Move(tempPath, filePath);

            Console.WriteLine("Wrote world file: " + filePath);
        }
    }
}
