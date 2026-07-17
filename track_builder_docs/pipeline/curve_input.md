# Curve Input Format

## GeoJSON

NTAD-style FeatureCollection. Each feature needs an `OBJECTID` and a `LineString` or `MultiLineString` (longest part used).

```json
{
  "type": "FeatureCollection",
  "features": [
    {
      "type": "Feature",
      "properties": { "OBJECTID": 1101 },
      "geometry": {
        "type": "LineString",
        "coordinates": [[-110.4, 47.0], [-110.39, 47.01]]
      }
    }
  ]
}
```

Coordinates are `[longitude, latitude]`. The network extractor reverses vertex order so travel direction matches the historical single-OBJECTID convention.

## OBJECTID list (`bbox_objectids.txt`)

One integer per line; `#` comments allowed. Built by hand or by `select_bbox_objectids.py` using corners in that script (same bbox idea as the network extract).

## `config.py`

| Parameter | Used by | Description |
|-----------|---------|-------------|
| `GEOJSON_FILE` | all extracts | GeoJSON path relative to `Tools/curve-fitter` |
| `TARGET_OBJECTID` | `extract_primitives.py` | Single feature |
| `STRAIGHT_TOLERANCE` | fit | RMS perpendicular error (m) for lines |
| `CIRCLE_TOLERANCE` | fit | RMS radial error (m) for arcs |
| `FLIP_X_COORDINATES` | local frame | Negate easting before origin |
| `MAX_STRAIGHT_LENGTH` | split | Cap straight length (default 2048) |
| `PRIMITIVES_OUTPUT` | single-ID | Output filename |

Tighter tolerances → more segments, closer to source vertices. Looser → fewer, smoother primitives.

## Bbox selection

`select_bbox_objectids.py` keeps an OBJECTID if **any** vertex lies inside the lat/lon rectangle from `CORNER_A` / `CORNER_B`. Edit those corners when changing study area, then re-run extract.

## Density tips

- Sparse polylines underfit curves; densify or loosen circle tolerance carefully.
- Tiny 2-point stubs are valid (exported as one straight).
- Degenerate zero-length pairs fail and appear under `error` in the network JSON.
