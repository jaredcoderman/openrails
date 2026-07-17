"""Select GeoJSON OBJECTIDs that have any vertex inside a lat/lon bounding box."""

import json
from pathlib import Path

import config

# Corner points as (latitude, longitude)
CORNER_A = (47.2633, -110.4884)
CORNER_B = (46.36949, -109.26755)

OUTPUT_FILE = "bbox_objectids.txt"


def _bbox_from_corners(corner_a, corner_b):
    lat_min = min(corner_a[0], corner_b[0])
    lat_max = max(corner_a[0], corner_b[0])
    lon_min = min(corner_a[1], corner_b[1])
    lon_max = max(corner_a[1], corner_b[1])
    return lat_min, lat_max, lon_min, lon_max


def _iter_positions(coordinates):
    """Yield (lon, lat) pairs from nested GeoJSON coordinate arrays."""
    if not coordinates:
        return
    if isinstance(coordinates[0], (int, float)):
        yield coordinates[0], coordinates[1]
        return
    for item in coordinates:
        yield from _iter_positions(item)


def _point_in_bbox(lon, lat, lat_min, lat_max, lon_min, lon_max):
    return lat_min <= lat <= lat_max and lon_min <= lon <= lon_max


def select_objectids(geojson_path, lat_min, lat_max, lon_min, lon_max):
    with open(geojson_path, "r", encoding="utf-8") as handle:
        data = json.load(handle)

    matches = []
    features = data.get("features", [])
    for feature in features:
        props = feature.get("properties") or {}
        object_id = props.get("OBJECTID")
        if object_id is None:
            continue

        geometry = feature.get("geometry") or {}
        coordinates = geometry.get("coordinates")
        for lon, lat in _iter_positions(coordinates):
            if _point_in_bbox(lon, lat, lat_min, lat_max, lon_min, lon_max):
                matches.append(object_id)
                break

    return sorted(matches), len(features)


def main():
    lat_min, lat_max, lon_min, lon_max = _bbox_from_corners(CORNER_A, CORNER_B)
    geojson_path = Path(__file__).resolve().parent / config.GEOJSON_FILE
    output_path = Path(__file__).resolve().parent / OUTPUT_FILE

    matches, feature_count = select_objectids(
        geojson_path, lat_min, lat_max, lon_min, lon_max
    )

    with open(output_path, "w", encoding="utf-8") as handle:
        for object_id in matches:
            handle.write(f"{object_id}\n")

    print(f"GeoJSON: {geojson_path.name}")
    print(f"BBox lat [{lat_min}, {lat_max}], lon [{lon_min}, {lon_max}]")
    print(f"Features scanned: {feature_count}")
    print(f"OBJECTIDs with vertices in bbox: {len(matches)}")
    print(f"Wrote: {output_path}")


if __name__ == "__main__":
    main()
