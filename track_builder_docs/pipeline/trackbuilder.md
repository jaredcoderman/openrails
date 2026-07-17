# TrackBuilder Details

`Source/TdbDump/TrackBuilder.cs` turns fitted network JSON into a pin-connected Open Rails track graph.

## Role

| Input | Output |
|-------|--------|
| `bbox_network_local.json` (preferred) or legacy `primitives.json` | `FeatureChain`s + `TrackPrimitive`s, then `BuildAllNodes()` → `TrEndNode` / `TrackNode` / `TrJunctionNode` list |

Base tile constants: `BaseTileX = -12842`, `BaseTileZ = 14734`.

## High-level `BuildAllNodes`

```
1. Assign vector-node IDs (1..N) to each non-empty FeatureChain
2. FindEndpointLinks — geo ends within EndpointSnapMeters (25 m)
3. AlignLinkedChains — translate so reconstructed joints coincide
   (AlignMultiWayClusters first, then tree walk from longest seed)
4. CloseLinkedResiduals / CloseSmallResidualGaps — reseat tip
   straights onto partners (never append reverse/collinear twins)
5. CreateJunctionNodes — 3-way geo clusters → TrJunctionNode
   + ReshapeJunctionApproach on geo headings (before vector snapshot)
6. Assign WorldFileUiD / WF names on sections
7. Snapshot each chain’s sections into a TrackNode vector
8. WireVectorSide — pin to junction, linked vector, or new TrEndNode
9. Return nodes ordered by id
```

**Important:** Junction reshape mutates `chain.Sections` and must run **before** the vector section list is copied. Otherwise the TDB keeps pre-reshape overlapping tip geometry.

## Per-feature placement

`BuildFromNetwork`:

- One `FeatureChain` per OBJECTID with primitives.
- Sections placed from each primitive’s absolute `start` pose when present; chain start/end and **geo** start/end/headings stored separately (`GeoStartX/Z/Ay`, `GeoEndX/Z/Ay` from `points_local`).
- Within-feature joints use continuity helpers that **do not chord-reseat curves** (preserves fitted arcs).

Legacy `BuildFromLegacyPrimitives` builds a single chain from a flat primitive list.

## Endpoint snap

Matching uses **geo** endpoints (`GeoStart*` / `GeoEnd*`), not reconstructed ends. Reconstruction drift can be large while source polylines still meet in QGIS.

- Greedy closest pairs within 25 m → bidirectional `EndpointLink`s.
- Alignment translates whole chains; multi-way clusters share one meeting point.
- Residuals: lengthen/shorten tip **straights** onto the partner. Curves stay; optional short forward fillers only when safe.

## Junctions (3-way)

`FindGeoEndpointClusters` groups geo ends that all lie near each other.

For each cluster of size **3**:

1. Drop pairwise links inside the cluster (junction owns topology).
2. `AssignJunctionRoles` → stem, main (through), diverging (largest heading delta from through).
3. `ReshapeJunctionApproach` on each leg using that leg’s geo tip heading:
   - Through legs: shorter approach (~60 m).
   - Diverging: longer (~160 m) so spur arcs that swung into the through line are stripped and replaced by a tip straight on the geo diverge angle.
4. Emit `TrJunctionNode` (`trpins` 1 in + 2 out): stem, main, diverging.
5. Record `junctionSides` so vectors pin to the junction instead of ends.

Clusters with size ≠ 3 are skipped (logged). `ShapeIndex = 0` (no switch mesh yet).

### Why tip reshape

Chained placement + snap can collapse the turnout angle so spur and through look almost parallel and **overlap** near the frog. Forcing the tip onto `GeoStartAy` / `GeoEndAy` restores the ~QGIS diverge (e.g. ~13°) and clears crossing curves.

## Pin wiring (`WireVectorSide`)

Priority:

1. Junction pin for this endpoint → `TrPin(junctionId, direction)`
2. Else pairwise link → `TrPin(otherVectorId, otherIsStart ? 0 : 1)`
3. Else allocate `TrEndNode` at that tip pose

Junction pin **Direction** on the vector is `0` for stem (into junction) and `1` for either outlet.

## Design rules (lessons learned)

| Do | Don’t |
|----|--------|
| Match topology on geo ends | Match only on drifted reconstructed ends |
| Reseat residual gaps in place | Append reverse or collinear twin straights |
| Keep fitted curves; reshape junction tips deliberately | Mid-feature snap that inserts angled joint fillers everywhere |
| Snapshot vectors **after** junction reshape | Snapshot before reshape |

## Constants worth knowing

| Name | Typical | Meaning |
|------|---------|---------|
| `EndpointSnapMeters` | 25 | Geo end match radius |
| Diverging tip approach | ~160 m | Strip overlapping spur approach |
| Through tip approach | ~60 m | Keep through mostly intact |
| Section indices | 40001+ | Dynamic entries in `tsection.dat` |

## Related types (`Models.cs`)

- `FeatureChain` — per-OBJECTID geometry + geo poses + `VectorNodeId`
- `TrJunctionNode` — 3-way switch topology
- `NetworkLocalFile` / `NetworkFeature` — JSON DTOs
- `TrackPrimitive` — straight/curve + optional `Start` pose; `SignedAngle` for OR convention

## Testing tips

1. Console link/translate/junction counts look sane.
2. Track Viewer Ctrl+R after every rebuild.
3. At a known T (e.g. 1101 / 1865 / 2017), tip headings should diverge; spur should not cut through the main.
4. Pairwise joints should meet without long ghost straights.
