# Quick Start Guide

GeoJSON railroad lines → Open Rails TDB in a few steps.

## Pipeline

```
NTAD GeoJSON
    → select OBJECTIDs (bbox)
    → fit network (shared local meters)
    → TdbDump
    → .tdb + tsection.dat + WORLD/*.w
    → Track Viewer Ctrl+R
```

## Prerequisites

- Python 3 + `numpy`, `pyproj` (curve-fitter venv under `Tools/curve-fitter` if you use it)
- .NET SDK (build `Source/TdbDump`)
- Route folder already configured in `Source/TdbDump/Program.cs` (default: BNSF Scenic copy)

## Step 1 — Configure fitter

Edit `Tools/curve-fitter/config.py`:

```python
GEOJSON_FILE = 'NTAD_....geojson'   # in the curve-fitter folder
STRAIGHT_TOLERANCE = 0.1
CIRCLE_TOLERANCE = 1.0
FLIP_X_COORDINATES = False
```

## Step 2 — Choose OBJECTIDs

Either edit `Tools/curve-fitter/bbox_objectids.txt` (one ID per line), or:

```powershell
cd Tools\curve-fitter
py -3 select_bbox_objectids.py
```

That writes IDs whose vertices fall in the lat/lon box defined in the script.

## Step 3 — Fit the network

```powershell
cd Tools\curve-fitter
py -3 extract_bbox_network.py
```

Produces:

| File | Use |
|------|-----|
| `bbox_network_local.json` | Input for TdbDump |
| `bbox_network.geojson` | Drop in QGIS to verify selection |

## Step 4 — Run TdbDump

```powershell
copy Tools\curve-fitter\bbox_network_local.json Source\TdbDump\bin\Debug\
dotnet build Source\TdbDump -c Debug
cd Source\TdbDump\bin\Debug
.\TdbDump.exe
```

Writes into the route path hard-coded in `Program.cs`:

- `…/BNSF_Scenic.tdb`
- `…/tsection.dat`
- `…/WORLD/w-012842+014734.w`

Console should mention endpoint snap counts and any `TrJunctionNode` created.

## Step 5 — Verify

1. Open the route in **Track Viewer**.
2. Press **Ctrl+R** to reload the TDB.
3. Compare junctions / diverge angles to QGIS (`bbox_network.geojson`).

Scenario `.pat` / `.act` generation is still first-feature-only when that feature still has two free ends; networked junctions often skip scenario write — topology in the TDB is the main deliverable.

## Legacy single-feature path

```powershell
# config.py TARGET_OBJECTID = …
py -3 extract_primitives.py          # → primitives.json
# place primitives.json next to TdbDump.exe (or only network JSON present)
.\TdbDump.exe
```

TdbDump prefers `bbox_network_local.json` when present.

## Next

- [Full Walkthrough](pipeline/full_walkthrough.md)
- [TrackBuilder](pipeline/trackbuilder.md) (snap, junctions, tip reshape)
- [Troubleshooting](troubleshooting.md)
