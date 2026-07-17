# README — Track Builder Documentation

Guide for the Open Rails track-building pipeline: GeoJSON → fitted primitives → TDB / tsection / world files.

## Overview

1. **Curve fitter** (`Tools/curve-fitter`) — Fit real railroad polylines to straights + circular arcs.
2. **TdbDump** (`Source/TdbDump`) — Build a multi-feature track database with endpoint snap and junctions.
3. **Verify** — Track Viewer (reload TDB) or Open Rails.

Primary path today is a **bbox network** of many OBJECTIDs sharing one local CRS, not a single hand-authored curve list.

## Quick Links

- [Quick Start](quick_start.md)
- [Full Walkthrough](pipeline/full_walkthrough.md)
- [Troubleshooting](troubleshooting.md)
- [Glossary](glossary.md)

## Documentation Map

### Pipeline
- [Curve Fitter Overview](pipeline/curve_fitter_overview.md)
- [Curve Input](pipeline/curve_input.md) / [Curve Output](pipeline/curve_output.md)
- [TdbDump Overview](pipeline/tdbdump_overview.md)
- [TdbDump Architecture](pipeline/tdbdump_architecture.md)
- [TrackBuilder](pipeline/trackbuilder.md)
- [Writers](pipeline/writers.md)

### Formats & Concepts
- [TDB](formats/tdb.md), [World](formats/world.md), [PAT](formats/pat.md), …
- [Coordinates](concepts/coordinates.md), [Pins](concepts/pins.md), [UIDs](concepts/uids.md)

## Mental Model

| Piece | Meaning |
|-------|---------|
| OBJECTID | One GeoJSON feature → one fitted chain → one TDB vector node |
| `bbox_network_local.json` | Shared UTM-ish frame + per-feature primitives with absolute `start` poses |
| Endpoint snap | Match features whose **geo** ends are within ~25 m; translate/reseat so reconstructed joints meet |
| TrJunctionNode | 3 geo-ends in one cluster → stem / main / diverging; tip reshape keeps diverge angle |
| DynTrack | One world object per section (not one packed DynTrack for a whole chain) |

## Typical Commands

```powershell
cd Tools\curve-fitter
# optional: py -3 select_bbox_objectids.py
py -3 extract_bbox_network.py
copy bbox_network_local.json ..\..\Source\TdbDump\bin\Debug\

cd ..\..\Source\TdbDump
dotnet build -c Debug
cd bin\Debug
.\TdbDump.exe
```

Outputs (configured in `Program.cs`) go to the BNSF Scenic route copy:

- `BNSF_Scenic.tdb`
- `tsection.dat`
- `WORLD/w-012842+014734.w`

Reload in Track Viewer with **Ctrl+R**.

## What Changed vs Older Docs

Older docs described a single End→Vector→End polyline from `primitives.json` / hand `curves.json`. That legacy path still loads, but the supported workflow is:

- Multi-feature network JSON
- Per-feature vector nodes + pairwise pins
- Optional `TrJunctionNode` for T-junctions
- Junction tip reshape so fitted spur curves do not visually cross the through track

See [TrackBuilder](pipeline/trackbuilder.md) for the current build steps.
