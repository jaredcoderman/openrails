# Troubleshooting

| Symptom | Fix |
|---------|-----|
| Empty / wrong network in TdbDump | Put a fresh `bbox_network_local.json` next to `TdbDump.exe` (`Source/TdbDump/bin/Debug/`). That name wins over `primitives.json`. |
| Missing short stubs / gaps | Re-run `extract_bbox_network.py` (2-point polylines export as one straight). |
| Long duplicated or reverse straights at joints | Rebuild with current TrackBuilder (reseats tip straights; does not append reverse twins). |
| Spur crosses through track at a T | Rebuild so junction tip reshape runs before the TDB snapshot. Ctrl+R in Track Viewer. Paths meet at the frog but should not form an “X”. |
| “Skipping N-way cluster” | Only 3-way junctions are implemented. |
| Scenario files skipped | Normal when the first feature has no free ends. TDB is still valid. |
| Track Viewer shows old geometry | **Ctrl+R** after every TdbDump run; check file timestamps. |
| Track missing in Open Rails | Confirm `WORLD/w-012842+014734.w` naming (6-digit padded tiles) and that TDB + world were updated together. |
| Mirrored vs QGIS | Toggle `FLIP_X_COORDINATES` in `config.py`, re-extract, rebuild. |
| High fit RMS | Adjust `STRAIGHT_TOLERANCE` / `CIRCLE_TOLERANCE`; re-extract. |

Loop when debugging: extract → copy JSON → `dotnet build` → `TdbDump.exe` → Ctrl+R. Use a small `bbox_objectids.txt` to isolate a junction.
