# README - Track Builder Documentation

Complete guide for the Open Rails track building pipeline.

## Overview

This documentation covers the complete process of generating Open Rails track data:

1. **Curve Fitter** (Python) - Mathematical track definitions
2. **TdbDump** (C#/.NET) - Convert to Open Rails formats  
3. **Integration** - Load into Open Rails

## Quick Links

- **Getting Started**: [Quick Start Guide](quick_start.md)
- **Full Example**: [Pipeline Walkthrough](pipeline/full_walkthrough.md)
- **Having Problems**: [Troubleshooting Guide](troubleshooting.md)
- **Need a Definition**: [Glossary](glossary.md)

## Documentation Structure

### Pipeline
- [Curve Fitter Overview](pipeline/curve_fitter_overview.md) - Input and purpose
- [Curve Input Format](pipeline/curve_input.md) - How to define curves
- [Curve Output Format](pipeline/curve_output.md) - What the fitter produces
- [TdbDump Overview](pipeline/tdbdump_overview.md) - C# tool overview
- [TdbDump Architecture](pipeline/tdbdump_architecture.md) - Deep technical dive
- [TrackBuilder Details](pipeline/trackbuilder.md) - Node structure generation
- [Writers Details](pipeline/writers.md) - File output generation

### File Formats
- [Track Database (.tdb)](formats/tdb.md) - Main track geometry
- [Path Files (.pat)](formats/pat.md) - Navigation waypoints
- [World Files (.w)](formats/world.md) - 3D geometry rendering
- [Service Files (.srv)](formats/srv.md) - Service definitions
- [Activity Files (.act)](formats/act.md) - Playable scenarios
- [Consist Files (.con)](formats/consist.md) - Train compositions

### Concepts
- [Coordinate Systems](concepts/coordinates.md) - Tiles and positions
- [Pin Connections](concepts/pins.md) - How nodes link
- [Track Sections](concepts/track_sections.md) - Curve geometry
- [UIDs and References](concepts/uids.md) - Object identification

### Deep Dives
- [Pin Semantics](deep_dives/pin_semantics.md) - Understanding pins
- [Quaternions](deep_dives/quaternions.md) - Rotation math

### Reference
- [Troubleshooting](troubleshooting.md) - Common issues and fixes
- [Glossary](glossary.md) - Terminology

## Key Concepts

Before starting, understand:

1. **Tiles**: World is divided into 2048×2048m tiles
   - Identified by (TileX, TileZ)
   - Local coordinates within tile: (0-2048, 0-2048)
   - See [Coordinate Systems](concepts/coordinates.md)

2. **Pins**: Track nodes connect via pins
   - Direction specifies *linked node's* side, not current node
   - All connections must be reciprocal
   - See [Pin Connections](concepts/pins.md)

3. **Files Reference Each Other**:
   - Activity (.act) → Service (.srv) → Path (.pat) + Consist (.con)
   - See [Full Walkthrough](pipeline/full_walkthrough.md)

4. **Track Structure**:
   - Start TrEndNode → TrVectorNode (all sections) → End TrEndNode
   - See [Track Database](formats/tdb.md)

## First Steps

1. Read [Quick Start](quick_start.md)
2. Follow [Full Walkthrough](pipeline/full_walkthrough.md) with a simple example
3. Create curves.json with your track definition
4. Run curve fitter
5. Run TdbDump
6. Copy files to route
7. Test in Open Rails
8. If problems, check [Troubleshooting](troubleshooting.md)

## Building Your First Track

### Minimal Track Example

`curves.json`:
```json
{
  "base_tile_x": -12842,
  "base_tile_z": 14734,
  "curves": [
    {"radius": 0, "length": 500, "elevation_change": 0}
  ]
}
```

This creates a single 500m straight section.

### Files to Copy to Route

```
ROUTES/MyRoute/
├── tdb.dat                   ← Generated track.tdb
├── PATHS/
│   └── TestTrack.pat        ← Generated track.pat
├── WORLD/
│   └── w-012842+014734.w    ← Generated .w file
├── SERVICES/
│   └── TestService.srv      ← Create this manually
└── ACTIVITIES/
    └── TestActivity.act     ← Create this manually
```

### File Templates

**TestService.srv**:
```
SIMISA@@@@@@@@@@JINX0S0t______

Service (
    Serial ( 1 )
    Name ( "Test Service" )
    PathID ( TestTrack )
    TrainConfig ( BNSF_Manifest )  ← Existing consist
)
```

**TestActivity.act**:
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

## Troubleshooting Tips

### Track Not Visible
1. Check [Troubleshooting - Track Not Visible](troubleshooting.md#track-not-visible-in-game)
2. Verify tile names match coordinates
3. Check OpenRailsLog.txt for errors

### Pin Errors
1. See [Troubleshooting - Pin Connection Errors](troubleshooting.md#pin-connection-errors)
2. Read [Pin Connections](concepts/pins.md)
3. Verify pins are reciprocal

### Activity Won't Load
1. Check [Troubleshooting - Activity Won't Load](troubleshooting.md#activity-wont-load)
2. Verify service and consist exist
3. Check syntax in .act file

## File Format Overview

| File | Purpose | Location |
|------|---------|----------|
| `.tdb` | Track geometry | `tdb.dat` (route root) |
| `.pat` | Path waypoints | `PATHS/*.pat` |
| `.w` | World geometry | `WORLD/w-*.w` |
| `.srv` | Service definition | `SERVICES/*.srv` |
| `.act` | Activity scenario | `ACTIVITIES/*.act` |
| `.con` | Train consist | `TRAINS/CONSISTS/*.con` |

See [File Formats](formats/tdb.md) for complete details.

## Common Tasks

### Add a Curve to Track
See [Curve Input Format](pipeline/curve_input.md)

### Change Weather/Season
See [Activity Files](formats/act.md#season--weather)

### Add Grade/Elevation
See [Curve Input Format](pipeline/curve_input.md) - elevation_change parameter

### Create New Consist
See [Consist Files](formats/consist.md)

### Debug Pin Issues
See [Deep Dive: Pin Semantics](deep_dives/pin_semantics.md)

## Resources

- **Open Rails Source**: /Source/Orts.Formats.Msts/
- **TdbDump Tool**: /Source/TdbDump/
- **Curve Fitter**: /Program/CurveFitter/
- **Log File**: OpenRailsLog.txt (check this first for errors!)

## Key Files to Reference

When implementing:

- `TrackDatabaseFile.cs` - TDB structure validation
- `Traveller.cs` - Curve to position conversion
- `MapForm.cs` - Debug map rendering (pin validation)
- `WorldFile.cs` - World file parsing

## Getting Help

When asking questions, provide:
1. Relevant error from OpenRailsLog.txt
2. Exact error message
3. What you were trying to do
4. Simplified test case
5. Files involved

Check [Glossary](glossary.md) for term definitions.

## Quick Reference

**Tile Naming**: `w-012842+014734.w` (6-digit coordinates with +/- prefix)

**Common Values**:
- Base tile: (-12842, 14734)
- Tile size: 2048×2048 meters
- Straight section index: 40001
- Dynamic section index: 50001+
- No rotation: (0, 0, 0, 1) in quaternion

**Time Format**: `StartTime ( Hour Minute Second )` - 24-hour format

**Seasons**: 0=Spring, 1=Summer, 2=Autumn, 3=Winter

**Weather**: 0=Clear, 1=Snow, 2=Rain

## Next Steps

1. Work through [Quick Start](quick_start.md)
2. Try [Full Walkthrough](pipeline/full_walkthrough.md) example
3. Create your own track
4. Reference specific sections as needed
5. Use [Troubleshooting](troubleshooting.md) for problems

Good luck building!
