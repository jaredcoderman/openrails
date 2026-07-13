# Service Files (.srv) Format

Service files define train services, linking consists, paths, and schedule information.

## Overview

- **Format**: SIMISA Text Format (STF)
- **Location**: `ROUTES/[RouteID]/SERVICES/[ServiceName].srv`
- **Purpose**: Defines train consist, path, and schedule
- **Used by**: Activities and AI traffic

## File Structure

```
SIMISA@@@@@@@@@@JINX0S0t______

Service (
    Serial ( 1 )
    Name ( "Service Name" )
    PathID ( path_name )
    TrainConfig ( consist_name )
    ...schedule items...
)
```

## Service Properties

```
Service (
    Serial ( 1 )
    Name ( DisplayName )
    PathID ( PathFileName )
    TrainConfig ( ConsistFileName )
    StartTime ( 8 30 0 )
    Efficiency ( 0.9 )
    Reliability ( 0.99 )
)
```

| Property | Type | Description |
|----------|------|-------------|
| `Serial` | int | Version number (usually 1) |
| `Name` | string | Display name for service |
| `PathID` | string | Filename of `.pat` file (no extension) |
| `TrainConfig` | string | Filename of `.con` consist file (no extension) |
| `StartTime` | time | Initial departure time (Hour Minute Second) |
| `Efficiency` | float | Engine efficiency factor (0-1) |
| `Reliability` | float | Engine reliability (0-1) |

## PathID

Links to a path file:

```
PathID ( TestTrack )
```

Open Rails loads: `PATHS/TestTrack.pat`

## TrainConfig

Links to a consist (train composition) file:

```
TrainConfig ( BNSF_Manifest )
```

Open Rails loads: `TRAINS/CONSISTS/BNSF_Manifest.con`

## Example Service File

```
SIMISA@@@@@@@@@@JINX0S0t______

Service (
    Serial ( 1 )
    Name ( "Express Passenger Service" )
    PathID ( MainLine )
    TrainConfig ( Passenger_Consist )
    StartTime ( 9 0 0 )
    Efficiency ( 0.95 )
    Reliability ( 0.98 )
    Preference ( 100 )
)
```

## Key Points

### PathID Links

The `PathID` in `.srv` is what Open Rails actually uses, not the `PathID` in the `.act` file!

```
Activity file PathID ( TestTrack ) ─┐
                                     ├─ Ignored, not used
                                     │
Service file    PathID ( RealPath ) ◄─ Actually loaded
```

### Consist Reference

The `TrainConfig` value determines which train (locomotive + cars) is used.

Example file structure:
```
ROUTES/MyRoute/
├── SERVICES/
│   └── MyService.srv          # PathID ( MainPath )
│                              # TrainConfig ( SteamLoco )
├── PATHS/
│   └── MainPath.pat           # Used from .srv
└── TRAINS/CONSISTS/
    └── SteamLoco.con          # The consist definition
```

## Schedule Items (Optional)

Services can include schedule points for stations:

```
Service (
    ...
    Consists (
        Consist (
            Name ( Engine )
        )
    )
    Starts (
        Start (
            Time ( 9 0 0 )
            Location ( StartPoint )
        )
    )
)
```

## Creating a Simple Service

For a basic test track:

```
SIMISA@@@@@@@@@@JINX0S0t______

Service (
    Serial ( 1 )
    Name ( "Test Service" )
    PathID ( TestTrack )
    TrainConfig ( TestTrain )
)
```

This creates a service that:
1. Uses `PATHS/TestTrack.pat` for the route
2. Uses `TRAINS/CONSISTS/TestTrain.con` for the consist
3. Can be referenced in `.act` files by the service name

## Integration Flow

```
Activity File (.act)
    │
    ├─ Player_Service_Definition ( MyService )
    │
    ├─> Loads SERVICES/MyService.srv
    │
    ├─> From .srv, reads:
    │   ├─ PathID ( MainTrack )
    │   │  └─> Loads PATHS/MainTrack.pat
    │   │
    │   └─ TrainConfig ( TestTrain )
    │      └─> Loads TRAINS/CONSISTS/TestTrain.con
    │
    └─> Now ready to start activity
```

## Validation

Open Rails checks:
- [ ] PathID file exists in PATHS folder
- [ ] TrainConfig file exists in TRAINS/CONSISTS folder
- [ ] StartTime is valid (0-23 hours, 0-59 minutes/seconds)

## Error Messages

If something fails:

| Error | Cause |
|-------|-------|
| "Path file not found" | PathID references non-existent `.pat` |
| "Train consist not found" | TrainConfig references non-existent `.con` |
| "Invalid service definition" | Syntax error in `.srv` file |

## Reading Reference

See source: `Orts.Formats.Msts/ServiceFile.cs`
