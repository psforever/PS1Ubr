using AmenityExtractor.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AmenityExtractor
{
    /// <summary>
    /// Optional regression check: compares the GUIDs assigned by <see cref="GUIDAssigner"/> with a GUID dump recorded from the real game client.
    /// Set the environment variable CLIENT_GUID_DUMPS to a folder containing CSV files named after the output files (map04.csv, ugd04.csv, ...) with the columns
    /// seq,guid,name,x,y,z,... (see the client dump documentation). Maps without a dump are skipped.
    /// Objects are matched by type name (case-insensitive) and the horizontal position the client sorts on (SortX/SortY); a client row whose name is not the
    /// type of any extractor object (the dump prints garbage for some names) is matched by position only, and its GUID is still compared.
    /// The check is strict by default: any GUID difference, unmatched client row or extractor object missing from the dump prints the differences and ends the
    /// run with exit code 1 before the zonemap of that map is written. Set CLIENT_GUID_STRICT=0 to only print them. CLIENT_GUID_VERBOSE=1 lists every unmatched row.
    /// </summary>
    public static class ClientDumpChecker
    {
        private const float PositionTolerance = 0.01f;

        public static void Check(List<PlanetSideObject> objects, string outputName)
        {
            var folder = Environment.GetEnvironmentVariable("CLIENT_GUID_DUMPS");
            if (string.IsNullOrEmpty(folder)) return;
            var path = Path.Combine(folder, outputName + ".csv");
            if (!File.Exists(path)) return;

            var byName = objects.GroupBy(o => o.ObjectType.Replace("!", "").ToLowerInvariant()).ToDictionary(g => g.Key, g => g.ToList());
            var used = new HashSet<int>();
            int rows = 0, equal = 0, different = 0, clientOnly = 0, matchedByPosition = 0;
            var differing = new List<string>();
            var unmatchedClient = new List<string>();
            var verbose = Environment.GetEnvironmentVariable("CLIENT_GUID_VERBOSE") == "1";
            var strict = Environment.GetEnvironmentVariable("CLIENT_GUID_STRICT") != "0";

            foreach (var line in File.ReadLines(path).Skip(1))
            {
                var c = line.Split(',');
                if (c.Length < 5) continue;
                rows++;
                var guid = int.Parse(c[1]);
                var x = float.Parse(c[3], System.Globalization.CultureInfo.InvariantCulture);
                var y = float.Parse(c[4], System.Globalization.CultureInfo.InvariantCulture);
                var z = c.Length > 5 ? float.Parse(c[5], System.Globalization.CultureInfo.InvariantCulture) : 0f;

                // A name that is not the type of any extractor object cannot be matched by name: use the position (and then still compare the GUID).
                var knownName = byName.TryGetValue(c[2].ToLowerInvariant(), out var candidates);
                if (!knownName) candidates = objects;

                PlanetSideObject best = null;
                var bestDistance = double.MaxValue;
                foreach (var o in candidates.Where(o => !used.Contains(o.Id)))
                {
                    var horizontal = Math.Sqrt(Math.Pow(o.SortX - x, 2) + Math.Pow(o.SortY - y, 2));
                    // Objects at identical x/y are told apart by height; the exported height already includes the client's lift.
                    var score = horizontal + Math.Abs(z - o.SortZ) * 1e-4;
                    if (score < bestDistance) { bestDistance = score; best = o; }
                }

                if (best == null || bestDistance > PositionTolerance)
                {
                    clientOnly++;
                    unmatchedClient.Add($"client-only {guid} {c[2]} {x} {y} {z}" + (best != null ? $" (nearest {bestDistance:F3})" : ""));
                    continue;
                }

                if (!knownName) matchedByPosition++;
                used.Add(best.Id);
                if (best.GUID == guid) equal++;
                else differing.Add($"{best.ObjectType} at {best.SortX:F2} {best.SortY:F2}: client GUID {guid} ({c[2]}), extractor GUID {best.GUID}");
            }

            different = differing.Count;
            var missingFromDump = objects.Where(o => !used.Contains(o.Id)).ToList();
            Console.WriteLine($"Client dump check {outputName}: {rows} client rows, {equal} GUIDs equal, {different} different, {clientOnly} unmatched client rows, {missingFromDump.Count} extractor objects not in the dump" + (matchedByPosition > 0 ? $" ({matchedByPosition} rows matched by position because their name is not a known type)" : ""));

            if (different == 0 && clientOnly == 0 && missingFromDump.Count == 0) return;

            var limit = verbose ? int.MaxValue : 20;
            foreach (var d in differing.Take(limit)) Console.Error.WriteLine("  different: " + d);
            foreach (var u in unmatchedClient.Take(limit)) Console.Error.WriteLine("  " + u);
            foreach (var o in missingFromDump.Take(limit)) Console.Error.WriteLine($"  extractor-only {o.GUID} {o.ObjectType} {o.SortX:F3} {o.SortY:F3} {o.SortZ:F3}");
            if (!strict) return;

            Console.Error.WriteLine($"Client dump check {outputName} FAILED (set CLIENT_GUID_STRICT=0 to only print differences). No zonemap was written for {outputName}.");
            Environment.Exit(1);
        }
    }
}
