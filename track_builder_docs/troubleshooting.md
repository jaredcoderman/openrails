# Troubleshooting

Common issues and how to resolve them.

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

**Check:** Local X, Z coordinates are within [0, 2048]

If position is (2500, 1000):
- 2500 > 2048 ← Out of bounds!
- Should be: Tile+1, local (452, 1000)

**Fix:** Verify HandleTileBoundary logic in TrackBuilder.

#### 4. Incorrect VDbId References

**Check:** World file VDbId matches TDB node ID

```
.tdb:
tracknode ( 2 ... )         ← Node ID = 2

.w file:
DyntrackObj (
    VDbId ( 2 0 0 )         ← References node 2 ✓
)
```

If VDbId = 1 but node 1 is an end node, it won't render.

### Diagnostic Steps

1. Check `OpenRailsLog.txt` for error messages
2. Verify file names with explorer
3. Open .tdb in text editor and check coordinates
4. Confirm world files are in WORLD folder
5. Test with simpler track geometry first

## Pin Connection Errors

### Symptom
`Ignored invalid track node pin [dir] link to track node X`

### Causes

#### 1. Pin References Out-of-Bounds Node

```
TrPin ( 99 0 )    ← Node 99 doesn't exist!
```

**Fix:** Ensure all pin node IDs are valid and within node count.

#### 2. Pin Direction Wrong

```
trpins ( 1 0
    TrPin ( 2, 1 )    ← TrEndNode should have direction 1? ✗
)
```

End nodes should usually have `TrPin ( nextNode, 1 )`.

**Fix:** Validate pin directions match expected topology.

#### 3. Non-Reciprocal Pins

```
Node 1: TrPin ( 2, 0 )
Node 2: TrPin ( 3, 0 )    ← Doesn't link back to Node 1! ✗
```

**Fix:** Ensure bidirectional connections.

#### 4. Vector Node Not Multi-Section

If each section is a separate node:

```
Node 1 ──→ Node 2 ──→ Node 3    ← Vector nodes pinning to each other ✗
```

MapViewer will try to access UiD on vector nodes and crash.

**Fix:** Combine sections into single multi-section VectorNode.

### Resolution

1. Check all node IDs in pins (should be 1-based, not 0)
2. Verify pin directions (0 or 1)
3. Ensure reciprocal connections
4. Use single multi-section vector node
5. Re-examine TDB structure against example

## Activity Won't Load

### Symptom
Activity shows load error or crashes immediately.

### Causes

#### 1. Service File Missing

```
Activity: Player_Service_Definition ( MyService )
Missing:  SERVICES/MyService.srv
```

**Fix:** Create the .srv file with correct name.

#### 2. Path File Missing

```
Service: PathID ( MyPath )
Missing: PATHS/MyPath.pat
```

**Fix:** Ensure .pat file exists.

#### 3. Consist Not Found

```
Service: TrainConfig ( MyCons ist )
Missing: TRAINS/CONSISTS/MyConsist.con
```

**Fix:** Use existing consist or create one.

#### 4. Invalid Activity Syntax

```
StartTime ( 25 0 0 )    ← Hour = 25? Invalid! ✗
Season ( 10 )           ← Only 0-3 valid ✗
```

**Fix:** Validate time, season, weather values.

#### 5. Route ID Wrong

```
Activity: RouteID ( BadRouteName )
Actual:   ROUTES/BNSF_Scenic/
```

**Fix:** Match route folder name exactly.

### Diagnostic Steps

1. Check `OpenRailsLog.txt` for specific error
2. Verify folder/file names exist
3. Test activity syntax against provided examples
4. Start with minimal activity, add complexity
5. Check file permissions (read-only?)

## Path Problems

### Symptom
Train starts off track or doesn't follow path.

### Causes

#### 1. Waypoint Coordinates Wrong

TrackPDP coordinates don't match TDB positions.

**Fix:** Ensure each TrackPDP copies X, Y, Z from corresponding TrVectorSection.

#### 2. Path Too Short

Only 1-2 waypoints when track has many sections.

**Fix:** Create TrackPDP for each TrVectorSection.

#### 3. Tile Mismatch in Path

```
TrackPDP ( -12842 14734 0 0 0 ... )   ← Tile A
TrVectorSection X/Z = 0                ← Matches ✓

TrackPDP ( -12843 14734 100 0 0 ... )  ← Tile B
TrVectorSection TileX/TileZ = (-12843, 14734)  ← Matches ✓
```

If they don't match, train will warp.

**Fix:** Verify path waypoint tiles match section tiles.

#### 4. Last Waypoint Doesn't End Path

```
TrPathNode ( 00000000 X 4294967295 4294967295 )  ← Correct (end)
TrPathNode ( 00000000 X 999 4294967295 )         ← Links to node 999! ✗
```

**Fix:** Final node should have NextMainNode = 0xFFFFFFFF.

### Resolution

1. Manually compare TrackPDP coordinates to TDB
2. Count sections vs. waypoints (should match)
3. Check path linking (0xFFFFFFFF at end)
4. Start position matters (first TrackPDP = start point)

## Render Issues

### Symptom
Track appears but looks wrong (twisted, flipped, wrong position).

### Causes

#### 1. Coordinate System Mismatch

TSRE5 and Open Rails use different internal coordinate systems.

**Fix:** Apply coordinate transformations in WorldWriter:
- Negate X position
- Keep Z positive
- Adjust quaternion for 180° Y rotation

#### 2. Euler Angles Wrong

```
AX = 0      ← Pitch (forward/back tilt)
AY = 0      ← Yaw/Heading (direction)
AZ = 0      ← Roll (banking)
```

If these don't match curve geometry, track will be twisted.

**Fix:** Verify angle calculations in TrackBuilder.UpdatePosition().

#### 3. Quaternion Incorrect

Quaternion doesn't match Euler angles.

**Fix:** Use correct conversion formula (ZYX order):
```
cy = cos(ay/2); sy = sin(ay/2)
cp = cos(ax/2); sp = sin(ax/2)
cr = cos(az/2); sr = sin(az/2)

qx = sr*cp*cy - cr*sp*sy
qy = cr*sp*cy + sr*cp*sy
qz = cr*cp*sy - sr*sp*cy
qw = cr*cp*cy + sr*sp*sy
```

## Map Viewer Crashes

### Symptom
MapViewer crashes with NullReferenceException at line 337.

### Cause
Vector nodes with single sections pinning directly to each other.

### Fix
Use single multi-section TrVectorNode containing all sections:

```
TrVectorNode (
    TrVectorSections ( 25   ← All 25 sections in ONE node
        50001 ...
        50002 ...
        ...
        50025 ...
    )
)
```

NOT:
```
TrVectorNode ( 50001 ... ) → TrVectorNode ( 50002 ... )  ← Each section separate ✗
```

## General Debugging Tips

### 1. Check Logs First
```
C:\Users\{username}\OneDrive\Desktop\OpenRailsLog.txt
```

Stack traces point to exact problem location.

### 2. Simplify Test Case
Start with single straight section, then add complexity.

### 3. Use TrackViewer
TrackViewer validates TDB and shows structure:
```
File → Open → tdb.dat
```

### 4. Validate File Format
Open .tdb, .pat, .w files in text editor:
- Check syntax (matching parentheses)
- Verify values are reasonable
- Look for typos

### 5. Compare With Working Examples
Compare generated files against known-good tracks.

### 6. Isolate Problem
Disable features one at a time:
- Test with flat track (AZ = 0)
- Test without world file
- Test with single waypoint path

## Getting Help

Provide when asking for help:
- [ ] Relevant section of OpenRailsLog.txt
- [ ] Exact error message
- [ ] What you were trying to do
- [ ] Simplified test case
- [ ] Files involved (tdb, pat, w, act)

Common questions answered in:
- [Pin Connections](../concepts/pins.md)
- [Coordinate Systems](../concepts/coordinates.md)
- [File Formats](../formats/tdb.md)
