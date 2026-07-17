# TdbDump Overview

C# tool (`Source/TdbDump`) that converts curve-fitter output into Open Rails route files.

## Purpose

| Input | Outputs |
|-------|---------|
| `bbox_network_local.json` (preferred) or `primitives.json` | `BNSF_Scenic.tdb`, `tsection.dat`, `WORLD/w-*.w`, optional scenario set for first free-ended feature |

Route paths are currently hard-coded in `Program.cs` (BNSF Scenic copy). Adjust there for another route.

## Architecture

```
bbox_network_local.json
        ↓
TrackBuilder (load → place → snap → junctions → pins)
        ↓
    ┌───┼───────────────┐
    ↓   ↓               ↓
TSection  TDBWriter   WorldWriter (DynTracks from chains)
Writer    (+ junctions)
```

## Key components

| File | Role |
|------|------|
| `Program.cs` | Load builder, write tsection/tdb/world/scenario |
| `TrackBuilder.cs` | Network graph construction |
| `Models.cs` | Nodes, chains, primitives, DynTrack helpers |
| `TDBWriter.cs` | End / vector / junction nodes |
| `TSectionWriter.cs` | Dynamic `TrackSection` / `SectionCurve` entries |
| `WorldWriter.cs` | Dyntrack objects into one base-tile `.w` |
| `ScenarioWriter.cs` | PAT/ACT/SRV for first feature when possible |

## Workflow

```csharp
var track = new TrackBuilder();           // finds network or legacy JSON
var allNodes = track.BuildAllNodes();     // snap + junctions + pins

TSectionWriter.UpdateTSectionDat(…, track.Primitives);
// write each TrEndNode / TrJunctionNode / TrackNode
DynamicTrack.MakeDynamicTrackObjects(track.Chains, track.Primitives);
WorldWriter.WriteWorldFiles(…);
```

## Node kinds in the TDB

- **TrVectorNode** — one per OBJECTID chain; many `TrVectorSection`s.
- **TrEndNode** — free tips not consumed by a link or junction.
- **TrJunctionNode** — 3-way geo cluster (stem + main + diverging).

Counts: e.g. 39 features → 39 vectors + ends + junctions ≈ 40+ TDB nodes.

## World files

One DynTrack per **section** (not one packed object for an entire vector). Positions come from post-reshape `chain.Sections`, so junction tip fixes appear in the world file as well as the TDB.

## Configuration

- Input search: `bbox_network_local.json`, else `primitives.json` (working directory / known paths via `FindInputFile`).
- Base tile: `(-12842, 14734)` in TrackBuilder / WorldWriter.
- Default elevation for world: ~1000 m (flat placeholder).

## Integration

After a successful run, reload Track Viewer (**Ctrl+R**) or launch the route in Open Rails. Prefer TDB for topology checks; world is for mesh/DynTrack visuals.

## Next

- [TrackBuilder](trackbuilder.md)
- [Architecture](tdbdump_architecture.md)
- [Writers](writers.md)
