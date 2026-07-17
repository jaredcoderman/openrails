# Curve Fitter Overview

Python tools under `Tools/curve-fitter` turn NTAD (or similar) GeoJSON polylines into Open Rails–friendly **straight** and **circular arc** primitives.

## Scripts

| Script | Role |
|--------|------|
| `config.py` | Shared paths and fit tolerances |
| `select_bbox_objectids.py` | List OBJECTIDs with any vertex in a lat/lon bbox → `bbox_objectids.txt` |
| `extract_bbox_network.py` | **Primary**: fit every listed OBJECTID in one shared local meter frame → `bbox_network_local.json` |
| `extract_primitives.py` | Legacy: fit one `TARGET_OBJECTID` → `primitives.json` |
| `circle_fitter.py` | PCA lines, Taubin circles, model selection, chained refinement |
| `main.py` | Older single-feature entry (prefer the extract scripts) |

## Network extract (what TdbDump expects)

`extract_bbox_network.py`:

1. Loads OBJECTIDs from `bbox_objectids.txt`.
2. Pulls those features from the GeoJSON in `config.GEOJSON_FILE`.
3. Builds **one** UTM zone + local origin for all vertices (optional `FLIP_X_COORDINATES`).
4. Fits each polyline independently (same fitter as the single-ID path).
5. Writes:
   - `bbox_network.geojson` — WGS84 for QGIS
   - `bbox_network_local.json` — CRS + `points_local` + primitives with absolute `start` poses

Two-point polylines become a single straight (model selection needs ≥3 points).

## Fitting algorithm (per feature)

1. Convert lon/lat → shared local `(x, z)` meters (`z` = northing-ish).
2. Grow segments with model selection: PCA straight vs Taubin circle within tolerances.
3. Optional **chained refinement** so successive primitives meet with less endpoint drift.
4. Split straights longer than `MAX_STRAIGHT_LENGTH` (2048 m tile-friendly).
5. Export primitives; each includes `start: {x, z, ay}` on the shared frame so C# can place sections without integrating only length/angle (which drifts).

## Config knobs (`config.py`)

| Setting | Meaning |
|---------|---------|
| `GEOJSON_FILE` | Input FeatureCollection |
| `TARGET_OBJECTID` | Single-ID path only |
| `STRAIGHT_TOLERANCE` / `CIRCLE_TOLERANCE` | RMS fit thresholds (m) |
| `FLIP_X_COORDINATES` | Mirror easting before local origin |
| `MAX_STRAIGHT_LENGTH` | Split long straights |
| `PRIMITIVES_OUTPUT` | Single-ID output name |

Optional getattr defaults in `extract_primitives.py`: max circle radius, min curve sweep/sagitta, chained refinement, etc.

## What the fitter does *not* do

- No TDB pins, junctions, or world files (that is TdbDump).
- No guarantee reconstructed chains meet neighbors in meters — geo ends meet; reconstruction can drift hundreds of meters until TrackBuilder aligns.

## Next

- [Input format](curve_input.md) / [Output format](curve_output.md)
- [TdbDump Overview](tdbdump_overview.md)
