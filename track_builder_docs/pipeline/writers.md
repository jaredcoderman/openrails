# Writers Details

Writers turn `TrackBuilder` output into route files. Entry point: `Program.cs`.

## Overview

```
track.Primitives  →  TSectionWriter  →  tsection.dat
allNodes          →  TDBWriter       →  BNSF_Scenic.tdb
track.Chains      →  DynTracks       →  WorldWriter → WORLD/w-*.w
first free chain  →  ScenarioWriter  →  .pat / .act / .srv (optional)
```

## TSectionWriter

Writes dynamic track sections for every `TrackPrimitive` (including tip reseats / junction straights).

Format used by this project (compact `SectionCurve` lines):

```
TrackSections ( N
  TrackSection (
    SectionCurve ( 0 ) 40001 822.47 0          ; straight: length, radius 0
  )
  TrackSection (
    SectionCurve ( 1 ) 40002 0.991214 176.38   ; curve: angle rad, radius m
  )
)
```

Section indices are assigned in TrackBuilder (typically from 40001).

## TDBWriter

Three writers:

| Method | Node |
|--------|------|
| `WriteEndNode` | `TrEndNode` |
| `WriteVectorNode` | Multi-section vector for one OBJECTID |
| `WriteJunctionNode` | `TrJunctionNode` (`trpins` 1 2, stem/main/div) |

Pins use Open Rails conventions (direction = linked node’s side). See [Pins](../concepts/pins.md).

## WorldWriter

- Builds DynTrack list via `DynamicTrack.MakeDynamicTrackObjects(chains, primitives)` — **one DynTrack per section** on each chain (post-junction geometry).
- Writes into the base tile file `w-012842+014734.w` (offsets other tiles into that file’s local frame).
- Applies MSTS X negation on position/quaternion Z component at write time.

## ScenarioWriter

Still aimed at a **single** playable path: first `FeatureChain` when it still has two `TrEndNode`s. Dense snapped networks often skip this (logged). Full network path stitching is future work.

## PAT / ACT / SRV / Consist

Format references remain under [formats/](../formats/pat.md). Generation for those is not the main network deliverable anymore; TDB + tsection + world are.

## Related

- [TdbDump Overview](tdbdump_overview.md)
- [TrackBuilder](trackbuilder.md)
- [World format](../formats/world.md)
