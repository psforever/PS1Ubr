using AmenityExtractor.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AmenityExtractor
{
    public static class GUIDAssigner
    {
        /*
         GUIDs are assigned the way the game client does it (verified against debugger dumps of the client for all 23 generated zones - surface maps, battle islands and caves - with 0 GUID differences):

         1. All map objects go into ONE list in creation order (our Id order). The client then sorts that list with an STLport stable_sort (see ClientStableSort).
         2. The comparator (ClientObjectLess) puts objects that have a map id (structures and groundcover roots, i.e. !IsChildObject) before everything else, then compares the type name
            case-insensitively (_stricmp), then X and Y, which only count when they differ by at least 0.1 (otherwise the next axis decides), then Z.
            The tolerance makes the comparator inconsistent for near ties, so the exact stable_sort algorithm matters, not just the key.
         3. GUIDs are handed out one by one in that order. Some objects reserve extra GUIDs for sub-objects (see objectTypesWithMultipleGuids).
         4. The coordinates compared are the ones the client computes: the parent rotation is the exact 14-bit angle (not rounded to degrees) and .lst coordinates are parsed by truncating toward
            zero (ClientFloat). They are kept in SortX/SortY/SortZ and exported as AbsX/AbsY/AbsZ, so output and GUID order always agree.

         Extra GUIDs reserved after some objects: see objectTypesWithMultipleGuids below.
         Objects the client creates but we do not export (unused LLU sockets) still consume a GUID, which shows up as a gap.
        */

        /// <summary>
        /// The client builds the vector to sort in two passes over its quadtree: first every root placement, then (for each root that has children, in the same walk order)
        /// the root's children. Root order here is our creation order; children are grouped under their root in creation order.
        /// </summary>
        public static List<PlanetSideObject> ClientInputOrder(List<PlanetSideObject> mapObjects)
        {
            var byId = mapObjects.ToDictionary(o => o.Id);
            PlanetSideObject RootOf(PlanetSideObject o)
            {
                while (o.IsChildObject && o.Owner.HasValue && byId.TryGetValue(o.Owner.Value, out var p) && p != o) o = p;
                return o;
            }
            var ordered = mapObjects.OrderBy(x => x.Id).ToList();
            var roots = ordered.Where(o => !o.IsChildObject).ToList();

            // Roots come out of the quadtree walk in first-visit order (insertion order = creation order). Roots without a known box keep creation order at the end.
            var tree = new ClientQuadtree(0f, 0f, 8192f, 8192f, 6);
            for (var i = 0; i < roots.Count; i++)
            {
                var r = roots[i];
                if (!r.BoxMinX.HasValue) continue;
                tree.Add(i, (float)r.SortX + r.BoxMinX.Value, (float)r.SortY + r.BoxMinY, (float)r.SortX + r.BoxMaxX, (float)r.SortY + r.BoxMaxY);
            }
            var walked = tree.Walk();
            var walkedSet = new HashSet<int>(walked);
            var rootsInWalkOrder = walked.Select(i => roots[i]).Concat(roots.Where((r, i) => !walkedSet.Contains(i))).ToList();

            var result = new List<PlanetSideObject>(rootsInWalkOrder);
            var rootOrder = rootsInWalkOrder.Select((r, i) => (r.Id, i)).ToDictionary(x => x.Id, x => x.i);
            result.AddRange(ordered.Where(o => o.IsChildObject).OrderBy(o => rootOrder.TryGetValue(RootOf(o).Id, out var i) ? i : int.MaxValue));
            return result;
        }

        public static void AssignGUIDs(List<PlanetSideObject> mapObjects)
        {
            var currentGUID = 1;

            // Some objects allocate extra GUIDs for sub-terminals while they are constructed; they follow the object's own GUID and are not map objects (client-confirmed on four zones, see the notes on reserved GUID ranges):
            //  - amp_station, comm_station, comm_station_dsp, cryo_facility, tech_plant (building basetype 1..5): 2 = recharge_terminal_weapon_module, medical_terminal_healing_module
            //  - pad_landing_frame, pad_landing_tower_frame: 2 = rearm terminal, recharge terminal
            //  - repair_silo: 3 = rearm terminal, bfr_rearm_terminal, recharge terminal
            //  - crystals_vehicle_a / crystals_vehicle_b: 1 = crystals_vehicle_recharge_terminal
            // The counts come from the definitions' has_aggregate_*_terminal properties and the facility basetypes; the server creates these sub-objects itself when it loads the zonemap (PSF-LoginServer Zones.scala).
            var objectTypesWithMultipleGuids = new Dictionary<string, int>()
            {
                { "amp_station", 2 }, { "comm_station", 2 }, {"comm_station_dsp", 2}, {"cryo_facility", 2 }, {"tech_plant", 2}, {"pad_landing_frame", 2},
                { "pad_landing_tower_frame", 2}, {"repair_silo", 3}, {"crystals_vehicle_a", 1}, {"crystals_vehicle_b", 1}
            };



            // Repair silos in sanctuaries are groundcover roots (not part of a building) and are numbered in sort order like any other root; no special skip is needed
            // (confirmed on the VS sanctuary, map13).

            // Sort like the client (see the comment at the top of this class), then hand out GUIDs in that order.
            var orderedObjects = ClientStableSort.Sort(ClientInputOrder(mapObjects), ClientObjectLess);

            foreach (var obj in orderedObjects)
            {
                obj.GUID = currentGUID;
                currentGUID++;
                if (objectTypesWithMultipleGuids.ContainsKey(obj.ObjectType)) currentGUID += objectTypesWithMultipleGuids[obj.ObjectType]; // Skip reserved GUIDS

                // ! prefix found in cave groundcover files indicate the object should be classed as an "entity" guid, not a structure guid. Trim it once processed.
                obj.ObjectType = obj.ObjectType.Replace("!", "");
                obj.ObjectName = obj.ObjectName.Replace("!", "");
            }
        }

        private const float ClientCoordinateTolerance = 0.1f;

        // Comparator from the game client (a < b): objects with a map id (non-children) first, then _stricmp on the name (A-Z folded to lowercase), then X and Y only compared when they differ by at least 0.1, otherwise Z.
        private static bool ClientObjectLess(PlanetSideObject a, PlanetSideObject b)
        {
            if (a.IsChildObject != b.IsChildObject) return !a.IsChildObject;
            var c = CompareNamesIgnoreCase(a.ObjectType.Replace("!", ""), b.ObjectType.Replace("!", ""));
            if (c != 0) return c < 0;
            if (Math.Abs((float)a.SortX - (float)b.SortX) >= ClientCoordinateTolerance) return (float)a.SortX < (float)b.SortX;
            if (Math.Abs((float)a.SortY - (float)b.SortY) >= ClientCoordinateTolerance) return (float)a.SortY < (float)b.SortY;
            return (float)a.SortZ < (float)b.SortZ;
        }

        private static int CompareNamesIgnoreCase(string a, string b)
        {
            var length = Math.Min(a.Length, b.Length);
            for (var i = 0; i < length; i++)
            {
                var ca = char.ToLowerInvariant(a[i]);
                var cb = char.ToLowerInvariant(b[i]);
                if (ca != cb) return ca < cb ? -1 : 1;
            }
            return a.Length.CompareTo(b.Length);
        }
    }
}
