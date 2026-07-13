# UIDs and References

UIDs (Universal Identifiers) are unique identifiers used throughout Open Rails to link data across files.

## What is a UID?

A UID is a unique 12-element identifier array:

```
UID ( TileX TileZ X Y Z AX AY AZ WorldId Reserved Reserved Reserved )
```

Encodes position and orientation information.

## UID Structure

| Element | Type | Description |
|---------|------|-------------|
| 0 | int | TileX coordinate |
| 1 | int | TileZ coordinate |
| 2 | float | Position X (local) |
| 3 | float | Position Y (elevation) |
| 4 | float | Position Z (local) |
| 5 | float | Angle AX (pitch) |
| 6 | float | Angle AY (yaw) |
| 7 | float | Angle AZ (roll) |
| 8 | int | World ID / UID variant |
| 9-11 | int | Reserved (usually 0) |

## Example UIDs

### Level crossing at tile origin:
```
uid ( -12842 14734 0 100 0 0 0 0 0 0 0 0 )
```
- Tile: (-12842, 14734)
- Position: (0, 100, 0) — origin with 100m elevation
- Rotation: (0, 0, 0) — not rotated
- World ID: 0

### Level crossing rotated 90°:
```
uid ( -12842 14734 500 100 500 0 1.5707963 0 0 0 0 0 )
```
- Tile: (-12842, 14734)
- Position: (500, 100, 500)
- Rotation: (0, π/2, 0) — 90° yaw (heading north)
- World ID: 0

## Where UIDs Appear

### TrEndNode (in .tdb)

```
tracknode ( 1
    uid ( -12842 14734 0 100 0 0 0 0 0 0 0 0 )
    trendnode ( )
    trpins ( ...
```

Stores the position/orientation of the end node.

### World Files (in .w)

DyntrackObj references TDB sections via VDbId:

```
DyntrackObj (
    SectionIdx ( 50001 )
    VDbId ( 2 0 0 )   # References TDB node 2, entry 0
    ...
)
```

The `2` refers to a UID value in the TDB.

### TrVectorSection (in .tdb)

```
50001 -12842 14734 0 0 0 0 0 0 -12842 14734 2
                                          └─ WorldFileUiD = 2
```

The `WorldFileUiD` links to a world file object.

## UID Scope

### Local UIDs (in .tdb)

UIDs used within a TrVectorNode are **local identifiers**, not globally unique:

```
TrVectorNode (
    TrVectorSections ( 3
        50001 ... -12842 14734 2    ← UiD = 2 (local to this node)
        50002 ... -12842 14734 2    ← Same UiD = 2
        50003 ... -12842 14734 2    ← Same UiD = 2
    )
)
```

All sections in one node typically share the same WorldFileUiD.

### Global UIDs

Node IDs themselves serve as global UIDs:

```
tracknode ( 1 ... )    ← Node ID 1
tracknode ( 2 ... )    ← Node ID 2
tracknode ( 3 ... )    ← Node ID 3
```

These are globally unique across the entire TDB.

## World File References

World files use UIDs to reference TDB content:

```
.w file:
DyntrackObj (
    SectionIdx ( 50001 )
    Position ( -433 100 25 )
    VDbId ( 2 0 0 )           ← Links to Node ID 2 in TDB
)

.tdb file:
tracknode ( 2
    TrVectorNode (
        TrVectorSections ( 25
            50001 ... 2      ← WorldFileUiD = 2
```

The `2` in `VDbId` matches the node ID.

## UID Validation

Open Rails validates:
- [ ] UID tiles exist in world
- [ ] UID coordinates are in valid range
- [ ] UID world ID is recognized
- [ ] Related UIDs are consistent

### Common Issues

**"UID 0"** (default/null UID)
- Often used for scenery objects that don't connect to TDB
- Valid for non-track objects

**Mismatched UIDs**
- World file VDbId doesn't match TDB node ID
- Can cause rendering issues or crashes

**Out-of-bounds UID**
- Tile coordinates outside expected range
- May prevent object from rendering

## UID Format Interpretation

When reading UIDs in Open Rails code:

```csharp
public struct UID
{
    public int TileX { get; set; }           // Element 0
    public int TileZ { get; set; }           // Element 1
    public float X { get; set; }             // Element 2
    public float Y { get; set; }             // Element 3
    public float Z { get; set; }             // Element 4
    public float AX { get; set; }            // Element 5
    public float AY { get; set; }            // Element 6
    public float AZ { get; set; }            // Element 7
    public uint WorldID { get; set; }        // Element 8
}
```

## Creating Valid UIDs

When generating TDB:

```csharp
// For end node
var uid = new[] { tileX, tileZ, x, y, z, ax, ay, az, 0, 0, 0, 0 };

// For world file reference
vdbId = (uint)nodeId;  // Simple: use node ID as UID
```

## Best Practices

1. **Consistent UIDs**: Use same UID for all sections in a node
2. **Unique Node IDs**: Ensure 1-based node IDs are unique
3. **Verify Mappings**: Check world file VDbId matches TDB node ID
4. **Test Rendering**: Verify objects appear in correct locations

## References

- `Orts.Formats.Msts/TrackDatabaseFile.cs` - UID parsing
- `Orts.Simulation/Simulation/Simulator.cs` - UID validation
