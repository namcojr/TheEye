import importlib.util
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET


spec = importlib.util.spec_from_file_location(
    'sample_lisbon_map', Path(__file__).with_name('sample-lisbon-map.py'))
sample = importlib.util.module_from_spec(spec)
spec.loader.exec_module(sample)


class SampleLisbonMapTests(unittest.TestCase):
    def test_closed_apron_is_polygon(self):
        element = {
            'type': 'way', 'id': 1, 'tags': {'aeroway': 'apron'},
            'geometry': [{'lon': x, 'lat': y}
                         for x, y in [(0, 0), (1, 0), (1, 1), (0, 0)]],
        }
        self.assertEqual('Polygon', sample.geometry(element)['type'])
        element['tags'] = {'highway': 'primary'}
        self.assertEqual('LineString', sample.geometry(element)['type'])
        element['tags'] = {'aeroway': 'runway'}
        self.assertEqual('Polygon', sample.geometry(element)['type'])
        element['tags']['area'] = 'no'
        self.assertEqual('LineString', sample.geometry(element)['type'])

    def test_boundary_preserves_outer_and_inner_outlines(self):
        members = [{
            'type': 'way', 'role': role,
            'geometry': [{'lon': 0, 'lat': 0}, {'lon': 1, 'lat': 1}],
        } for role in ('outer', 'inner')]
        members.append({'type': 'node', 'role': 'admin_centre'})
        shape = sample.geometry({'type': 'relation', 'id': 1, 'members': members})
        self.assertEqual('MultiLineString', shape['type'])
        self.assertEqual(2, len(shape['coordinates']))

    def test_missing_geometry_is_explicit_failure(self):
        with self.assertRaisesRegex(ValueError, 'Incomplete geometry'):
            sample.coordinates([None])
        with self.assertRaisesRegex(ValueError, 'Missing member geometry'):
            sample.geometry({'type': 'relation', 'id': 1, 'members': []})

    def test_partial_overpass_results_are_rejected(self):
        with self.assertRaisesRegex(ValueError, 'incomplete/error'):
            sample.convert({'remark': 'runtime error: Query timed out', 'elements': []})

    def test_classification_and_local_theme_previews(self):
        payload = {'elements': [{
            'type': 'node', 'id': 1, 'lon': -9.13, 'lat': 38.77,
            'tags': {'aeroway': 'gate', 'ref': 'A&B <1>'},
        }]}
        collection = sample.convert(payload)
        self.assertEqual('airport', collection['features'][0]['properties']['layer'])
        dark = sample.preview(collection, sample.PALETTES['dark'],
                              sample.AIRPORT_BOUNDS, 'Airport')
        light = sample.preview(collection, sample.PALETTES['light'],
                               sample.AIRPORT_BOUNDS, 'Airport')
        ET.fromstring(dark)
        ET.fromstring(light)
        self.assertNotEqual(dark, light)
        self.assertIn('A&amp;B &lt;1&gt;', dark)
        self.assertNotIn('<script', dark)


if __name__ == '__main__':
    unittest.main()
