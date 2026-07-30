"""
Global Configuration
====================

Centralized configuration file for all curve-fitter scripts.
Change values here to apply them everywhere.
"""

# ============================================================================
# RAILROAD DATA
# ============================================================================

# GeoJSON file containing railroad network data
GEOJSON_FILE = 'NTAD_North_American_Rail_Network_Lines_BNSF_2685269841624876744.geojson'

# Object ID to process (change this to analyze different railroad segments)
TARGET_OBJECTID = 1859

# ============================================================================
# SEGMENTATION PARAMETERS
# ============================================================================

# STRAIGHT LINE TOLERANCE
# RMS perpendicular error tolerance for straight line segments (meters)
STRAIGHT_TOLERANCE = 0.1

# CIRCULAR ARC TOLERANCE
# RMS radial error tolerance for circular arc segments (meters)
CIRCLE_TOLERANCE = 1

# Initial number of points to start each segment with
INITIAL_SEGMENT_SIZE = 10

# Minimum number of points required to form a valid segment
MIN_SEGMENT_SIZE = 3

# JSON primitives export file (for C# TrackBuilder)
PRIMITIVES_OUTPUT = 'primitives.json'

# ============================================================================
# COORDINATE TRANSFORMATION
# ============================================================================

# Flip X coordinates (mirror over Y-axis)
# Set to True to flip all X coordinates before processing
# False = geographic east = +X. True mirrors over the local Z/north axis (negate X).
FLIP_X_COORDINATES = False

# Maximum length for straight segments (meters)
# Straights longer than this will be split into chunks to respect tile limits
# Open Rails tile limit is typically 2048m
MAX_STRAIGHT_LENGTH = 2048

# Reject near-infinite radius "curves" (almost straight); treat as straight instead.
MAX_CIRCLE_RADIUS = 8000.0

