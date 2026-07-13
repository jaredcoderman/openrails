# TrackBuilder Details

TrackBuilder is the core component that transforms input curve data into a valid Open Rails TDB structure.

## Overview

```csharp
public class TrackBuilder
{
    private List<TrackNode> _nodes;
    private int _tileX;
    private int _tileZ;
    private float _x, _y, _z;  // Current position
    private float _ax, _ay, _az;  // Current rotation
    
    public TrackNode[] Build();
}
```

## Key Methods

### BuildAllNodes()

Main method that generates the complete TDB structure.

```csharp
public void BuildAllNodes()
{
    // Step 1: Create start end nodes
    var startEndNode = new TrackNode 
    { 
        Id = 1,
        TrEndNode = new TrEndNode(),
        Pins = new List<TrPin> 
        { 
            new TrPin { Node = 2, Direction = 1 }
        }
    };
    _nodes.Add(startEndNode);
    
    // Step 2: Create single vector node with all sections
    var vectorNode = new TrackNode 
    { 
        Id = 2,
        TrVectorNode = true,
        Sections = new List<TrVectorSection>()
    };
    
    // Add all sections to this node
    foreach (var inputSection in _inputSections)
    {
        var section = new TrVectorSection
        {
            TileX = _tileX,
            TileZ = _tileZ,
            X = _x,
            Y = _y,
            Z = _z,
            AX = _ax,
            AY = _ay,
            AZ = _az,
            WorldFileUiD = (uint)2  // All sections share same UID
        };
        vectorNode.Sections.Add(section);
        
        // Update position for next section
        UpdatePosition(inputSection);
    }
    
    // Set vector node pins
    vectorNode.Pins = new List<TrPin>
    {
        new TrPin { Node = 1, Direction = 0 },  // Back to start
        new TrPin { Node = 3, Direction = 1 }   // Forward to end
    };
    _nodes.Add(vectorNode);
    
    // Step 3: Create end end node
    var endEndNode = new TrackNode
    {
        Id = 3,
        TrEndNode = new TrEndNode(),
        Pins = new List<TrPin>
        {
            new TrPin { Node = 2, Direction = 0 }
        }
    };
    _nodes.Add(endEndNode);
}
```

### UpdatePosition(Section section)

Updates accumulated position and rotation based on curve data.

```csharp
private void UpdatePosition(Section section)
{
    // Calculate heading change
    float dHeading = section.radius == 0 ? 0 : section.length / section.radius;
    
    // Update position based on current heading and curve
    if (section.radius == 0)
    {
        // Straight section
        _x += section.length * MathF.Cos(_ay);
        _z += section.length * MathF.Sin(_ay);
    }
    else
    {
        // Curved section
        float arcRadius = MathF.Abs(section.radius);
        float dx = arcRadius * MathF.Sin(dHeading) * MathF.Cos(_ay);
        float dz = arcRadius * MathF.Sin(dHeading) * MathF.Sin(_ay);
        
        _x += dx;
        _z += dz;
    }
    
    // Update elevation
    _y += section.elevation_change;
    
    // Update heading
    _ay += dHeading;
    
    // Handle tile boundaries
    HandleTileBoundary();
}
```

### HandleTileBoundary()

Adjusts tile coordinates when position crosses tile boundaries.

```csharp
private void HandleTileBoundary()
{
    const float TILE_SIZE = 2048f;
    
    // Check X boundary
    if (_x >= TILE_SIZE)
    {
        int tilesEast = (int)(_x / TILE_SIZE);
        _tileX += tilesEast;
        _x -= tilesEast * TILE_SIZE;
    }
    else if (_x < 0)
    {
        int tilesWest = (int)((-_x + TILE_SIZE - 1) / TILE_SIZE);
        _tileX -= tilesWest;
        _x += tilesWest * TILE_SIZE;
    }
    
    // Check Z boundary (same as X but for Z axis)
    if (_z >= TILE_SIZE)
    {
        int tilesSouth = (int)(_z / TILE_SIZE);
        _tileZ += tilesSouth;
        _z -= tilesSouth * TILE_SIZE;
    }
    else if (_z < 0)
    {
        int tilesNorth = (int)((-_z + TILE_SIZE - 1) / TILE_SIZE);
        _tileZ -= tilesNorth;
        _z += tilesNorth * TILE_SIZE;
    }
}
```

## Pin Connection Strategy

### Node Structure

```
┌─────────────┐
│ TrEndNode 1 │
└──────┬──────┘
       │ Pin: (2, 1) - connects to node 2's side 1
       ↓
┌─────────────────────────────┐
│ TrVectorNode 2 (all sections)│
│ - Section 0                 │
│ - Section 1                 │
│ - Section N                 │
└──────┬────────────┬─────────┘
       │            │
   Pin │            │ Pin
  (1,0)│            │(3,1)
       │            │
       ↓            ↓
                    TrEndNode 3
                    └─────────────┘
```

### Direction Semantics

- **Pin.Direction** = which side of the *linked* node
  - 0 = Input side of linked node
  - 1 = Output side of linked node

- **trpins header** = pins on sides of *current* node
  - `trpins ( 1 1 )` = 1 pin on side 0, 1 pin on side 1

### Reciprocal Connections

For any valid connection:
- If Node A pins to (Node B, direction D), then
- Node B must pin to (Node A, opposite direction)

Example:
- Node 1 → Pin (2, 1): "Connect to Node 2's output side"
- Node 2 → Pin (1, 0): "Connect to Node 1's input side" ✓ Reciprocal!

## Initialization

```csharp
public TrackBuilder(List<Section> sections, string outputRoute)
{
    _inputSections = sections;
    _nodes = new List<TrackNode>();
    
    // Start at base tile
    _tileX = -12842;
    _tileZ = 14734;
    
    // Start at origin within tile
    _x = 0;
    _y = 0;
    _z = 0;
    
    // Start facing east
    _ax = 0;
    _ay = 0;  // Radians, 0 = east
    _az = 0;
}
```

## Output

BuildAllNodes() produces a TrackNode array:

```
Index 0: null (Open Rails uses 1-based indexing)
Index 1: TrEndNode (start)
Index 2: TrVectorNode (all sections)
Index 3: TrEndNode (end)
```

This structure is then passed to writers:

```csharp
var builder = new TrackBuilder(sections);
var nodes = builder.BuildAllNodes();

tdbWriter.Write(nodes, "output.tdb");
worldWriter.Write(nodes, "world/");
pathWriter.Write(nodes, "output.pat");
```

## Testing

To verify correct track generation:

1. Check tile boundaries are crossed correctly
2. Verify pin connections are reciprocal
3. Validate position accumulates properly
4. Ensure rotations match curve data
5. Load in Open Rails to visually confirm

## Troubleshooting

### Track appears disconnected
- Check pin connections are reciprocal
- Verify TileX/TileZ calculations

### Wrong coordinates in world
- Verify UpdatePosition calculations
- Check HandleTileBoundary logic
- Ensure base tile is correct

### Incorrect heading
- Check _ay is being updated correctly
- Verify dHeading calculation for curves
- Ensure radius sign is correct (positive = right turn)
