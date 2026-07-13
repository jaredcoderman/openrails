# TdbDump Architecture

Deep dive into the internal architecture of TdbDump.

## Component Diagram

```
┌─────────────────────────────────────────────────┐
│           Program.cs (Entry Point)              │
└──────────────┬──────────────────────────────────┘
               │ Parses arguments
               ↓
┌─────────────────────────────────────────────────┐
│      Input Handler (Reads JSON curve data)      │
└──────────────┬──────────────────────────────────┘
               │
               ↓
┌─────────────────────────────────────────────────┐
│         Models.cs (Data Structures)             │
│  ┌────────────────────────────────────────────┐ │
│  │ - TrackNode / TrVectorSection              │ │
│  │ - TrPin / TrEndNode                        │ │
│  │ - DynamicTrack / WorldFile                 │ │
│  └────────────────────────────────────────────┘ │
└──────────────┬──────────────────────────────────┘
               │
               ↓
┌─────────────────────────────────────────────────┐
│       TrackBuilder.cs (TDB Generation)          │
│  ┌────────────────────────────────────────────┐ │
│  │ BuildAllNodes() → TrackNode[] with pins    │ │
│  │ CalculateTiles() → TileX, TileZ            │ │
│  │ CalculateUIDs() → Unique identifiers       │ │
│  └────────────────────────────────────────────┘ │
└──────────────┬──────────────────────────────────┘
               │
        ┌──────┼──────┬──────────┐
        ↓      ↓      ↓          ↓
     TDB     World  Path      Activity
    Writer   Writer Writer    Template
        │      │      │          │
        ↓      ↓      ↓          ↓
    .tdb    .w    .pat        .act
```

## Data Flow

### 1. Input Parsing

```csharp
// Read JSON from curve fitter
var json = File.ReadAllText(inputPath);
var trackData = JsonConvert.DeserializeObject<TrackData>(json);

// trackData contains:
// - base_tile_x, base_tile_z
// - sections[] with X, Y, Z, AX, AY, AZ, radius, length
```

### 2. Model Construction

Models.cs defines:

```csharp
public class TrackNode
{
    public int Id;
    public List<TrVectorSection> Sections;
    public List<TrPin> Pins;
}

public class TrVectorSection
{
    public int SectionIndex;
    public int TileX, TileZ;
    public float X, Y, Z;
    public float AX, AY, AZ;
    public uint WorldFileUiD;
}

public class TrPin
{
    public int Node;      // Linked node ID
    public int Direction; // 0 or 1 (linked node's side)
}
```

### 3. TrackBuilder Processing

```csharp
public class TrackBuilder
{
    // Input: JSON section data
    // Output: Valid TDB node structure
    
    public TrackNode[] BuildAllNodes()
    {
        // 1. Create start TrEndNode (ID 1)
        // 2. Create single TrVectorNode (ID 2) with all sections
        // 3. Create end TrEndNode (ID 3)
        // 4. Set up reciprocal pin connections
        // 5. Calculate coordinates, tiles, UIDs
        // 6. Return complete node array
    }
}
```

Key algorithm:

```
For each input section:
  - Accumulate position and rotation
  - Calculate which tile it falls into
  - Create TrVectorSection entry
  - Set tile coordinates (TileX, TileZ)
  - Set local coordinates (X, Y, Z)
  - Set rotation angles (AX, AY, AZ)
  - Assign unique UID

Create start/end nodes with pins pointing to vector node
Set vector node pins pointing back to end nodes
```

### 4. Output Generation

Each writer takes the node array and generates files:

**TDBWriter**
```csharp
public void Write(TrackNode[] nodes, string filename)
{
    // Generate STF format:
    // trackdb (
    //     tracknodes ( count
    //         tracknode ( id
    //             trvectornode ( ... )
    //             trpins ( inCount outCount
    //                 TrPin ( linkId direction )
    //             )
    //         )
    //     )
    // )
}
```

**WorldWriter**
```csharp
public void Write(TrackNode[] nodes, string outputDir)
{
    // Generate DynTrack objects for world files
    // For each section, create DyntrackObj with:
    // - Position (negated Z for TSRE5 compatibility)
    // - Quaternion rotation
    // - Reference to TDB via UiD
}
```

**PathWriter**
```csharp
public void Write(TrackNode[] nodes, string filename)
{
    // Generate .pat file with TrackPDPs and TrPathNodes
    // Each TrackPDP = one waypoint on path
    // TrPathNodes link them together
}
```

## Key Design Decisions

### Single Vector Node
Instead of one node per section, all sections go into **one TrVectorNode**. This:
- Allows MapViewer to use optimized rendering path
- Avoids accessing UiD on vector nodes (they don't have one)
- Matches expected MSTS structure

### Pin Reciprocity
Every pin connection is bidirectional:
- If Node A pins to Node B, Node B must pin back to Node A
- Directions specify which side of the target node
- Side 0 = "input", Side 1 = "output" (conceptually)

### Coordinate Transformation
- TSRE5 expects negated Z for compatibility
- X coordinate sign handling for proper orientation
- Quaternion adjustments for 180-degree Y rotation

### Tile Management
- All sections may span multiple tiles
- Each section's TileX, TileZ indicates its home tile
- Local X, Z are relative within that tile (0-2048)

## Extending TdbDump

To add new features:

1. **Add to Models.cs** - New data structure
2. **Update TrackBuilder** - Calculate or populate new structure
3. **Update Writers** - Output new fields
4. **Update Program.cs** - Accept new options

Example: Adding super-elevation:

```csharp
// Models.cs
public class TrVectorSection
{
    public float BankingAngle;  // New field
}

// TrackBuilder.cs
section.BankingAngle = inputSection.banking ?? 0;

// TDBWriter.cs
writer.Write($"{section.BankingAngle} ");
```

## Performance Considerations

- Sections are batched into single VectorNode for efficiency
- Tile calculations use modulo arithmetic
- Pin arrays pre-allocated based on node count
- File I/O buffered

Typical performance: ~100K sections in <5 seconds.
