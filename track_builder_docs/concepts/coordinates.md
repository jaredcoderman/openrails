# Coordinate Systems

Understanding Open Rails' coordinate systems is crucial for track building.

## World Coordinates

The Open Rails world is infinite, but organized into discrete tiles.

### Tile System

```
Tiles (2048m × 2048m each)
┌────────────────┬────────────────┬────────────────┐
│(-12842, 14735) │(-12841, 14735) │(-12840, 14735) │
├────────────────┼────────────────┼────────────────┤
│(-12842, 14734) │(-12841, 14734) │(-12840, 14734) │ ◄─ Base tile
├────────────────┼────────────────┼────────────────┤
│(-12842, 14733) │(-12841, 14733) │(-12840, 14733) │
└────────────────┴────────────────┴────────────────┘
```

- **TileX**: Horizontal position (-13000 to 13000 typical)
- **TileZ**: Vertical position (maps to north-south)
- **Negative TileX** = West of origin
- **Positive TileX** = East of origin
- **Negative TileZ** = South of origin
- **Positive TileZ** = North of origin

### Local Coordinates

Within each tile (0-2048):

```
Within one tile:
(0, 0) ──────────X───────────→ (2048, 0)
 │
 Z
 │
 │
 v
(0, 2048) ──────────────────── (2048, 2048)

Northwest corner: (0, 0)
Northeast corner: (2048, 0)
Southwest corner: (0, 2048)
Southeast corner: (2048, 2048)
```

- **X**: 0-2048 (West to East within tile)
- **Z**: 0-2048 (North to South within tile, counter-intuitive!)
- **Y**: Elevation (height above sea level)

## Tile Boundary Crossing

When a track section goes beyond tile boundaries, it must be adjusted:

```csharp
// Check if X crosses boundary
if (x >= 2048)
{
    int tilesEast = (int)(x / 2048);
    tileX += tilesEast;
    x -= tilesEast * 2048;
}

// Check if Z crosses boundary
if (z >= 2048)
{
    int tilesSouth = (int)(z / 2048);
    tileZ += tilesSouth;
    z -= tilesSouth * 2048;
}
```

### Example

Track at tile (-12842, 14734) with local position (2100, 500):
1. X = 2100 exceeds tile width (2048)
2. Move to tile X = -12841 (tileX + 1)
3. New local X = 2100 - 2048 = 52
4. New position: Tile (-12841, 14734), local (52, 500)

## World Filenames

World file names encode their tile:

```
w-[TILEX]+[TILEZ].w
```

**Examples:**
```
w-012842+014734.w   # Absolute coordinates shown
w-012843+014733.w   # +1 East, -1 North
w-012841+014735.w   # -1 West, +1 South
```

Note: The format typically uses absolute tile coordinates, not relative offsets.

## Euler Angles (Rotations)

Track orientation uses three Euler angles:

- **AX (Roll)**: Rotation around X axis (banking/super-elevation)
  - 0 = level
  - π/4 ≈ 45° bank
  - π/2 = 90° bank

- **AY (Yaw)**: Rotation around Y axis (heading/direction)
  - 0 = East
  - π/2 ≈ 1.5707... = North
  - π = West
  - 3π/2 = South

- **AZ (Pitch)**: Rotation around Z axis (grade/slope)
  - 0 = level
  - Positive = upgrade (climbing)
  - Negative = downgrade (descending)

### Example Headings

```
AY = 0          → Heading East
AY = π/2        → Heading North
AY = π          → Heading West
AY = 3π/2       → Heading South
```

## Quaternions

Quaternions represent rotations more efficiently than Euler angles:

```
Quaternion (Qx, Qy, Qz, Qw)
```

Conversion from Euler angles:
```csharp
// Assuming ZYX rotation order
float cy = cos(ay * 0.5);
float sy = sin(ay * 0.5);
float cp = cos(ax * 0.5);
float sp = sin(ax * 0.5);
float cr = cos(az * 0.5);
float sr = sin(az * 0.5);

qx = sr * cp * cy - cr * sp * sy;
qy = cr * sp * cy + sr * cp * sy;
qz = cr * cp * sy - sr * sp * cy;
qw = cr * cp * cy + sr * sp * sy;
```

## Elevation (Y)

Elevation is stored in meters above sea level.

```
Y = 0       Sea level
Y = 100     100 meters elevation
Y = -50     50 meters below sea level
```

## Coordinate Transformation Example

Converting from one tile system to another:

**Input:**
- Tile (-12842, 14734)
- Local position (500, 1000)
- Elevation 100m
- Heading North (AY = π/2)

**Same absolute position expressed as:**
- Tile (-12841, 14734)
- Local position (500 + 2048, 1000) = (2548, 1000) ← Out of bounds!
- Must adjust: Tile (-12840, 14734), local (500, 1000)

## Important Notes

1. **Z axis direction**: In Open Rails, increasing Z typically means moving North (counter-intuitive for many)
2. **Tile boundaries**: Always check when accumulating positions
3. **Negative coordinates**: Tiles can have negative X and Z
4. **Cross-tile tracks**: Must split into multiple world file entries
5. **Coordinate verification**: Always validate that positions fall within [0, 2048] for local coordinates

## Debugging Coordinate Issues

To verify coordinates:

1. Check tile is in valid range (typically -13000 to 13000)
2. Verify local X and Z are 0-2048
3. Ensure world file name matches tile coordinates
4. Confirm UIDs in world files reference correct TDB sections
5. Use `OpenRailsLog.txt` for coordinate warnings

## References

- Open Rails source: `Orts.Simulation/Simulation/Traveller.cs`
- Track positioning logic in `TrackViewer`
