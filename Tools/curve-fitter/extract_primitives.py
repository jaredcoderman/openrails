"""
Extract Primitive Parameters from Fitted Segments (Straight + Curves)
=====================================================================

Exports both straight line and circular arc primitives to JSON.

Unified primitive format:
- Straight: type="straight", radius=length, angle=0, clockwise=false
- Curve:    type="curve", radius=radius, angle=sweep_angle, clockwise=direction
"""

import json
import subprocess
import numpy as np
from config import (
    GEOJSON_FILE,
    TARGET_OBJECTID,
    STRAIGHT_TOLERANCE,
    CIRCLE_TOLERANCE,
    INITIAL_SEGMENT_SIZE,
    MIN_SEGMENT_SIZE,
    PRIMITIVES_OUTPUT,
    MAX_STRAIGHT_LENGTH
)
from circle_fitter import (
    latlons_to_cartesian,
    segment_polyline_model_selection
)


def split_long_straights(segments, max_length=2048):
    """
    Split straight segments longer than max_length into multiple chunks.
    
    For each straight segment exceeding max_length:
    - Calculate the number of chunks needed
    - Distribute original polyline points across chunks
    - Recalculate geometry (length, errors) for each chunk
    
    Curves are left unchanged (they're already constrained by radius).
    
    Args:
        segments: List of segment dicts from segmentation
        max_length: Maximum length per straight segment in meters (default 2048)
        
    Returns:
        List of segments with long straights split into chunks
    """
    new_segments = []
    
    for segment in segments:
        if segment['type'] != 'straight' or segment['length'] <= max_length:
            # Keep curves and short straights as-is
            new_segments.append(segment)
        else:
            # Split this long straight
            num_chunks = int(np.ceil(segment['length'] / max_length))
            points = segment['points']
            point_indices = segment['point_indices']
            
            print(f"\n  Splitting long straight (length={segment['length']:.1f}m) into {num_chunks} chunks")
            
            # Distribute points across chunks
            points_per_chunk = len(points) / num_chunks
            
            for chunk_idx in range(num_chunks):
                # Calculate which points belong to this chunk
                start_point_idx = int(np.round(chunk_idx * points_per_chunk))
                end_point_idx = int(np.round((chunk_idx + 1) * points_per_chunk))
                
                # Ensure last chunk includes all remaining points
                if chunk_idx == num_chunks - 1:
                    end_point_idx = len(points)
                
                # Extract chunk points
                chunk_points = points[start_point_idx:end_point_idx]
                chunk_indices = point_indices[start_point_idx:end_point_idx]
                
                if len(chunk_points) < 2:
                    continue  # Skip degenerate chunks
                
                # Recalculate geometry for this chunk
                chunk_length = float(np.linalg.norm(chunk_points[-1] - chunk_points[0]))
                
                # Recalculate fit quality (perpendicular distances to line)
                try:
                    from circle_fitter import fit_line_pca, calculate_line_errors
                    fit = fit_line_pca(chunk_points)
                    rms_error, errors = calculate_line_errors(chunk_points, fit)
                    max_error = float(np.max(errors)) if len(errors) else 0.0
                except Exception:
                    # Fallback: use chord-based fit
                    direction = chunk_points[-1] - chunk_points[0]
                    norm = np.linalg.norm(direction)
                    direction = direction / norm if norm > 1e-9 else np.array([1.0, 0.0])
                    normal = np.array([-direction[1], direction[0]])
                    errors = np.abs(np.dot(chunk_points - chunk_points[0], normal))
                    rms_error = float(np.sqrt(np.mean(errors ** 2)))
                    max_error = float(np.max(errors)) if len(errors) else 0.0
                    fit = {'point': chunk_points[0], 'direction': direction, 'normal': normal}
                
                # Create chunk segment
                chunk_segment = {
                    'segment_number': 0,  # Will be renumbered later
                    'type': 'straight',
                    'start_index': chunk_indices[0],
                    'end_index': chunk_indices[-1],
                    'point_count': len(chunk_indices),
                    'point_indices': chunk_indices,
                    'points': chunk_points,
                    'length': chunk_length,
                    'rms_error': float(rms_error),
                    'max_error': max_error,
                    'fit': fit
                }
                
                new_segments.append(chunk_segment)
                print(f"    Chunk {chunk_idx + 1}: {chunk_length:.1f}m ({len(chunk_indices)} points)")
    
    return new_segments


def extract_primitive_from_segment(segment, segment_number):
    """
    Extract primitive parameters from a segment (straight or curve).
    
    Returns unified format:
      Straight: {type, radius=0, angle=length, clockwise=false, ...}
      Curve: {type, radius, angle=sweep_angle, clockwise, ...}
    """
    
    if segment['type'] == 'straight':
        return {
            'segment_number': segment_number,
            'type': 'straight',
            'length': segment.get('length', 0),
            'radius': 0.0,  # Zero radius for straights
            'angle': segment.get('length', 0),  # Use length as angle in export
            'clockwise': False,  # Not applicable
            'rms_error': segment.get('rms_error', 0),
            'max_error': segment.get('max_error', 0),
            'point_count': segment.get('point_count', 0)
        }
    else:  # curve
        return {
            'segment_number': segment_number,
            'type': 'curve',
            'radius': segment.get('radius', 0),
            'angle': segment.get('angle', 0),
            'arc_length': segment.get('arc_length', 0),
            'clockwise': segment.get('clockwise', False),
            'rms_error': segment.get('rms_error', 0),
            'max_error': segment.get('max_error', 0),
            'point_count': segment.get('point_count', 0)
        }


def main():
    # Load data
    print("=" * 80)
    print("STEP 1: Loading data")
    print("=" * 80)
    
    geojson_file = GEOJSON_FILE
    with open(geojson_file, 'r', encoding='utf-8') as f:
        data = json.load(f)
    
    latlons = None
    for feature in data['features']:
        if feature.get('properties', {}).get('OBJECTID') == TARGET_OBJECTID:
            latlons = feature['geometry']['coordinates']
            break
    
    if latlons is None:
        print(f"ERROR: Could not find OBJECTID {TARGET_OBJECTID}")
        return
    
    # Reverse the vertices to process the polyline backwards
    latlons = list(reversed(latlons))
    
    print(f"Found polyline with {len(latlons)} vertices (reversed)")
    
    print("\n" + "=" * 80)
    print("STEP 2: Converting coordinates")
    print("=" * 80)
    
    cartesian_points, transformer = latlons_to_cartesian(latlons)
    print(f"Converted to {len(cartesian_points)} Cartesian points")
    
    print("\n" + "=" * 80)
    print("STEP 3: Model-selection segmentation")
    print("=" * 80)
    print(f"Straight tolerance: {STRAIGHT_TOLERANCE} m")
    print(f"Circle tolerance: {CIRCLE_TOLERANCE} m")
    print()
    
    segments = segment_polyline_model_selection(
        cartesian_points,
        straight_tolerance=STRAIGHT_TOLERANCE,
        circle_tolerance=CIRCLE_TOLERANCE,
        initial_segment_size=INITIAL_SEGMENT_SIZE,
        min_segment_size=MIN_SEGMENT_SIZE
    )
    
    # Split long straights to respect tile limits
    print("\n" + "=" * 80)
    print("STEP 3B: Splitting long straights (max {:.0f}m per section)".format(MAX_STRAIGHT_LENGTH))
    print("=" * 80)
    
    segments = split_long_straights(segments, max_length=MAX_STRAIGHT_LENGTH)
    
    # Renumber segments after splitting
    for i, segment in enumerate(segments, 1):
        segment['segment_number'] = i
    
    print(f"\nTotal segments after splitting: {len(segments)}")
    
    # Extract primitives
    print("\n" + "=" * 80)
    print("STEP 4: Extracting primitive parameters")
    print("=" * 80)
    
    primitives = []
    for i, segment in enumerate(segments, 1):
        prim = extract_primitive_from_segment(segment, i)
        primitives.append(prim)
        
        print(f"\nSegment {i}: {prim['type'].upper()}")
        if prim['type'] == 'straight':
            print(f"  Length: {prim['length']:.1f} m")
            print(f"  Export: radius=0, angle={prim['angle']:.1f} (distance)")
            print(f"  RMS Error: {prim['rms_error']:.4f} m")
            print(f"  Points: {prim['point_count']}")
        else:
            print(f"  Radius: {prim['radius']:.1f} m")
            print(f"  Angle: {np.degrees(prim['angle']):.2f}° ({prim['angle']:.6f} rad)")
            print(f"  Arc Length: {prim['arc_length']:.1f} m")
            print(f"  Direction: {'Clockwise' if prim['clockwise'] else 'Counterclockwise'}")
            print(f"  RMS Error: {prim['rms_error']:.4f} m")
            print(f"  Points: {prim['point_count']}")
    
    # Export JSON
    print("\n" + "=" * 80)
    print("STEP 5: Exporting JSON")
    print("=" * 80)
    
    export_segments = []
    for prim in primitives:
        segment = {
            "type": prim['type'],
            "radius": round(prim['radius'], 2),
            "angle": round(prim['angle'], 6),
            "clockwise": bool(prim['clockwise'])
        }
        
        # For straights, also include Length field for C# to use
        if prim['type'] == 'straight':
            segment['length'] = round(prim['length'], 2)
        
        export_segments.append(segment)
    
    export_data = {
        "segments": export_segments
    }
    
    # Export to local file
    with open(PRIMITIVES_OUTPUT, 'w') as f:
        json.dump(export_data, f, indent=2)
    
    print(f"\nExported to {PRIMITIVES_OUTPUT}:")
    print(json.dumps(export_data, indent=2))
    
    # Build and run C# TdbDump project
    try:
        openrails_path = r'C:\Users\jared\main\openrails\Source\TdbDump\primitives.json'
        with open(openrails_path, 'w') as f:
            json.dump(export_data, f, indent=2)
        print(f"\nAlso exported to {openrails_path}")
        
        print("\n" + "=" * 80)
        print("STEP 6: Building C# project")
        print("=" * 80)
        
        subprocess.run(
            r'cd /d C:\Users\jared\main\openrails\Source\TdbDump && dotnet build -c Debug && .\bin\Debug\TdbDump.exe',
            shell=True,
            check=True
        )
    except Exception as e:
        print(f"\nWarning: Could not build C# project: {e}")
    
    print("\n" + "=" * 80)
    print("COMPLETE")
    print("=" * 80)


if __name__ == '__main__':
    main()
