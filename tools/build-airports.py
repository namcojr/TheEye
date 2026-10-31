"""Builds the compact airport and runway table The Eye ships as an embedded
resource: src/TheEye.Core/Resources/Maps/airports.txt.

The source is the public-domain OurAirports data set, which is downloaded
separately (the CSVs are not committed):

    https://davidmegginson.github.io/ourairports-data/airports.csv
    https://davidmegginson.github.io/ourairports-data/runways.csv

Usage, from the repository root:

    python tools/build-airports.py [--source .scratch/ourairports] [--target ...]

Output: one line per airport that has an ICAO location indicator, sorted, so a
regenerated file only differs where the data changed.

    ICAO|lat|lon|name|leLat,leLon,heLat,heLon,widthM;leLat,...

The runway part is empty for an airport without a usable runway, and the runways
are separated by ';' because the fields inside one runway are comma separated.
Closed runways, runways shorter than 600 m and aerodromes without an ICAO code
are dropped: the map draws runways only when zoomed in and labels them by ICAO,
so the rest would be dead weight in the binary.
"""

import argparse
import csv
import os
import re

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_SOURCE = os.path.join(ROOT, '.scratch', 'ourairports')
DEFAULT_TARGET = os.path.join(ROOT, 'src', 'TheEye.Core', 'Resources', 'Maps', 'airports.txt')

ICAO_CODE = re.compile(r'^[A-Z]{4}$')
AIRPORT_TYPES = ('large_airport', 'medium_airport', 'small_airport')

# Shortest runway worth drawing: 2000 ft is about 600 m.
MINIMUM_RUNWAY_FEET = 2000
# Fallback width for a runway the data set leaves without one, in feet.
DEFAULT_RUNWAY_FEET = 100
FEET_TO_METRES = 0.3048


def number(value):
    """Reads a CSV number, answering None for a blank or malformed field."""
    if not value:
        return None
    try:
        return float(value)
    except ValueError:
        return None


def coordinate(value):
    """Formats a coordinate with 5 decimals (about 1 m) and no trailing zeros."""
    text = f'{value:.5f}'.rstrip('0').rstrip('.')
    return '0' if text in ('', '-0') else text


def read_runways(path):
    """Runways grouped by airport ident, already in the compact map form."""
    runways = {}
    count = 0
    with open(path, newline='', encoding='utf-8') as source:
        for row in csv.DictReader(source):
            if row['closed'] == '1':
                continue

            length = number(row['length_ft'])
            if length is None or length < MINIMUM_RUNWAY_FEET:
                continue

            ends = [number(row[column]) for column in (
                'le_latitude_deg', 'le_longitude_deg',
                'he_latitude_deg', 'he_longitude_deg')]
            if any(end is None for end in ends):
                continue

            width = number(row['width_ft'])
            if width is None or width < 15:
                width = DEFAULT_RUNWAY_FEET

            runways.setdefault(row['airport_ident'], []).append(
                ','.join([coordinate(ends[0]), coordinate(ends[1]),
                          coordinate(ends[2]), coordinate(ends[3]),
                          str(round(width * FEET_TO_METRES))]))
            count += 1

    return runways, count


def read_airports(path, runways):
    """One line per airport with an ICAO code, sorted by that code."""
    lines = []
    seen = set()
    with open(path, newline='', encoding='utf-8') as source:
        for row in csv.DictReader(source):
            if row['type'] not in AIRPORT_TYPES:
                continue

            icao = row['icao_code'] or row['ident']
            if not ICAO_CODE.match(icao) or icao in seen:
                continue

            latitude = number(row['latitude_deg'])
            longitude = number(row['longitude_deg'])
            if latitude is None or longitude is None:
                continue

            # The reader splits on '|' and on newlines, so the name must not
            # carry either; the rest of the whitespace is collapsed as well.
            name = ' '.join(row['name'].replace('|', ' ').split())
            if not name:
                name = icao

            seen.add(icao)
            lines.append('{}|{}|{}|{}|{}'.format(
                icao, coordinate(latitude), coordinate(longitude), name,
                ';'.join(runways.get(row['ident'], []))))

    lines.sort()
    return lines


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', default=DEFAULT_SOURCE,
                        help='directory holding airports.csv and runways.csv')
    parser.add_argument('--target', default=DEFAULT_TARGET,
                        help='file to write the compact table to')
    arguments = parser.parse_args()

    runways, runway_count = read_runways(os.path.join(arguments.source, 'runways.csv'))
    lines = read_airports(os.path.join(arguments.source, 'airports.csv'), runways)

    directory = os.path.dirname(arguments.target)
    if directory:
        os.makedirs(directory, exist_ok=True)

    with open(arguments.target, 'w', encoding='utf-8', newline='\n') as target:
        target.write('\n'.join(lines))
        target.write('\n')

    print(f'{len(lines):,} airports, {runway_count:,} runways, '
          f'{os.path.getsize(arguments.target):,} bytes')


if __name__ == '__main__':
    main()
