# Open Rails Track Builder Documentation

Welcome to the comprehensive documentation for the Open Rails Track Builder pipeline!

## What is This?

This documentation covers the complete pipeline for generating Open Rails track data:

1. **Python Curve Fitter** - Generates smooth track curves from mathematical definitions
2. **TdbDump Tool** - Converts curve data into Open Rails track database format
3. **Open Rails File Formats** - Understanding how track data is stored and interpreted

## Quick Navigation

- **Just getting started?** → Head to [Quick Start](quick_start.md)
- **Want to understand the full pipeline?** → See [Full Pipeline Walkthrough](pipeline/full_walkthrough.md)
- **Need to know about a specific file format?** → Check [File Formats](formats/tdb.md)
- **Having issues?** → Visit [Troubleshooting](troubleshooting.md)

## Project Structure

```
openrails/
├── Program/
│   ├── CurveFitter/          # Python curve fitting tool
│   └── ...
├── Source/
│   └── TdbDump/              # C# tool for generating TDB files
└── track_builder_docs/       # This documentation
```

## Key Concepts

- **Tile Coordinates**: Track positions are referenced by world tiles (TileX, TileZ)
- **World Coordinates**: Within each tile, precise X, Y, Z positions
- **TrVectorSection**: Individual track sections with curve data
- **Pin Connections**: How track nodes link together
- **UIDs**: Universal identifiers for referencing track sections in world files

## Getting Help

- Check the [Glossary](glossary.md) for terminology
- See [Pin Connections](concepts/pins.md) for connectivity details
- Review [Troubleshooting](troubleshooting.md) for common errors
