"""Extract strict, optionally G1-refined Open Rails primitives from GeoJSON."""

import copy
import json
import numpy as np
import config

from circle_fitter import (
    calculate_chained_reconstruction_errors,
    is_overfragmented_segmentation,
    latlons_to_cartesian,
    refine_segments_chained,
    segment_polyline_model_selection,
)


# Existing settings
GEOJSON_FILE = config.GEOJSON_FILE
TARGET_OBJECTID = config.TARGET_OBJECTID
STRAIGHT_TOLERANCE = config.STRAIGHT_TOLERANCE
CIRCLE_TOLERANCE = config.CIRCLE_TOLERANCE
INITIAL_SEGMENT_SIZE = config.INITIAL_SEGMENT_SIZE
MIN_SEGMENT_SIZE = config.MIN_SEGMENT_SIZE
PRIMITIVES_OUTPUT = config.PRIMITIVES_OUTPUT
MAX_STRAIGHT_LENGTH = config.MAX_STRAIGHT_LENGTH

# New optional settings. Existing config.py files continue to work unchanged.
STRAIGHT_MAX_TOLERANCE = getattr(config, "STRAIGHT_MAX_TOLERANCE", STRAIGHT_TOLERANCE)
CIRCLE_MAX_TOLERANCE = getattr(config, "CIRCLE_MAX_TOLERANCE", CIRCLE_TOLERANCE)
MAX_CIRCLE_RADIUS = getattr(config, "MAX_CIRCLE_RADIUS", 100000.0)
MIN_CURVE_SWEEP_DEGREES = getattr(config, "MIN_CURVE_SWEEP_DEGREES", 1.0)
MIN_CURVE_SAGITTA = getattr(config, "MIN_CURVE_SAGITTA", 0.25)
CURVE_IMPROVEMENT_RATIO = getattr(config, "CURVE_IMPROVEMENT_RATIO", 0.65)
ROBUST_CIRCLE_FIT = getattr(config, "ROBUST_CIRCLE_FIT", False)
CHAINED_REFINEMENT = getattr(config, "CHAINED_REFINEMENT", True)
CHAINED_ROBUST_SCALE = getattr(config, "CHAINED_ROBUST_SCALE", 2.0)
BOUNDARY_BACKTRACK_POINTS = getattr(config, "BOUNDARY_BACKTRACK_POINTS", 1)
MIN_CURVE_POINTS = getattr(config, "MIN_CURVE_POINTS", 5)


def split_long_straights(segments, max_length=2048.0):
    """Split straight primitives evenly without dropping boundary distances."""
    output = []
    for segment in segments:
        if segment["type"] != "straight" or segment["length"] <= max_length:
            output.append(segment)
            continue

        chunks = int(np.ceil(segment["length"] / max_length))
        chunk_length = segment["length"] / chunks
        start = np.asarray(segment["points"][0], dtype=float)
        end = np.asarray(segment["points"][-1], dtype=float)

        for index in range(chunks):
            a = index / chunks
            b = (index + 1) / chunks
            chunk = dict(segment)
            chunk["points"] = np.vstack((start + a * (end - start), start + b * (end - start)))
            chunk["point_indices"] = [segment["start_index"], segment["end_index"]]
            chunk["point_count"] = 2
            chunk["length"] = float(chunk_length)
            output.append(chunk)

    for number, segment in enumerate(output, 1):
        segment["segment_number"] = number
    return output


def extract_primitive_from_segment(segment, segment_number):
    if segment["type"] == "straight":
        return {
            "segment_number": segment_number,
            "type": "straight",
            "length": float(segment["length"]),
            "radius": 0.0,
            "angle": float(segment["length"]),
            "clockwise": False,
            "rms_error": float(segment.get("rms_error", 0.0)),
            "max_error": float(segment.get("max_error", 0.0)),
            "point_count": int(segment.get("point_count", 0)),
        }
    return {
        "segment_number": segment_number,
        "type": "curve",
        "radius": float(segment["radius"]),
        "angle": float(segment["angle"]),
        "arc_length": float(segment["radius"] * segment["angle"]),
        "clockwise": bool(segment["clockwise"]),
        "rms_error": float(segment.get("rms_error", 0.0)),
        "max_error": float(segment.get("max_error", 0.0)),
        "point_count": int(segment.get("point_count", 0)),
    }


def _load_target_coordinates():
    with open(GEOJSON_FILE, "r", encoding="utf-8") as handle:
        data = json.load(handle)

    for feature in data["features"]:
        if feature.get("properties", {}).get("OBJECTID") == TARGET_OBJECTID:
            coordinates = feature["geometry"]["coordinates"]
            # Preserve the direction used by the original pipeline.
            return list(reversed(coordinates))
    raise ValueError(f"Could not find OBJECTID {TARGET_OBJECTID}")


def extract_primitives():
    latlons = _load_target_coordinates()
    points, _ = latlons_to_cartesian(latlons)

    print(f"Loaded {len(points)} vertices for OBJECTID {TARGET_OBJECTID}")
    print(
        "Limits: "
        f"straight RMS={STRAIGHT_TOLERANCE:g}m/max={STRAIGHT_MAX_TOLERANCE:g}m, "
        f"circle RMS={CIRCLE_TOLERANCE:g}m/max={CIRCLE_MAX_TOLERANCE:g}m"
    )

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
    print(
        "Chained error before refinement: "
        f"RMS={before['rms_error']:.3f}m, max={before['max_error']:.3f}m, "
        f"endpoint={before['final_endpoint_error']:.3f}m"
    )

    if CHAINED_REFINEMENT and not is_overfragmented_segmentation(segments, points):
        refined_segments = refine_segments_chained(
            points,
            copy.deepcopy(segments),
            robust_scale=CHAINED_ROBUST_SCALE,
        )
        after = calculate_chained_reconstruction_errors(points, refined_segments)
        print(
            "Chained error after refinement:  "
            f"RMS={after['rms_error']:.3f}m, max={after['max_error']:.3f}m, "
            f"endpoint={after['final_endpoint_error']:.3f}m"
        )
        if after["rms_error"] <= before["rms_error"] + 1e-9:
            segments = refined_segments
        else:
            print("Refinement rejected because it increased chained RMS error")
    elif CHAINED_REFINEMENT:
        print(
            "Skipping chained refinement: over-fragmented segmentation "
            f"({len(segments)} segments)"
        )

    # Validate before splitting: splitting does not change the rendered geometry.
    segments = split_long_straights(segments, MAX_STRAIGHT_LENGTH)
    primitives = [
        extract_primitive_from_segment(segment, number)
        for number, segment in enumerate(segments, 1)
    ]

    for primitive in primitives:
        if primitive["type"] == "straight":
            details = f"length={primitive['length']:.2f}m"
        else:
            direction = "CW" if primitive["clockwise"] else "CCW"
            details = (
                f"radius={primitive['radius']:.2f}m, "
                f"sweep={np.degrees(primitive['angle']):.3f}deg, {direction}"
            )
        print(
            f"{primitive['segment_number']:>3}. {primitive['type']:<8} {details}; "
            f"fit RMS={primitive['rms_error']:.3f}m, max={primitive['max_error']:.3f}m"
        )

    export_data = {
        "segments": [
            {
                "type": primitive["type"],
                "radius": round(primitive["radius"], 2),
                "angle": round(primitive["angle"], 6),
                "clockwise": bool(primitive["clockwise"]),
                **(
                    {"length": round(primitive["length"], 2)}
                    if primitive["type"] == "straight"
                    else {}
                ),
            }
            for primitive in primitives
        ]
    }

    with open(PRIMITIVES_OUTPUT, "w", encoding="utf-8") as handle:
        json.dump(export_data, handle, indent=2)
    print(f"Exported {len(primitives)} primitives to {PRIMITIVES_OUTPUT}")
    return export_data


if __name__ == "__main__":
    extract_primitives()