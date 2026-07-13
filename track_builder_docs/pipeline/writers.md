# Writers Details

The writer components transform TrackBuilder output into various file formats.

## Writer Architecture

```
TDBWriter ──→ .tdb (STF format)
WorldWriter ──→ .w (World geometry)
PathWriter ──→ .pat (Path waypoints)
```

## TDBWriter

Generates the `.tdb` (Track Database) file in SIMISA Text Format (STF).

### Output Format

```
SIMISA@@@@@@@@@@JINX0t0t______

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
                trvectorsections ( 25
                    50001 0 0 0 0 0 1 00 0 0 0 0 0 0 0 0
                    50002 0 0 0 0 0 1 00 0 0 100 0 0 0 0 0
                    ...more sections...
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

### Key Methods

```csharp
public class TDBWriter
{
    public void Write(TrackNode[] nodes, string filename)
    {
        using (var writer = new StreamWriter(filename))
        {
            WriteHeader(writer);
            WriteTrackDB(writer, nodes);
            WriteFooter(writer);
        }
    }
    
    private void WriteHeader(StreamWriter w)
    {
        w.WriteLine("SIMISA@@@@@@@@@@JINX0t0t______");
        w.WriteLine();
    }
    
    private void WriteTrackDB(StreamWriter w, TrackNode[] nodes)
    {
        w.WriteLine("trackdb (");
        w.WriteLine($"    tracknodes ( {nodes.Length - 1}");  // -1 for null at [0]
        
        for (int i = 1; i < nodes.Length; i++)
        {
            WriteTrackNode(w, nodes[i], i);
        }
        
        w.WriteLine("    )");
        w.WriteLine("    tritemtable ( 0 )");
        w.WriteLine(")");
    }
    
    private void WriteTrackNode(StreamWriter w, TrackNode node, int id)
    {
        w.WriteLine($"        tracknode ( {id}");
        
        if (node.TrEndNode != null)
            WriteEndNode(w, node);
        else if (node.TrVectorNode)
            WriteVectorNode(w, node);
        
        w.WriteLine("        )");
    }
    
    private void WriteEndNode(StreamWriter w, TrackNode node)
    {
        w.WriteLine("            uid ( 0 0 0 0 0 0 0 0 0 0 0 0 )");
        w.WriteLine("            trendnode ( )");
        WritePins(w, node);
    }
    
    private void WriteVectorNode(StreamWriter w, TrackNode node)
    {
        w.WriteLine("            trvectornode (");
        w.WriteLine($"                trvectorsections ( {node.Sections.Count}");
        
        foreach (var section in node.Sections)
        {
            WriteVectorSection(w, section);
        }
        
        w.WriteLine("                )");
        w.WriteLine("                tritemrefs ( 0 )");
        w.WriteLine("            )");
        
        WritePins(w, node);
    }
    
    private void WriteVectorSection(StreamWriter w, TrVectorSection section)
    {
        // Format: SectionIndex TileX TileZ X Y Z AX AY AZ WFNameX WFNameZ WorldFileUiD
        w.Write($"                    {section.SectionIndex} ");
        w.Write($"{section.TileX} {section.TileZ} ");
        w.Write($"{section.X.ToString("F1")} {section.Y.ToString("F1")} {section.Z.ToString("F1")} ");
        w.Write($"{section.AX.ToString("F6")} {section.AY.ToString("F6")} {section.AZ.ToString("F6")} ");
        w.Write($"{section.WFNameX} {section.WFNameZ} ");
        w.WriteLine(section.WorldFileUiD.ToString("X"));
    }
    
    private void WritePins(StreamWriter w, TrackNode node)
    {
        // Calculate inpins/outpins based on pin directions
        int inPins = node.Pins.Count(p => p.Direction == 0);
        int outPins = node.Pins.Count(p => p.Direction == 1);
        
        w.WriteLine($"            trpins ( {inPins} {outPins}");
        
        foreach (var pin in node.Pins)
        {
            w.WriteLine($"                TrPin ( {pin.Node} {pin.Direction} )");
        }
        
        w.WriteLine("            )");
    }
}
```

## WorldWriter

Generates `.w` (World) files containing dynamic track objects.

### Output Format

```
SIMISA@@@@@@@@@@JINX0W0t______

Dyntrack (
    Tr_WorldFile (
        Serial ( 1 )
        TrackObj (
            SectionIdx ( 50001 )
            Elevation ( 100 )
            CollideFlags ( 7 )
            StaticFlags ( 0 )
            Position ( -433 100 25 )
            QDirection ( 0 0.707107 0 0.707107 )
            VDbId ( 2 0 0 )
        )
        DyntrackObj (
            SectionIdx ( 50001 )
            Elevation ( 100 )
            CollideFlags ( 7 )
            StaticFlags ( 0 )
            Position ( -433 100 25 )
            QDirection ( 0 0.707107 0 0.707107 )
            VDbId ( 2 0 0 )
            TrackSections ( 1
                TrackSection ( 50001 0 0 0 0 0 1 00 0 0 0 0 0 0 0 0 )
            )
        )
    )
)
```

### Key Methods

```csharp
public class WorldWriter
{
    public void Write(TrackNode[] nodes, string outputDir)
    {
        // Group sections by tile
        var tileGroups = GroupSectionsByTile(nodes);
        
        foreach (var tileGroup in tileGroups)
        {
            var filename = WorldFileNameFromTile(tileGroup.Key);
            WriteWorldFile(filename, tileGroup.Value);
        }
    }
    
    private string WorldFileNameFromTile((int x, int z) tile)
    {
        return $"w-{tile.x:D6}{tile.z:D6}.w";
    }
    
    private void WriteWorldFile(string filename, List<TrVectorSection> sections)
    {
        using (var writer = new StreamWriter(filename))
        {
            writer.WriteLine("SIMISA@@@@@@@@@@JINX0W0t______");
            writer.WriteLine();
            writer.WriteLine("Dyntrack (");
            writer.WriteLine("    Tr_WorldFile (");
            
            foreach (var section in sections)
            {
                WriteDyntrackObj(writer, section);
            }
            
            writer.WriteLine("    )");
            writer.WriteLine(")");
        }
    }
    
    private void WriteDyntrackObj(StreamWriter w, TrVectorSection section)
    {
        // Apply coordinate transformation for TSRE5 compatibility
        float posX = -section.X;  // Negate X
        float posZ = section.Z;   // Keep Z positive
        float posY = section.Y;
        
        // Convert Euler angles to quaternion
        var quat = EulerToQuaternion(section.AX, section.AY, section.AZ);
        
        w.WriteLine("        DyntrackObj (");
        w.WriteLine($"            SectionIdx ( {section.SectionIndex} )");
        w.WriteLine($"            Elevation ( {posY} )");
        w.WriteLine($"            CollideFlags ( 7 )");
        w.WriteLine($"            StaticFlags ( 0 )");
        w.WriteLine($"            Position ( {posX} {posY} {posZ} )");
        w.WriteLine($"            QDirection ( {quat.X} {quat.Y} {quat.Z} {quat.W} )");
        w.WriteLine($"            VDbId ( {section.WorldFileUiD} 0 0 )");
        w.WriteLine("            TrackSections ( 1");
        w.Write($"                TrackSection ( {section.SectionIndex} ");
        // ... write section data ...
        w.WriteLine(")");
        w.WriteLine("            )");
        w.WriteLine("        )");
    }
    
    private Quaternion EulerToQuaternion(float ax, float ay, float az)
    {
        // Convert Euler angles (roll, pitch, yaw) to quaternion
        // Using ZYX convention as per Open Rails
        
        float cy = MathF.Cos(ay * 0.5f);
        float sy = MathF.Sin(ay * 0.5f);
        float cp = MathF.Cos(ax * 0.5f);
        float sp = MathF.Sin(ax * 0.5f);
        float cr = MathF.Cos(az * 0.5f);
        float sr = MathF.Sin(az * 0.5f);
        
        return new Quaternion(
            sr * cp * cy - cr * sp * sy,  // X
            cr * sp * cy + sr * cp * sy,  // Y
            cr * cp * sy - sr * sp * cy,  // Z
            cr * cp * cy + sr * sp * sy   // W
        );
    }
}
```

## PathWriter

Generates `.pat` (Path) files with waypoints and navigation.

### Output Format

```
SIMISA@@@@@@@@@@JINX0P0t______

Serial ( 1 )

TrackPDPs (
    TrackPDP ( -12842 14734 0 0 0 2 0 )
    TrackPDP ( -12842 14734 100 0 0 2 0 )
    TrackPDP ( -12842 14734 200 0 0 2 0 )
)

TrackPath (
    TrPathName ( TestTrack )
    Name ( "Test Track" )
    TrPathStart ( Start )
    TrPathEnd ( End )
    TrPathNodes ( 3
        TrPathNode ( 00000000 1 4294967295 0 )
        TrPathNode ( 00000000 2 4294967295 1 )
        TrPathNode ( 00000000 4294967295 4294967295 2 )
    )
)
```

### Key Methods

```csharp
public class PathWriter
{
    public void Write(TrackNode[] nodes, string filename)
    {
        // Extract all sections
        var allSections = nodes
            .Where(n => n.TrVectorNode)
            .SelectMany(n => n.Sections)
            .ToList();
        
        using (var writer = new StreamWriter(filename))
        {
            writer.WriteLine("SIMISA@@@@@@@@@@JINX0P0t______");
            writer.WriteLine();
            writer.WriteLine("Serial ( 1 )");
            writer.WriteLine();
            
            WriteTrackPDPs(writer, allSections);
            WriteTrackPath(writer, allSections);
        }
    }
    
    private void WriteTrackPDPs(StreamWriter w, List<TrVectorSection> sections)
    {
        w.WriteLine("TrackPDPs (");
        
        for (int i = 0; i < sections.Count; i++)
        {
            var s = sections[i];
            w.WriteLine($"    TrackPDP ( {s.TileX} {s.TileZ} {s.X.ToString("F1")} {s.Y.ToString("F1")} {s.Z.ToString("F1")} 2 0 )");
        }
        
        w.WriteLine(")");
        w.WriteLine();
    }
    
    private void WriteTrackPath(StreamWriter w, List<TrVectorSection> sections)
    {
        w.WriteLine("TrackPath (");
        w.WriteLine("    TrPathName ( TestTrack )");
        w.WriteLine("    Name ( \"Test Track\" )");
        w.WriteLine("    TrPathStart ( Start )");
        w.WriteLine("    TrPathEnd ( End )");
        w.WriteLine($"    TrPathNodes ( {sections.Count}");
        
        for (int i = 0; i < sections.Count; i++)
        {
            uint nextNode = i < sections.Count - 1 ? (uint)(i + 1) : 0xFFFFFFFF;
            w.WriteLine($"        TrPathNode ( 00000000 {i} {nextNode} {i} )");
        }
        
        w.WriteLine("    )");
        w.WriteLine(")");
    }
}
```

## Extending Writers

To add a new file format:

1. Create new writer class:
```csharp
public class MyFormatWriter
{
    public void Write(TrackNode[] nodes, string filename) { ... }
}
```

2. Integrate into Program.cs:
```csharp
var myWriter = new MyFormatWriter();
myWriter.Write(trackNodes, outputPath);
```

## Error Handling

All writers include error checking:

```csharp
try
{
    writer.Write(nodes, filename);
    Console.WriteLine($"✓ Generated {filename}");
}
catch (Exception ex)
{
    Console.WriteLine($"✗ Failed to generate {filename}: {ex.Message}");
    throw;
}
```
