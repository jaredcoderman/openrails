# Curve Output Format

The curve fitter produces **primitives** - simplified representations of straight lines and curves fitted to the railroad coordinate data.

## Output Structure

```json
{
  "segments": [
    {
      "type": "straight",
      "radius": 0.0,
      "angle": 2048.5,
      "clockwise": false,
      "length": 2048.5
    },
    {
      "type": "curve",
      "radius": 500.0,
      "angle": 0.785398,
      "clockwise": true,
      "length": 392.7
    },
    {
      "type": "straight",
      "radius": 0.0,
      "angle": 1500.25,
      "clockwise": false,
      "length": 1500.25
    }
  ]
}
```

## Primitive Types

### Straight Primitive

```json
{
  "type": "straight",
  "radius": 0.0,
  "angle": 1500.25,
  "clockwise": false,
  "length": 1500.25
}
```

| Field | Type | Description |
|-------|------|-------------|
| `type` | string | Always "straight" |
| `radius` | float | Always 0.0 for straight |
| `angle` | float | Length along the straight section (meters) |
| `clockwise` | bool | False for straights |
| `length` | float | Same as angle - distance traveled (meters) |

### Curve Primitive

```json
{
  "type": "curve",
  "radius": 500.0,
  "angle": 0.785398,
  "clockwise": true,
  "length": 392.7
}
```

| Field | Type | Description |
|-------|------|-------------|
| `type` | string | Always "curve" |
| `radius` | float | Circle radius in meters |
| `angle` | float | Arc sweep angle in radians (0 to 2π) |
| `clockwise` | bool | True = right turn, False = left turn |
| `length` | float | Arc length traveled (radius × angle) |

## How Primitives Are Generated

### 1. Coordinate Conversion
Real railroad coordinates (lat/lon) → Local Cartesian (UTM) in meters

### 2. Model Fitting
For each segment:
- **Fit straight line** using PCA
- **Fit circular arc** using Taubin's method
- **Select winner** based on RMS error vs tolerance

### 3. Arc Parameter Calculation
For curves, compute:
- **Center point**: From least-squares circle fit
- **Radius**: Distance from center to points
- **Angles**: `arctan2()` from center to start/end points
- **Sweep angle**: Total rotation (radians)
- **Direction**: Clockwise if cross product < 0

### 4. Splitting Long Straights
Any straight > 2048m is split into multiple primitives (Open Rails tile limit)

## Example Workflow

**Input: GeoJSON railroad coordinates (50 points, ~2km)**

```
Point sequence: (lat1, lon1) → (lat2, lon2) → ... → (lat50, lon50)

Convert to Cartesian: (x1, y1) → (x2, y2) → ... → (x50, y50)

Fit segments:
├─ Points 0-15: Fit line → RMS error 0.3m ✓ (< 1.0m tolerance)
│  Output: {"type": "straight", "length": 500}
│
├─ Points 15-35: Fit circle (R=450m) → RMS error 0.8m ✓ (< 1.5m tolerance)
│  Output: {"type": "curve", "radius": 450, "angle": 0.524, "clockwise": true}
│
└─ Points 35-50: Fit line → RMS error 0.2m ✓ (< 1.0m tolerance)
   Output: {"type": "straight", "length": 300}

Final primitives.json:
[
  {"type": "straight", "length": 500},
  {"type": "curve", "radius": 450, "angle": 0.524, "clockwise": true},
  {"type": "straight", "length": 300}
]
```

## Key Differences from Input

| Aspect | Input | Output |
|--------|-------|--------|
| **Source** | Real coordinates (lat/lon) | Fitted primitives |
| **Detail level** | Per-vertex points | Generalized segments |
| **Position data** | Yes (full coordinates) | No (primitives only) |
| **World coords** | None | None (TdbDump adds these) |
| **Rotation data** | None | Curve angles only |

## What TdbDump Does Next

The primitives are consumed by TdbDump to:

1. **Calculate world coordinates**
   - Starting position (base tile + offset)
   - For each primitive, compute endpoint position

2. **Calculate rotations**
   - Starting heading (0° East)
   - For each primitive, update heading based on angle
   - Generate Euler angles (AX, AY, AZ)

3. **Generate TDB entries**
   - Create TrVectorSection for each primitive
   - Set SectionIndex, tile coordinates, position, rotation

4. **Generate world geometry**
   - Create DynTrackObj with primitives
   - Set position and quaternion rotation

5. **Generate path waypoints**
   - Create TrackPDP for each primitive endpoint

See [TdbDump Overview](tdbdump_overview.md) for the next step in the pipeline.
