# Curve Fitter Usage Examples

The curve fitter processes real railroad GeoJSON data and extracts curve/straight primitives.

## Workflow Overview

```
GeoJSON file (railroad coordinates)
        ↓
Configure config.py (tolerances, file path)
        ↓
Run extract_primitives.py
        ↓
primitives.json (straight/curve segments)
        ↓
TdbDump processes primitives
```

## Setup

### 1. Activate Virtual Environment

```bash
cd Tools\curve-fitter
.\Scripts\Activate.ps1
```

You should see `(curve-fitter)` in your prompt.

### 2. Prepare GeoJSON File

Ensure you have a GeoJSON file with railroad network data (lat/lon coordinates).

### 3. Configure config.py

Edit `Tools/curve-fitter/config.py`:

```python
GEOJSON_FILE = r"C:\path\to\railroad_data.geojson"
TARGET_OBJECTID = 12345  # Which railroad segment to extract
STRAIGHT_TOLERANCE = 1.0    # Meters RMS error for lines
CIRCLE_TOLERANCE = 1.5      # Meters RMS error for curves
FLIP_X_COORDINATES = False
PRIMITIVES_OUTPUT = "primitives.json"
```

### 4. Run the Fitter

```bash
python extract_primitives.py
```

Or to run the full pipeline (including TdbDump):

```bash
python main.py
```

## Example: Simple Railroad

**GeoJSON input** (3000m track with straight + curve sections):

```json
{
  "features": [{
    "properties": {"OBJECTID": 1},
    "geometry": {
      "type": "LineString",
      "coordinates": [
        [-122.500, 47.650],
        [-122.501, 47.651],
        ... 50 points ...
        [-122.530, 47.680]
      ]
    }
  }]
}
```

**Configuration:**

```python
TARGET_OBJECTID = 1
STRAIGHT_TOLERANCE = 1.0
CIRCLE_TOLERANCE = 1.5
```

**Output** (`primitives.json`):

```json
{
  "segments": [
    {
      "type": "straight",
      "radius": 0.0,
      "angle": 500.0,
      "length": 500.0,
      "clockwise": false
    },
    {
      "type": "curve",
      "radius": 450.0,
      "angle": 0.785398,
      "length": 353.5,
      "clockwise": true
    },
    {
      "type": "straight",
      "radius": 0.0,
      "angle": 1000.0,
      "length": 1000.0,
      "clockwise": false
    }
  ]
}
```

This means:
- 500m straight section
- 45-degree curve (π/4 radians) with 450m radius, turning right
- 1000m straight section

## Example: Long Straight (Tile Boundary Handling)

**Input:** 2500m straight (exceeds 2048m tile limit)

**Curve fitter output:**

```json
{
  "segments": [
    {
      "type": "straight",
      "length": 2048.0
    },
    {
      "type": "straight",
      "length": 452.0
    }
  ]
}
```

Automatically split into two segments to respect tile boundaries.

## Example: Complex Track

**Multi-element track:** straight → right curve → straight → left curve → straight

```json
{
  "segments": [
    {"type": "straight", "length": 400},
    {"type": "curve", "radius": 500, "angle": 0.5236, "clockwise": true},
    {"type": "straight", "length": 600},
    {"type": "curve", "radius": 600, "angle": 0.4189, "clockwise": false},
    {"type": "straight", "length": 300}
  ]
}
```

Visual representation:
```
Straight 400m
     ↓
  ╭─ Curve (R=500m, 30°)
  │
Straight 600m
     ↓
  ╰─ Curve (R=600m, 24°, left)
  │
Straight 300m
```

## Understanding Tolerance Parameters

### STRAIGHT_TOLERANCE = 0.5 (Tight)
- Fits straights closely to original data
- More segments, higher precision
- Slower TdbDump processing

### STRAIGHT_TOLERANCE = 2.0 (Loose)
- Generalized straights
- Fewer segments, faster processing
- May miss small deviations

**Recommendation:** Start with 1.0-1.5 meters for realistic track.

## Troubleshooting

### "Object ID not found in GeoJSON"
- Check TARGET_OBJECTID matches actual OBJECTID in data
- Verify GeoJSON file is valid

### "Too many/few segments generated"
- Adjust STRAIGHT_TOLERANCE and CIRCLE_TOLERANCE
- Lower tolerance = more segments
- Higher tolerance = fewer segments

### "Coordinates don't look right"
- Check FLIP_X_COORDINATES setting
- Verify GeoJSON uses standard [lon, lat] order
- Ensure sufficient point density in source data

## Next Steps

Once primitives.json is generated:

1. TdbDump reads it
2. Calculates world coordinates
3. Generates TDB, .w, and .pat files
4. Copy to route folder
5. Load in Open Rails!

See [TdbDump Overview](tdbdump_overview.md) for details.
