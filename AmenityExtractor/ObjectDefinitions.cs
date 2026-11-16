using System;
using System.Collections.Generic;
using System.IO;

namespace AmenityExtractor
{
    /// <summary>
    /// Minimal reader of game_objects.adb.lst: resolves an inherited property of an object definition.
    /// The client only creates (and allocates a GUID for) a map record whose definition resolves a "type" property.
    /// </summary>
    public static class ObjectDefinitions
    {
        private static Dictionary<string, string> _parents;
        private static Dictionary<string, Dictionary<string, string>> _props;

        public static void Load(string planetsideFolder)
        {
            _parents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _props = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            var path = Path.Combine(planetsideFolder, "startup.pak-out", "game_objects.adb.lst");
            if (!File.Exists(path)) return;
            foreach (var line in File.ReadLines(path))
            {
                var p = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length >= 4 && p[0] == "set_resource_parent") _parents[p[1]] = p[3];
                else if (p.Length >= 4 && p[0] == "add_property")
                {
                    if (!_props.TryGetValue(p[1], out var d)) _props[p[1]] = d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    d[p[2]] = p[3];
                }
            }
        }

        public static string GetProperty(string objectName, string property)
        {
            if (_parents == null) Load(Program.PlanetsideFolder);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var o = objectName;
            while (o != null && seen.Add(o))
            {
                if (_props.TryGetValue(o, out var d) && d.TryGetValue(property, out var v)) return v;
                if (!_parents.TryGetValue(o, out o)) break;
            }
            return null;
        }
    }
}
