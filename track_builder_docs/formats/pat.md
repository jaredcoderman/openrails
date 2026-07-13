# Path Files (.pat) Format

Path files define the route that trains follow, consisting of waypoints and navigation nodes.

## Overview

- **Format**: SIMISA Text Format (STF)
- **Location**: `ROUTES/[RouteID]/PATHS/[PathName].pat`
- **Purpose**: Defines waypoints and links for train navigation
- **Used by**: Activities, AI trains, and player trains

## File Structure

```
SIMISA@@@@@@@@@@JINX0P0t______

Serial ( 1 )

TrackPDPs (
    TrackPDP ( [tileix] [tiliz] [x] [y] [z] [flag1] [flag2] )
    ...waypoints...
)

TrackPath (
    TrPathName ( [name] )
    ...properties...
    TrPathNodes ( [count]
        TrPathNode ( [flags] [pdp_index] [next_main] [next_siding] )
        ...links...
    )
)
```

## TrackPDP (Track Path Departure Point)

Defines a waypoint in the path.

```
TrackPDP ( TileX TileZ X Y Z Flag1 Flag2 )
```

| Parameter | Type | Description |
|-----------|------|-------------|
| `TileX`, `TileZ` | int | Tile coordinates |
| `X` | float | Position X within tile |
| `Y` | float | Elevation (height) |
| `Z` | float | Position Z within tile |
| `Flag1` | int | Path flags (usually 2) |
| `Flag2` | int | Reserved/unused (usually 0) |

### Coordinate System

- **TileX, TileZ**: World tile coordinates (same as TDB)
- **X**: East-West position within tile (0-2048)
- **Z**: North-South position within tile (0-2048)
- **Y**: Elevation in meters

### Flags

| Value | Meaning |
|-------|---------|
| 0 | Start of path |
| 1 | Waypoint |
| 2 | Regular waypoint |

## TrPathNode (Track Path Node)

Links waypoints together, forming the path.

```
TrPathNode ( Flags PDPIndex NextMainNode NextSidingNode )
```

| Parameter | Type | Description |
|-----------|------|-------------|
| `Flags` | hex | Flags (usually 00000000) |
| `PDPIndex` | int | Index into TrackPDPs array (0-based) |
| `NextMainNode` | uint | Index to next node on main path; 0xFFFFFFFF (-1) for end |
| `NextSidingNode` | uint | Index to siding path; 0xFFFFFFFF (-1) for none |

## TrackPath Properties

```
TrackPath (
    TrPathName ( TestTrack )
    Name ( "Test Track Name" )
    TrPathStart ( StartLocation )
    TrPathEnd ( EndLocation )
    TrPathNodes ( count
        ...node definitions...
    )
)
```

| Property | Type | Description |
|----------|------|-------------|
| `TrPathName` | string | Internal path identifier |
| `Name` | string | Display name |
| `TrPathStart` | string | Starting location name |
| `TrPathEnd` | string | Ending location name |
| `TrPathNodes` | array | Array of path nodes |

## Example: Simple Linear Path

```
SIMISA@@@@@@@@@@JINX0P0t______

Serial ( 1 )

TrackPDPs (
    TrackPDP ( -12842 14734 0.0 100.0 0.0 2 0 )
    TrackPDP ( -12842 14734 500.0 100.0 100.0 2 0 )
    TrackPDP ( -12842 14734 1000.0 100.0 200.0 2 0 )
    TrackPDP ( -12842 14734 1500.0 100.0 300.0 2 0 )
)

TrackPath (
    TrPathName ( TestTrack )
    Name ( "Test Track" )
    TrPathStart ( Start )
    TrPathEnd ( End )
    TrPathNodes ( 4
        TrPathNode ( 00000000 0 1 4294967295 )
        TrPathNode ( 00000000 1 2 4294967295 )
        TrPathNode ( 00000000 2 3 4294967295 )
        TrPathNode ( 00000000 3 4294967295 4294967295 )
    )
)
```

This creates a path with 4 waypoints linked in sequence.

## Path with Siding

```
TrackPDPs (
    TrackPDP ( -12842 14734 0.0 100.0 0.0 2 0 )
    TrackPDP ( -12842 14734 100.0 100.0 50.0 2 0 )
    TrackPDP ( -12842 14734 100.0 100.0 100.0 2 0 )
    TrackPDP ( -12842 14734 200.0 100.0 150.0 2 0 )
)

TrackPath (
    TrPathName ( MainLine )
    TrPathNodes ( 4
        TrPathNode ( 00000000 0 1 2 )          # Node 0: next=1 (main), siding=2
        TrPathNode ( 00000000 1 3 4294967295 ) # Node 1: next=3 (main), siding=none
        TrPathNode ( 00000000 2 4294967295 4294967295 ) # Node 2: siding end
        TrPathNode ( 00000000 3 4294967295 4294967295 ) # Node 3: main path end
    )
)
```

## Path Flags

The `Flags` field in `TrPathNode` can contain:

```
0x00 - Normal waypoint
0x20 - Player/human path
0x40 - AI path
0x80 - Avoid at start (skip for initial placement)
```

Example with flags:
```
TrPathNode ( 00000020 0 1 4294967295 )  # Player path waypoint
```

## Coordinate Translation

When converting from TDB to PAT:

1. For each TrVectorSection in TDB
2. Create a TrackPDP using its tile and position:
   ```
   TrackPDP ( section.TileX section.TileZ section.X section.Y section.Z 2 0 )
   ```
3. Link waypoints sequentially in TrPathNodes

## Integration with Activities

Activity files reference path by name:

```
PathID ( TestTrack )
```

Open Rails looks for: `PATHS/TestTrack.pat`

Inside, it reads the `TrPathName` and path nodes.

## Validation

Open Rails validates:
- [ ] All PDPIndex values reference valid TrackPDPs
- [ ] NextMainNode/NextSidingNode are valid or 0xFFFFFFFF
- [ ] Path forms a valid chain (no infinite loops)
- [ ] Starting node is accessible

## Troubleshooting

### "Train starting position off path"

The train's initial position doesn't align with path waypoints.

**Fix**: Ensure first TrackPDP is at train's start position.

### "Path endpoints not found"

**Fix**: Verify `TrPathEnd` index points to final node (0xFFFFFFFF as next).

## Reading Reference

See source: `Orts.Formats.Msts/PathFile.cs`
