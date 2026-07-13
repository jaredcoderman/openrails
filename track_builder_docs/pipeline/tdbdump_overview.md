# TdbDump Overview

TdbDump is the C# tool that converts curve fitter output into Open Rails track files.

## Purpose

TdbDump transforms abstract track data into Open Rails format:

- **Input**: Curve fitter output (JSON)
- **Output**: 
  - `.tdb` (Track Database)
  - `.pat` (Path/Track waypoints)
  - `.w` (World geometry files)
  - `.act` (Activity template)

## Architecture

```
Input (JSON)
    ↓
Models.cs (Data structures)
    ↓
TrackBuilder (Generate TDB structure)
    ↓
TDBWriter (Write .tdb file)
WorldWriter (Write .w files)
PathWriter (Write .pat file)
    ↓
Output Files
```

## Key Components

### Models.cs
Defines data structures:
- `TrackNode` - Nodes in the track database
- `TrVectorSection` - Individual track sections
- `TrPin` - Connections between nodes
- `TrEndNode` - Track terminus points
- `DynamicTrack` - World file track objects

### TrackBuilder.cs
Constructs the TDB node structure:
- Converts sections into `TrVectorNode` entries
- Creates `TrEndNode` entries at start/end
- Establishes pin connections
- Calculates tile coordinates and UIDs

### Writers
Generate output files:
- `TDBWriter` - `.tdb` file format
- `WorldWriter` - `.w` file format
- `PathWriter` - `.pat` file format

## Workflow

1. **Load input data**
   ```csharp
   var trackData = JsonConvert.DeserializeObject<TrackData>(inputJson);
   ```

2. **Build node structure**
   ```csharp
   var builder = new TrackBuilder(trackData);
   var nodes = builder.BuildAllNodes();
   ```

3. **Write outputs**
   ```csharp
   var tdbWriter = new TDBWriter();
   tdbWriter.Write(nodes, "output.tdb");
   
   var worldWriter = new WorldWriter();
   worldWriter.Write(nodes, "world/");
   ```

## Configuration

Base tile coordinates (editable in TrackBuilder):
```csharp
_tileX = -12842;  // Tile X
_tileZ = 14734;   // Tile Z
```

These determine where track appears in the world.

## Output Files

### track.tdb
```
trackdb (
    tracknodes (
        tracknode (
            trvectornode (
                trvectorsections ( ... )
            )
            trpins ( ... )
        )
    )
)
```

### w-012842+014734.w
```
Dyntrack (
    Tr_WorldFile (
        TrackObj (
            ...
        )
        DyntrackObj (
            ...
        )
    )
)
```

### track.pat
```
Serial ( 1 )
TrackPDPs ( ... )
TrackPath (
    TrPathName ( TrackName )
    TrPathNodes ( ... )
)
```

## Integration with Open Rails

Copy generated files to your route:

```
ROUTES/MyRoute/
├── tdb.dat               # Generated .tdb
├── PATHS/
│   └── TrackName.pat     # Generated .pat
└── WORLD/
    └── w-012842+014734.w # Generated .w
```

## Next Steps

See:
- [TrackBuilder Details](trackbuilder.md)
- [Writers Details](writers.md)
- [Full Pipeline Walkthrough](full_walkthrough.md)
