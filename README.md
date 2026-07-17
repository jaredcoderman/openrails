# Open Rails

[![Documentation](https://img.shields.io/badge/docs-Track%20builder-blue)](https://jaredt82.github.io/openrails/)

Open-source train simulator with a small pipeline for building track from real railroad GeoJSON.

## Track builder

Fit NTAD-style polylines, snap them into a network, and write Open Rails `.tdb` / `tsection.dat` / world DynTracks.

**[Documentation](track_builder_docs/)** · **[Getting started](track_builder_docs/getting-started.md)** · **[How it works](track_builder_docs/how-it-works.md)** · **[Troubleshooting](track_builder_docs/troubleshooting.md)**

| Tool | Path |
|------|------|
| Curve fitter | `Tools/curve-fitter/` |
| TdbDump | `Source/TdbDump/` |

```powershell
cd Tools\curve-fitter
py -3 extract_bbox_network.py
copy bbox_network_local.json ..\..\Source\TdbDump\bin\Debug\
dotnet build ..\..\Source\TdbDump -c Debug
cd ..\..\Source\TdbDump\bin\Debug
.\TdbDump.exe
```

Hosted docs (MkDocs): https://jaredt82.github.io/openrails/

## Contributing docs

1. Edit files in `track_builder_docs/`
2. Push to `master` — GitHub Actions builds and deploys Pages

## License

See LICENSE.
