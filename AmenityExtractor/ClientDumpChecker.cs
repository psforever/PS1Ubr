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
    /// Objects are matched by type name (case-insensitive) and the horizontal position the client sorts on (SortX/SortY).
    /// </summary>
    public static class ClientDumpChecker
    {
        private const float PositionTolerance = 0.01f;
        private const float ClientHeightLift = 0.2f;

        public static void Check(List<PlanetSideObject> objects, string outputName)
        {
            var folder = Environment.GetEnvironmentVariable("CLIENT_GUID_DUMPS");
            if (string.IsNullOrEmpty(folder)) return;
            var path = Path.Combine(folder, outputName + ".csv");
            if (!File.Exists(path)) return;

            var byName = objects.GroupBy(o => o.ObjectType.Replace("!", "").ToLowerInvariant()).ToDictionary(g => g.Key, g => g.ToList());
            var used = new HashSet<int>();
            int rows = 0, equal = 0, different = 0, clientOnly = 0;
            var differing = new List<string>();
            var verbose = Environment.GetEnvironmentVariable("CLIENT_GUID_VERBOSE") == "1";

            foreach (var line in File.ReadLines(path).Skip(1))
            {
                var c = line.Split(',');
                if (c.Length < 5) continue;
                rows++;
                var guid = int.Parse(c[1]);
                var x = float.Parse(c[3], System.Globalization.CultureInfo.InvariantCulture);
                var y = float.Parse(c[4], System.Globalization.CultureInfo.InvariantCulture);
                var z = c.Length > 5 ? float.Parse(c[5], System.Globalization.CultureInfo.InvariantCulture) : 0f;

                PlanetSideObject best = null;
                var bestDistance = double.MaxValue;
                if (byName.TryGetValue(c[2].ToLowerInvariant(), out var candidates))
                {
                    foreach (var o in candidates.Where(o => !used.Contains(o.Id)))
                    {
                        var horizontal = Math.Sqrt(Math.Pow(o.SortX - x, 2) + Math.Pow(o.SortY - y, 2));
                        // Objects at identical x/y are told apart by height: the client's z is ours plus a lift that is usually 0.2.
                        var score = horizontal + Math.Abs(z - o.SortZ - ClientHeightLift) * 1e-4;
                        if (score < bestDistance) { bestDistance = score; best = o; }
                    }
                }

                if (best == null || bestDistance > PositionTolerance) { clientOnly++; if (verbose) Console.WriteLine($"  client-only {guid} {c[2]} {x} {y} {z}" + (best != null ? $" (nearest {bestDistance:F3})" : "")); continue; }
                used.Add(best.Id);
                if (best.GUID == guid) equal++;
                else { different++; if (differing.Count < 5) differing.Add($"{c[2]} client {guid} extractor {best.GUID}"); }
            }

            Console.WriteLine($"Client dump check {outputName}: {rows} client rows, {equal} GUIDs equal, {different} different, {clientOnly} client-only (objects the extractor does not export, e.g. removed LLU sockets), {objects.Count - used.Count} extractor-only");
            if (verbose) foreach (var o in objects.Where(o => !used.Contains(o.Id))) Console.WriteLine($"  extractor-only {o.GUID} {o.ObjectType} {o.SortX:F3} {o.SortY:F3} {o.SortZ:F3}");
            foreach (var d in differing) Console.WriteLine("  " + d);
        }
    }
}
