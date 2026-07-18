# Getting started

Build a multi-feature track database from NTAD-style GeoJSON.

## Prerequisites

- Python 3 with `numpy` and `pyproj` (optional venv under `Tools/curve-fitter`)
- .NET SDK (to build `Source/TdbDump`)
- Route output path set in `Source/TdbDump/Program.cs` (default: BNSF Scenic copy)

## 1. Configure the fitter

Edit `Tools/curve-fitter/config.py`:

```python
GEOJSON_FILE = 'NTAD_....geojson'   # file in Tools/curve-fitter
STRAIGHT_TOLERANCE = 0.1
CIRCLE_TOLERANCE = 1.0
FLIP_X_COORDINATES = False
```

## 2. Choose OBJECTIDs

Edit `Tools/curve-fitter/bbox_objectids.txt` (one ID per line), or select by bbox:

```powershell
cd Tools\curve-fitter
py -3 select_bbox_objectids.py
```

Corners for the bbox live in that script (and in `extract_bbox_network.py`).

## 3. Fit the network

```powershell
cd Tools\curve-fitter
py -3 extract_bbox_network.py
```

| Output | Use |
|--------|-----|
| `bbox_network_local.json` | Input for TdbDump |
| `bbox_network.geojson` | QGIS check of selected lines |

## 4. Build and run TdbDump

```powershell
copy Tools\curve-fitter\bbox_network_local.json Source\TdbDump\bin\Debug\
dotnet build Source\TdbDump -c Debug
cd Source\TdbDump\bin\Debug
.\TdbDump.exe
```

Writes into the route from `Program.cs`:

- `BNSF_Scenic.tdb`
- `tsection.dat`
- `WORLD/w-012842+014734.w`

Console reports endpoint snap counts and any 3-way junctions.

## 5. Verify

1. Open the route in Track Viewer.
2. Press **Ctrl+R** to reload the TDB.
3. Compare junctions to QGIS (`bbox_network.geojson`).

Scenario files (`.pat` / `.act`) are only attempted for the first feature when it still has two free ends. Dense snapped networks often skip that; the TDB is the main deliverable.

## Single-feature (legacy)

```powershell
# set TARGET_OBJECTID in config.py
py -3 extract_primitives.py    # → primitives.json
```

TdbDump prefers `bbox_network_local.json` when both are present.
