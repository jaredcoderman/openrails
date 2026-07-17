# Curve Output Format

## Primary: `bbox_network_local.json`

Written by `extract_bbox_network.py`. This is what TrackBuilder loads first.

```json
{
  "crs": {
    "epsg": 32612,
    "origin_easting": …,
    "origin_northing": …,
    "flip_x": false,
    "axes": "x=easting-ish (after flip), z=northing"
  },
  "source": {
    "geojson": "….geojson",
    "objectid_list": "bbox_objectids.txt",
    "bbox_corners_latlon": [[lat, lon], [lat, lon]]
  },
  "features": [
    {
      "objectid": 2017,
      "vertex_count": 120,
      "start": { "x": 100.0, "z": 200.0, "ay": -0.54 },
      "end": { "x": 500.0, "z": 80.0 },
      "points_local": [[100.0, 200.0], …],
      "fit": { "rms_error": 0.4, "max_error": 1.2, "endpoint_error": 0.8 },
      "primitives": [
        {
          "type": "straight",
          "length": 64.2,
          "radius": 0.0,
          "angle": 64.2,
          "clockwise": false,
          "start": { "x": 100.0, "z": 200.0, "ay": -0.54 }
        },
        {
          "type": "curve",
          "radius": 176.38,
          "angle": 0.991214,
          "clockwise": true,
          "start": { "x": …, "z": …, "ay": … }
        }
      ]
    }
  ]
}
```

### Per-feature fields

| Field | Role |
|-------|------|
| `objectid` | Stable ID → one TDB vector chain |
| `start` / `end` | Geo polyline ends in local meters (`ay` = travel heading at start) |
| `points_local` | Full polyline for geo heading at either end / debug |
| `primitives[]` | Fitted sections; each has absolute `start` pose |
| `error` | Present instead of primitives if fit failed |

### Primitive fields

| Field | Straight | Curve |
|-------|----------|-------|
| `type` | `"straight"` | `"curve"` |
| `length` | meters | omitted (use `radius * angle`) |
| `radius` | `0` | meters |
| `angle` | same as length (legacy) | sweep radians |
| `clockwise` | false | true = OR right-hand sign convention |
| `start` | `{x,z,ay}` on shared frame | same |

Heading `ay` uses Open Rails–style yaw: `atan2(dx, dz)` with `0` along +Z.

Also written: `bbox_network.geojson` (WGS84) for QGIS — not consumed by TdbDump.

## Legacy: `primitives.json`

From `extract_primitives.py` (one OBJECTID). Shape is a flat `segments` / primitive list without multi-feature CRS. TrackBuilder still accepts it via `BuildFromLegacyPrimitives` when no network JSON is found.

## How TdbDump uses this

1. Prefer `bbox_network_local.json` beside the exe (or discoverable path).
2. Place each primitive from its `start` pose (chained continuity within a feature).
3. Keep geo start/end (and headings from `points_local`) for snap + junction tip reshape.
4. Assign section indices into `tsection.dat` (`SectionCurve` style entries).

See [TrackBuilder](trackbuilder.md).
