# Glossary

Key terminology used throughout Open Rails track building.

## Terms

### A

**AX, AY, AZ** (Euler Angles)
- Rotation angles around X, Y, Z axes
- AX = pitch (forward/back tilt)
- AY = yaw/heading (direction facing)
- AZ = roll (banking/side tilt)
- Measured in radians

### C

**Consist**
- A train composition (locomotives and cars)
- Defined in `.con` files
- References shapes from TRAINSET folder

**Curve Fitter**
- Python tool that converts curve definitions to track geometry
- Takes curve radius, length, elevation as input
- Outputs position and rotation for each track section

### D

**DynTrack** (Dynamic Track)
- Track geometry defined in world files (`.w`)
- Contrasts with static track from shape files
- Contains multiple TrackSections with curve data

**DynTrackObj**
- Object in world file representing dynamic track
- Contains position, orientation (quaternion), and track sections
- Referenced by VDbId

### E

**Euler Angles**
- See AX, AY, AZ

**End Node**
- See TrEndNode

### P

**Path**
- Sequence of waypoints (TrackPDPs) defining a route
- Stored in `.pat` files
- Links waypoints using TrPathNodes

**PathID**
- Reference to a path file (without extension)
- Used in services and activities
- Example: `PathID ( MainLine )` → loads `PATHS/MainLine.pat`

**Pin**
- Connection between two track nodes
- Contains: target node ID and direction
- Stored in `TrPin` structure

**Player_Service_Definition**
- In activity files, specifies which service to use
- Service name used to find `.srv` file
- Service contains path and consist information

### Q

**Quaternion** (Q-rotation)
- Normalized rotation representation (Qx, Qy, Qz, Qw)
- Used in world files for track orientation
- More efficient than Euler angles

### R

**Route**
- Base scenario in Open Rails
- Contains TDB, paths, world files, services, activities
- Located in `ROUTES/[RouteName]/`

### S

**Section** / **Track Section**
- Smallest unit of track geometry
- Has radius, length, elevation change
- References shape via SectionIndex

**SectionIndex**
- Reference number to a track section definition
- Standard sections: 40000+
- Dynamic sections: 50000+

**Service**
- Links a path and consist together
- Stored in `.srv` file
- Can have schedule information

**Shape**
- 3D model file for train consist
- Located in `TRAINS/TRAINSET/[ShapeName]/`
- Has `.s` and `.sd` files

### T

**TDB** (Track Database)
- Master database of all track geometry
- File: `tdb.dat`
- Contains track nodes and pin connections

**TrEndNode**
- Track node marking start or end of segment
- Has 1 input, 0 output pins
- References a UID

**TrJunctionNode**
- Track node where track splits or merges
- Typically 3 pins (1 input, 2 outputs for Y-junction)
- References a UID

**TrPin**
- Connection between track nodes
- Contains: node ID and direction (0 or 1)
- Must be reciprocal

**TrVectorNode**
- Track node containing curved sections
- Has 1 input, 1 output pins
- Contains multiple TrVectorSections

**TrVectorSection**
- Individual track section within a TrVectorNode
- Contains position, rotation, curve data
- References SectionIndex and WorldFileUiD

### U

**UID** (Universal ID)
- 12-element unique identifier
- Stores position and orientation
- Used to reference track in world files

**UiD**
- Short form of UID

### V

**VDbId**
- References TDB node from world file
- Format: `( NodeID 0 0 )`
- Links DynTrackObj to track database

### W

**World File** (`.w`)
- Contains 3D geometry and objects for one tile
- File name: `w-[TileX]+[TileZ].w`
- Contains DynTrackObj entries for track

**WorldFileUiD**
- In TrVectorSection, references world file object
- Usually matches node ID
- Used to link track database to rendered geometry

## Acronyms

| Acronym | Meaning |
|---------|---------|
| TDB | Track Database |
| STF | SIMISA Text Format |
| PAT | Path/Track file |
| CON | Consist file |
| SRV | Service file |
| ACT | Activity file |
| UID | Universal ID |
| VDbId | Virtual Database ID |
| AX/AY/AZ | Euler Angles (X/Y/Z) |
| Qx/Qy/Qz/Qw | Quaternion components |

## File Extensions

| Extension | Purpose | Location |
|-----------|---------|----------|
| `.tdb` | Track database | `ROUTES/*/tdb.dat` |
| `.pat` | Path/waypoints | `ROUTES/*/PATHS/*.pat` |
| `.w` | World geometry | `ROUTES/*/WORLD/w-*.w` |
| `.srv` | Service definition | `ROUTES/*/SERVICES/*.srv` |
| `.act` | Activity scenario | `ROUTES/*/ACTIVITIES/*.act` |
| `.con` | Consist/train | `TRAINS/CONSISTS/*.con` |
| `.s` | Shape geometry | `TRAINS/TRAINSET/*/.s` |
| `.sd` | Shape descriptor | `TRAINS/TRAINSET/*/.sd` |

## Folder Structure

```
ROUTES/
├── [RouteName]/
│   ├── tdb.dat              (Track database)
│   ├── PATHS/
│   │   └── *.pat            (Path files)
│   ├── WORLD/
│   │   └── w-*.w            (World files by tile)
│   ├── SERVICES/
│   │   └── *.srv            (Service definitions)
│   └── ACTIVITIES/
│       └── *.act            (Activity scenarios)

TRAINS/
├── CONSISTS/
│   └── *.con                (Consist definitions)
└── TRAINSET/
    └── [ShapeName]/
        ├── *.s              (Shape geometry)
        └── *.sd             (Shape descriptor)
```

## Common Patterns

### Activity Loading Chain

```
Activity (.act)
  ↓
Player_Service_Definition name → Service.srv
  ├─ PathID → Path.pat
  └─ TrainConfig → Consist.con
```

### Track Display Chain

```
World File (.w)
  ├─ VDbId → TDB Node ID
  └─ TrackSections → TrVectorSections in TDB
      ├─ SectionIndex → tsection.dat
      └─ WorldFileUiD → Matching VDbId
```

### Coordinate Hierarchy

```
Tile (e.g., -12842, 14734)
  ├─ Local X, Z (0-2048)
  ├─ Elevation Y
  └─ Rotation (AX, AY, AZ or Quaternion)
```

## Concepts to Understand

Before building tracks, understand:

1. **Pin Semantics** - How nodes connect
2. **Coordinate Systems** - Tiles vs. local coordinates
3. **File Dependencies** - How files reference each other
4. **Track Sections** - Curve data and geometry
5. **UIDs** - How objects are referenced

See [Concepts](../concepts/) section for details.
