using System;
using System.Collections.Generic;
using System.IO;
using Orts.Formats.Msts;

namespace TdbDump
{
    /// <summary>
    /// Stamps flat MSTS terrain tiles under the track footprint by copying an
    /// existing flat TILES pair (no DEM generation). Open Rails ground comes
    /// from TILES/*.t + *_y.raw, not from world meshes.
    /// </summary>
    public static class TerrainStamper
    {
        /// <summary>
        /// Elevation of the default flat template (-01b5e098):
        /// sample 32768 * scale 0.001953125 + floor -63 = 1 m.
        /// </summary>
        public const float FlatTerrainY = 1f;

        public const string DefaultTemplateTileName = "-01b5e098";

        /// <summary>
        /// Delete leftover scenic terrain so only the flat template remains,
        /// then copy that template onto every track tile (plus an optional
        /// border). Keeps <paramref name="templateTileName"/>.t / _y.raw so
        /// later stamps still have a source. Also clears LO_TILES (distant
        /// mountains from the donor route).
        /// </summary>
        /// <returns>Number of tile pairs newly stamped.</returns>
        public static int StampFlatTiles(
            string routeDirectory,
            IEnumerable<(int TileX, int TileZ)> trackTiles,
            int borderTiles = 1,
            string templateTileName = DefaultTemplateTileName)
        {
            if (string.IsNullOrWhiteSpace(routeDirectory))
                throw new ArgumentException("Route directory is required.", nameof(routeDirectory));
            if (trackTiles == null)
                throw new ArgumentNullException(nameof(trackTiles));

            string tilesDir = Path.Combine(routeDirectory, "TILES");
            if (!Directory.Exists(tilesDir))
                throw new DirectoryNotFoundException("TILES folder not found: " + tilesDir);

            string templateT = Path.Combine(tilesDir, templateTileName + ".t");
            string templateY = Path.Combine(tilesDir, templateTileName + "_y.raw");
            if (!File.Exists(templateT) || !File.Exists(templateY))
            {
                throw new FileNotFoundException(
                    "Flat terrain template missing under TILES: "
                    + templateTileName + ".t / " + templateTileName + "_y.raw");
            }

            int removed = ClearNonTemplateTiles(tilesDir, templateTileName);
            int removedLo = ClearLoTiles(routeDirectory);

            var footprint = new HashSet<(int, int)>();
            foreach (var tile in trackTiles)
                footprint.Add(tile);

            if (footprint.Count == 0)
            {
                Console.WriteLine(
                    "Terrain cleanup: removed " + removed + " TILES file(s), "
                    + removedLo + " LO_TILES file(s); no track tiles to stamp.");
                return 0;
            }

            var toStamp = new HashSet<(int, int)>(footprint);
            if (borderTiles > 0)
            {
                foreach (var (tx, tz) in footprint)
                {
                    for (int dx = -borderTiles; dx <= borderTiles; dx++)
                    {
                        for (int dz = -borderTiles; dz <= borderTiles; dz++)
                            toStamp.Add((tx + dx, tz + dz));
                    }
                }
            }

            int stamped = 0;
            int skippedTemplate = 0;
            foreach (var (tileX, tileZ) in toStamp)
            {
                string name = TileName.FromTileXZ(tileX, tileZ, TileName.Zoom.Small);
                // Footprint landed on the template's own tile — already flat.
                if (string.Equals(name, templateTileName, StringComparison.OrdinalIgnoreCase))
                {
                    skippedTemplate++;
                    continue;
                }

                string destT = Path.Combine(tilesDir, name + ".t");
                string destY = Path.Combine(tilesDir, name + "_y.raw");
                File.Copy(templateT, destT, overwrite: true);
                File.Copy(templateY, destY, overwrite: true);
                stamped++;
            }

            Console.WriteLine(
                "Terrain stamp: removed " + removed + " old TILES + "
                + removedLo + " LO_TILES; stamped " + stamped
                + " flat tile(s) from " + templateTileName
                + " (kept template"
                + (skippedTemplate > 0 ? ", " + skippedTemplate + " on template tile" : "")
                + "; " + footprint.Count + " track tile(s), border "
                + borderTiles + ")");
            return stamped;
        }

        /// <summary>
        /// Delete every TILES file except the flat template .t / _y.raw pair.
        /// </summary>
        public static int ClearNonTemplateTiles(
            string tilesDir,
            string templateTileName = DefaultTemplateTileName)
        {
            if (!Directory.Exists(tilesDir))
                return 0;

            string keepT = templateTileName + ".t";
            string keepY = templateTileName + "_y.raw";
            int removed = 0;

            foreach (string path in Directory.EnumerateFiles(tilesDir))
            {
                string name = Path.GetFileName(path);
                if (string.Equals(name, keepT, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, keepY, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Only touch terrain tile data — leave any stray non-tile files alone.
                string lower = name.ToLowerInvariant();
                if (!(lower.EndsWith(".t")
                      || lower.EndsWith("_y.raw")
                      || lower.EndsWith("_f.raw")
                      || lower.EndsWith("_e.raw")
                      || lower.EndsWith("_n.raw")))
                    continue;

                File.Delete(path);
                removed++;
            }

            return removed;
        }

        /// <summary>
        /// Remove distant-mountain LO_TILES left over from the donor route.
        /// </summary>
        public static int ClearLoTiles(string routeDirectory)
        {
            string loDir = Path.Combine(routeDirectory, "LO_TILES");
            if (!Directory.Exists(loDir))
                return 0;

            int removed = 0;
            foreach (string path in Directory.EnumerateFiles(loDir))
            {
                File.Delete(path);
                removed++;
            }
            return removed;
        }

        public static IEnumerable<(int TileX, int TileZ)> CollectTilesFromChains(
            IReadOnlyList<FeatureChain> chains)
        {
            var seen = new HashSet<(int, int)>();
            if (chains == null)
                yield break;

            foreach (var chain in chains)
            {
                if (chain?.Sections == null)
                    continue;
                foreach (var node in chain.Sections)
                {
                    var section = node?.Section;
                    if (section == null)
                        continue;
                    var key = (section.TileX, section.TileZ);
                    if (seen.Add(key))
                        yield return key;
                }
            }
        }
    }
}
