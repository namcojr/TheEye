"""Download a bounded OSM vector sample and generate offline themed SVG previews.

Usage: python tools\\sample-lisbon-map.py --target <output-directory>
       python tools\\sample-lisbon-map.py --target <output-directory> --reuse

Uses Python's standard library only. The sample is not installed into TheEye.
Public Overpass is for evaluation, not a production map streaming service.
"""

import argparse
from collections import Counter
from datetime import datetime, timezone
import gzip
import html
import json
import math
from pathlib import Path
import urllib.parse
import urllib.request


ENDPOINT = 'https://overpass-api.de/api/interpreter'
MAX_RESPONSE_BYTES = 8 * 1024 * 1024
ROAD_BOUNDS = (38.68, -9.24, 38.83, -9.08)
AIRPORT_BOUNDS = (38.759, -9.151, 38.803, -9.120)
QUERY = """[out:json][timeout:60][maxsize:33554432];
wr["aeroway"="aerodrome"]["icao"="LPPT"](38.755,-9.15,38.795,-9.11)->.airport;
.airport map_to_area->.airportArea;
(
  way["highway"~"^(motorway|trunk|primary|motorway_link|trunk_link|primary_link)$"](38.68,-9.24,38.83,-9.08);
  .airport;
  nwr["aeroway"~"^(runway|taxiway|apron|terminal|hangar|gate|parking_position|helipad)$"](area.airportArea);
  way["building"]["building"!="no"](area.airportArea);
  relation["boundary"="administrative"]["admin_level"="7"]["name"="Lisboa"](38.68,-9.24,38.83,-9.08);
);
out geom;
"""
PALETTES = {
    'dark': {'land': '#1B2A35', 'border': '#4A6070',
             'label': '#6FE3F5', 'accent': '#38BDF8'},
    'light': {'land': '#F7F4EA', 'border': '#9AAEBC',
              'label': '#0E6C7C', 'accent': '#0369A1'},
}


def coordinates(points):
    if any(point is None for point in points):
        raise ValueError('Incomplete geometry returned by Overpass.')
    return [[point['lon'], point['lat']] for point in points]


def geometry(element):
    kind = element['type']
    if kind == 'node':
        return {'type': 'Point', 'coordinates': [element['lon'], element['lat']]}
    if kind == 'way':
        points = coordinates(element.get('geometry', []))
        if len(points) < 2:
            raise ValueError(f"Missing geometry for way {element['id']}.")
        tags = element.get('tags', {})
        area = tags.get('area') != 'no' and (
            tags.get('area') == 'yes' or 'building' in tags
            or tags.get('aeroway') in
            ('aerodrome', 'runway', 'taxiway', 'apron', 'terminal', 'hangar', 'helipad'))
        if area and len(points) >= 4 and points[0] == points[-1]:
            return {'type': 'Polygon', 'coordinates': [points]}
        return {'type': 'LineString', 'coordinates': points}

    # Boundary/multipolygon member outlines preserve holes without guessing
    # how to assemble incomplete regional relation rings.
    members = [member for member in element.get('members', [])
               if member['type'] == 'way'
               and member.get('role', '') in ('', 'outer', 'inner')]
    lines = [coordinates(member.get('geometry', [])) for member in members]
    if not lines or any(len(line) < 2 for line in lines):
        raise ValueError(f"Missing member geometry for relation {element['id']}.")
    return {'type': 'MultiLineString', 'coordinates': lines}


def convert(payload):
    if 'remark' in payload:
        raise ValueError(f"Overpass reported an incomplete/error result: {payload['remark']}")
    features = []
    for element in sorted(payload['elements'], key=lambda item: (item['type'], item['id'])):
        tags = element.get('tags', {})
        if tags.get('boundary') == 'administrative':
            layer, category = 'boundaries', 'municipality'
        elif 'aeroway' in tags:
            layer, category = 'airport', tags['aeroway']
        elif 'building' in tags:
            layer, category = 'buildings', tags['building']
        elif 'highway' in tags:
            layer, category = 'roads', tags['highway']
        else:
            raise ValueError(f"Unexpected feature: {element['type']}/{element['id']}")
        properties = {'layer': layer, 'category': category}
        properties.update({key: tags[key] for key in
                           ('name', 'ref', 'icao', 'width', 'surface', 'admin_level')
                           if key in tags})
        features.append({'type': 'Feature',
                         'id': f"{element['type']}/{element['id']}",
                         'properties': properties, 'geometry': geometry(element)})
    return {'type': 'FeatureCollection', 'features': features}


def compact(value):
    return json.dumps(value, ensure_ascii=False, separators=(',', ':')).encode('utf-8')


def project(point):
    lon, lat = point
    return math.radians(lon), math.log(math.tan(math.pi / 4 + math.radians(lat) / 2))


def mix(first, second, ratio):
    channels = [round(int(first[index:index + 2], 16) * (1 - ratio)
                      + int(second[index:index + 2], 16) * ratio)
                for index in (1, 3, 5)]
    return '#' + ''.join(f'{channel:02X}' for channel in channels)


def preview(collection, palette, bounds, title):
    south, west, north, east = bounds
    left, bottom = project([west, south])
    right, top = project([east, north])
    scale = min(1120 / (right - left), 650 / (top - bottom))
    map_width, map_height = (right - left) * scale, (top - bottom) * scale
    origin_x, origin_y = 40 + (1120 - map_width) / 2, 60 + (650 - map_height) / 2

    def screen(point):
        x, y = project(point)
        return origin_x + (x - left) * scale, origin_y + (top - y) * scale

    def path(points, closed=False):
        text = ' '.join(f'{x:.2f},{y:.2f}' for x, y in map(screen, points))
        return f'<{"polygon" if closed else "polyline"} points="{text}"/>'

    content = []
    order = {'airport': 0, 'buildings': 1, 'roads': 2, 'boundaries': 3}
    for feature in sorted(collection['features'],
                          key=lambda item: order[item['properties']['layer']]):
        properties = feature['properties']
        layer, category = properties['layer'], properties['category']
        shape = feature['geometry']
        fill, stroke, width, dash = 'none', palette['border'], 1, ''
        if layer == 'boundaries':
            stroke, width, dash = palette['accent'], 2, ' stroke-dasharray="7 5"'
        elif layer == 'roads':
            stroke, width = palette['label'], 1.5
        elif category == 'runway':
            stroke, width = mix(palette['land'], palette['label'], 0.7), 6
            if shape['type'] == 'Polygon':
                fill, width = stroke, 1
        elif category == 'taxiway':
            stroke, width = mix(palette['land'], palette['label'], 0.5), 2
            if shape['type'] == 'Polygon':
                fill, width = stroke, 1
        elif shape['type'] == 'Polygon':
            fill = mix(palette['land'], palette['border'], 0.3)
        kind, points = shape['type'], shape['coordinates']
        if kind == 'Point':
            x, y = screen(points)
            draw = f'<circle cx="{x:.2f}" cy="{y:.2f}" r="2"/>'
        elif kind == 'LineString':
            draw = path(points)
        elif kind == 'Polygon':
            draw = path(points[0], True)
        else:
            draw = ''.join(path(line) for line in points)
        tooltip = html.escape(f"{category}: {properties.get('name', properties.get('ref', feature['id']))}")
        content.append(f'<g fill="{fill}" stroke="{stroke}" stroke-width="{width}"{dash}>'
                       f'<title>{tooltip}</title>{draw}</g>')
    return (
        '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1200 800">'
        f'<rect width="1200" height="800" fill="{palette["land"]}"/>'
        f'<defs><clipPath id="map"><rect x="{origin_x:.2f}" y="{origin_y:.2f}" '
        f'width="{map_width:.2f}" height="{map_height:.2f}"/>'
        '</clipPath></defs>'
        f'<g fill="{palette["label"]}" font-family="sans-serif">'
        f'<text x="40" y="30" font-size="20">{html.escape(title)}</text></g>'
        '<g clip-path="url(#map)">' + ''.join(content) + '</g>'
        f'<g fill="{palette["label"]}" font-family="sans-serif" font-size="13">'
        '<text x="40" y="745">Dashed: municipal boundary | Lines: major roads / runway / taxiway | Shapes: buildings / aprons</text>'
        '<text x="40" y="767">Vector-only evaluation: no coastline or land-use background; runway widths illustrative.</text>'
        '<text x="40" y="789">Data: OpenStreetMap contributors | ODbL 1.0 | https://www.openstreetmap.org/copyright</text>'
        '</g></svg>')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--target', type=Path, required=True)
    parser.add_argument('--endpoint', default=ENDPOINT)
    parser.add_argument('--reuse', action='store_true',
                        help='regenerate entirely offline from the saved response')
    args = parser.parse_args()
    args.target.mkdir(parents=True, exist_ok=True)
    raw_path = args.target / 'overpass.json'
    transport_path = args.target / 'transport.json'
    if args.reuse:
        raw = raw_path.read_bytes()
        transport = json.loads(transport_path.read_text(encoding='utf-8'))
    else:
        request_body = urllib.parse.urlencode({'data': QUERY}).encode('ascii')
        request = urllib.request.Request(
            args.endpoint, data=request_body,
            headers={'User-Agent': 'TheEye-MapSample/1.0',
                     'Accept-Encoding': 'gzip',
                     'Content-Type': 'application/x-www-form-urlencoded'})
        with urllib.request.urlopen(request, timeout=90) as response:
            body = response.read(MAX_RESPONSE_BYTES + 1)
            if len(body) > MAX_RESPONSE_BYTES:
                raise ValueError(
                    'Response exceeds the 8 MiB sample download limit. '
                    f'Content-Type: {response.headers.get("Content-Type")}; '
                    f'Content-Encoding: {response.headers.get("Content-Encoding", "identity")}')
            encoding = response.headers.get('Content-Encoding', 'identity')
            if encoding not in ('gzip', 'identity'):
                raise ValueError(f'Unsupported content encoding: {encoding}')
            raw = gzip.decompress(body) if encoding == 'gzip' else body
            transport = {'endpoint': args.endpoint,
                         'request_body_bytes': len(request_body),
                         'response_body_bytes': len(body), 'content_encoding': encoding,
                         'downloaded_utc': datetime.now(timezone.utc).isoformat()}
        # Validate before persisting anything that looks like a successful sample.
        convert(json.loads(raw))
        raw_path.write_bytes(raw)
        transport_path.write_bytes(compact(transport))
    payload = json.loads(raw)
    collection = convert(payload)
    features = collection['features']
    for layer in ('boundaries', 'roads', 'airport', 'buildings'):
        if not any(feature['properties']['layer'] == layer for feature in features):
            raise ValueError(f'Required sample layer is empty: {layer}')
    if not any(feature['properties']['category'] == 'runway' for feature in features):
        raise ValueError('No runways returned for the Lisbon airport sample.')
    data = compact(collection)
    (args.target / 'lisbon.geojson').write_bytes(data)
    (args.target / 'lisbon.geojson.gz').write_bytes(gzip.compress(data, mtime=0))
    counts = Counter(feature['properties']['layer'] for feature in features)
    statistics = {
        **transport, 'osm_base_timestamp': payload.get('osm3s', {}).get('timestamp_osm_base'),
        'decoded_response_bytes': len(raw),
        'geojson_bytes': len(data), 'geojson_gzip_bytes': len(gzip.compress(data, mtime=0)),
        'layers': {
            layer: {'features': counts[layer],
                    'geojson_gzip_bytes': len(gzip.compress(compact(
                        {'type': 'FeatureCollection',
                         'features': [f for f in features if f['properties']['layer'] == layer]}),
                        mtime=0))}
            for layer in sorted(counts)},
        'airport_categories': dict(sorted(Counter(
            f['properties']['category'] for f in features
            if f['properties']['layer'] == 'airport').items())),
        'runway_references': sorted({f['properties']['ref'] for f in features
                                     if f['properties']['category'] == 'runway'
                                     and 'ref' in f['properties']}),
        'boundary_names': [f['properties'].get('name') for f in features
                           if f['properties']['layer'] == 'boundaries'],
        'limitations': [
            'Major roads only within the Lisbon bounding box; ways are not clipped at its edge.',
            'Airport buildings/features are selected within the mapped LPPT aerodrome area.',
            'Relation geometries are member outlines, not assembled filled multipolygons.',
            'No geometry simplification; SVG runway/taxiway widths are illustrative.',
            'Counts are OSM features, not physical objects: runway lines, areas and thresholds can overlap.',
            'No completeness or navigational accuracy guarantee.',
            'Response body sizes exclude HTTP headers and TLS overhead.',
            'Public Overpass is not a production streaming tile service.',
        ],
        'license': 'ODbL-1.0', 'attribution': 'OpenStreetMap contributors',
        'license_url': 'https://www.openstreetmap.org/copyright',
    }
    (args.target / 'analysis.json').write_text(
        json.dumps(statistics, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    for theme, palette in PALETTES.items():
        for view, bounds in (('city', ROAD_BOUNDS), ('airport', AIRPORT_BOUNDS)):
            (args.target / f'{view}-{theme}.svg').write_text(
                preview(collection, palette, bounds, f'Lisbon {view} - {theme}'),
                encoding='utf-8')
    (args.target / 'preview.html').write_text("""<!doctype html>
<html lang="en"><meta charset="utf-8"><title>Lisbon offline vector sample</title>
<style>
body { margin: 20px; background: #171E25; color: #E8EDF2; font: 16px sans-serif; }
select { margin: 0 18px 0 6px; padding: 5px; } a { color: #38BDF8; }
img { display: block; width: min(100%, 1200px); margin-top: 16px; }
</style>
<h1>Lisbon offline vector sample</h1>
<label>View<select id="view"><option value="airport">Airport</option>
<option value="city">City / major roads / boundary</option></select></label>
<label>Theme<select id="theme"><option value="dark">Dark</option>
<option value="light">Light</option></select></label>
<p>Same OSM geometry in both themes. All files are local; no map server is contacted.</p>
<p><a href="analysis.json">Measured sizes and limitations</a> |
<a href="lisbon.geojson">GeoJSON</a> |
<a href="https://www.openstreetmap.org/copyright">OpenStreetMap contributors / ODbL</a></p>
<img id="preview" src="airport-dark.svg" alt="Themed Lisbon vector map">
<script>
const view = document.getElementById('view');
const theme = document.getElementById('theme');
function update() {
  document.getElementById('preview').src = view.value + '-' + theme.value + '.svg';
}
view.addEventListener('change', update);
theme.addEventListener('change', update);
</script></html>
""", encoding='utf-8')
    print(json.dumps(statistics, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
