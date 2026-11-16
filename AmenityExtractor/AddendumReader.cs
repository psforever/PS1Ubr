using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace PS1Ubr
{
    /// <summary>
    /// Reads the per-placement height adjustments ("nudge &lt;map&gt; &lt;map object id&gt; &lt;height&gt;") from the client's addendum.lst.
    /// The client adds the value to the height of the matching map-object placement when it loads the map.
    /// </summary>
    public static class AddendumReader
    {
        public static Dictionary<(string map, int id), float> ReadNudges(string planetsideFolder)
        {
            var nudges = new Dictionary<(string map, int id), float>();
            var file = Directory.GetFiles(planetsideFolder, "addendum.lst", SearchOption.AllDirectories).FirstOrDefault();
            if (file == null) return nudges;

            foreach (var line in File.ReadLines(file))
            {
                var parts = line.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 4 && parts[0] == "nudge" && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                {
                    nudges[(parts[1], id)] = ClientFloat.Parse(parts[3]);
                }
            }

            return nudges;
        }
    }
}
