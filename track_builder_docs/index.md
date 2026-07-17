# Open Rails Track Builder Documentation

Pipeline for turning real railroad GeoJSON into Open Rails track data (TDB, tsection, world DynTracks).

## What This Pipeline Does

1. **Curve fitter** (`Tools/curve-fitter`) — Fits NTAD/GeoJSON polylines to straight + circular-arc primitives in a shared local meter frame.
2. **TdbDump / TrackBuilder** (`Source/TdbDump`) — Places one TDB vector chain per OBJECTID, snaps endpoints, builds 3-way junctions, writes route files.
3. **Verify** — Track Viewer (TDB, Ctrl+R) and/or Open Rails world DynTracks.

## Current Workflow (Network)

```
GeoJSON (NTAD BNSF lines)
        ↓
select_bbox_objectids.py  →  bbox_objectids.txt
        ↓
extract_bbox_network.py   →  bbox_network_local.json (+ QGIS geojson)
        ↓
copy JSON → Source/TdbDump/bin/Debug/
        ↓
TdbDump.exe               →  .tdb / tsection.dat / WORLD/*.w
        ↓
Track Viewer Ctrl+R  (or load route in OR)
```

Single-OBJECTID `extract_primitives.py` → `primitives.json` still works as a legacy path.

## Quick Navigation

| Goal | Doc |
|------|-----|
| Run it now | [Quick Start](quick_start.md) |
| End-to-end example | [Full Pipeline Walkthrough](pipeline/full_walkthrough.md) |
| Fitter details | [Curve Fitter Overview](pipeline/curve_fitter_overview.md) |
| TrackBuilder / junctions | [TrackBuilder Details](pipeline/trackbuilder.md) |
| File formats | [formats/](formats/tdb.md) |
| Stuck? | [Troubleshooting](troubleshooting.md) |

## Project Layout

```
openrails/
├── Tools/curve-fitter/     # Python fit + bbox network extract
├── Source/TdbDump/         # C# TrackBuilder + writers
└── track_builder_docs/     # This documentation
```

## Key Ideas

- **One OBJECTID → one TDB vector node** (chain of sections), not one mega-polyline for the whole route.
- **Snap on GeoJSON ends** (not reconstructed ends) so topology matches QGIS even when chained reconstruction drifts.
- **3-way clusters → TrJunctionNode**; tip geometry is reshaped onto geo headings so spurs diverge instead of overlapping the through line.
- **Tiles**: Base tile for BNSF Scenic work is `(-12842, 14734)`; local coords within 2048 m tiles.
