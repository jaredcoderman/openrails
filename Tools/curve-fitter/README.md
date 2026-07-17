# Curve Fitter

Fit NTAD/GeoJSON railroad polylines into Open Rails straight + circular-arc primitives.

## Quick network workflow

```powershell
# optional: edit CORNER_A / CORNER_B in select_bbox_objectids.py
py -3 select_bbox_objectids.py          # → bbox_objectids.txt

py -3 extract_bbox_network.py           # → bbox_network_local.json
                                        # → bbox_network.geojson (QGIS)

copy bbox_network_local.json ..\..\Source\TdbDump\bin\Debug\
```

Configure `GEOJSON_FILE`, tolerances, and `FLIP_X_COORDINATES` in `config.py`.

## Scripts

| File | Purpose |
|------|---------|
| `config.py` | Paths + fit tolerances |
| `select_bbox_objectids.py` | OBJECTIDs intersecting a lat/lon bbox |
| `extract_bbox_network.py` | Multi-OBJECTID fit, shared local CRS |
| `extract_primitives.py` | Single `TARGET_OBJECTID` → `primitives.json` |
| `circle_fitter.py` | PCA / Taubin / model selection / chained refine |

## Outputs for TdbDump

- **`bbox_network_local.json`** — preferred input (features + primitives with absolute `start` poses)
- **`primitives.json`** — legacy single-feature input

Docs: `track_builder_docs/` (start at `quick_start.md` and `pipeline/curve_fitter_overview.md`).

## Notes

- Two-point polylines export as one straight.
- Vertices are reversed on load to keep travel direction consistent with the older single-ID path.
- Reconstruction can drift between features; TdbDump snaps on **geo** ends and builds junctions.
