# Curve Fitter Overview

The Python Curve Fitter is the first step in the pipeline. It **reverse-engineers railroad curves** from real-world coordinate data (GeoJSON), fitting them into straight lines and circular arcs suitable for Open Rails.

## Purpose

The curve fitter takes **existing railroad coordinates** (latitude/longitude) and:

- Converts coordinates to local Cartesian (UTM) system
- Fits straight lines and circular arcs to the coordinate sequence
- Determines curve parameters (radius, angle, direction)
- Produces **primitives** (straight/curve definitions) for track building
- Handles long straights by splitting at tile boundaries

## Input

The curve fitter reads from **GeoJSON railroad network files** containing:

- **Real railroad coordinates**: lat/lon polyline data
- **ObjectID**: Which railroad segment to process
- **Configuration** (config.py):
  - `STRAIGHT_TOLERANCE`: RMS error tolerance for line fitting (meters)
  - `CIRCLE_TOLERANCE`: RMS error tolerance for circle fitting (meters)
  - `FLIP_X_COORDINATES`: Mirror the coordinate system if needed
  - Input/output file paths

See [Curve Input Format](curve_input.md) for details.

## Output

The curve fitter produces **primitives** - simplified representations:

- **Straight Primitive**: `{"type": "straight", "radius": 0.0, "angle": length, "length": meters}`
- **Curve Primitive**: `{"type": "curve", "radius": meters, "angle": radians, "clockwise": bool}`

These primitives are exported to `primitives.json` for TdbDump conversion.

See [Curve Output Format](curve_output.md) for details.

## How It Works

### Step 1: Load and Convert Coordinates

Read GeoJSON file with railroad lat/lon coordinates and convert to local Cartesian meters (using UTM projection).

### Step 2: Segmentation (Model Selection)

For each sequence of points:
1. **Try straight line fit** - Uses PCA (Principal Component Analysis) to fit a line
2. **Try circular arc fit** - Uses Taubin's least-squares method to fit a circle
3. **Select winner** - Chooses the model that covers more points within tolerance
4. **Grow segment** - Incrementally adds points until error exceeds tolerance

### Step 3: Arc Parameter Calculation

For curved segments:
- Calculate center point and radius from circle fit
- Compute sweep angle (total rotation)
- Determine direction (clockwise vs counter-clockwise)
- Generate curve primitive

### Step 4: Handle Long Straights

Split any straight section exceeding 2048m (Open Rails tile limit) into multiple primitives.

### Step 5: Export Primitives

Output `primitives.json` with all segments ready for TdbDump.

## Example Workflow

```
Real railroad GeoJSON
├─ Coordinates: (lat1, lon1), (lat2, lon2), ..., (latN, lonN)
│
├─ Convert to Cartesian meters (UTM)
│
├─ Fit segments:
│  ├─ Points 0-15: Fit line (straight) → RMS error 0.2m
│  ├─ Points 15-32: Fit circle (curve, R=500m) → RMS error 0.5m
│  └─ Points 32-48: Fit line (straight) → RMS error 0.3m
│
└─ Output primitives.json
   ├─ Straight primitive (0-15)
   ├─ Curve primitive (15-32, R=500m)
   └─ Straight primitive (32-48)
```

## Algorithms Used

### 1. PCA Line Fitting
- Finds principal component (direction of least variance)
- Centers on centroid of points
- Calculates perpendicular distances (RMS error)

### 2. Taubin Circle Fitting
- Least-squares optimization to minimize radial distance
- Finds center point and radius
- Calculates RMS radial error

### 3. Model Selection
- Fits both models to each growing point sequence
- Compares RMS errors against configured tolerances
- Selects model covering more points within tolerance
- Guarantees every vertex is covered by exactly one segment

## Integration with TdbDump

The `primitives.json` output is consumed by TdbDump to:
- Calculate world coordinates and rotations
- Generate TDB track database entries
- Create path waypoints
- Produce world geometry files

See [TdbDump Overview](tdbdump_overview.md) for the next step.
