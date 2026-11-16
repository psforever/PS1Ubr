#!/usr/bin/env python3
"""Convert x32dbg log lines (see guid_dump.x32dbg.txt) to CSV.

Usage:
  guid_dump_to_csv.py <debugger-log.txt>                 > client_guids.csv     (all rows, one CSV on stdout)
  guid_dump_to_csv.py <debugger-log.txt> <output-folder>                        (one CSV per zone load: map04.csv, ugd04.csv, ...)

Lines:
  GUIDDUMP_MAP,<map name>                      marks the start of a zone load (written when the client begins loading a zone)
  GUIDDUMP,<guid>,<name>,<x>,<y>,<z>,<map id>,<id2>,<obj guid field>   one per created map object (floats are hex bit patterns)
  GUIDLINK_OWNER,<object guid>,<building guid>,<index>                  an object linked to its owning building (client map loader)
  GUIDLINK_DOOR,<door guid>,<lock guid>                                  a door linked to an IFF lock
With an output folder, links are written to <zone>_links.csv (columns kind,from_guid,to_guid,extra; kind is owner or door).

Zones are split at the GUIDDUMP_MAP markers. Without markers, a new zone starts whenever the GUID sequence goes backwards and the files are named zone1.csv, zone2.csv, ...
When the same zone is loaded twice the second file gets a _2 suffix. Columns: seq,guid,name,x,y,z,map_id,id2,obj_guid_field (seq restarts at 1 for each zone)."""
import os, re, struct, sys

LINK = re.compile(r'GUIDLINK_(OWNER|DOOR),(\d+),(\d+)(?:,(\d+))?')
ROW = re.compile(r'GUIDDUMP,(\d+),("?[^,]*"?),([0-9A-Fa-f]+),([0-9A-Fa-f]+),([0-9A-Fa-f]+),(-?\d+),(-?\d+),(-?\d+)')
MARK = re.compile(r'GUIDDUMP_MAP,"?([^",\r\n]*)"?')
HEADER = 'seq,guid,name,x,y,z,map_id,id2,obj_guid_field'


def f(h):
    return struct.unpack('<f', struct.pack('<I', int(h, 16)))[0]


def zones(path):
    """Yield (zone name or None, [rows], [links]) in log order; a link is (kind, from guid, to guid, extra)."""
    current, rows, last, links = None, [], -1, []
    for line in open(path, errors='replace'):
        m = MARK.search(line)
        if m:
            if rows or links: yield current, rows, links
            current, rows, last, links = m.group(1).strip().lower(), [], -1, []
            continue
        m = LINK.search(line)
        if m:
            links.append((m.group(1).lower(), int(m.group(2)), int(m.group(3)), m.group(4) or ''))
            continue
        m = ROW.search(line)
        if not m: continue
        guid = int(m.group(1))
        if current is None and guid < last and rows:      # no markers: a GUID reset means a new zone
            yield None, rows, links
            rows, links = [], []
        last = guid
        rows.append((guid, m.group(2).strip('"'), f(m.group(3)), f(m.group(4)), f(m.group(5)), m.group(6), m.group(7), m.group(8)))
    if rows or links: yield current, rows, links


def lines(rows):
    yield HEADER
    for seq, (guid, name, x, y, z, a, b, c) in enumerate(rows, 1):
        yield f'{seq},{guid},{name},{x:.4f},{y:.4f},{z:.4f},{a},{b},{c}'


if __name__ == '__main__':
    log = sys.argv[1]
    if len(sys.argv) < 3:
        allrows = [r for _, rows, _ in zones(log) for r in rows]
        print('\n'.join(lines(allrows)))
    else:
        out = sys.argv[2]
        os.makedirs(out, exist_ok=True)
        seen, unnamed = {}, 0
        for name, rows, links in zones(log):
            if name is None:
                unnamed += 1
                name = f'zone{unnamed}'
            safe = re.sub(r'[^a-z0-9_-]', '_', name)
            seen[safe] = seen.get(safe, 0) + 1
            fname = safe + ('' if seen[safe] == 1 else f'_{seen[safe]}') + '.csv'
            with open(os.path.join(out, fname), 'w') as fh:
                fh.write('\n'.join(lines(rows)) + '\n')
            print(f'{fname}: {len(rows)} rows', file=sys.stderr)
            if links:
                with open(os.path.join(out, fname[:-4] + '_links.csv'), 'w') as fh:
                    fh.write('kind,from_guid,to_guid,extra\n' + '\n'.join(f'{k},{a},{b},{e}' for k, a, b, e in links) + '\n')
                print(f'{fname[:-4]}_links.csv: {len(links)} links', file=sys.stderr)
