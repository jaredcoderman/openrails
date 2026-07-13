# Track Database (.tdb) Format

The `.tdb` file is the core database of track geometry in Open Rails.

## Overview

- **Format**: SIMISA Text Format (STF)
- **Location**: `ROUTES/[RouteID]/tdb.dat`
- **Purpose**: Defines all track nodes, connections, and geometry
- **Structure**: Hierarchical blocks with properties

## File Structure

```
SIMISA@@@@@@@@@@JINX0t0t______

trackdb (
    tracknodes ( [count]
        tracknode ( [id]
            [node properties]
        )
        ...more nodes...
    )
    tritemtable ( [count] )
)
```

## Track Nodes

Each track node has an ID (1-based) and can be one of three types:

### 1. TrVectorNode (Vector/Curved Section)

Contains one or more track sections with curves and elevations.

```
tracknode ( 2
    trvectornode (
        trvectorsections ( 25
            50001 -12842 14734 0.0 100.0 0.0 0.0 0.0 0.0 -12842 14734 2
            50002 -12842 14734 100.0 100.0 0.0 0.0 1.5707 0.0 -12842 14734 2
            ...more sections...
        )
        tritemrefs ( 0 )
    )
    trpins ( 1 1
        TrPin ( 1 0 )
        TrPin ( 3 1 )
    )
)
```

**TrVectorSection Format:**
```
SectionIndex TileX TileZ X Y Z AX AY AZ WFNameX WFNameZ WorldFileUiD
```

| Field | Type | Description |
|-------|------|-------------|
| `SectionIndex` | int | Reference to track section in `tsection.dat` |
| `TileX`, `TileZ` | int | Tile coordinates |
| `X`, `Y`, `Z` | float | Position within tile |
| `AX`, `AY`, `AZ` | float | Euler angles (radians) - pitch, yaw, roll |
| `WFNameX`, `WFNameZ` | int | World file tile coordinates |
| `WorldFileUiD` | hex | Unique ID for world file reference |

### 2. TrJunctionNode (Junction)

Allows track to split into multiple paths.

```
tracknode ( 4
    trjunctionnode (
        trjunctiondata (
            uidcount ( 1 )
            uid ( 0 0 0 0 0 0 0 0 0 0 0 0 )
        )
    )
    trpins ( 3 1
        TrPin ( 3 0 )
        TrPin ( 5 1 )
        TrPin ( 6 1 )
    )
)
```

### 3. TrEndNode (Track Terminus)

Marks the beginning or end of a track segment.

```
tracknode ( 1
    uid ( 0 0 0 0 0 0 0 0 0 0 0 0 )
    trendnode ( )
    trpins ( 1 0
        TrPin ( 2 1 )
    )
)
```

## Pin Connections

Pins define how nodes connect to each other.

### trpins Block

```
trpins ( [inpins] [outpins]
    TrPin ( [node_id] [direction] )
    TrPin ( [node_id] [direction] )
)
```

| Parameter | Meaning |
|-----------|---------|
| `inpins` | Number of pins on side 0 (input side) |
| `outpins` | Number of pins on side 1 (output side) |
| `node_id` | ID of the linked node |
| `direction` | Which side of the linked node (0 or 1) |

### Pin Semantics

- **Pin.Direction** specifies which side of the *linked* node
- **trpins header** specifies sides of the *current* node

Example:
```
Node 1: TrPin ( 2 1 )  → "Link to node 2's output side"
Node 2: TrPin ( 1 0 )  → "Link to node 1's input side" ✓ Reciprocal
```

## UIDs (Universal IDs)

UIDs on end nodes and junctions contain coordinate information:

```
uid ( TileX TileZ X Y Z AX AY AZ WorldId 0 0 0 )
```

12-element array: tile coordinates, position, rotation angles, world ID, and padding.

**Example:**
```
uid ( -12842 14734 500 100 200 0 0 0 0 0 0 0 )
```

Vector nodes do NOT have UIDs - they reference back to world files via `WorldFileUiD`.

## Valid Topologies

### Simple Linear Track

```
TrEndNode (1) ←→ TrVectorNode (2) ←→ TrEndNode (3)
```

Pin structure:
- Node 1: `trpins ( 1 0 ); TrPin ( 2 1 )`
- Node 2: `trpins ( 1 1 ); TrPin ( 1 0 ); TrPin ( 3 1 )`
- Node 3: `trpins ( 1 0 ); TrPin ( 2 0 )`

### Junction (3-way)

```
        TrVectorNode (2)
               ↑
               │
TrVectorNode (1) → Junction (3) ← TrVectorNode (4)
```

## Validation

Open Rails validates:

1. **Pin Reciprocity**: Every pin connection must be bidirectional
2. **Pin Counts**: 
   - TrEndNode: 1 input, 0 output
   - TrVectorNode: 1 input, 1 output
   - TrJunctionNode: varies (typically 3 inputs, 1 output for Y-junctions)
3. **Node References**: Pin node IDs must reference existing nodes
4. **Direction Values**: Must be 0 or 1

## Common Issues

### "Route cannot be loaded"

Check:
- [ ] Pin connections are reciprocal
- [ ] All pin.Node IDs reference valid nodes
- [ ] End nodes are at start/end of track
- [ ] Vector node has 1 input, 1 output pin

### "Ignored invalid track node pin"

This warning appears when:
- Pin direction doesn't match expected side
- Node reference is out of bounds
- Vector node pins link directly to another vector node with mismatched structure

**Fix**: Ensure all vector nodes are grouped into single node, or add junction nodes.

## Example: Minimal Valid Track

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
                trvectorsections ( 1
                    50001 -12842 14734 0 0 0 0 0 0 -12842 14734 2
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

This defines a single straight track section with start and end nodes.

## Reading Reference

See source: `Orts.Formats.Msts/TrackDatabaseFile.cs`
