# Quick Start Guide - Curve Fitter

## Running the Tool

### Option 1: Full Workflow (Recommended)
```bash
cd C:\Users\jared\main\openrails\Tools\curve-fitter
python main.py
```
This runs the complete pipeline:
1. Extract primitives from GeoJSON
2. Export to C# project
3. Build C# solution
4. Run TdbDump.exe

### Option 2: Extract Only
```bash
python extract_primitives.py
```
Just extract primitives to `primitives.json` without C# integration.

## Configuration

Edit `config.py` to customize:

```python
GEOJSON_FILE = 'your_railroad_data.geojson'  # Input file
TARGET_OBJECTID = 1909                       # Which segment to process
STRAIGHT_TOLERANCE = 0.1                     # Line tolerance (meters)
CIRCLE_TOLERANCE = 0.5                       # Arc tolerance (meters)
PRIMITIVES_OUTPUT = 'primitives.json'        # Output file
MAX_STRAIGHT_LENGTH = 2048                   # Tile size limit (meters)
FLIP_X_COORDINATES = True                    # Coordinate transformation
```

## Output

### primitives.json
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

This JSON is automatically:
- Saved locally to `primitives.json`
- Exported to C# TdbDump project
- Used to build track geometry

## File Overview

| File | Purpose |
|------|---------|
| **main.py** | Entry point - orchestrates extraction + C# build |
| **extract_primitives.py** | Core extraction logic - can be used standalone |
| **circle_fitter.py** | Circle/line fitting algorithms - pure math |
| **config.py** | Configuration parameters |
| **README.md** | Full documentation |
| **REFACTORING_NOTES.md** | Architecture explanation |

## Troubleshooting

### C# Build Fails
- Check that `C:\Users\jared\main\openrails\Source\TdbDump\` exists
- Ensure .NET SDK is installed: `dotnet --version`
- Try running main.py with admin privileges

### Extraction Only
- Run `python extract_primitives.py` instead
- This skips the C# build entirely

### Configuration Issues
- Edit `config.py` to point to correct GeoJSON file
- Verify `TARGET_OBJECTID` exists in the GeoJSON
- Adjust tolerances for different fitting strategies

## Architecture

```
main.py (what you run)
  ↓
  extract_primitives()
    ├─ Load GeoJSON
    ├─ Convert coordinates
    ├─ Segment polyline (straight + curves)
    ├─ Split long straights
    └─ Export JSON
  ↓
  build_and_run_tdbdump()
    ├─ Write to C# project
    ├─ dotnet build
    └─ Run TdbDump.exe
  ↓
Complete!
```

## Performance

- Typical railroad segments: **0.5-2 seconds**
- C# build: **5-15 seconds** (depends on system)
- TdbDump execution: **1-5 seconds**

## Next Steps

1. **Prepare GeoJSON** - Get railroad network data
2. **Configure** - Edit `config.py` with your data file and target ID
3. **Run** - `python main.py`
4. **Check Output** - Review `primitives.json`
5. **Track Building** - Use primitives in Open Rails TrackBuilder

Enjoy building! 🛤️
