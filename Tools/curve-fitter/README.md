# Curve Fitter Tool

Python utility for converting railroad polylines into fitting primitives (straight lines and circular arcs) suitable for Open Rails track building.

## Overview

This tool processes GeoJSON railroad network data and segments polylines into optimal straight and curved sections, exporting the results as JSON primitives for use in the C# TrackBuilder component.

## Files

- **`main.py`** - Entry point. Orchestrates primitive extraction + C# build/run
- **`extract_primitives.py`** - Core extraction logic. Importable for reuse
- **`circle_fitter.py`** - Core algorithms for coordinate conversion, circle/line fitting, and segmentation
- **`config.py`** - Centralized configuration (tolerances, file paths, parameters)

## Usage

```bash
# Main workflow - extract primitives AND run C# TdbDump
python main.py

# Or extract primitives only (without C# integration)
python extract_primitives.py
```

**`main.py`** - Recommended entry point. Orchestrates:
1. Primitive extraction from GeoJSON
2. Export to C# TdbDump project
3. Build C# project
4. Run TdbDump.exe

**`extract_primitives.py`** - Can be used standalone to just extract primitives without C# integration. Also importable as a module.

Configuration is managed in `config.py`:
- `GEOJSON_FILE` - Input GeoJSON file path
- `TARGET_OBJECTID` - Which railroad segment to process
- `STRAIGHT_TOLERANCE` - RMS perpendicular error tolerance for lines (meters)
- `CIRCLE_TOLERANCE` - RMS radial error tolerance for curves (meters)
- `PRIMITIVES_OUTPUT` - Output JSON file for primitives

## Pipeline

1. **Load GeoJSON** - Reads railroad network data
2. **Coordinate Conversion** - Transforms lat/lon to local Cartesian (UTM)
3. **Segmentation** - Model-selection algorithm:
   - Attempts straight line fit
   - Attempts circular arc fit
   - Selects the model covering more points within tolerance
4. **Long Straight Splitting** - Breaks straights exceeding 2048m tile limit
5. **Primitive Extraction** - Generates unified primitive format for C#
6. **JSON Export** - Outputs to `primitives.json`

## Architecture

```
main.py (Orchestrator)
  ├─ extract_primitives() [extract_primitives.py]
  │  ├─ Load GeoJSON
  │  ├─ Coordinate conversion [circle_fitter.py]
  │  ├─ Segmentation [circle_fitter.py]
  │  └─ Return export_data
  │
  └─ build_and_run_tdbdump(export_data)
     ├─ Export to C# project
     ├─ Build C# solution
     └─ Run TdbDump.exe
```

**Separation of Concerns:**
- `extract_primitives.py` - Focuses purely on curve fitting and primitive extraction
- `main.py` - Orchestrates the workflow and C# integration
- Both can be used independently

## Algorithm Details

### Model Selection Segmentation

For each segment position, the algorithm:

1. **Tries straight line fit** - PCA-based fit, grows incrementally by adding points until RMS error exceeds tolerance
2. **Tries circular arc fit** - Least-squares circle fitting, grows similarly
3. **Selects winner** - The model covering more points within tolerance wins
4. **Robustness** - Guarantees every vertex is covered by exactly one segment

### Primitive Format

**Straight Segment:**
```json
{
  "type": "straight",
  "radius": 0.0,
  "angle": 2048.5,
  "clockwise": false,
  "length": 2048.5
}
```

**Curve Segment:**
```json
{
  "type": "curve",
  "radius": 500.0,
  "angle": 0.785398,
  "clockwise": true
}
```

## Output

Generates `primitives.json` with segments suitable for C# TrackBuilder:

```json
{
  "segments": [
    {
      "type": "straight",
      "radius": 0.0,
      "angle": 1500.25,
      "clockwise": false,
      "length": 1500.25
    },
    {
      "type": "curve",
      "radius": 450.0,
      "angle": 0.5236,
      "clockwise": false
    }
  ]
}
```

## Dependencies

- numpy
- scipy
- pyproj (for coordinate transformation)

## Notes

- All coordinates are in meters
- X coordinates can be flipped for local coordinate system adjustment (see `FLIP_X_COORDINATES` in config)
- Long straights are automatically split to respect Open Rails tile limits (2048m)
- Circles with radius >100km or sweep >180° are rejected and treated as straights
