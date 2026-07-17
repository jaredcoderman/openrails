# Troubleshooting

Common issues for the **network** pipeline (GeoJSON → bbox fit → TdbDump). Older single-polyline notes still apply where noted.

## Network / TdbDump

### Wrong or empty network loaded

**Check:** `bbox_network_local.json` sits next to `TdbDump.exe` (`Source/TdbDump/bin/Debug/`). TdbDump prefers that name over `primitives.json`.

**Fix:** Re-copy after every `extract_bbox_network.py` run.

### Missing short OBJECTIDs / gaps next to stubs

**Cause:** Two-point GeoJSON features used to be dropped (fitter required ≥3 points).

**Fix:** Current `extract_bbox_network.py` emits a single straight for 2-point polylines. Re-extract and rebuild.

### Long duplicated / reverse straights at joints

**Cause:** Old filler logic appended reverse or collinear twin sections.

**Fix:** Current builder reseats tip straights onto partners. Rebuild with latest `TrackBuilder.cs`.

### Spur overlaps through track at a T-junction

**Cause:** Reconstruction collapsed the diverge angle, and/or a fitted spur curve swung into the main; reshape must run **before** the TDB vector snapshot.

**Fix:** Rebuild with current code (`ReshapeJunctionApproach` + junctions before section snapshot). Ctrl+R in Track Viewer. Tip should follow geo heading (~QGIS diverge); near the frog paths are close by design, but should not form an “X”.

### “Skipping N-way cluster”

Only **3-way** junctions are implemented. Larger endpoint clusters are left as pairwise links / ends.

### Scenario files skipped

Expected when the first feature’s ends are snapped or junctioned (no free `TrEndNode` pair). TDB topology is still valid.

### Track Viewer shows old geometry

Press **Ctrl+R** after every TdbDump run. Confirm timestamps on `BNSF_Scenic.tdb` / `tsection.dat`.

---

## Track Not Visible in Game

### Symptom
Generated track files exist but don't appear when loading the activity.

### Causes & Fixes

#### 1. World File Naming

**Check:** File names match tile coordinates

```
Expected: w-012842+014734.w
Wrong:    w-012842+14734.w     ← Missing leading zeros
          w-12842+14734.w      ← Tile coords without padding
```

**Fix:** Use format `w-[+/-XXXXXX][+/-XXXXXX].w` with 6-digit padding.

#### 2. Tile Mismatch Between Files

**Check:** Consistency across TDB, .w files, and .pat

- TrVectorSection.TileX/TileZ in TDB
- World file name coordinates
- TrackPDP tile coordinates in .pat

Must all match!

#### 3. Coordinates Out of Tile

**Check:** Local X, Z coordinates are within a sensible tile-local range after placement. TrackBuilder re-tiles via `PlaceWorld`.

#### 4. Looking at world vs TDB

Track Viewer primarily validates **TDB**. DynTracks in `.w` are a separate visual path; both should match post-reshape chains.

### Diagnostic Steps

1. Check Open Rails / Track Viewer logs
2. Confirm TdbDump console feature + junction counts
3. Open `.tdb` and confirm vector/junction nodes exist
4. Confirm world file is under `WORLD/`
5. Compare a junction to QGIS (`bbox_network.geojson`)

## Pin Connection Errors

### Symptom
`Ignored invalid track node pin [dir] link to track node X`

### Causes

#### 1. Pin References Out-of-Bounds Node

```
TrPin ( 99 0 )    ← Node 99 doesn't exist!
```

#### 2. Non-reciprocal pins

If A pins to B, B must pin back appropriately. Junction wiring is generated in `WireVectorSide` / `CreateJunctionNodes` — prefer fixing builder logic over hand-editing TDB.

#### 3. Confusing Direction

Pin **Direction** is the side on the **linked** node, not “forward along this chain.” See [Pin Semantics](deep_dives/pin_semantics.md).

## Curve Fitter Issues

| Problem | Fix |
|---------|-----|
| No features fitted | Wrong GeoJSON path / OBJECTIDs not in file |
| Huge RMS | Tighten or loosen tolerances; check flipped X |
| QGIS and local disagree | Shared CRS / `FLIP_X_COORDINATES` mismatch — keep flip consistent between extract and expectations |

## MapViewer / single-section vectors

Do **not** emit one vector node per section for the whole route. Current design: **one vector node per OBJECTID** with many sections. That is intentional and different from “one vector for the entire railroad.”

## General Debugging Tips

1. Re-run extract → copy JSON → rebuild → Ctrl+R as one loop.
2. Use a small `bbox_objectids.txt` when isolating a junction.
3. Log lines from TrackBuilder (`Endpoint snap…`, `Junction…`) are the first signal of topology health.
4. Prefer geometry questions against TDB + QGIS before chasing world quaternion issues.

See also [Quick Start](quick_start.md) and [TrackBuilder](pipeline/trackbuilder.md).
