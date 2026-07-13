"""
Circle Fitting and Polyline Segmentation Tool
==============================================

Core module implementing circle and line fitting algorithms for railroad
polyline segmentation into straight lines and circular arcs.

Key components:
  - Coordinate conversion (lat/lon to local Cartesian)
  - Least-squares circle fitting (Taubin method)
  - PCA-based line fitting
  - Model-selection segmentation (straight vs. curve)
  - Arc primitive generation
"""

import json
import numpy as np
from scipy.optimize import least_squares
from pyproj import Transformer
from config import FLIP_X_COORDINATES


# ============================================================================
# COORDINATE CONVERSION
# ============================================================================

def latlons_to_cartesian(latlons):
    """
    Convert a list of (lon, lat) tuples to local Cartesian coordinates in meters.
    
    Uses UTM projection for the local coordinate system. We determine the UTM zone
    based on the first point and project all points to that zone.
    
    Args:
        latlons: List of (lon, lat) tuples
        
    Returns:
        numpy array of shape (N, 2) with [x, y] in meters, and a transformer object
    """
    if len(latlons) == 0:
        raise ValueError("Cannot convert empty coordinate list")
    
    center_lon, center_lat = latlons[0]
    
    # UTM zone determination
    utm_zone = int((center_lon + 180) / 6) + 1
    utm_crs = f"EPSG:{32600 + utm_zone}" if center_lat >= 0 else f"EPSG:{32700 + utm_zone}"
    
    transformer = Transformer.from_crs("EPSG:4326", utm_crs, always_xy=True)
    
    # Convert all points
    cartesian_points = []
    for lon, lat in latlons:
        x, y = transformer.transform(lon, lat)
        cartesian_points.append([x, y])
    
    cartesian_points = np.array(cartesian_points)
    
    # Apply X-coordinate flip if configured
    if FLIP_X_COORDINATES:
        cartesian_points[:, 0] = -cartesian_points[:, 0]
    
    return cartesian_points, transformer


# ============================================================================
# STRAIGHT LINE FITTING
# ============================================================================

def fit_line_pca(points):
    """
    Fit a line to points using PCA (Principal Component Analysis).
    
    The line is represented as:
      - point: a point on the line (centroid of input points)
      - direction: unit vector along the line (first principal component)
    
    Args:
        points: numpy array of shape (N, 2) with [x, y] coordinates
        
    Returns:
        dict with:
          - 'point': [x, y] on the line (centroid)
          - 'direction': [dx, dy] unit vector along line
          - 'normal': [nx, ny] unit vector perpendicular to line
    """
    points = np.asarray(points)
    
    if len(points) < 2:
        raise ValueError("Need at least 2 points to fit a line")
    
    # Center the points
    centroid = np.mean(points, axis=0)
    centered = points - centroid
    
    # Compute covariance matrix
    cov = np.cov(centered.T)
    
    # Find eigenvectors
    eigenvalues, eigenvectors = np.linalg.eig(cov)
    
    # The first eigenvector (largest eigenvalue) is the direction of the line
    idx = np.argsort(eigenvalues)[::-1]
    direction = eigenvectors[:, idx[0]]
    direction = direction / np.linalg.norm(direction)
    
    # Normal is perpendicular to direction
    normal = np.array([-direction[1], direction[0]])
    
    return {
        'point': centroid,
        'direction': direction,
        'normal': normal
    }


def calculate_line_errors(points, line_fit):
    """
    Calculate perpendicular distances from points to fitted line.
    
    Args:
        points: numpy array of shape (N, 2)
        line_fit: dict from fit_line_pca with 'point', 'direction', 'normal'
        
    Returns:
        tuple (rms_error, errors) where:
          - rms_error: root mean square perpendicular distance
          - errors: array of perpendicular distances for each point
    """
    line_point = line_fit['point']
    normal = line_fit['normal']
    
    # Vector from line point to each data point
    v = points - line_point
    
    # Perpendicular distance is the dot product with the normal
    distances = np.abs(np.dot(v, normal))
    
    rms_error = np.sqrt(np.mean(distances ** 2))
    
    return rms_error, distances


# ============================================================================
# CIRCLE FITTING
# ============================================================================

def fit_circle_taubin(points):
    """
    Fit a circle by minimizing the geometric distance of every point to the circle.
    
    Uses Taubin's method with least-squares optimization.
    
    Args:
        points: numpy array of shape (N, 2) with [x, y] coordinates
        
    Returns:
        dict with:
          - 'center': [cx, cy] center point
          - 'radius': circle radius in same units as input
    """
    points = np.asarray(points)

    if len(points) < 3:
        raise ValueError("Need at least 3 points")

    x = points[:, 0]
    y = points[:, 1]

    # Initial guess
    cx0 = np.mean(x)
    cy0 = np.mean(y)
    r0 = np.mean(np.sqrt((x - cx0) ** 2 + (y - cy0) ** 2))

    # Residual function: distance to circle
    def residuals(params):
        cx, cy, r = params
        distances = np.sqrt((x - cx) ** 2 + (y - cy) ** 2)
        return distances - r

    result = least_squares(residuals, x0=[cx0, cy0, r0])
    cx, cy, r = result.x

    return {
        "center": np.array([cx, cy]),
        "radius": r
    }


def calculate_rms_error(points, fit):
    """
    Calculate RMS radial error for a circle fit.
    
    Args:
        points: numpy array of shape (N, 2)
        fit: dict from fit_circle_taubin with 'center' and 'radius'
        
    Returns:
        tuple (rms_error, errors) where:
          - rms_error: root mean square radial distance
          - errors: array of signed radial distances for each point
    """
    center = fit["center"]
    radius = fit["radius"]

    distances = np.linalg.norm(points - center, axis=1)
    errors = distances - radius
    rms = np.sqrt(np.mean(errors ** 2))

    return rms, errors


# ============================================================================
# ARC PRIMITIVE GENERATION
# ============================================================================

def compute_arc_parameters(center, radius, start_point, end_point):
    """
    Compute arc parameters (angles and direction) from circle fit and endpoints.
    
    Args:
        center: numpy array [cx, cy] - circle center
        radius: float - circle radius
        start_point: numpy array [x, y] - first point of segment
        end_point: numpy array [x, y] - last point of segment
        
    Returns:
        dict with:
          'start_angle': angle to start point (radians)
          'end_angle': angle to end point (radians)
          'sweep_angle': absolute rotation angle (radians)
          'clockwise': boolean - True if sweep is clockwise
    """
    cx, cy = center
    
    # Calculate angle from center to start and end points
    start_angle = np.arctan2(start_point[1] - cy, start_point[0] - cx)
    end_angle = np.arctan2(end_point[1] - cy, end_point[0] - cx)
    
    # Determine sweep direction using cross product
    v1 = start_point - center
    v2 = end_point - center
    cross_z = v1[0] * v2[1] - v1[1] * v2[0]
    
    clockwise = cross_z < 0
    
    # Calculate the sweep angle
    if clockwise:
        sweep_angle = start_angle - end_angle
        if sweep_angle < 0:
            sweep_angle += 2 * np.pi
    else:
        sweep_angle = end_angle - start_angle
        if sweep_angle < 0:
            sweep_angle += 2 * np.pi
    
    return {
        'start_angle': start_angle,
        'end_angle': end_angle,
        'sweep_angle': sweep_angle,
        'clockwise': clockwise
    }


def generate_arc_points(center, radius, start_angle, end_angle, clockwise, num_samples=100):
    """
    Generate sampled points along a circular arc.
    
    Args:
        center: numpy array [cx, cy] - circle center
        radius: float - circle radius
        start_angle: float - starting angle in radians
        end_angle: float - ending angle in radians
        clockwise: bool - if True, sweep clockwise; if False, counterclockwise
        num_samples: int - approximately how many points to generate
        
    Returns:
        numpy array of shape (N, 2) with [x, y] points sampled along the arc
    """
    cx, cy = center
    
    if clockwise:
        # Clockwise: angles go from start_angle down to end_angle
        t = np.linspace(0, 1, num_samples)
        angles = start_angle - t * (start_angle - end_angle)
        angles = np.where(angles < -np.pi, angles + 2*np.pi, angles)
        angles = np.where(angles > np.pi, angles - 2*np.pi, angles)
    else:
        # Counterclockwise: angles go from start_angle up to end_angle
        t = np.linspace(0, 1, num_samples)
        angles = start_angle + t * (end_angle - start_angle)
        angles = np.where(angles < -np.pi, angles + 2*np.pi, angles)
        angles = np.where(angles > np.pi, angles - 2*np.pi, angles)
    
    # Convert angles to Cartesian coordinates
    x = cx + radius * np.cos(angles)
    y = cy + radius * np.sin(angles)
    
    arc_points = np.column_stack([x, y])
    
    return arc_points


# ============================================================================
# MODEL-SELECTION BASED SEGMENTATION
# ============================================================================

def _fit_straight_robust(points, indices):
    """
    Build a straight-line fit/error summary for the given indices.
    
    Tries PCA fit first, falls back to chord-based fit if PCA fails.
    Guarantees a straight result is always produced.
    """
    pts = points[indices]
    
    try:
        fit = fit_line_pca(pts)
        rms_error, errors = calculate_line_errors(pts, fit)
    except Exception:
        start, end = pts[0], pts[-1]
        direction = end - start
        norm = np.linalg.norm(direction)
        direction = direction / norm if norm > 1e-9 else np.array([1.0, 0.0])
        normal = np.array([-direction[1], direction[0]])
        fit = {'point': start, 'direction': direction, 'normal': normal}
        v = pts - start
        errors = np.abs(np.dot(v, normal))
        rms_error = np.sqrt(np.mean(errors ** 2)) if len(errors) else 0.0
    
    max_error = float(np.max(errors)) if len(errors) else 0.0
    length = float(np.linalg.norm(pts[-1] - pts[0]))
    
    return {
        'type': 'straight',
        'point_count': len(indices),
        'rms_error': float(rms_error),
        'max_error': max_error,
        'length': length,
        'fit': fit
    }


def _extend_segment_to_index(segment, points, new_end_index):
    """
    Extend an already-accepted segment to cover all points up to new_end_index.
    
    Used to absorb trailing points that are too few to form their own segment,
    guaranteeing that every vertex is covered by exactly one primitive.
    """
    start_index = segment['start_index']
    new_indices = list(range(start_index, new_end_index + 1))
    new_points = points[new_indices]
    
    try:
        if segment['type'] == 'straight':
            fit = fit_line_pca(new_points)
            rms_error, errors = calculate_line_errors(new_points, fit)
            segment['fit'] = fit
            segment['rms_error'] = float(rms_error)
            segment['max_error'] = float(np.max(errors)) if len(errors) else 0.0
            segment['length'] = float(np.linalg.norm(new_points[-1] - new_points[0]))
        else:
            fit = fit_circle_taubin(new_points)
            rms_error, errors = calculate_rms_error(new_points, fit)
            arc_params = compute_arc_parameters(fit['center'], fit['radius'], new_points[0], new_points[-1])
            segment['fit'] = fit
            segment['radius'] = fit['radius']
            segment['rms_error'] = float(rms_error)
            segment['max_error'] = float(np.max(np.abs(errors))) if len(errors) else 0.0
            segment['angle'] = arc_params['sweep_angle']
            segment['arc_length'] = fit['radius'] * arc_params['sweep_angle']
            segment['clockwise'] = arc_params['clockwise']
            segment['arc_params'] = arc_params
    except Exception:
        pass
    
    segment['end_index'] = new_end_index
    segment['point_indices'] = new_indices
    segment['points'] = new_points
    segment['point_count'] = len(new_indices)


def segment_polyline_model_selection(
    points,
    straight_tolerance,
    circle_tolerance,
    initial_segment_size=10,
    min_segment_size=3
):
    """
    Segment a polyline into both straight lines and circular arcs.
    
    For each segment start, attempts both a straight line fit and a circular arc fit.
    Grows both independently until each exceeds its tolerance.
    The model that covers MORE points while staying within tolerance wins.
    
    Guarantees:
      - Every vertex of the input polyline is covered by exactly one segment
      - A straight-line candidate is always available
      - The algorithm never crashes or stalls on pathological input
    
    Args:
        points: numpy array of [x, y] in meters
        straight_tolerance: max RMS perpendicular error for lines (meters)
        circle_tolerance: max RMS radial error for circles (meters)
        initial_segment_size: start with this many points per model
        min_segment_size: minimum points to form a NEW independent segment
        
    Returns:
        List of segment dicts with 'type' field ('straight' or 'curve')
    """
    
    points = np.asarray(points)
    
    if len(points) < 2:
        raise ValueError("Polyline too short")
    
    # Degenerate case: not enough points for one minimal segment
    if len(points) < min_segment_size:
        all_indices = list(range(len(points)))
        straight = _fit_straight_robust(points, all_indices)
        return [{
            'segment_number': 1,
            'type': 'straight',
            'start_index': 0,
            'end_index': len(points) - 1,
            'point_count': len(all_indices),
            'point_indices': all_indices,
            'points': points[all_indices],
            'rms_error': straight['rms_error'],
            'max_error': straight['max_error'],
            'length': straight['length'],
            'fit': straight['fit']
        }]
    
    segments = []
    current_start_idx = 0
    segment_number = 1
    
    print("\n" + "=" * 80)
    print("MODEL-SELECTION BASED POLYLINE SEGMENTATION")
    print("=" * 80)
    
    while current_start_idx < len(points) - 1:
        
        remaining = len(points) - current_start_idx
        
        # Not enough points left to justify starting a new segment
        if remaining < min_segment_size:
            if segments:
                _extend_segment_to_index(segments[-1], points, len(points) - 1)
            else:
                all_indices = list(range(current_start_idx, len(points)))
                straight = _fit_straight_robust(points, all_indices)
                segments.append({
                    'segment_number': segment_number,
                    'type': 'straight',
                    'start_index': all_indices[0],
                    'end_index': all_indices[-1],
                    'point_count': len(all_indices),
                    'point_indices': all_indices,
                    'points': points[all_indices],
                    'rms_error': straight['rms_error'],
                    'max_error': straight['max_error'],
                    'length': straight['length'],
                    'fit': straight['fit']
                })
            break
        
        segment_start_count = min(initial_segment_size, remaining)
        current_indices = list(range(current_start_idx, current_start_idx + segment_start_count))
        
        print(f"\n" + "-" * 80)
        print(f"Starting at point {current_start_idx}")
        print("-" * 80)
        
        # ====================================================================
        # TRY STRAIGHT LINE FIT
        # ====================================================================
        
        current_indices_straight = list(range(current_start_idx, current_start_idx + segment_start_count))
        
        while True:
            try:
                current_points = points[current_indices_straight]
                fit = fit_line_pca(current_points)
                rms_error, errors = calculate_line_errors(current_points, fit)
            except Exception:
                break
            
            if rms_error <= straight_tolerance:
                next_idx = current_indices_straight[-1] + 1
                
                if next_idx >= len(points):
                    break
                
                test_indices = current_indices_straight + [next_idx]
                
                try:
                    test_points = points[test_indices]
                    test_fit = fit_line_pca(test_points)
                    test_rms, _ = calculate_line_errors(test_points, test_fit)
                except Exception:
                    break
                
                if test_rms > straight_tolerance:
                    break
                
                current_indices_straight = test_indices
            else:
                break
        
        straight_result = _fit_straight_robust(points, current_indices_straight)
        straight_indices = current_indices_straight
        
        # ====================================================================
        # TRY CIRCULAR ARC FIT
        # ====================================================================
        
        circle_result = None
        circle_indices = None
        
        try:
            current_indices_circle = list(range(current_start_idx, current_start_idx + segment_start_count))
            
            while True:
                current_points = points[current_indices_circle]
                
                try:
                    fit = fit_circle_taubin(current_points)
                    rms_error, _ = calculate_rms_error(current_points, fit)
                except ValueError:
                    break
                
                if rms_error <= circle_tolerance:
                    next_idx = current_indices_circle[-1] + 1
                    
                    if next_idx >= len(points):
                        break
                    
                    test_indices = current_indices_circle + [next_idx]
                    test_points = points[test_indices]
                    
                    try:
                        test_fit = fit_circle_taubin(test_points)
                        test_rms, _ = calculate_rms_error(test_points, test_fit)
                    except ValueError:
                        break
                    
                    if test_rms > circle_tolerance:
                        break
                    
                    current_indices_circle = test_indices
                else:
                    break
            
            # Store circle result
            current_points = points[current_indices_circle]
            fit = fit_circle_taubin(current_points)
            rms_error, errors = calculate_rms_error(current_points, fit)
            max_error = np.max(np.abs(errors))
            
            start_point = current_points[0]
            end_point = current_points[-1]
            arc_params = compute_arc_parameters(fit['center'], fit['radius'], start_point, end_point)
            
            circle_result = {
                'type': 'curve',
                'point_count': len(current_indices_circle),
                'radius': fit['radius'],
                'rms_error': rms_error,
                'max_error': max_error,
                'angle': arc_params['sweep_angle'],
                'arc_length': fit['radius'] * arc_params['sweep_angle'],
                'clockwise': arc_params['clockwise'],
                'fit': fit,
                'arc_params': arc_params
            }
            circle_indices = current_indices_circle
            
            # Reject circles with unreasonably large sweep angles (> 180°)
            if abs(arc_params['sweep_angle']) > np.pi:
                circle_result = None
                circle_indices = None
            
        except Exception:
            circle_result = None
        
        # ====================================================================
        # PRINT MODEL COMPARISON
        # ====================================================================
        
        if straight_result:
            print("\nStraight:")
            print(f"    accepted points: {straight_result['point_count']}")
            print(f"    length: {straight_result['length']:.1f} m")
            print(f"    RMS: {straight_result['rms_error']:.4f} m")
            print(f"    Max error: {straight_result['max_error']:.4f} m")
        else:
            print("\nStraight: FAILED TO FIT")
        
        if circle_result:
            print("\nCircle:")
            print(f"    accepted points: {circle_result['point_count']}")
            print(f"    radius: {circle_result['radius']:.1f} m")
            print(f"    angle: {np.degrees(circle_result['angle']):.2f}° ({circle_result['angle']:.6f} rad)")
            print(f"    arc length: {circle_result['arc_length']:.1f} m")
            print(f"    RMS: {circle_result['rms_error']:.4f} m")
            print(f"    Max error: {circle_result['max_error']:.4f} m")
        else:
            print("\nCircle: FAILED TO FIT")
        
        # ====================================================================
        # CHOOSE WINNER
        # ====================================================================
        
        winner = None
        winner_indices = None
        
        if straight_result and circle_result:
            straight_rms = straight_result['rms_error']
            circle_rms = circle_result['rms_error']
            straight_count = straight_result['point_count']
            circle_count = circle_result['point_count']
            circle_radius = circle_result['radius']
            
            # Reject circles with extremely large radii (>100km)
            if circle_radius > 100000:
                print(f"\nWinner: STRAIGHT (circle radius {circle_radius:.0f}m is too large)")
                winner = straight_result
                winner_indices = straight_indices
            else:
                # Check if circle has significantly better RMS error
                circle_rms_ratio = circle_rms / straight_rms if straight_rms > 0 else 1.0
                
                if circle_rms_ratio < 0.5:
                    print(f"\nWinner: CURVE (better fit: {circle_rms:.4f}m vs {straight_rms:.4f}m = {circle_rms_ratio:.2%})")
                    winner = circle_result
                    winner_indices = circle_indices
                elif straight_count > circle_count:
                    print(f"\nWinner: STRAIGHT (more points: {straight_count} vs {circle_count})")
                    winner = straight_result
                    winner_indices = straight_indices
                elif circle_count > straight_count:
                    print(f"\nWinner: CURVE (more points: {circle_count} vs {straight_count})")
                    winner = circle_result
                    winner_indices = circle_indices
                else:
                    print(f"\nWinner: CURVE (tie-breaker, both cover {straight_count} points)")
                    winner = circle_result
                    winner_indices = circle_indices
        else:
            print(f"\nWinner: STRAIGHT (circle unavailable)")
            winner = straight_result
            winner_indices = straight_indices
        
        # Safety net: guarantee forward progress
        if winner is None or winner_indices is None or winner_indices[-1] <= current_start_idx:
            forced_indices = list(range(current_start_idx, current_start_idx + max(2, min(segment_start_count, len(points) - current_start_idx))))
            winner = _fit_straight_robust(points, forced_indices)
            winner_indices = forced_indices
            print("\nWinner: STRAIGHT (forced fallback to guarantee progress)")
        
        # ====================================================================
        # BUILD FULL SEGMENT DICT
        # ====================================================================
        
        segment = {
            'segment_number': segment_number,
            'type': winner['type'],
            'start_index': winner_indices[0],
            'end_index': winner_indices[-1],
            'point_count': len(winner_indices),
            'point_indices': winner_indices,
            'points': points[winner_indices],
            'rms_error': winner['rms_error'],
            'max_error': winner['max_error']
        }
        
        if winner['type'] == 'straight':
            segment['length'] = winner['length']
            segment['fit'] = winner['fit']
        else:
            segment['radius'] = winner['radius']
            segment['angle'] = winner['angle']
            segment['arc_length'] = winner['arc_length']
            segment['clockwise'] = winner['clockwise']
            segment['fit'] = winner['fit']
            segment['arc_params'] = winner['arc_params']
        
        segments.append(segment)
        
        current_start_idx = winner_indices[-1]
        segment_number += 1
    
    # Final safety net: verify the last segment reaches the final vertex
    if segments and segments[-1]['end_index'] != len(points) - 1:
        _extend_segment_to_index(segments[-1], points, len(points) - 1)
    
    print(f"\n" + "=" * 80)
    print(f"SEGMENTATION COMPLETE: {len(segments)} segments")
    total_covered = segments[-1]['end_index'] - segments[0]['start_index'] + 1 if segments else 0
    print(f"Coverage: points {segments[0]['start_index']} to {segments[-1]['end_index']} "
          f"({total_covered} / {len(points)} vertices)")
    print("=" * 80)
    
    return segments


if __name__ == '__main__':
    print("Circle Fitter Module")
    print("This module is designed to be imported. See extract_primitives.py for usage.")
