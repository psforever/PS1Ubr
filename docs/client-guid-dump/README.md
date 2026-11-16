# Dumping the client's map object GUIDs with x32dbg

`guid_dump.x32dbg.txt` is an x32dbg script that logs every map object the PlanetSide client creates, with the GUID the client gave it. The AmenityExtractor can compare its own GUIDs with such a dump.

## What it logs
One line per created map object, written to the debugger log without stopping the client:
- `GUIDDUMP,<guid>,<name>,<x>,<y>,<z>,<map id>,<id2>,<object guid field>` (x, y, z are float bit patterns in hex).
- `GUIDDUMP_MAP,<map name>` once at the start of each zone load, so one log can hold several zones.
- Optional link lines from the same script: `GUIDLINK_OWNER,<object>,<building>,<index>` (an object linked to its owning building) and `GUIDLINK_DOOR,<door>,<lock>` (a door linked to an IFF lock).

## Requirements
- 32-bit x32dbg and the 32-bit `planetside.exe`. Addresses are absolute (image base `0x00400000`, no ASLR) and were taken from one client build. If your build differs, check that the instruction at each address matches the description in the script header before trusting the log.
- Start the client with `/K:StagingTest /CC`, working directory = the client folder. Without a `-K:` argument the client shows "Please start PlanetSide using the LaunchPad" and exits, which a debugger reports as the process terminating. The client does not otherwise detect debuggers at startup, so no anti-debug plugin was needed.
- Alternatively start the client normally (or via its launcher), wait for the login screen and use File > Attach in x32dbg. Map objects are created when a zone loads, so attaching before zoning is early enough.

## Capturing
1. Load the script in the Script tab (View > Script, right-click > Load Script, then Run), or run `scriptload <path>` then `scriptrun` in the command bar, or paste the lines into the command bar.
2. Redirect the Log tab to a file (Log tab, right-click menu) and resume with F9.
3. Log in and zone. To capture several zones in one session, travel through warp gates; keep one log file. Zoning to the same zone twice produces that zone twice.

Breakpoints (all silent, condition `0`, so they log without breaking): `8c3644` (just after the GUID is registered in the map object creation loop), `8c6fc2` (zone marker), and for the optional link lines `8c3993` and `943bee`. Do not enable the fast-resume option on them: it skips the logging.

## Splitting and comparing
```bash
python3 guid_dump_to_csv.py <debugger log> <output folder>
```
writes one CSV per zone (`map04.csv`, `ugd04.csv`, ... columns `seq,guid,name,x,y,z,map_id,id2,obj_guid_field`; a repeated zone gets a `_2` suffix) and `<zone>_links.csv` when link lines are present.

Run the extractor with `CLIENT_GUID_DUMPS=<that folder>`: after assigning GUIDs it matches each client row to an extractor object by name and position (tolerance 0.01) and prints how many GUIDs are equal or different. Add `CLIENT_GUID_VERBOSE=1` to list the unmatched rows.

## Caveats
- The `mb_pad_creation` objects print a garbage name in the dump; their GUIDs and positions are right, so they show up as unmatched rows in the comparison.
- Capturing slows zone loads a little; the breakpoints are silent but still run for every object.
- Floats are logged as hex and converted by the script; positions in the CSV are rounded to four decimals.
