# Activity Files (.act) Format

Activity files define scenarios in Open Rails, specifying the route, player service, path, and objectives.

## Overview

- **Format**: SIMISA Text Format (STF)
- **Location**: `ROUTES/[RouteID]/ACTIVITIES/[ActivityName].act`
- **Purpose**: Defines a playable scenario with goals and conditions
- **Contains**: Route, train service, start position, weather, and events

## File Structure

```
SIMISA@@@@@@@@@@JINX0a0t______

Tr_Activity (
    Serial ( 1 )
    Tr_Activity_Header (
        ...header properties...
    )
    Tr_Activity_File (
        ...activity properties...
        Events ( ...objective definitions... )
    )
)
```

## Tr_Activity_Header

High-level activity information displayed in menus.

```
Tr_Activity_Header (
    RouteID ( BNSF_Scenic )
    Name ( "Test Track Activity" )
    Description ( "A simple test of the track system" )
    Briefing ( "This is a test activity for verifying track generation" )
    CompleteActivity ( 1 )
    Type ( 0 )
    Mode ( 2 )
    StartTime ( 9 0 0 )
    Season ( 1 )
    Weather ( 0 )
    PathID ( TestTrack )
    StartingSpeed ( 0 )
    Duration ( 4 0 )
    Difficulty ( 0 )
    FuelWater ( 100 )
    FuelCoal ( 100 )
    FuelDiesel ( 100 )
)
```

### Header Parameters

| Property | Type | Values | Description |
|----------|------|--------|-------------|
| `RouteID` | string | Route folder name | Which route to load |
| `Name` | string | Any text | Activity display name |
| `Description` | string | Any text | Short description |
| `Briefing` | string | Any text | Detailed briefing text |
| `CompleteActivity` | int | 0/1 | Whether activity can be completed |
| `Type` | int | 0=Train ride, 1=???, 2=??? | Activity type |
| `Mode` | int | 0=Introductory, 2=Player, 3=Tutorial | Game mode |
| `StartTime` | time | Hour Minute Second | Starting time of day |
| `Season` | int | 0=Spring, 1=Summer, 2=Autumn, 3=Winter | Season |
| `Weather` | int | 0=Clear, 1=Snow, 2=Rain | Weather condition |
| `PathID` | string | Path filename | Starting path (mostly ignored) |
| `StartingSpeed` | int | 0-50 | Train initial speed (mph) |
| `Duration` | time | Hour Minute | Max activity duration |
| `Difficulty` | int | 0=Easy, 1=Medium, 2=Hard | Activity difficulty |

## Tr_Activity_File

Detailed activity configuration.

```
Tr_Activity_File (
    Player_Service_Definition ( ServiceName
        Player_Traffic_Definition ( 79200 )
        UiD ( 0 )
    )
    NextServiceUID ( 2 )
    NextActivityObjectUID ( 32768 )
    ORTSAIHornAtCrossings ( 1 )
    ORTSAICrossingHornPattern ( US )
    Traffic_Definition ( 1_AITraffic
        Service_Definition ( "AI Service" 74300
            UiD ( 1 )
        )
    )
    Events (
        ...event definitions...
    )
    ActivityRestrictedSpeedZones (
        ...speed restrictions...
    )
)
```

### Player_Service_Definition

Links to the service file (which contains path and consist):

```
Player_Service_Definition ( MustComeDown
    Player_Traffic_Definition ( 79200 )
    UiD ( 0 )
)
```

The name (`MustComeDown`) is used to find: `SERVICES/MustComeDown.srv`

That `.srv` file specifies:
- `PathID` (which `.pat` file)
- `TrainConfig` (which `.con` consist)

## Events

Define objectives and completion conditions.

### AllStops Event

Complete activity when train stops at all stations:

```
EventCategoryAction (
    EventTypeAllStops ( )
    ID ( 0 )
    Activation_Level ( 1 )
    Outcomes (
        ActivitySuccess ( )
    )
    Name ( "Stop at all stations" )
)
```

### Location Event

Complete activity when reaching a location:

```
EventCategoryLocation (
    EventTypeLocation ( )
    ID ( 1 )
    Activation_Level ( 1 )
    Outcomes (
        ActivitySuccess ( )
    )
    Name ( "Reach destination" )
    Location ( TileX TileZ X Y Z )
    TriggerOnStop ( 1 )
)
```

Location format: `( TileX TileZ X Y Z )`

## Time Parameters

### StartTime & Duration

24-hour format:

```
StartTime ( 9 30 0 )    # 9:30:00 AM
Duration ( 2 15 )       # 2 hours 15 minutes
```

## Season & Weather

### Season Values
- `0` = Spring (green grass, flowers)
- `1` = Summer (green, lush)
- `2` = Autumn (browns, oranges)
- `3` = Winter (white snow, bare trees)

### Weather Values
- `0` = Clear (sunny)
- `1` = Snow (precipitation, white)
- `2` = Rain (precipitation, wet)

## Example: Minimal Activity

```
SIMISA@@@@@@@@@@JINX0a0t______

Tr_Activity (
    Serial ( 1 )
    Tr_Activity_Header (
        RouteID ( BNSF_Scenic )
        Name ( "Simple Test" )
        Description ( "Test track loading" )
        Briefing ( "This is a test" )
        CompleteActivity ( 1 )
        Type ( 0 )
        Mode ( 2 )
        StartTime ( 9 0 0 )
        Season ( 1 )
        Weather ( 0 )
        PathID ( TestTrack )
        StartingSpeed ( 0 )
        Duration ( 1 0 )
        Difficulty ( 0 )
        FuelWater ( 100 )
        FuelCoal ( 100 )
        FuelDiesel ( 100 )
    )
    Tr_Activity_File (
        Player_Service_Definition ( SimpleService
            Player_Traffic_Definition ( 79200 )
            UiD ( 0 )
        )
        NextServiceUID ( 1 )
        NextActivityObjectUID ( 32768 )
        Events ( )
    )
)
```

## File Reference Chain

```
Activity File (.act)
    │
    ├─ RouteID ( BNSF_Scenic )
    │  └─> Loads route from ROUTES/BNSF_Scenic/
    │
    ├─ Player_Service_Definition ( MyService )
    │  └─> Loads SERVICES/MyService.srv
    │      ├─> PathID ( MainTrack )
    │      │   └─> Loads PATHS/MainTrack.pat
    │      └─> TrainConfig ( Consist )
    │          └─> Loads TRAINS/CONSISTS/Consist.con
    │
    └─ Events
       └─> Location references ( TileX TileZ X Y Z )
           └─> Must match track coordinates in TDB
```

## Common Issues

### "Activity not found"
The `.act` file doesn't exist or is in wrong location.

### "Route cannot be loaded"
RouteID doesn't match a route folder.

### "Player Service Definition not found"
Player_Service_Definition name doesn't match any `.srv` file.

### "Train starting position off path"
Starting location doesn't align with path waypoints.

### "Activity crashes on load"
Typically caused by missing service file or invalid consist reference.

## Reading Reference

See source: `Orts.Formats.Msts/ActivityFile.cs`
