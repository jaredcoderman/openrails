"""Railway polyline fitting with strict line/arc segmentation.

Important properties of this implementation:
* Circle fitting uses a Taubin SVD initializer followed by geometric refinement.
* Candidate segments must pass both RMS and maximum-error limits.
* A failed initial window is never silently accepted.
* Arc direction and sweep are determined from all ordered points.
* Arc sampling handles the -pi/pi boundary correctly.
* Optional chained refinement optimizes primitives in their rendered G1-continuous form.
"""

from __future__ import annotations

import numpy as np
from scipy.optimize import least_squares

try:
    from pyproj import Transformer
except ImportError:
    Transformer = None

try:
    from config import FLIP_X_COORDINATES
except ImportError:
    FLIP_X_COORDINATES = False


EPS = 1e-12


# ---------------------------------------------------------------------------
# Coordinate conversion
# ---------------------------------------------------------------------------

def latlons_to_cartesian(latlons):
    """Convert ``(longitude, latitude)`` pairs to UTM coordinates in meters."""
    if not latlons:
        raise ValueError("Cannot convert an empty coordinate list")
    if Transformer is None:
        raise ImportError("latlons_to_cartesian requires pyproj: pip install pyproj")

    center_lon, center_lat = latlons[0]
    utm_zone = int((center_lon + 180.0) / 6.0) + 1
    epsg = 32600 + utm_zone if center_lat >= 0 else 32700 + utm_zone
    transformer = Transformer.from_crs("EPSG:4326", f"EPSG:{epsg}", always_xy=True)

    points = np.asarray(
        [transformer.transform(lon, lat) for lon, lat in latlons],
        dtype=float,
    )
    if FLIP_X_COORDINATES:
        points[:, 0] *= -1.0
    return points, transformer


def remove_consecutive_duplicates(points, minimum_spacing=1e-6):
    """Remove consecutive vertices closer than ``minimum_spacing`` meters."""
    points = np.asarray(points, dtype=float)
    if len(points) < 2:
        return points.copy()
    keep = np.r_[True, np.linalg.norm(np.diff(points, axis=0), axis=1) > minimum_spacing]
    return points[keep]


# ---------------------------------------------------------------------------
# Straight fitting
# ---------------------------------------------------------------------------

def fit_line_pca(points):
    """Return the total-least-squares line through a set of 2-D points."""
    points = np.asarray(points, dtype=float)
    if len(points) < 2:
        raise ValueError("Need at least two points to fit a line")

    centroid = points.mean(axis=0)
    centered = points - centroid
    covariance = centered.T @ centered / max(len(points) - 1, 1)
    eigenvalues, eigenvectors = np.linalg.eigh(covariance)
    direction = eigenvectors[:, np.argmax(eigenvalues)]

    # PCA eigenvectors have arbitrary sign. Preserve polyline travel direction.
    if np.dot(direction, points[-1] - points[0]) < 0:
        direction = -direction
    direction /= max(np.linalg.norm(direction), EPS)
    normal = np.array([-direction[1], direction[0]])
    return {"point": centroid, "direction": direction, "normal": normal}


def calculate_line_errors(points, line_fit):
    """Return RMS and per-point perpendicular distances to a fitted line."""
    points = np.asarray(points, dtype=float)
    distances = np.abs((points - line_fit["point"]) @ line_fit["normal"])
    rms = float(np.sqrt(np.mean(distances ** 2))) if len(distances) else 0.0
    return rms, distances


# ---------------------------------------------------------------------------
# Circle fitting
# ---------------------------------------------------------------------------

def _kasa_initial_guess(points):
    """Fallback algebraic circle initializer for degenerate Taubin cases."""
    x, y = points[:, 0], points[:, 1]
    matrix = np.column_stack((2.0 * x, 2.0 * y, np.ones(len(points))))
    rhs = x * x + y * y
    cx, cy, constant = np.linalg.lstsq(matrix, rhs, rcond=None)[0]
    radius_sq = constant + cx * cx + cy * cy
    if not np.isfinite(radius_sq) or radius_sq <= EPS:
        raise ValueError("Points do not define a stable circle")
    return np.array([cx, cy]), float(np.sqrt(radius_sq))


def _taubin_svd_initial_guess(points):
    """Taubin's SVD algebraic circle fit on centered/scaled coordinates."""
    centroid = points.mean(axis=0)
    x = points[:, 0] - centroid[0]
    y = points[:, 1] - centroid[1]
    z = x * x + y * y
    z_mean = float(np.mean(z))
    if z_mean <= EPS:
        raise ValueError("Coincident points cannot define a circle")

    z0 = (z - z_mean) / (2.0 * np.sqrt(z_mean))
    design = np.column_stack((z0, x, y))
    _, _, vt = np.linalg.svd(design, full_matrices=False)
    vector = vt[-1]

    a0 = vector[0] / (2.0 * np.sqrt(z_mean))
    a1 = vector[1]
    a2 = vector[2]
    a3 = -z_mean * a0
    if abs(a0) <= EPS:
        raise ValueError("Taubin fit is numerically degenerate")

    center_local = -np.array([a1, a2]) / (2.0 * a0)
    radius_sq = (
        a1 * a1 + a2 * a2 - 4.0 * a0 * a3
    ) / (4.0 * a0 * a0)
    if not np.isfinite(radius_sq) or radius_sq <= EPS:
        raise ValueError("Taubin fit produced an invalid radius")
    return centroid + center_local, float(np.sqrt(radius_sq))


def fit_circle_taubin(points, robust=False, refine=True, max_nfev=2000):
    """Fit a circle using Taubin initialization plus optional geometric refinement.

    Coordinates are normalized before optimization, which is important for UTM
    coordinates and for shallow, large-radius railway arcs.

    ``refine=False`` returns the algebraic (Taubin/Kasa) seed only. That is much
    cheaper and is used while greedily growing candidates; accepted segments are
    re-fit with ``refine=True`` so final geometry stays geometrically refined.
    """
    points = np.asarray(points, dtype=float)
    if len(points) < 3:
        raise ValueError("Need at least three points to fit a circle")

    origin = points.mean(axis=0)
    local = points - origin
    scale = float(np.max(np.linalg.norm(local, axis=1)))
    if scale <= EPS:
        raise ValueError("Coincident points cannot define a circle")
    normalized = local / scale

    try:
        center0, radius0 = _taubin_svd_initial_guess(normalized)
    except (ValueError, np.linalg.LinAlgError):
        center0, radius0 = _kasa_initial_guess(normalized)

    if not refine:
        return {
            "center": origin + scale * np.asarray(center0, dtype=float),
            "radius": float(scale * radius0),
            "optimizer_success": False,
        }

    def residuals(parameters):
        cx, cy, log_radius = parameters
        radius = np.exp(log_radius)
        distances = np.linalg.norm(normalized - np.array([cx, cy]), axis=1)
        return distances - radius

    result = least_squares(
        residuals,
        x0=np.array([center0[0], center0[1], np.log(max(radius0, EPS))]),
        x_scale="jac",
        loss="soft_l1" if robust else "linear",
        f_scale=max(1.0 / scale, 1e-6),
        max_nfev=max_nfev,
    )
    if not result.success or not np.all(np.isfinite(result.x)):
        raise ValueError(f"Circle refinement failed: {result.message}")

    center = origin + scale * result.x[:2]
    radius = scale * np.exp(result.x[2])
    return {
        "center": center,
        "radius": float(radius),
        "optimizer_success": bool(result.success),
    }


def calculate_rms_error(points, fit):
    """Return RMS and signed radial residuals for a circle fit."""
    points = np.asarray(points, dtype=float)
    errors = np.linalg.norm(points - fit["center"], axis=1) - fit["radius"]
    rms = float(np.sqrt(np.mean(errors ** 2))) if len(errors) else 0.0
    return rms, errors


# ---------------------------------------------------------------------------
# Arc parameters and sampling
# ---------------------------------------------------------------------------

def compute_arc_parameters(center, radius, start_point, end_point, points=None):
    """Calculate ordered sweep and direction, preferably using all arc points."""
    del radius  # Retained in the public signature for compatibility.
    center = np.asarray(center, dtype=float)
    start_point = np.asarray(start_point, dtype=float)
    end_point = np.asarray(end_point, dtype=float)

    ordered = (
        np.asarray(points, dtype=float)
        if points is not None and len(points) >= 2
        else np.vstack((start_point, end_point))
    )
    angles = np.unwrap(np.arctan2(ordered[:, 1] - center[1], ordered[:, 0] - center[0]))
    signed_sweep = float(angles[-1] - angles[0])
    clockwise = signed_sweep < 0.0
    sweep = abs(signed_sweep)
    start_angle = float(angles[0])

    steps = np.diff(angles)
    if len(steps):
        expected_sign = -1.0 if clockwise else 1.0
        monotonic_fraction = float(np.mean(expected_sign * steps >= -1e-8))
    else:
        monotonic_fraction = 1.0

    return {
        "start_angle": start_angle,
        "end_angle": start_angle + signed_sweep,
        "signed_sweep": signed_sweep,
        "sweep_angle": sweep,
        "clockwise": bool(clockwise),
        "monotonic_fraction": monotonic_fraction,
    }


def generate_arc_points(center, radius, start_angle, end_angle, clockwise, num_samples=100):
    """Sample the minor ordered arc without a -pi/pi wrapping failure."""
    if clockwise:
        sweep = (start_angle - end_angle) % (2.0 * np.pi)
        direction = -1.0
    else:
        sweep = (end_angle - start_angle) % (2.0 * np.pi)
        direction = 1.0
    angles = start_angle + direction * np.linspace(0.0, sweep, num_samples)
    return np.asarray(center) + radius * np.column_stack((np.cos(angles), np.sin(angles)))


# ---------------------------------------------------------------------------
# Strict candidate generation
# ---------------------------------------------------------------------------

def _straight_result(points, indices):
    pts = points[indices]
    fit = fit_line_pca(pts)
    rms, errors = calculate_line_errors(pts, fit)
    return {
        "type": "straight",
        "indices": list(indices),
        "point_count": len(indices),
        "rms_error": float(rms),
        "max_error": float(np.max(errors)) if len(errors) else 0.0,
        "length": float(np.linalg.norm(pts[-1] - pts[0])),
        "fit": fit,
    }


def _curve_result(points, indices, robust=False, refine=True):
    pts = points[indices]
    fit = fit_circle_taubin(pts, robust=robust, refine=refine)
    rms, errors = calculate_rms_error(pts, fit)
    arc = compute_arc_parameters(fit["center"], fit["radius"], pts[0], pts[-1], pts)
    return {
        "type": "curve",
        "indices": list(indices),
        "point_count": len(indices),
        "radius": float(fit["radius"]),
        "rms_error": float(rms),
        "max_error": float(np.max(np.abs(errors))) if len(errors) else 0.0,
        "angle": float(arc["sweep_angle"]),
        "arc_length": float(fit["radius"] * arc["sweep_angle"]),
        "clockwise": bool(arc["clockwise"]),
        "fit": fit,
        "arc_params": arc,
    }


def _grow_straight(points, start, rms_limit, max_limit, minimum_points):
    best = None
    first_end = start + max(2, minimum_points) - 1
    for end in range(first_end, len(points)):
        try:
            candidate = _straight_result(points, range(start, end + 1))
        except (ValueError, np.linalg.LinAlgError):
            break
        if candidate["rms_error"] > rms_limit or candidate["max_error"] > max_limit:
            break
        best = candidate
    return best


def _grow_curve(
    points,
    start,
    rms_limit,
    max_limit,
    minimum_points,
    max_radius,
    min_sweep,
    min_sagitta,
    robust,
):
    best = None
    first_end = start + max(3, minimum_points) - 1
    for end in range(first_end, len(points)):
        try:
            # Algebraic-only during growth: full geometric LS is the hot path and
            # is re-run once on the accepted index range.
            candidate = _curve_result(
                points, range(start, end + 1), robust=robust, refine=False
            )
        except (ValueError, np.linalg.LinAlgError, OverflowError):
            # Before the first valid circle, keep looking — shallow prefixes are
            # often degenerate. After a valid best exists, stop: later ends are
            # not worth O(n) failed fits on noisy/fragmented polylines.
            if best is None:
                continue
            break

        radius = candidate["radius"]
        sweep = candidate["angle"]
        sagitta = radius * (1.0 - np.cos(0.5 * sweep))
        # Error, reversal, or a major arc marks a real boundary. Minimum sweep
        # and sagitta only determine when the growing candidate is mature enough
        # to store; they must not stop a shallow arc from continuing to grow.
        if (
            candidate["rms_error"] > rms_limit
            or candidate["max_error"] > max_limit
            or sweep > np.pi
            or candidate["arc_params"]["monotonic_fraction"] < 0.8
        ):
            break
        if radius <= max_radius and sweep >= min_sweep and sagitta >= min_sagitta:
            best = candidate
    return best


def _choose_model(straight, curve, curve_improvement_ratio):
    if straight is None:
        return curve
    if curve is None:
        return straight
    if curve["point_count"] > straight["point_count"]:
        return curve
    if straight["point_count"] > curve["point_count"]:
        return straight

    # On equal coverage, prefer the simpler line unless the circle is materially better.
    if straight["rms_error"] <= EPS:
        return straight
    if curve["rms_error"] <= curve_improvement_ratio * straight["rms_error"]:
        return curve
    return straight


def _candidate_end_heading(candidate, points):
    """Travel-direction tangent heading at a candidate's final endpoint."""
    indices = candidate["indices"]
    if candidate["type"] == "straight":
        direction = candidate["fit"]["direction"]
        return float(np.arctan2(direction[1], direction[0]))
    end = points[indices[-1]]
    center = candidate["fit"]["center"]
    radial = np.arctan2(end[1] - center[1], end[0] - center[0])
    return float(radial - np.pi / 2.0 if candidate["clockwise"] else radial + np.pi / 2.0)


def _angle_difference(a, b):
    return abs(float(np.arctan2(np.sin(a - b), np.cos(a - b))))


def segment_polyline_model_selection(
    points,
    straight_tolerance,
    circle_tolerance,
    initial_segment_size=10,
    min_segment_size=3,
    straight_max_tolerance=None,
    circle_max_tolerance=None,
    max_circle_radius=100000.0,
    min_curve_sweep_degrees=1.0,
    min_curve_sagitta=0.25,
    curve_improvement_ratio=0.65,
    robust_circle_fit=False,
    boundary_backtrack_points=1,
    min_curve_points=5,
):
    """Greedily partition a polyline using only candidates that passed limits.

    ``initial_segment_size`` is retained for call compatibility. Unlike the old
    implementation, it is not forced into the first candidate; doing that was
    the source of invalid 10-point segments being silently accepted.
    """
    del initial_segment_size
    points = remove_consecutive_duplicates(points)
    if len(points) < 2:
        raise ValueError("Polyline must contain at least two distinct points")

    straight_max = straight_tolerance if straight_max_tolerance is None else straight_max_tolerance
    circle_max = circle_tolerance if circle_max_tolerance is None else circle_max_tolerance
    min_sweep = np.radians(min_curve_sweep_degrees)
    segments = []
    start = 0

    while start < len(points) - 1:
        straight = _grow_straight(
            points, start, straight_tolerance, straight_max, min_segment_size
        )
        curve = _grow_curve(
            points,
            start,
            circle_tolerance,
            circle_max,
            max(min_segment_size, min_curve_points),
            max_circle_radius,
            min_sweep,
            min_curve_sagitta,
            robust_circle_fit,
        )
        winner = _choose_model(straight, curve, curve_improvement_ratio)

        # A two-point straight is an exact, safe fallback and guarantees progress.
        if winner is None:
            winner = _straight_result(points, [start, start + 1])

        # Greedy growth can absorb the first vertex of the next feature. Test a
        # few nearby boundaries and keep the one whose fitted end tangent best
        # matches the outgoing source chord. Unlike unconditional backtracking,
        # this preserves an already-correct arc-to-tangent boundary.
        backtrack = max(0, int(boundary_backtrack_points))
        winner_indices = winner["indices"]
        if backtrack and winner_indices[-1] < len(points) - 1:
            boundary_candidates = [winner]
            for amount in range(1, backtrack + 1):
                shortened = winner_indices[:-amount]
                try:
                    if winner["type"] == "curve" and len(shortened) >= max(3, min_curve_points):
                        boundary_candidates.append(
                            _curve_result(points, shortened, robust=robust_circle_fit)
                        )
                    elif winner["type"] == "straight" and len(shortened) >= 2:
                        boundary_candidates.append(_straight_result(points, shortened))
                except (ValueError, np.linalg.LinAlgError, OverflowError):
                    pass

            def join_mismatch(candidate):
                end = candidate["indices"][-1]
                outgoing = points[end + 1] - points[end]
                outgoing_heading = np.arctan2(outgoing[1], outgoing[0])
                return _angle_difference(
                    _candidate_end_heading(candidate, points), outgoing_heading
                )

            winner = min(boundary_candidates, key=join_mismatch)

        # Growth used algebraic circle seeds; lock in a geometric refine for the
        # accepted index range so exported primitives stay as accurate as before.
        if winner["type"] == "curve":
            try:
                winner = _curve_result(
                    points, winner["indices"], robust=robust_circle_fit, refine=True
                )
            except (ValueError, np.linalg.LinAlgError, OverflowError):
                pass

        indices = winner.pop("indices")
        segment = {
            "segment_number": len(segments) + 1,
            "start_index": indices[0],
            "end_index": indices[-1],
            "point_indices": indices,
            "points": points[indices],
            **winner,
        }
        segments.append(segment)
        start = indices[-1]  # Adjacent primitives deliberately share one endpoint.

    return segments


# ---------------------------------------------------------------------------
# G1-continuous chained refinement and validation
# ---------------------------------------------------------------------------

def _initial_heading(segment):
    pts = segment["points"]
    if segment["type"] == "straight":
        delta = pts[-1] - pts[0]
        return float(np.arctan2(delta[1], delta[0]))

    radial_angle = np.arctan2(
        pts[0, 1] - segment["fit"]["center"][1],
        pts[0, 0] - segment["fit"]["center"][0],
    )
    return float(radial_angle - np.pi / 2.0 if segment["clockwise"] else radial_angle + np.pi / 2.0)


def _fractions_along(points):
    if len(points) <= 1:
        return np.zeros(len(points))
    distances = np.linalg.norm(np.diff(points, axis=0), axis=1)
    cumulative = np.r_[0.0, np.cumsum(distances)]
    if cumulative[-1] <= EPS:
        return np.linspace(0.0, 1.0, len(points))
    return cumulative / cumulative[-1]


def _render_from_parameters(start, heading, segment, values, fractions):
    if segment["type"] == "straight":
        length = np.exp(values[0])
        direction = np.array([np.cos(heading), np.sin(heading)])
        rendered = start + fractions[:, None] * length * direction
        return rendered, rendered[-1], heading

    radius = np.exp(values[0])
    sweep = np.exp(values[1])
    sign = -1.0 if segment["clockwise"] else 1.0
    curvature = sign / radius
    delta = sign * sweep * fractions
    rendered = np.column_stack((
        start[0] + (np.sin(heading + delta) - np.sin(heading)) / curvature,
        start[1] + (-np.cos(heading + delta) + np.cos(heading)) / curvature,
    ))
    return rendered, rendered[-1], heading + sign * sweep


def refine_segments_chained(
    points,
    segments,
    robust_scale=2.0,
    regularization=0.03,
    max_nfev=500,
):
    """Jointly refine primitive lengths/radii/sweeps as one connected track.

    The first source point is fixed. The initial heading and every primitive
    parameter are optimized together, so the result has continuous endpoints
    and tangents exactly like a sequential Open Rails reconstruction.
    """
    if not segments:
        return segments
    points = np.asarray(points, dtype=float)
    heading0 = _initial_heading(segments[0])
    x0 = [heading0]
    lower = [heading0 - np.pi]
    upper = [heading0 + np.pi]

    for segment in segments:
        if segment["type"] == "straight":
            value = max(float(segment["length"]), 1e-3)
            x0.append(np.log(value))
            lower.append(np.log(max(value * 0.2, 1e-4)))
            upper.append(np.log(value * 5.0))
        else:
            radius = max(float(segment["radius"]), 1e-3)
            sweep = np.clip(float(segment["angle"]), 1e-5, np.pi)
            x0.extend((np.log(radius), np.log(sweep)))
            lower.extend((np.log(max(radius * 0.2, 1e-4)), np.log(max(sweep * 0.2, 1e-6))))
            upper.extend((np.log(radius * 5.0), np.log(min(np.pi, sweep * 5.0))))

    x0 = np.asarray(x0)
    lower = np.asarray(lower)
    upper = np.maximum(np.asarray(upper), lower + 1e-9)

    # Precompute once — used on every residual evaluation.
    segment_fractions = [_fractions_along(segment["points"]) for segment in segments]
    source_stack = np.vstack([segment["points"] for segment in segments])

    def render_all(parameters):
        position = points[0].copy()
        heading = parameters[0]
        cursor = 1
        rendered_parts = []
        for segment, fractions in zip(segments, segment_fractions):
            count = 1 if segment["type"] == "straight" else 2
            values = parameters[cursor:cursor + count]
            cursor += count
            rendered, position, heading = _render_from_parameters(
                position, heading, segment, values, fractions
            )
            rendered_parts.append(rendered)
        return np.vstack(rendered_parts)

    def objective(parameters):
        rendered = render_all(parameters)
        geometric = (rendered - source_stack).ravel()
        penalty = np.sqrt(regularization) * (parameters - x0)
        return np.r_[geometric, penalty]

    result = least_squares(
        objective,
        x0=x0,
        bounds=(lower, upper),
        loss="soft_l1",
        f_scale=max(float(robust_scale), 1e-3),
        x_scale="jac",
        max_nfev=max_nfev,
    )

    parameters = result.x
    segments[0]["chained_initial_heading"] = float(parameters[0])
    cursor = 1
    for segment in segments:
        if segment["type"] == "straight":
            segment["length"] = float(np.exp(parameters[cursor]))
            cursor += 1
        else:
            segment["radius"] = float(np.exp(parameters[cursor]))
            segment["angle"] = float(np.exp(parameters[cursor + 1]))
            segment["arc_length"] = segment["radius"] * segment["angle"]
            cursor += 2

    return segments


def is_overfragmented_segmentation(segments, points=None):
    """True when model selection shattered into mostly 2-point stubs.

    Chained least-squares is O(segments * points * nfev) and does not recover
    useful geometry on these cases (RMS often stays tens of meters). Skipping
    it keeps extract/fit responsive without hurting well-segmented features.
    """
    if not segments:
        return False
    short = 0
    for segment in segments:
        count = segment.get("point_count")
        if count is None:
            count = len(segment.get("points", []))
        if int(count) <= 2:
            short += 1
    n = len(segments)
    if short >= max(8, int(0.4 * n)):
        return True
    if points is not None and n > max(40, int(0.4 * len(points))):
        return True
    return False


def calculate_chained_reconstruction_errors(points, segments):
    """Measure source vertices against the sequentially rendered primitives."""
    if not segments:
        return {"rms_error": 0.0, "max_error": 0.0, "final_endpoint_error": 0.0}
    points = np.asarray(points, dtype=float)
    position = points[0].copy()
    heading = float(segments[0].get("chained_initial_heading", _initial_heading(segments[0])))
    errors = []

    for segment in segments:
        fractions = _fractions_along(segment["points"])
        if segment["type"] == "straight":
            values = np.array([np.log(max(segment["length"], 1e-9))])
        else:
            values = np.log([max(segment["radius"], 1e-9), max(segment["angle"], 1e-9)])
        rendered, position, heading = _render_from_parameters(
            position, heading, segment, values, fractions
        )
        errors.extend(np.linalg.norm(rendered - segment["points"], axis=1))

    errors = np.asarray(errors)
    return {
        "rms_error": float(np.sqrt(np.mean(errors ** 2))),
        "max_error": float(np.max(errors)),
        "final_endpoint_error": float(np.linalg.norm(position - points[-1])),
    }