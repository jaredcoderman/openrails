# Curve Input Format

The curve fitter reads **GeoJSON files containing real railroad network coordinates**.

## Input Source

GeoJSON file format with railroad polylines:

```json
{
  "type": "FeatureCollection",
  "features": [
    {
      "type": "Feature",
      "properties": {
        "OBJECTID": 12345,
        "railroad_name": "BNSF Main Line"
      },
      "geometry": {
        "type": "LineString",
        "coordinates": [
          [-120.5, 38.5],
          [-120.501, 38.501],
          [-120.502, 38.502],
          ...more coordinates...
        ]
      }
    }
  ]
}
```

**Coordinates format**: `[longitude, latitude]` pairs (standard GeoJSON)

## Configuration (config.py)

Before running, configure these parameters:

| Parameter | Type | Description |
|-----------|------|-------------|
| `GEOJSON_FILE` | str | Path to input GeoJSON file |
| `TARGET_OBJECTID` | int | Which railroad segment to process (OBJECTID value) |
| `STRAIGHT_TOLERANCE` | float | RMS perpendicular error tolerance for line fitting (meters) |
| `CIRCLE_TOLERANCE` | float | RMS radial error tolerance for circle fitting (meters) |
| `FLIP_X_COORDINATES` | bool | Mirror X-coordinates for local coordinate system (true/false) |
| `PRIMITIVES_OUTPUT` | str | Output JSON file path for primitives |

### Tolerance Values

These determine how "tight" the fitting is:

- **Smaller values** (e.g., 0.5m) = More segments, closer to original
- **Larger values** (e.g., 2.0m) = Fewer segments, more generalized

Example config:

```python
GEOJSON_FILE = r"C:\data\railroad_network.geojson"
TARGET_OBJECTID = 12345
STRAIGHT_TOLERANCE = 1.0  # 1 meter RMS error for lines
CIRCLE_TOLERANCE = 1.5    # 1.5 meter RMS error for curves
FLIP_X_COORDINATES = False
PRIMITIVES_OUTPUT = "primitives.json"
```

## Coordinate System

### Input Coordinates
- **Format**: Latitude/Longitude (WGS84)
- **Projection**: Automatically detected based on first coordinate
- **UTM Zone**: Calculated from longitude

### Internal Conversion
The fitter converts to **local Cartesian (UTM)** for processing:
- All distances in meters
- Local X-Y coordinate system
- Allows precise distance/angle calculations

### Optional X-Flip
If your coordinate system needs mirroring:
```python
FLIP_X_COORDINATES = True  # Negates all X values
```

## Data Quality Notes

For best results, the GeoJSON railroad data should:
- Have sufficient point density (points every ~10-50 meters)
- Follow actual track geometry closely
- Include both straight sections and curves
- Be free of large jumps or missing segments
- Represent a continuous path

## Example: Real Railroad Data

A typical railroad polyline with 50 points representing 2km of track:

```json
{
  "properties": {"OBJECTID": 1, "name": "Test Track"},
  "geometry": {
    "type": "LineString",
    "coordinates": [
      [-122.500, 47.650],
      [-122.501, 47.651],
      [-122.502, 47.652],
      [-122.503, 47.653],
      [-122.504, 47.654],
      ...46 more points...
    ]
  }
}
```

After conversion to UTM and fitting:
- Segments 0-15 fit to straight line
- Segments 15-35 fit to circular arc (R=500m)
- Segments 35-50 fit to straight line

## Running the Fitter

```bash
cd Tools\curve-fitter
python extract_primitives.py
```

Or use the venv:

```bash
cd Tools\curve-fitter
.\Scripts\Activate.ps1
python extract_primitives.py
```

This reads config.py settings and processes the GeoJSON file.

## Output Location

Primitives are written to the path specified in config.py:

```python
PRIMITIVES_OUTPUT = "primitives.json"  # Relative to current directory
```

See [Curve Output Format](curve_output.md) for what the output contains.
