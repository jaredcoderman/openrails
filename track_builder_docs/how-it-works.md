# How it works

Two tools, one handoff file.

```
GeoJSON OBJECTIDs
    → shared local (x, z) meters + fitted primitives
    → one TDB vector chain per OBJECTID
    → snap / junctions / DynTracks
```

## Curve fitter

[`extract_bbox_network.py`](../Tools/curve-fitter/extract_bbox_network.py) loads IDs from `bbox_objectids.txt`, puts every vertex in **one** UTM-based local frame, and fits each polyline to straights and circular arcs.

- Each primitive includes an absolute `start` pose (`x`, `z`, `ay`) so placement does not rely only on integrating length/angle.
- Two-point features become a single straight.
- `bbox_network.geojson` is the same selection in WGS84 for QGIS.

## TrackBuilder

[`TrackBuilder`](../Source/TdbDump/TrackBuilder.cs) loads `bbox_network_local.json` and builds the graph:

| Step | What happens |
|------|----------------|
| Place | One `FeatureChain` per OBJECTID; sections from primitive start poses |
| Snap | Match **geo** endpoints within ~25 m (source ends, not drifted reconstruction) |
| Align | Translate chains so joints meet; reseat tip straights for leftovers |
| Junctions | 3 geo-ends in a cluster → `TrJunctionNode` (stem / main / diverging) |
| Tip reshape | Replace tip approach with a straight on the geo heading so turnouts keep their diverge angle |
| Wire | Pins to junction, neighbor vector, or `TrEndNode` |
| Write | `tsection.dat`, `.tdb`, world DynTracks (one DynTrack per section) |

Junction reshape runs **before** vector sections are snapshotted for the TDB. Through legs use a shorter tip rewrite; the diverging leg uses a longer one so spur curves do not cross the through line.

## Outputs

| File | Contents |
|------|----------|
| `*.tdb` | Vector nodes (one per OBJECTID), ends, junctions |
| `tsection.dat` | Dynamic section definitions |
| `WORLD/w-*.w` | DynTrack visuals from the same chains |

Base tile for the current BNSF Scenic setup: `(-12842, 14734)`.
