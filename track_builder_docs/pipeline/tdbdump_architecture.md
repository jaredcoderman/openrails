# TdbDump Architecture

How the C# side is wired today.

## Component diagram

```
┌──────────────────────────────────────────┐
│ Program.cs                               │
│  TrackBuilder → writers → route files    │
└──────────────────┬───────────────────────┘
                   ↓
┌──────────────────────────────────────────┐
│ TrackBuilder                             │
│  BuildFromNetwork / LegacyPrimitives     │
│  BuildAllNodes:                          │
│    links → align → reseat → junctions    │
│    → UID → vector snapshot → pin wire    │
└──────────────────┬───────────────────────┘
                   ↓
     ┌─────────────┼──────────────┬────────────┐
     ↓             ↓              ↓            ↓
 TSectionWriter  TDBWriter   WorldWriter  ScenarioWriter
     ↓             ↓              ↓            ↓
 tsection.dat   .tdb          WORLD/*.w   .pat/.act/.srv*
```

\*Scenario write is best-effort for the first chain with two free ends.

## Data model (`Models.cs`)

```
NetworkLocalFile
  crs, features[] → NetworkFeature
                      objectid, start/end, points_local, primitives[]

FeatureChain
  ObjectId, Sections[], Geo* poses, Start/End reconstructed, VectorNodeId

TrackPrimitive
  SectionIndex, Type, Length/Radius/Angle/Clockwise, Start pose

TrackNode          — vector (Id, Sections, Pins)
TrEndNode          — terminus
TrJunctionNode     — 3-way (ShapeIndex, pose, Pins ordered stem/main/div)
TrVectorSection    — one TDB section row + WorldFileUiD
TrPin(Node, Pin)   — Pin = direction on the *linked* node
```

## Load path

```csharp
new TrackBuilder()
  → FindInputFile("bbox_network_local.json")
    ?? FindInputFile("primitives.json")
  → BuildFromNetwork  or  BuildFromLegacyPrimitives
```

Network path registers every primitive into `_primitives` with a unique `SectionIndex` (from 40001 upward) and builds `_chains`.

## BuildAllNodes stages

1. **ID reserve** — vector ids 1..N for chains.
2. **Geo links** — endpoints within 25 m.
3. **Align** — multi-way clusters, then connected components.
4. **Reseat** — close residual gaps without twin reverse straights.
5. **Junctions** — 3-way only; reshape tips; add `TrJunctionNode` into `allNodes`.
6. **UIDs** — unique `WorldFileUiD` per section for DynTracks.
7. **Snapshot** — `TrackNode.Sections` copied from chain (must be post-reshape).
8. **Wire** — junction / link / end pins.
9. **Order** — sort by node id for stable TDB.

## Writers

- **TSectionWriter** — `SectionCurve ( 0|1 ) index length|angle radius` style dynamic sections for every primitive (including reseated tips).
- **TDBWriter** — `WriteEndNode`, `WriteVectorNode`, `WriteJunctionNode`.
- **WorldWriter** — packs all DynTracks into base tile `w-012842+014734.w` with MSTS X negation on write.
- **DynamicTrack.MakeDynamicTrackObjects** — iterates `chain.Sections` only (orphaned removed tip sections are not written).

## Pin / junction semantics

Unchanged Open Rails rules: pin direction names the **linked** node’s side. Junctions use header `1 2` (one in, two out). See [Pin Connections](../concepts/pins.md) and [Pin Semantics](../deep_dives/pin_semantics.md).

## Extension points

| Want | Where |
|------|--------|
| Different route path | `Program.cs` constants |
| Snap radius | `EndpointSnapMeters` |
| Tip lengths at turnouts | `ReshapeJunctionApproach` call sites |
| N-way switches | `CreateJunctionNodes` (currently 3 only) |
| Switch meshes | `TrJunctionNode.ShapeIndex` + shapes |
| Full-network paths | Scenario / path stitching (not done) |
