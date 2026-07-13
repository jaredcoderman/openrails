# Consist Files (.con) Format

Consist files define train compositions - which locomotives and cars make up a train.

## Overview

- **Format**: SIMISA Text Format (STF)
- **Location**: `TRAINS/CONSISTS/[ConsistName].con`
- **Purpose**: Defines train makeup (engine type, number of cars, cargo)
- **Used by**: Services, activities, and multiplayer

## File Structure

```
SIMISA@@@@@@@@@@JINX0C0t______

Consist (
    Name ( "Consist Name" )
    Engine ( Filename
        UiD ( 1 )
    )
    ...wagon entries...
)
```

## Basic Components

### Engine (Locomotive)

```
Engine ( AEM7
    UiD ( 1 )
)
```

| Parameter | Description |
|-----------|-------------|
| `Filename` | Locomotive shape filename (no extension) |
| `UiD` | Unique ID (usually 1 for first engine) |

### Wagon/Car

```
Wagon ( Box
    UiD ( 2 )
)
```

Multiple wagons can be added in sequence.

## Example: Simple Consist

```
SIMISA@@@@@@@@@@JINX0C0t______

Consist (
    Name ( "Steam Train" )
    Engine ( LNER_A4
        UiD ( 1 )
    )
    Wagon ( Coach_Passenger_1920
        UiD ( 2 )
    )
    Wagon ( Coach_Passenger_1920
        UiD ( 3 )
    )
    Wagon ( Coach_Passenger_1920
        UiD ( 4 )
    )
)
```

This creates:
1. LNER A4 steam locomotive
2. Three passenger coaches

## Complex Consist Example

```
SIMISA@@@@@@@@@@JINX0C0t______

Consist (
    Name ( "Freight Manifest" )
    Engine ( BNSF_Dash9
        UiD ( 1 )
    )
    Engine ( BNSF_Dash9
        UiD ( 2 )
    )
    Wagon ( Box_Car
        UiD ( 3 )
    )
    Wagon ( Box_Car
        UiD ( 4 )
    )
    Wagon ( Gondola
        UiD ( 5 )
    )
    Wagon ( Gondola
        UiD ( 6 )
    )
    Wagon ( Tank_Car
        UiD ( 7 )
    )
    Wagon ( Caboose
        UiD ( 8 )
    )
)
```

This creates:
1. Two BNSF Dash 9 diesel locomotives (double-headed)
2. Two box cars
3. Two gondola cars
4. One tank car
5. One caboose

## Shape References

The filenames (e.g., `LNER_A4`, `Box_Car`) refer to shape definitions.

Shape files are typically located in:
```
TRAINS/
├── TRAINSET/
│   ├── LNER_A4/
│   │   ├── LNER_A4.sd  (shape descriptor)
│   │   └── LNER_A4.s   (shape geometry)
│   └── Box_Car/
│       ├── Box_Car.sd
│       └── Box_Car.s
```

## Integration

When an activity uses this consist:

```
Activity (.act)
    │
    ├─ Player_Service_Definition ( MyService )
    │  └─> SERVICES/MyService.srv
    │      └─> TrainConfig ( Steam_Train )
    │          └─> TRAINS/CONSISTS/Steam_Train.con (this file)
    │              └─> References AEM7, Coach_Passenger_1920
    │                  └─> Loads from TRAINS/TRAINSET/
```

## Notes

- **Order matters**: Wagons appear in sequence from front to rear
- **UIDs should be unique** within the consist
- **Shape names** must exist in TRAINSET folder or game will crash
- **Multiple engines** are allowed (double-headers, helpers, etc.)

## Common Issues

### "Train shape not found"
The filename in the consist doesn't match a shape in TRAINSET folder.

### "Consist not found"
The consist file doesn't exist or wrong filename referenced in service.

### "Train appears as generic model"
Consist is valid but shape rendering failed.

## Reading Reference

See source: `Orts.Formats.Msts/ConsistFile.cs` or `TRAINS.DAT`
