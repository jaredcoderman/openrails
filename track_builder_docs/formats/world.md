# World Files (.w) Format

World files contain the 3D geometry and dynamic objects that appear in the game world.

## Overview

- **Format**: SIMISA Text Format (STF)
- **Location**: `ROUTES/[RouteID]/WORLD/w-[TileX]+[TileZ].w`
- **Purpose**: Defines terrain, buildings, trees, and track geometry
- **Scope**: Each file covers one 2048×2048 meter tile

## File Naming

World files use tile coordinates in their names:

```
w-[TileX]+[TileZ].w
```

**Example:**
```
w-012842+014734.w  # Tile (-12842, 14734) using absolute coords
```

## File Structure

```
SIMISA@@@@@@@@@@JINX0W0t______

Dyntrack (
    Tr_WorldFile (
        Serial ( 1 )
        
        TrackObj ( ... )
        DyntrackObj ( ... )
        StaticObj ( ... )
    )
)
```

## TrackObj (Static Track)

References static track geometry from a shape file.

```
TrackObj (
    SectionIdx ( 50001 )
    Elevation ( 100 )
    CollideFlags ( 7 )
    StaticFlags ( 0 )
    Position ( 0 0 0 )
    QDirection ( 0 0 0 1 )
    VDbId ( 0 0 0 )
    StaticFlags ( 0 )
    FileName ( levels/gta2 )
)
```

| Field | Type | Description |
|-------|------|-------------|
| `SectionIdx` | int | Track section ID from `tsection.dat` |
| `Elevation` | float | Base elevation (Y) in meters |
| `CollideFlags` | int | Collision detection (usually 7 = all faces) |
| `Position` | float×3 | X Y Z coordinates within tile |
| `QDirection` | float×4 | Quaternion rotation (Qx Qy Qz Qw) |
| `VDbId` | int×3 | Database reference (usually 0 0 0) |
| `FileName` | string | Path to shape file (without extension) |

## DyntrackObj (Dynamic Track)

Generated track geometry - this is what TdbDump creates.

```
DyntrackObj (
    SectionIdx ( 50001 )
    Elevation ( 100 )
    CollideFlags ( 7 )
    StaticFlags ( 0 )
    Position ( -433 100 25 )
    QDirection ( 0 0.707107 0 0.707107 )
    VDbId ( 2 0 0 )
    TrackSections ( 1
        TrackSection ( 50001 -12842 14734 0 0 0 0 0 0 0 0 0 0 0 0 )
    )
)
```

| Field | Type | Description |
|-------|------|-------------|
| `SectionIdx` | int | First track section ID |
| `Elevation` | float | Base elevation |
| `Position` | float×3 | X Y Z offset from tile origin |
| `QDirection` | float×4 | Quaternion for orientation |
| `VDbId` | int×3 | UID from TDB (ID, 0, 0) |
| `TrackSections` | array | Array of track section definitions |

### TrackSection Format

```
TrackSection ( SectionIdx TileX TileZ X Y Z AX AY AZ ... )
```

Complete track section with curve data.

## Coordinate System

### Tile Space
- World is divided into 2048m × 2048m tiles
- Tile (-12842, 14734) is at world origin
- Tile (+1, 0) is 2048m East
- Tile (0, +1) is 2048m North

### Local Space (within tile)
- X: 0 to 2048 (West to East)
- Z: 0 to 2048 (South to North)
- Y: elevation (positive = up)

### Position Example
File `w-012842+014734.w` with `Position ( 500 100 1000 )`:
- Tile: (-12842, 14734)
- Local offset: (500, 1000) within tile
- Elevation: 100m
- World position: Depends on how tiles map to world coords

## Quaternions

Rotation stored as normalized quaternion (Qx, Qy, Qz, Qw).

**Example rotations:**

```
No rotation:            0 0 0 1
90° around Y (north):   0 0.707107 0 0.707107
180° around Y:          0 1 0 0
90° around X (pitch):   0.707107 0 0 0.707107
```

## Example: Simple Dyntrack

```
SIMISA@@@@@@@@@@JINX0W0t______

Dyntrack (
    Tr_WorldFile (
        Serial ( 1 )
        DyntrackObj (
            SectionIdx ( 50001 )
            Elevation ( 0 )
            CollideFlags ( 7 )
            StaticFlags ( 0 )
            Position ( 0 0 0 )
            QDirection ( 0 0 0 1 )
            VDbId ( 2 0 0 )
            TrackSections ( 1
                TrackSection ( 50001 -12842 14734 0 0 0 0 0 0 0 0 0 0 0 0 )
            )
        )
    )
)
```

This is a single straight track section at tile origin.

## Multi-Section Dyntrack

Multiple sections in one DyntrackObj:

```
DyntrackObj (
    SectionIdx ( 50001 )
    Position ( 0 0 0 )
    QDirection ( 0 0 0 1 )
    VDbId ( 2 0 0 )
    TrackSections ( 3
        TrackSection ( 50001 -12842 14734 0 0 0 0 0 0 ... )
        TrackSection ( 50002 -12842 14734 100 0 0 0 1.5707 0 ... )
        TrackSection ( 50003 -12842 14734 200 0 50 0 1.5707 0 ... )
    )
)
```

## Coordinate Transformation Notes

Open Rails and TSRE5 use different coordinate systems internally. When converting from TDB to .w files:

1. **X Position**: May need negation or adjustment based on target viewer
2. **Z Position**: Often requires coordinate system conversion
3. **Quaternion**: Axis orientations may differ between implementations
4. **Tile References**: Ensure VDbId matches TDB UIDs

## Tiles Spanning Multiple Files

A long track can span multiple world files:

```
w-012842+014734.w   (contains sections 0-5)
w-012843+014734.w   (contains sections 6-10)
w-012842+014735.w   (contains sections 11-15)
```

Each file contains DyntrackObj entries for its tile region.

## StaticObj (for reference)

Static scenery objects:

```
StaticObj (
    Position ( 1000 100 1000 )
    QDirection ( 0 0 0 1 )
    VDbId ( 0 0 0 )
    StaticFlags ( 0 )
    FileName ( levels/trees/oak )
)
```

## Reading Reference

See source: `Orts.Formats.Msts/WorldFile.cs`
