# Track builder

Turn real railroad GeoJSON into Open Rails track files (TDB, tsection, world DynTracks).

```
GeoJSON  →  curve-fitter  →  bbox_network_local.json  →  TdbDump  →  route files
```

| Tool | Location | Job |
|------|----------|-----|
| Curve fitter | `Tools/curve-fitter` | Fit polylines to straights + circular arcs |
| TdbDump | `Source/TdbDump` | Snap network, junctions, write route files |

## Docs

| | |
|--|--|
| [Getting started](getting-started.md) | Install, configure, run the pipeline |
| [How it works](how-it-works.md) | Fit → place → snap → junctions → outputs |
| [Troubleshooting](troubleshooting.md) | Common failures and fixes |

Verify results in Track Viewer (**Ctrl+R** to reload the TDB) or by comparing to QGIS (`bbox_network.geojson`).
