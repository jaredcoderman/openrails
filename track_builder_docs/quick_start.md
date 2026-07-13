# Quick Start Guide

Get track data from railroad coordinates to Open Rails in minutes!

## The Pipeline

```
Real railroad GeoJSON
        ↓
Curve Fitter (Python)
        ↓
primitives.json
        ↓
TdbDump (C#)
        ↓
.tdb, .pat, .w files
        ↓
Copy to route
        ↓
Load in Open Rails
```

## Step 1: Get Railroad Data

You need GeoJSON with real railroad coordinates (lat/lon).

**Example sources:**
- Survey data
- OpenStreetMap railway data
- Your own GPS traces
- Other mapping data

File format:
```json
{
  "type": "FeatureCollection",
  "features": [{
    "properties": {"OBJECTID": 1},
    "geometry": {
      "type": "LineString",
      "coordinates": [[-122.5, 47.65], [-122.501, 47.651], ...]
    }
  }]
}
```

## Step 2: Configure Curve Fitter

Edit `Tools/curve-fitter/config.py`:

```python
GEOJSON_FILE = r"C:\path\to\your\railroad_data.geojson"
TARGET_OBJECTID = 1  # Which segment to process
STRAIGHT_TOLERANCE = 1.0  # Fit tolerance in meters
CIRCLE_TOLERANCE = 1.5
```

## Step 3: Run Curve Fitter

```bash
cd Tools\curve-fitter
.\Scripts\Activate.ps1
python extract_primitives.py
```

Creates `primitives.json` with curve/straight segments.

## Step 4: Run TdbDump

```bash
cd Source\TdbDump
dotnet run -- generate
```

Generates:
- `.tdb` file (track database)
- `.pat` file (path waypoints)
- `.w` files (world geometry)

## Step 5: Integrate into Route

Copy files to your route:

```
ROUTES/MyRoute/
├── tdb.dat                   ← Generated .tdb
├── PATHS/
│   └── Track.pat            ← Generated .pat
└── WORLD/
    └── w-012842+014734.w    ← Generated .w
```

## Step 6: Create Service & Activity Files

**Service** (`ROUTES/MyRoute/SERVICES/Track.srv`):
```
SIMISA@@@@@@@@@@JINX0S0t______

Service (
    Serial ( 1 )
    Name ( "Track Service" )
    PathID ( Track )
    TrainConfig ( BNSF_Manifest )
)
```

**Activity** (`ROUTES/MyRoute/ACTIVITIES/TestActivity.act`):
```
SIMISA@@@@@@@@@@JINX0a0t______

Tr_Activity (
    Serial ( 1 )
    Tr_Activity_Header (
        RouteID ( MyRoute )
        Name ( "Test Track" )
        Description ( "Testing generated track" )
        StartTime ( 9 0 0 )
        Season ( 1 )
        Weather ( 0 )
        PathID ( Track )
    )
    Tr_Activity_File (
        Player_Service_Definition ( Track_Service
            Player_Traffic_Definition ( 79200 )
        )
        NextServiceUID ( 1 )
        NextActivityObjectUID ( 32768 )
        Events ( )
    )
)
```

## Step 7: Test in Open Rails

1. Launch Open Rails
2. Select your route
3. Select the activity
4. Click "Start"
5. Train should appear on your generated track!

## Key Concepts

- **Curve Fitter**: Takes real railroad coordinates, fits them to straight lines and circular arcs
- **TdbDump**: Converts primitives to Open Rails format with world coordinates
- **Primitives**: Simplified representations (radius, angle) not full position data

## Next Steps

- Learn more: [Full Pipeline Walkthrough](pipeline/full_walkthrough.md)
- Reference: [File Formats](formats/tdb.md)
- Debug: [Troubleshooting](troubleshooting.md)
- Understand: [Concepts](concepts/coordinates.md)
