"""Step 1: load bbox OBJECTIDs into one shared local meter frame and fit each.

Outputs:
  bbox_network.geojson       - selected features in WGS84 for QGIS verification
  bbox_network_local.json    - shared CRS + local points + primitives per OBJECTID
"""

from __future__ import annotations

import copy
import json
from pathlib import Path

import numpy as np
from pyproj import Transformer

import config
from circle_fitter import (
    calculate_chained_reconstruction_errors,
    is_overfragmented_segmentation,
    refine_segments_chained,
    remove_consecutive_duplicates,
    segment_polyline_model_selection,
)
from extract_primitives import (
    BOUNDARY_BACKTRACK_POINTS,
    CHAINED_REFINEMENT,
    CHAINED_ROBUST_SCALE,
    CIRCLE_MAX_TOLERANCE,
    CIRCLE_TOLERANCE,
    CURVE_IMPROVEMENT_RATIO,
    INITIAL_SEGMENT_SIZE,
    MAX_CIRCLE_RADIUS,
    MAX_STRAIGHT_LENGTH,
    MIN_CURVE_POINTS,
    MIN_CURVE_SAGITTA,
    MIN_CURVE_SWEEP_DEGREES,
    MIN_SEGMENT_SIZE,
    ROBUST_CIRCLE_FIT,
    STRAIGHT_MAX_TOLERANCE,
    STRAIGHT_TOLERANCE,
    extract_primitive_from_segment,
    split_long_straights,
)

# Max tip drift before falling back to polyline chords (junction-critical).
MAX_ENDPOINT_ERROR_M = 5.0

OBJECTID_LIST_FILE = "bbox_objectids.txt"
GEOJSON_OUTPUT = "bbox_network.geojson"
LOCAL_JSON_OUTPUT = "bbox_network_local.json"

# Same bbox corners used to pick the OBJECTIDs (lat, lon).
CORNER_A = (47.2633, -110.4884)
CORNER_B = (46.36949, -109.26755)


def _script_dir():
    return Path(__file__).resolve().parent


def _parse_args(argv=None):
    import argparse

    p = argparse.ArgumentParser(
        description="Fit selected GeoJSON OBJECTIDs into bbox_network_local.json"
    )
    p.add_argument("--geojson", type=Path, default=None, help="Source WGS84 GeoJSON")
    p.add_argument(
        "--objectids",
        type=Path,
        default=None,
        help="Text file of OBJECTIDs (one per line)",
    )
    p.add_argument(
        "--output-local",
        type=Path,
        default=None,
        help="Output bbox_network_local.json path",
    )
    p.add_argument(
        "--output-geojson",
        type=Path,
        default=None,
        help="Output WGS84 verification GeoJSON path",
    )
    p.add_argument(
        "--corner-a",
        default=None,
        help="BBox corner A as lat,lon (metadata only)",
    )
    p.add_argument(
        "--corner-b",
        default=None,
        help="BBox corner B as lat,lon (metadata only)",
    )
    return p.parse_args(argv)


def _parse_latlon(text):
    if not text:
        return None
    parts = [p.strip() for p in text.split(",")]
    if len(parts) != 2:
        raise SystemExit(f"Expected lat,lon got: {text}")
    return float(parts[0]), float(parts[1])


def _load_objectids(path):
    objectids = []
    with open(path, "r", encoding="utf-8") as handle:
        for line in handle:
            text = line.strip()
            if not text or text.startswith("#"):
                continue
            objectids.append(int(text))
    return objectids


def _line_coordinates(geometry):
    """Return a single polyline as [(lon, lat), ...] from a GeoJSON geometry."""
    if geometry is None:
        return []
    geom_type = geometry.get("type")
    coordinates = geometry.get("coordinates") or []
    if geom_type == "LineString":
        return [(float(lon), float(lat)) for lon, lat, *_ in coordinates]
    if geom_type == "MultiLineString":
        # Use the longest part so short connector scraps don't replace the main run.
        parts = [
            [(float(lon), float(lat)) for lon, lat, *_ in part]
            for part in coordinates
            if part
        ]
        if not parts:
            return []
        return max(parts, key=len)
    return []


def _shared_utm_epsg(lat, lon):
    zone = int((lon + 180.0) / 6.0) + 1
    return 32600 + zone if lat >= 0 else 32700 + zone


def _build_shared_frame(all_lonlats, flip_x):
    """Build one UTM transform and local origin for every feature."""
    coords = np.asarray(all_lonlats, dtype=float)
    center_lon = 0.5 * (float(coords[:, 0].min()) + float(coords[:, 0].max()))
    center_lat = 0.5 * (float(coords[:, 1].min()) + float(coords[:, 1].max()))
    epsg = _shared_utm_epsg(center_lat, center_lon)
    transformer = Transformer.from_crs("EPSG:4326", f"EPSG:{epsg}", always_xy=True)

    easting, northing = transformer.transform(coords[:, 0], coords[:, 1])
    utm_points = np.column_stack((easting, northing))
    if flip_x:
        utm_points[:, 0] *= -1.0

    origin_easting = float(utm_points[:, 0].min())
    origin_northing = float(utm_points[:, 1].min())
    return {
        "epsg": epsg,
        "center_lon": center_lon,
        "center_lat": center_lat,
        "origin_easting": origin_easting,
        "origin_northing": origin_northing,
        "flip_x": bool(flip_x),
        "transformer": transformer,
    }


def _to_local_points(lonlats, frame):
    coords = np.asarray(lonlats, dtype=float)
    easting, northing = frame["transformer"].transform(coords[:, 0], coords[:, 1])
    points = np.column_stack((easting, northing))
    if frame["flip_x"]:
        points[:, 0] *= -1.0
    points[:, 0] -= frame["origin_easting"]
    points[:, 1] -= frame["origin_northing"]
    return points


def _heading(points):
    if len(points) < 2:
        return 0.0
    delta = points[1] - points[0]
    # Open Rails-style yaw: 0 looks +Z / north-ish; atan2(x, z).
    return float(np.arctan2(delta[0], delta[1]))


def _fallback_chord_primitives(points):
    """Build straight chords through every source polyline vertex.

    Used when the circle/straight model-selection fit drifts too far from the
    tip — better to follow GPS chords than leave a 100m gap at a junction.

    Stats are exact-by-construction (chords hit vertices). Do not score these
    with G1 chained reconstruction: corner kinks look like huge drift there,
    but TrackBuilder can adopt each chord's start_ay while keeping position
    continuous.
    """
    pts = remove_consecutive_duplicates(np.asarray(points, dtype=float))
    if len(pts) < 2:
        raise ValueError("Need at least 2 points for chord fallback")

    segments = []
    for i in range(len(pts) - 1):
        a = pts[i]
        b = pts[i + 1]
        length = float(np.linalg.norm(b - a))
        if length < 1e-3:
            continue
        segment = {
            "type": "straight",
            "length": length,
            "radius": 0.0,
            "angle": length,
            "clockwise": False,
            "rms_error": 0.0,
            "max_error": 0.0,
            "point_count": 2,
            "points": np.asarray([a, b], dtype=float),
            "start_index": i,
            "end_index": i + 1,
        }
        segments.append(segment)

    if not segments:
        raise ValueError("Degenerate polyline for chord fallback")

    segments = split_long_straights(segments, MAX_STRAIGHT_LENGTH)
    primitives = []
    for number, seg in enumerate(segments, 1):
        primitive = extract_primitive_from_segment(seg, number)
        segment_points = np.asarray(seg["points"], dtype=float)
        primitive["start_x"] = float(segment_points[0, 0])
        primitive["start_z"] = float(segment_points[0, 1])
        primitive["start_ay"] = _heading(segment_points)
        primitives.append(primitive)

    fit_stats = {"rms_error": 0.0, "max_error": 0.0, "final_endpoint_error": 0.0}
    return primitives, fit_stats


def _fit_feature(points):
    points = remove_consecutive_duplicates(points)
    if len(points) < 2:
        raise ValueError(f"Need at least 2 points, got {len(points)}")

    # Two-point polylines are valid single straights (common NTAD stubs).
    # The model-selection fitter requires MIN_SEGMENT_SIZE (>=3) points.
    if len(points) < MIN_SEGMENT_SIZE:
        delta = points[-1] - points[0]
        length = float(np.linalg.norm(delta))
        if length < 1e-3:
            raise ValueError(f"Degenerate 2-point feature (length {length:.6f}m)")
        segment = {
            "type": "straight",
            "length": length,
            "radius": 0.0,
            "angle": length,
            "clockwise": False,
            "rms_error": 0.0,
            "max_error": 0.0,
            "point_count": int(len(points)),
            "points": points,
            "start_index": 0,
            "end_index": int(len(points) - 1),
        }
        segments = split_long_straights([segment], MAX_STRAIGHT_LENGTH)
        primitives = []
        for number, seg in enumerate(segments, 1):
            primitive = extract_primitive_from_segment(seg, number)
            segment_points = np.asarray(seg["points"], dtype=float)
            primitive["start_x"] = float(segment_points[0, 0])
            primitive["start_z"] = float(segment_points[0, 1])
            primitive["start_ay"] = _heading(segment_points)
            primitives.append(primitive)
        fit_stats = {"rms_error": 0.0, "max_error": 0.0, "final_endpoint_error": 0.0}
        return primitives, fit_stats

    segments = segment_polyline_model_selection(
        points,
        straight_tolerance=STRAIGHT_TOLERANCE,
        circle_tolerance=CIRCLE_TOLERANCE,
        initial_segment_size=INITIAL_SEGMENT_SIZE,
        min_segment_size=MIN_SEGMENT_SIZE,
        straight_max_tolerance=STRAIGHT_MAX_TOLERANCE,
        circle_max_tolerance=CIRCLE_MAX_TOLERANCE,
        max_circle_radius=MAX_CIRCLE_RADIUS,
        min_curve_sweep_degrees=MIN_CURVE_SWEEP_DEGREES,
        min_curve_sagitta=MIN_CURVE_SAGITTA,
        curve_improvement_ratio=CURVE_IMPROVEMENT_RATIO,
        robust_circle_fit=ROBUST_CIRCLE_FIT,
        boundary_backtrack_points=BOUNDARY_BACKTRACK_POINTS,
        min_curve_points=MIN_CURVE_POINTS,
    )

    before = calculate_chained_reconstruction_errors(points, segments)
    if CHAINED_REFINEMENT and not is_overfragmented_segmentation(segments, points):
        refined = refine_segments_chained(
            points,
            copy.deepcopy(segments),
            robust_scale=CHAINED_ROBUST_SCALE,
        )
        after = calculate_chained_reconstruction_errors(points, refined)
        if after["rms_error"] <= before["rms_error"] + 1e-9:
            segments = refined
            before = after

    segments = split_long_straights(segments, MAX_STRAIGHT_LENGTH)
    primitives = []
    for number, segment in enumerate(segments, 1):
        primitive = extract_primitive_from_segment(segment, number)
        segment_points = np.asarray(segment["points"], dtype=float)
        primitive["start_x"] = float(segment_points[0, 0])
        primitive["start_z"] = float(segment_points[0, 1])
        primitive["start_ay"] = _heading(segment_points)
        primitives.append(primitive)

    # Tip drift breaks junctions (tangled DynTracks). Mid-path RMS alone is not
    # enough to chordify — long features like 1732 can have high RMS yet a
    # usable tip, and vertex chords look worse than a connected curve fit.
    ep = float(before.get("final_endpoint_error", 0.0))
    if ep > MAX_ENDPOINT_ERROR_M:
        fallback, fb_stats = _fallback_chord_primitives(points)
        return fallback, fb_stats

    return primitives, before


def _export_primitive(primitive):
    payload = {
        "type": primitive["type"],
        "radius": round(float(primitive["radius"]), 2),
        "angle": round(float(primitive["angle"]), 6),
        "clockwise": bool(primitive["clockwise"]),
        "rms_error": round(float(primitive.get("rms_error", 0.0)), 4),
        "max_error": round(float(primitive.get("max_error", 0.0)), 4),
        "point_count": int(primitive.get("point_count", 0)),
        # Absolute pose on the shared local frame. C# places each section
        # from these instead of integrating length/angle (which drifts).
        "start": {
            "x": round(float(primitive["start_x"]), 3),
            "z": round(float(primitive["start_z"]), 3),
            "ay": round(float(primitive["start_ay"]), 6),
        },
    }
    if primitive["type"] == "straight":
        payload["length"] = round(float(primitive["length"]), 2)
    return payload


def main(argv=None):
    args = _parse_args(argv)
    root = _script_dir()
    objectid_path = args.objectids or (root / OBJECTID_LIST_FILE)
    geojson_path = args.geojson or (root / config.GEOJSON_FILE)
    qgis_path = args.output_geojson or (root / GEOJSON_OUTPUT)
    local_path = args.output_local or (root / LOCAL_JSON_OUTPUT)
    flip_x = bool(getattr(config, "FLIP_X_COORDINATES", False))
    corner_a = _parse_latlon(args.corner_a) or CORNER_A
    corner_b = _parse_latlon(args.corner_b) or CORNER_B

    objectids = _load_objectids(objectid_path)
    wanted = set(objectids)
    print(f"Loaded {len(objectids)} OBJECTIDs from {objectid_path}")
    print(f"Source GeoJSON: {geojson_path}")

    with open(geojson_path, "r", encoding="utf-8") as handle:
        source = json.load(handle)

    selected = []
    for feature in source.get("features", []):
        object_id = (feature.get("properties") or {}).get("OBJECTID")
        if object_id in wanted:
            selected.append(feature)

    found_ids = {(f.get("properties") or {}).get("OBJECTID") for f in selected}
    missing = sorted(wanted - found_ids)
    if missing:
        print(f"Warning: {len(missing)} OBJECTIDs not found in GeoJSON: {missing[:10]}")

    # QGIS verification layer: original WGS84 geometries for the selected IDs.
    qgis_features = []
    for feature in selected:
        props = dict(feature.get("properties") or {})
        props["OBJECTID"] = props.get("OBJECTID")
        qgis_features.append(
            {
                "type": "Feature",
                "properties": {
                    "OBJECTID": props.get("OBJECTID"),
                    "FRAARCID": props.get("FRAARCID"),
                    "RROWNER1": props.get("RROWNER1"),
                    "TRKRGHTS1": props.get("TRKRGHTS1"),
                },
                "geometry": feature.get("geometry"),
            }
        )

    qgis_path.parent.mkdir(parents=True, exist_ok=True)
    with open(qgis_path, "w", encoding="utf-8") as handle:
        json.dump({"type": "FeatureCollection", "features": qgis_features}, handle)
    print(f"Wrote QGIS layer: {qgis_path} ({len(qgis_features)} features)")

    # Shared local frame from every selected vertex.
    all_lonlats = []
    feature_lonlats = {}
    for feature in selected:
        object_id = (feature.get("properties") or {}).get("OBJECTID")
        lonlats = _line_coordinates(feature.get("geometry"))
        # Keep the same travel direction convention as the single-OBJECTID pipeline.
        lonlats = list(reversed(lonlats))
        feature_lonlats[object_id] = lonlats
        all_lonlats.extend(lonlats)

    if not all_lonlats:
        raise SystemExit("No coordinates found for selected OBJECTIDs")

    frame = _build_shared_frame(all_lonlats, flip_x)
    print(
        f"Shared frame EPSG:{frame['epsg']} "
        f"origin=({frame['origin_easting']:.1f}, {frame['origin_northing']:.1f}) "
        f"flip_x={frame['flip_x']}"
    )

    features_out = []
    for object_id in objectids:
        lonlats = feature_lonlats.get(object_id)
        if not lonlats:
            features_out.append(
                {
                    "objectid": object_id,
                    "error": "not found in GeoJSON",
                }
            )
            continue

        local_points = _to_local_points(lonlats, frame)
        entry = {
            "objectid": object_id,
            "vertex_count": int(len(local_points)),
            "start": {
                "x": round(float(local_points[0, 0]), 3),
                "z": round(float(local_points[0, 1]), 3),
                "ay": round(_heading(local_points), 6),
            },
            "end": {
                "x": round(float(local_points[-1, 0]), 3),
                "z": round(float(local_points[-1, 1]), 3),
            },
            "points_local": [
                [round(float(x), 3), round(float(z), 3)] for x, z in local_points
            ],
        }

        try:
            primitives, errors = _fit_feature(local_points)
            entry["fit"] = {
                "rms_error": round(float(errors["rms_error"]), 4),
                "max_error": round(float(errors["max_error"]), 4),
                "endpoint_error": round(float(errors["final_endpoint_error"]), 4),
            }
            entry["primitives"] = [_export_primitive(p) for p in primitives]
            print(
                f"OBJECTID {object_id}: {len(local_points)} pts, "
                f"{len(primitives)} primitives, "
                f"RMS={errors['rms_error']:.3f}m, "
                f"endpoint={errors['final_endpoint_error']:.3f}m"
            )
        except Exception as exc:
            entry["error"] = str(exc)
            print(f"OBJECTID {object_id}: fit failed ({exc})")

        features_out.append(entry)

    local_path.parent.mkdir(parents=True, exist_ok=True)
    payload = {
        "crs": {
            "epsg": frame["epsg"],
            "center_lon": frame["center_lon"],
            "center_lat": frame["center_lat"],
            "origin_easting": frame["origin_easting"],
            "origin_northing": frame["origin_northing"],
            "flip_x": frame["flip_x"],
            "axes": "x=easting-ish (after flip), z=northing",
        },
        "source": {
            "geojson": str(geojson_path.name),
            "objectid_list": str(objectid_path.name),
            "bbox_corners_latlon": [list(corner_a), list(corner_b)],
        },
        "features": features_out,
    }
    with open(local_path, "w", encoding="utf-8") as handle:
        json.dump(payload, handle, indent=2)
    print(f"Wrote local network JSON: {local_path}")


if __name__ == "__main__":
    main()
