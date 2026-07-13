# Open Rails

[![Documentation](https://img.shields.io/badge/docs-Track%20Builder%20Pipeline-blue)](https://jaredt82.github.io/openrails/)

Open-source train simulator and route building tools.

## Track Builder Pipeline

Complete documentation for generating Open Rails tracks using the Python Curve Fitter and C# TdbDump tool.

### 📚 [View Full Documentation](https://jaredt82.github.io/openrails/)

- **[Quick Start](https://jaredt82.github.io/openrails/quick_start/)** - Get running in 5 minutes
- **[File Formats Reference](https://jaredt82.github.io/openrails/formats/)** - Complete guide to .tdb, .pat, .w, .srv, .act, .con files
- **[Full Pipeline Walkthrough](https://jaredt82.github.io/openrails/pipeline/full_walkthrough/)** - End-to-end example with real railroad data
- **[Troubleshooting](https://jaredt82.github.io/openrails/troubleshooting/)** - Common issues and solutions
- **[Glossary](https://jaredt82.github.io/openrails/glossary/)** - Terminology reference

## Key Tools

### Python Curve Fitter
Reverse-engineer railroad curves from real GeoJSON coordinates. Fits straight lines and circular arcs using mathematical optimization.

### C# TdbDump
Convert curve primitives into Open Rails format:
- Generate `.tdb` (track database)
- Create `.pat` (path waypoints)
- Produce `.w` (world geometry files)

## Quick Links

- **[Curve Fitter Overview](https://jaredt82.github.io/openrails/pipeline/curve_fitter_overview/)** - How it works
- **[TdbDump Architecture](https://jaredt82.github.io/openrails/pipeline/tdbdump_architecture/)** - System design
- **[Concepts](https://jaredt82.github.io/openrails/concepts/coordinates/)** - Coordinate systems, pins, UIDs

## Getting Started

1. Prepare GeoJSON file with railroad coordinates
2. Configure `Tools/curve-fitter/config.py`
3. Run curve fitter → `primitives.json`
4. Run TdbDump → `.tdb`, `.pat`, `.w` files
5. Copy to route
6. Load in Open Rails

See [Quick Start Guide](https://jaredt82.github.io/openrails/quick_start/) for detailed steps.

## Documentation Structure

```
📖 Track Builder Documentation
├── 🚀 Quick Start
├── 🔧 Pipeline
│   ├── Curve Fitter
│   ├── TdbDump
│   └── Full Walkthrough
├── 📄 File Formats
│   ├── .tdb (Track Database)
│   ├── .pat (Paths)
│   ├── .w (World)
│   ├── .srv (Services)
│   ├── .act (Activities)
│   └── .con (Consists)
├── 💡 Concepts
│   ├── Coordinates
│   ├── Pins
│   ├── Track Sections
│   └── UIDs
└── 📖 Reference
    ├── Troubleshooting
    ├── Glossary
    └── Deep Dives
```

## Resources

- **[📚 Full Documentation](https://jaredt82.github.io/openrails/)** - Complete reference guide
- **Source Code**: `Source/` directory
- **Curve Fitter**: `Tools/curve-fitter/` directory
- **TdbDump Tool**: `Source/TdbDump/` directory

## Contributing

To update documentation:

1. Edit markdown files in `track_builder_docs/`
2. Push to main branch
3. GitHub Actions automatically builds and deploys

Documentation is hosted on GitHub Pages and updates on every push.

## License

See LICENSE file for details.

---

**[Start with Quick Start Guide →](https://jaredt82.github.io/openrails/quick_start/)**
