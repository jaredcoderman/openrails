# Full Pipeline Walkthrough

Real NTAD lines → fitted network → TDB with junctions.

## 1. Pick a study area

Edit corners in `select_bbox_objectids.py` / `extract_bbox_network.py` if needed, then:

```powershell
cd Tools\curve-fitter
py -3 select_bbox_objectids.py
```

Or hand-edit `bbox_objectids.txt`.

## 2. Fit

```powershell
py -3 extract_bbox_network.py
```

Check QGIS with `bbox_network.geojson`. Skim `bbox_network_local.json` for `error` features and high `fit.rms_error`.

## 3. Build route files

```powershell
copy bbox_network_local.json ..\..\Source\TdbDump\bin\Debug\
dotnet build ..\..\Source\TdbDump -c Debug
cd ..\..\Source\TdbDump\bin\Debug
.\TdbDump.exe
```

Example console:

```
Loaded network …: 39 features, 304 sections
Endpoint snap (25m geo): 74 links, 36 chains translated, …
  Junction 40: stem oid 1101E, main oid 1865S, div oid 2017E
Junctions: 1 TrJunctionNode(s) for 3-way clusters
Wrote TrackSections to: …\tsection.dat
Wrote TrackNodes to: …\BNSF_Scenic.tdb (39 features, 43 TDB nodes)
… dynamic tracks written
```

## 4. What TrackBuilder did

```
Per OBJECTID: place sections from primitive start poses
        ↓
Match geo endpoints within 25 m → pairwise links
        ↓
Align chains (multi-way clusters first, then trees)
        ↓
Reseat residual gaps (no reverse twin straights)
        ↓
3-way clusters → TrJunctionNode + tip reshape on geo headings
        ↓
Wire pins (junction / neighbor vector / TrEndNode)
        ↓
Write tsection + tdb + DynTracks
```

## 5. Verify in Track Viewer

1. Open the route.
2. **Ctrl+R** reloads TDB (do this after every TdbDump run).
3. At T-junctions, through and diverge should separate like QGIS — not cross as an “X” at the frog.

World DynTracks use the same chain section lists after junction reshape.

## 6. Scenario files (optional / limited)

`Program.cs` still tries `ScenarioWriter` for the **first** feature only, and only if that chain still has two free `TrEndNode`s. Fully snapped / junctioned networks often skip this. Paths across the whole graph are not stitched yet.

## Data flow

```
GeoJSON OBJECTIDs
    → shared local (x,z) + primitives with start poses
    → FeatureChain per OBJECTID
    → TrackNodes + TrJunctionNodes + TrEndNodes
    → BNSF_Scenic.tdb + tsection.dat + WORLD/w-*.w
```

## Checklist

- [ ] `bbox_objectids.txt` matches the area you care about
- [ ] Extract finished without unexpected `error` features
- [ ] JSON copied next to `TdbDump.exe` before run
- [ ] Console shows expected feature count and junctions
- [ ] Track Viewer Ctrl+R shows connected topology
- [ ] Turnout diverge matches QGIS (not overlapping spur)

## Common failures

| Symptom | Likely cause |
|---------|----------------|
| Missing short stubs | Old fitter dropped 2-point features — use current `extract_bbox_network.py` |
| Long duplicated straights | Reverse/collinear fillers (fixed — use reseat path) |
| Spur crosses through at T | Tip reshape not applied / old TDB — rebuild after latest TrackBuilder |
| Scenario skipped | First feature has no free ends — expected for dense snap |

## Next

- [TrackBuilder](trackbuilder.md)
- [Troubleshooting](../troubleshooting.md)
