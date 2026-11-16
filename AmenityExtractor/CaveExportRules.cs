using System;
using System.Collections.Generic;
using System.Linq;
using AmenityExtractor.Models;

namespace AmenityExtractor
{
    /// <summary>
    /// Ownership of ground-cover objects in the caverns, as the server needs it in the exported zonemap. Applied after GUIDs are assigned (it only changes
    /// Owner and IsChildObject, which the client ordering does not use).
    ///
    /// The cave ground-cover files list some objects with a "!" prefix ("part of a building") without saying which building. The server loads building
    /// amenities through Owner and every other object as free-standing, so these objects need an explicit decision. The client's own link for these objects
    /// is not captured for caves, so nothing below is client-derived (inferred, not confirmed):
    ///  - crystals_* objects are free-standing (no owner, not a child). Server-driven: the server creates them without a building, and a non-child must not have an owner.
    ///  - vanu_equipment_term objects that no building model contains get their owner from CaveOwnerOverrides (a temporary table).
    /// </summary>
    public static class CaveExportRules
    {
        public const float PositionTolerance = 0.01f;

        public static void Apply(List<PlanetSideObject> objects, string map)
        {
            foreach (var crystal in objects.Where(o => o.ObjectType.StartsWith("crystals_", StringComparison.Ordinal)))
            {
                crystal.Owner = null;
                crystal.IsChildObject = false;
            }

            foreach (var entry in CaveOwnerOverrides.VanuEquipmentTermOwners.Concat(CaveOwnerOverrides.VanuEquipmentTermAdditions).Where(e => e.Map == map))
            {
                // Additions are free-standing placements that become children of the owner (a free-standing object must not have an owner)
                var terminal = objects.FirstOrDefault(o => o.ObjectType == "vanu_equipment_term" && o.Owner == null && o.IsChildObject == !entry.Addition && At(o, entry.X, entry.Y, entry.Z));
                var owner = objects.FirstOrDefault(o => o.ObjectType == entry.OwnerType && At(o, entry.OwnerX, entry.OwnerY, entry.OwnerZ));
                if (terminal == null || owner == null)
                {
                    Console.WriteLine($"Cave owner override for {map} at {entry.X}, {entry.Y}, {entry.Z} did not match ({(terminal == null ? "terminal" : "owner")} missing)");
                    continue;
                }

                terminal.Owner = owner.Id;
                terminal.IsChildObject = true;
            }
        }

        private static bool At(PlanetSideObject o, float x, float y, float z)
        {
            return Math.Abs(o.AbsX - x) <= PositionTolerance && Math.Abs(o.AbsY - y) <= PositionTolerance && Math.Abs(o.AbsZ - z) <= PositionTolerance;
        }
    }
}
