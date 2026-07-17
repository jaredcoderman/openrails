# Curve Fitter Usage Examples

## Network (preferred)

```powershell
cd Tools\curve-fitter
.\Scripts\Activate.ps1   # if using the local venv

# 1) OBJECTID list for a bbox (or edit bbox_objectids.txt by hand)
py -3 select_bbox_objectids.py

# 2) Fit every ID in one shared local meter frame
py -3 extract_bbox_network.py

# Inspect
#   bbox_network.geojson      → QGIS
#   bbox_network_local.json   → TdbDump
```

Copy JSON to the TdbDump output folder and run the exe (see [Quick Start](../quick_start.md)).

### Tight vs loose fit

```python
# config.py
STRAIGHT_TOLERANCE = 0.1   # stricter → more short straights
CIRCLE_TOLERANCE = 1.0
```

Raise tolerances if the polyline is noisy; lower if you need closer vertex following.

### Flip easting

If Track Viewer / OR looks mirrored vs QGIS:

```python
FLIP_X_COORDINATES = True   # must re-extract after changing
```

## Single OBJECTID (legacy)

```python
# config.py
TARGET_OBJECTID = 1859
PRIMITIVES_OUTPUT = 'primitives.json'
```

```powershell
py -3 extract_primitives.py
```

TdbDump loads this only when `bbox_network_local.json` is not found.

## Interpreting fit stats

In `bbox_network_local.json`, each feature may include:

```json
"fit": {
  "rms_error": 0.35,
  "max_error": 1.1,
  "endpoint_error": 0.6
}
```

High `endpoint_error` after chained refinement still leaves geo ends correct for snap — TrackBuilder aligns reconstructed chains separately.

## Two-point stubs

Features with only two vertices become one straight. They often sit next to longer OBJECTIDs and matter for snap continuity.

## Next

- [Curve Output](curve_output.md)
- [Full Walkthrough](full_walkthrough.md)
- [TrackBuilder](trackbuilder.md)
