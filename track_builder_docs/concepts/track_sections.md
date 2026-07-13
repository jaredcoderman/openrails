# Track Sections

Track sections are the fundamental building blocks of track geometry.

## Overview

A track section defines:
- Curve radius
- Length
- Elevation change
- Shape file reference

## Track Sections in TDB

In the `.tdb` file, sections are listed in `TrVectorSection`:

```
TrVectorSection (
    SectionIndex        X Y Z AX AY AZ WFNameX WFNameZ WorldFileUiD
)
```

The `SectionIndex` references an entry in `tsection.dat` (track sections file).

## SectionIndex Values

### Standard MSTS Sections

Standard sections typically start at **40000** and increment:
- 40001 = Straight horizontal
- 40002 = Curved horizontal
- 40003 = Curved upgrade
- etc.

### Dynamic Sections

Dynamic sections created by tools like TdbDump often use higher values:
- 50001 = First dynamic section
- 50002 = Second dynamic section
- etc.

## TrackSection Definition

In `tsection.dat`, a section defines:

```
TrackSection ( 40001
    Curved ( 0 )           # 0 = not curved, 1 = curved
    Length ( 100 )         # Length in meters
    Curve ( 0 0 0 )        # Radius, angle, height data
)
```

## Section Properties

| Property | Type | Description |
|----------|------|-------------|
| `Curved` | int | 0=straight, 1=curved |
| `Length` | float | Distance along section (meters) |
| `Radius` | float | Curve radius (0 for straight) |
| `Angle` | float | Arc angle (radians) |
| `Elevation` | float | Height change (meters) |

## Straight Section

```
TrackSection ( 40001
    Curved ( 0 )
    Length ( 100 )
    Curve ( 0 0 0 )     # No curve
)
```

Used when:
- Radius = 0
- No heading change needed
- No banking

## Curved Section

```
TrackSection ( 40002
    Curved ( 1 )
    Length ( 314.159 )         # Arc length
    Curve ( 500 1.5707963 0 )  # Radius 500m, angle π/2 (90°)
)
```

Used when:
- Radius ≠ 0
- Heading changes
- Creates smooth turn

## Grade Section (Upgrade)

```
TrackSection ( 40003
    Curved ( 0 )
    Length ( 200 )
    Curve ( 0 0 100 )   # Height change = 100m
)
```

Elevation change while maintaining straight horizontal track.

## Super-Elevation (Banking)

Banking reduces track lean during curves. Defined by:
- Curve radius
- Speed through curve
- Banking angle (AZ angle)

```
SuperElevation ( 500 60 0.1 )  # Radius, speed (mph), banking
```

## Curve Calculations

### Arc Length Formula
For a curve with radius R and angle θ (radians):

```
Length = R × θ
```

**Example:** 90° turn (π/2 radians) on 500m radius:
```
Length = 500 × π/2 ≈ 785m
```

### Heading Change
Heading changes by the arc angle:

```
NewHeading = OldHeading + angle
```

**Example:** Initially heading East (0), turn 90° left (π/2):
```
NewHeading = 0 + π/2 = π/2 (now heading North)
```

## Position Change in Curve

When traversing a curved section:

```csharp
float arcRadius = abs(radius);
float dHeading = length / arcRadius;

// Calculate displacement
float dx = arcRadius * sin(dHeading) * cos(heading);
float dz = arcRadius * sin(dHeading) * sin(heading);

// Update position
x += dx;
z += dz;
heading += dHeading;
```

## Section Index Ranges

Different tools use different ranges:

| Range | Source | Notes |
|-------|--------|-------|
| 40000-40999 | MSTS standard | Built-in sections |
| 50000-59999 | User-generated | Custom/dynamic tracks |
| 60000+ | Reserved | Advanced formats |

## Linking Sections

Sections link together in sequence:

```
Section 50001: Straight, length 100m
    ↓ (position updated)
Section 50002: Curve right (R=500m), length 314m
    ↓ (position + heading updated)
Section 50003: Straight, length 200m
    ↓
...end of track
```

## In TrVectorNode

All sections for a track segment go into one `TrVectorNode`:

```
TrVectorNode (
    TrVectorSections ( 3
        50001 -12842 14734 0 0 0 0 0 0 -12842 14734 2
        50002 -12842 14734 100 0 0 0 1.5707 0 -12842 14734 2
        50003 -12843 14734 50 0 314 0 1.5707 0 -12843 14734 2
    )
)
```

Each line represents the state at that section's start point.

## Validation

When Open Rails loads sections:
- [ ] SectionIndex exists in `tsection.dat`
- [ ] Length > 0
- [ ] Radius ≥ 0 (negative would be invalid)
- [ ] Angle within reasonable range
- [ ] Sections are contiguous (connect properly)

## Common Issues

### "Invalid track section index"
SectionIndex doesn't exist in `tsection.dat`.

### "Track disconnected"
Sections don't connect properly (position jump between sections).

### "Wrong track geometry"
Radius/angle calculations incorrect.

## References

- MSTS documentation
- Open Rails source: `Orts.Simulation/Simulation/Traveller.cs`
