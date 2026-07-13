# Full Pipeline Walkthrough

Follow real railroad data from GeoJSON through to loading in Open Rails.

## Step-by-Step Example

### 1. Prepare GeoJSON Railroad Data

You have a GeoJSON file with real railroad coordinates (`railroad_data.geojson`):

```json
{
  "type": "FeatureCollection",
  "features": [{
    "type": "Feature",
    "properties": {"OBJECTID": 1, "name": "Test Track"},
    "geometry": {
      "type": "LineString",
      "coordinates": [
        [-122.500, 47.650],
        [-122.501, 47.651],
        [-122.502, 47.652],
        [-122.503, 47.653],
        [-122.504, 47.654],
        [-122.505, 47.655],
        ... 50+ more coordinate pairs representing ~2km of track ...
      ]
    }
  }]
}
```

This represents real railroad coordinates that we'll fit to straight lines and curves.

### 2. Configure Curve Fitter

Edit `Tools/curve-fitter/config.py`:

```python
GEOJSON_FILE = r"C:\data\railroad_data.geojson"
TARGET_OBJECTID = 1
STRAIGHT_TOLERANCE = 1.0  # 1 meter RMS error for line fitting
CIRCLE_TOLERANCE = 1.5    # 1.5 meter RMS error for circle fitting
FLIP_X_COORDINATES = False
PRIMITIVES_OUTPUT = "primitives.json"
```

### 3. Run Curve Fitter

```bash
cd Tools\curve-fitter
.\Scripts\Activate.ps1
python extract_primitives.py
```

The curve fitter:
1. Converts lat/lon to local Cartesian (UTM) coordinates in meters
2. Fits straight lines using PCA
3. Fits circular arcs using Taubin's method
4. Selects best fit (straight or curve) for each segment
5. Produces `primitives.json`

**Output** (`primitives.json`):

```json
{
  "segments": [
    {
      "type": "straight",
      "radius": 0.0,
      "angle": 500.0,
      "clockwise": false,
      "length": 500.0
    },
    {
      "type": "curve",
      "radius": 450.0,
      "angle": 0.785398,
      "clockwise": true,
      "length": 353.5
    },
    {
      "type": "straight",
      "radius": 0.0,
      "angle": 1000.0,
      "clockwise": false,
      "length": 1000.0
    }
  ]
}
```

This means:
- 500m straight section
- 45-degree curve (π/4 radians) with 450m radius turning right
- 1000m straight section

### 4. Run TdbDump

```bash
cd Source\TdbDump
dotnet run
```

TdbDump reads `primitives.json` and:
1. **Loads** the primitives
2. **Calculates** world coordinates and rotations
3. **Builds** TDB node structure (TrackBuilder)
4. **Writes** three files:
   - `track.tdb` (track database with TrVectorSections)
   - `w-012842+014734.w` (world geometry)
   - `track.pat` (path waypoints)

### 5. Generated TDB Content

`track.tdb` contains:

```
trackdb (
    tracknodes ( 3
        tracknode ( 1
            uid ( 0 0 0 0 0 0 0 0 0 0 0 0 )
            trendnode ( )
            trpins ( 1 0
                TrPin ( 2 1 )
            )
        )
        tracknode ( 2
            trvectornode (
                trvectorsections ( 3
                    50001 -12842 14734 0 100 0 0 0 0 -12842 14734 2
                    50002 -12842 14734 500 100 0 0 0.785398 0 -12842 14734 2
                    50003 -12842 14734 1000 150 500 0 1.5707963 0 -12842 14734 2
                )
                tritemrefs ( 0 )
            )
            trpins ( 1 1
                TrPin ( 1 0 )
                TrPin ( 3 1 )
            )
        )
        tracknode ( 3
            uid ( 0 0 0 0 0 0 0 0 0 0 0 0 )
            trendnode ( )
            trpins ( 1 0
                TrPin ( 2 0 )
            )
        )
    )
    tritemtable ( 0 )
)
```

Three nodes:
- Node 1: Start TrEndNode
- Node 2: TrVectorNode with 3 TrVectorSections (one per primitive)
- Node 3: End TrEndNode

### 6. Generated World File

`w-012842+014734.w` contains DynTrack objects:

```
Dyntrack (
    Tr_WorldFile (
        Serial ( 1 )
        DyntrackObj (
            SectionIdx ( 50001 )
            Elevation ( 100 )
            CollideFlags ( 7 )
            Position ( 0 100 0 )
            QDirection ( 0 0 0 1 )
            VDbId ( 2 0 0 )
            TrackSections ( 3
                TrackSection ( 50001 -12842 14734 0 100 0 0 0 0 0 0 0 0 0 0 )
                TrackSection ( 50002 -12842 14734 500 100 0 0 0.785398 0 0 0 0 0 0 0 )
                TrackSection ( 50003 -12842 14734 1000 150 500 0 1.5707963 0 0 0 0 0 0 0 )
            )
        )
    )
)
```

DynTrackObj references Node 2 via VDbId, containing all 3 track sections.

### 7. Generated Path File

`track.pat` contains waypoints for each primitive endpoint:

```
TrackPDPs (
    TrackPDP ( -12842 14734 0 100 0 2 0 )
    TrackPDP ( -12842 14734 500 100 0 2 0 )
    TrackPDP ( -12842 14734 1000 150 500 2 0 )
)

TrackPath (
    TrPathName ( TestTrack )
    Name ( "Test Track" )
    TrPathStart ( Start )
    TrPathEnd ( End )
    TrPathNodes ( 3
        TrPathNode ( 00000000 0 1 4294967295 )
        TrPathNode ( 00000000 1 2 4294967295 )
        TrPathNode ( 00000000 2 4294967295 4294967295 )
    )
)
```

### 8. Copy to Route

```
ROUTES/BNSF_Scenic/
├── tdb.dat                          ← Copy/merge track.tdb
├── PATHS/
│   └── TestTrack.pat               ← Copy track.pat
└── WORLD/
    └── w-012842+014734.w           ← Copy .w file
```

### 9. Create Service File

Create `ROUTES/BNSF_Scenic/SERVICES/TestService.srv`:

```
SIMISA@@@@@@@@@@JINX0S0t______

Service (
    Serial ( 1 )
    Name ( "Test Service" )
    PathID ( TestTrack )
    TrainConfig ( BNSF_Manifest )
)
```

### 10. Create Activity File

Create `ROUTES/BNSF_Scenic/ACTIVITIES/TestActivity.act`:

```
SIMISA@@@@@@@@@@JINX0a0t______

Tr_Activity (
    Serial ( 1 )
    Tr_Activity_Header (
        RouteID ( BNSF_Scenic )
        Name ( "Test Activity" )
        Description ( "Testing generated track" )
        StartTime ( 9 0 0 )
        Season ( 1 )
        Weather ( 0 )
        PathID ( TestTrack )
    )
    Tr_Activity_File (
        Player_Service_Definition ( TestService
            Player_Traffic_Definition ( 79200 )
        )
        NextServiceUID ( 1 )
        NextActivityObjectUID ( 32768 )
        Events ( )
    )
)
```

### 11. Load in Open Rails

```
1. Launch Open Rails
2. Select route: BNSF_Scenic
3. Select activity: TestActivity
4. Click "Start"
5. Train should load on your generated track
6. Use controls to move along the track
```

## Data Flow Diagram

```
Real railroad GeoJSON
    │ (lat/lon coordinates)
    ↓
Curve Fitter (Python)
    │ • Convert coords to Cartesian
    │ • Fit lines and circles
    │ • Select best primitives
    ↓
primitives.json
    │ (radius, angle, clockwise)
    ↓
TdbDump (C#)
    │ • Calculate world coordinates
    │ • Build track nodes
    │ • Write TDB structure
    ↓
    ├─ track.tdb (TrVectorSections with calcs)
    ├─ w-012842+014734.w (DynTrackObj)
    └─ track.pat (TrackPDP waypoints)
    ↓
Copy to route
    ↓
    ├─ Services/TestService.srv
    ├─ Activities/TestActivity.act
    └─ Consists (existing)
    ↓
Open Rails loads
    │
    ↓
Track appears in game
```

## File Dependencies

```
Activity (.act)
    ├─ RouteID ──→ Route folder
    │
    └─ Player_Service_Definition ( TestService )
        ├─ Loads SERVICES/TestService.srv
        │   ├─ PathID ( TestTrack ) ──→ PATHS/TestTrack.pat
        │   │   └─ TrackPDPs match TDB coordinates
        │   └─ TrainConfig ( BNSF_Manifest ) ──→ TRAINS/CONSISTS/BNSF_Manifest.con
```

## What Curve Fitter Does (Not)

**Does:**
- ✓ Read real railroad GeoJSON coordinates
- ✓ Fit straight lines using PCA
- ✓ Fit circular arcs using Taubin method
- ✓ Output primitives (radius + angle)

**Does NOT:**
- ✗ Calculate world coordinates
- ✗ Generate arbitrary track data
- ✗ Accept manual curve definitions
- ✗ Output position/rotation data

(TdbDump does the coordinate calculation)

## Verification Checklist

- [ ] GeoJSON file has sufficient coordinate density
- [ ] config.py points to correct file and OBJECTID
- [ ] primitives.json generated successfully
- [ ] TdbDump runs without errors
- [ ] Generated .tdb, .pat, .w files exist
- [ ] Files copied to correct route folders
- [ ] Service and activity files created
- [ ] Activity loads without crashes
- [ ] Track visible in game
- [ ] Train can move along track

## Common Issues

| Problem | Cause | Fix |
|---------|-------|-----|
| "No primitives generated" | Wrong OBJECTID or bad GeoJSON | Verify config.py settings |
| "TdbDump crashes" | Missing primitives.json | Run curve fitter first |
| "Track not visible" | Wrong tile or coordinates | Verify world file name matches |
| "Train off track" | Path waypoints misaligned | Check TrackPDP coordinates in .pat |
| "Activity won't load" | Missing service/consist | Create Service.srv with valid consist |
| "Wrong track geometry" | Bad tolerance settings | Adjust STRAIGHT_TOLERANCE value |

## Next Steps

- Try with different railroad data
- Experiment with tolerance values
- Add multiple track segments
- Create more complex track layouts
- Build comprehensive railroad networks
