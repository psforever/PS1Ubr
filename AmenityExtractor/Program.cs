using AmenityExtractor.Models;
using FileReaders;
using FileReaders.Models;
using Newtonsoft.Json;
using PS1Ubr;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Formatting = Newtonsoft.Json.Formatting;

namespace AmenityExtractor
{
    public class Program
    {
        // Path to the mod ready planetside folder that has everything pre-extracted from the pak files.
        public static string PlanetsideFolder => _planetsideModReadyFolder;
        private static string _planetsideModReadyFolder = "C:\\Planetside (Mod ready)\\Planetside";

        private static List<string> _objectsWithGuids = new List<string>(File.ReadAllLines("ObjectsWithGuids.txt"));
        private static ConcurrentDictionary<string, CUberData> _ubrData = new ConcurrentDictionary<string, CUberData>();
        private static GameObjectsReader objReader = new GameObjectsReader(_planetsideModReadyFolder);

        // Per-placement height adjustments from the client's addendum.lst, keyed by (map name, map object id)
        private static Dictionary<(string map, int id), float> _nudges = AddendumReader.ReadNudges(_planetsideModReadyFolder);
        private static string _currentMap;

        static void Main(string[] args)
        {
            var maps = new List<(string name, string mapNumber, bool isCave)> {
                                                            ( "Solsar", "01", false),
                                                            ( "Hossin", "02", false),
                                                            ( "Cyssor", "03", false ),
                                                            ( "Ishundar", "04", false ),
                                                            ( "Forseral", "05", false ),
                                                            ( "Ceryshen", "06", false ),
                                                            ( "Esamir", "07", false ),
                                                            ( "Oshur Prime", "08", false ),
                                                            ( "Searhus", "09", false ),
                                                            ( "Amerish", "10", false ),
                                                            ( "HOME1 (NEW CONGLOMORATE SANCTUARY)", "11", false ),
                                                            ( "HOME2 (TERRAN REPUBLIC SANCTUARY)", "12", false ),
                                                            ( "HOME3 (VANU SOVREIGNTY SANCTUARY)", "13", false ),
                                                            ( "Nexus", "96", false ),
                                                            ( "Desolation", "97", false ),
                                                            ( "Ascension", "98", false ),
                                                            ( "Extinction", "99", false ),
                                                            ( "Supai", "01", true ),
                                                            ( "Hunhau", "02", true ),
                                                            ( "Adlivun", "03", true ),
                                                            ( "Byblos", "04", true ),
                                                            ( "Annwn", "05", true ),
                                                            ( "Drugaskan", "06", true )
            };

            // Load all *.ubr files
            Parallel.ForEach(Directory.GetFiles(_planetsideModReadyFolder, "*.ubr", SearchOption.AllDirectories),
                file => { _ubrData.TryAdd(file, UBRReader.GetUbrData(file)); });

            foreach (var map in maps)
            {
                var identifiableObjects = new List<PlanetSideObject>();

                var (mapName, mapNumber, isCave) = map;
                Console.WriteLine($"Processing map {mapNumber} - {mapName}");
                _currentMap = "map" + mapNumber;

                int id = 0;

                if (!isCave)
                {
                    // Read the contents_map mpo file, and the associated objects_map lst file
                    var mpoData = MPOReader.ReadMPOFile(_planetsideModReadyFolder, mapNumber);
                    foreach (var entry in mpoData)
                    {
                        ProcessObject(identifiableObjects, entry, ref id, mapId: mpoData.IndexOf(entry) + 1, isTopLevel: true, isMpoRecord: true);
                    }
                }

                // Read groundcover data
                var groundCoverData = LSTReader.ReadGroundCoverLST(_planetsideModReadyFolder, mapNumber, isCave);
                foreach (var line in groundCoverData)
                {
                    var mapObj = LstObjectToMapObject(line);
                    ProcessObject(
                        identifiableObjects,
                        mapObj,
                        ref id,
                        mapId: line.Id,
                        isTopLevel: !mapObj.HasBangPrefix
                    );
                }

                // Battle islands for some reason have an X/Y offset applied to all coordinates in game_objects.adb.lst. Thus, we need to account for that.
                //19318:add_property map99 mapOffsetX 1024.0
                //19319:add_property map99 mapOffsetY 1024.0
                if (new List<string>() { "96", "97", "98", "99" }.Contains(mapNumber))
                {
                    for (int i = 0; i < identifiableObjects.Count(); i++)
                    {
                        identifiableObjects[i].AbsX += 1024;
                        identifiableObjects[i].AbsY += 1024;
                        identifiableObjects[i].SortX += 1024;
                        identifiableObjects[i].SortY += 1024;
                    }
                }

                // Export the positions the way the client computes them (exact placement rotation, truncated .lst coordinates, client height adjustments).
                // The same values are used for ordering in GUIDAssigner, so output and GUID order always agree.
                foreach (var obj in identifiableObjects)
                {
                    obj.AbsX = (float)obj.SortX;
                    obj.AbsY = (float)obj.SortY;
                    obj.AbsZ = (float)obj.SortZ;
                }

                // bfr_building doesn't actually have a GUID itself - the door does which is a sub-object in the ubr file.
                // Remove it from the list before assigning GUIDs
                identifiableObjects.RemoveAll(x => x.ObjectType == "bfr_building");

                // Assign GUIDs to loaded map objects
                GUIDAssigner.AssignGUIDs(identifiableObjects);

                // Sanity checking to make sure the amount of objects we've got matches a list of expected object counts
                SanityChecker.CheckObjectCounts(identifiableObjects, mapName, mapNumber);

                // Sanity checking that assigned GUIDs match expected GUID ranges
                SanityChecker.CheckGuidRanges(identifiableObjects, mapName, mapNumber);

                // Optional check against a GUID dump recorded from the real client (CLIENT_GUID_DUMPS), before unused LLU sockets are removed
                ClientDumpChecker.Check(identifiableObjects, isCave ? $"ugd{mapNumber}" : $"map{mapNumber}");

                RemoveUnusedLluSockets(identifiableObjects, mapNumber);

                // Cave ground-cover objects need an explicit owner / child decision for the server (see CaveExportRules)
                if (isCave) CaveExportRules.Apply(identifiableObjects, $"ugd{mapNumber}");

                // Export to json file
                var json = ZonemapJson.Serialize(identifiableObjects.OrderBy(x => x.GUID));

                var filename = !isCave ? $"map{mapNumber}.json" : $"ugd{mapNumber}.json";
                File.WriteAllText(filename, json);

                if (isCave)
                {
                    // Read zipline data, reformat and export as json
                    var zplData = ZplReader.ReadZplFile(_planetsideModReadyFolder, mapNumber);

                    File.WriteAllText($"zpl_ugd{mapNumber}.json", ZonemapJson.Serialize(zplData));
                }
            }

            Console.WriteLine("Done");
            Console.ReadKey();
        }

        private static MapObject LstObjectToMapObject(GroundCover groundCoverObject)
        {
            return new MapObject()
            {
                ObjectType = groundCoverObject.ObjectType,
                ObjectName = groundCoverObject.ObjectName ?? groundCoverObject.ObjectType + "_" + groundCoverObject.Id,
                HorizontalPosition = groundCoverObject.AbsX,
                VerticalPosition = groundCoverObject.AbsY,
                HeightPosition = groundCoverObject.AbsZ,
                HorizontalRotation = groundCoverObject.Roll,
                LstType = groundCoverObject.LstType,
            };
        }

        private static void ProcessObject(List<PlanetSideObject> identifiableObjects, MapObject entry, ref int id, int? mapId, bool isTopLevel = false, bool isMpoRecord = false)
        {
            if (!_objectsWithGuids.Contains(entry.ObjectType)) return;
            Console.WriteLine($"Processing {entry.ObjectType}");

            // Load the relevant *.lst files for this object
            var (peHiddens, peEdits, pseRelativeObjects) = LSTReader.ReadLSTFile(_planetsideModReadyFolder, entry.ObjectType, entry.LstType);

            // Get the root mesh for this object
            var uberData = _ubrData.First(x =>
                x.Value.Entries.Select(y => y.Name).Contains(entry.ObjectType, StringComparer.OrdinalIgnoreCase));

            var objectRotationDegrees = MathFunctions.PS1RotationToDegrees(entry.HorizontalRotation);
            var objectRotationRadians = MathFunctions.DegreesToRadians(objectRotationDegrees);
            var exactRotationDegrees = MathFunctions.PS1RotationToExactDegrees(entry.HorizontalRotation);
            var exactRotationRadians = MathFunctions.DegreesToRadians(exactRotationDegrees);

            // Height of the placement as the client computes it. Placements read from the .mpo file (not groundcover, not caves) are raised: +0.2, +0.4 for names starting with "mainbase_",
            // plus the placement's nudge from addendum.lst; warpgates get no lift but still the nudge. (Client map loader, state 6.)
            var rootHeight = entry.HeightPosition;
            if (isMpoRecord)
            {
                if (!string.Equals(entry.ObjectType, "warpgate", StringComparison.OrdinalIgnoreCase))
                {
                    rootHeight += entry.ObjectType.StartsWith("mainbase_", StringComparison.Ordinal) ? 0.4f : 0.2f;
                }

                // The nudge applies to every .mpo placement, warpgates included
                if (mapId.HasValue && _nudges.TryGetValue((_currentMap, mapId.Value), out var nudge)) rootHeight += nudge;
            }

            var entryObject = new PlanetSideObject
            {
                Id = id,
                ObjectName = entry.ObjectName,
                ObjectType = entry.ObjectType,
                AbsX = entry.HorizontalPosition,
                AbsY = entry.VerticalPosition,
                AbsZ = rootHeight,
                YawDegrees = objectRotationDegrees,
                MapID = mapId,
                IsChildObject = !isTopLevel,
                SortX = entry.HorizontalPosition,
                SortY = entry.VerticalPosition,
                SortZ = rootHeight,
                ExactYawDegrees = exactRotationDegrees
            };
            identifiableObjects.Add(entryObject);
            id++;

            var parentRotationClockwise =
                MathFunctions.CounterClockwiseToClockwiseRotation(entry.HorizontalRotation);

            // Get the sub-entities from the UBR file that would have a GUID within this object
            var baseMesh = UBRReader.GetMeshSystem(entry.ObjectType, uberData.Value);
            if (isTopLevel)
            {
                var (bMinX, bMinY, bMaxX, bMaxY) = (float.MaxValue, float.MaxValue, float.MinValue, float.MinValue);
                foreach (var cx in new[] { baseMesh.BBMin.X, baseMesh.BBMax.X })
                {
                    foreach (var cy in new[] { baseMesh.BBMin.Y, baseMesh.BBMax.Y })
                    {
                        var (qx, qy) = MathFunctions.RotateXY(cx, cy, exactRotationRadians);
                        bMinX = Math.Min(bMinX, qx); bMaxX = Math.Max(bMaxX, qx);
                        bMinY = Math.Min(bMinY, qy); bMaxY = Math.Max(bMaxY, qy);
                    }
                }
                entryObject.BoxMinX = bMinX; entryObject.BoxMinY = bMinY; entryObject.BoxMaxX = bMaxX; entryObject.BoxMaxY = bMaxY;
            }
            foreach (var meshItem in baseMesh.PortalSystem.MeshItems)
            {
                // If it's not an entity that would be assigned a GUID we don't care about it and should skip it
                if (!_objectsWithGuids.Contains(meshItem.AssetName, StringComparer.OrdinalIgnoreCase))
                    continue;

                // If a line is in the pe_hidden list it should be removed from the game world e.g. Neti pad_landing is removed where the BFR building now exists
                if (peHiddens.Any(x => x.InstanceName == meshItem.InstanceName))
                    continue;

                var (rotX, rotY) = MathFunctions.RotateXY(meshItem.Transform[12], meshItem.Transform[13],
                    objectRotationRadians);
                var (exactRotX, exactRotY) = MathFunctions.RotateXY(meshItem.Transform[12], meshItem.Transform[13], exactRotationRadians);
                var meshItemYaw = MathFunctions.TransformToRotationDegrees(meshItem.Transform);

                // Convert from CCW to CW and apply 180 degree offset
                var yaw = parentRotationClockwise + (360 - (180 - meshItemYaw));

                identifiableObjects.Add(new PlanetSideObject
                {
                    Id = id,
                    ObjectName = meshItem.AssetName,
                    ObjectType = meshItem.AssetName,
                    Owner = entryObject.Id,
                    AbsX = entry.HorizontalPosition + rotX,
                    AbsY = entry.VerticalPosition + rotY,
                    AbsZ = rootHeight + meshItem.Transform[14],
                    SortX = entry.HorizontalPosition + exactRotX,
                    SortY = entry.VerticalPosition + exactRotY,
                    SortZ = rootHeight + meshItem.Transform[14],
                    YawDegrees = MathFunctions.NormalizeDegrees((int)yaw),
                    IsChildObject = true
                });
                id++;
            }

            ProcessLSTPeEdits(peEdits, identifiableObjects, objectRotationRadians, exactRotationRadians, baseX: entry.HorizontalPosition, baseY: entry.VerticalPosition, baseZ: rootHeight, ownerId: entryObject.Id, id: ref id);

            ProcessLSTPseRelativeObjects(pseRelativeObjects, identifiableObjects, ownerObject: entryObject, id: ref id);
        }

        private static void ProcessLSTPeEdits(List<Pe_Edit> peEdits, List<PlanetSideObject> identifiableObjects, double baseRotationRadians, double exactRotationRadians, float baseX, float baseY, float baseZ, int ownerId, ref int id)
        {
            foreach (var peEdit in peEdits)
            {
                if (!_objectsWithGuids.Contains(peEdit.ObjectType, StringComparer.OrdinalIgnoreCase)) continue;

                var (rotX, rotY) = MathFunctions.RotateXY(peEdit.RelX, peEdit.RelY, baseRotationRadians);
                var (exactRotX, exactRotY) = MathFunctions.RotateXY(peEdit.RelX, peEdit.RelY, exactRotationRadians);

                identifiableObjects.Add(new PlanetSideObject
                {
                    Id = id,
                    ObjectName = peEdit.ObjectType,
                    ObjectType = peEdit.ObjectType,
                    Owner = ownerId,
                    AbsX = baseX + rotX,
                    AbsY = baseY + rotY,
                    AbsZ = baseZ + peEdit.RelZ,
                    SortX = baseX + exactRotX,
                    SortY = baseY + exactRotY,
                    SortZ = baseZ + peEdit.RelZ,
                    IsChildObject = true
                });
                id++;
            }
        }

        private static void ProcessLSTPseRelativeObjects(List<Pse_RelativeObject> pseRelativeObjects, List<PlanetSideObject> identifiableObjects, PlanetSideObject ownerObject, ref int id)
        {
            foreach (var line in pseRelativeObjects)
            {
                if (!_objectsWithGuids.Contains(line.ObjectName.ToLower())) continue;
                Console.WriteLine($"Processing sub-object {line.ObjectName}");

                var uber = _ubrData.Single(x =>
                    x.Value.Entries.Select(y => y.Name).Contains(line.ObjectName.ToLower()));
                var mesh = UBRReader.GetMeshSystem(line.ObjectName, uber.Value);

                var (relativeObjectRotX, relativeObjectRotY) = MathFunctions.RotateXY(line.RelX, line.RelY, MathFunctions.DegreesToRadians(ownerObject.YawDegrees));
                (float x, float y, float z) relativeObjectPos = (relativeObjectRotX + ownerObject.AbsX, relativeObjectRotY + ownerObject.AbsY, (float)(line.RelZ + ownerObject.SortZ));

                var relativeObjectRotationDegrees = MathFunctions.PS1RotationToDegrees((int)line.Yaw);
                var exactRelativeObjectRotationDegrees = MathFunctions.PS1RotationToExactDegrees((int)line.Yaw);
                var (exactRelRotX, exactRelRotY) = MathFunctions.RotateXY(line.RelX, line.RelY, MathFunctions.DegreesToRadians(ownerObject.ExactYawDegrees));

                // The client also creates the relative object itself as a root placement (it sorts and gets a GUID like any other root)
                // when its definition has a type. No map id is known for these.
                // Note: such a root sits at the same position as the mesh child of the same model (for the map03 tech plant, each pad_landing root is at the exact
                // position of a pad_landing_frame child, which has its own GUID and owner). The two are separate client objects with separate GUIDs, so these are
                // not duplicates by mistake. The server has no use for the roots (it ignores the type) but they must be exported so the GUID numbering stays aligned.
                // The same model also has plain pad_landing mesh children about 1.8 units away (1.6 horizontally, 0.87 higher). The root's Yaw is the owner's rotation plus the row's.
                if (ObjectDefinitions.GetProperty(line.ObjectName, "type") != null)
                {
                    identifiableObjects.Add(new PlanetSideObject
                    {
                        Id = id,
                        ObjectName = line.ObjectName.ToLower(),
                        ObjectType = line.ObjectName.ToLower(),
                        // A root placement: no owner (the server requires a free-standing object to have none)
                        AbsX = relativeObjectPos.x,
                        AbsY = relativeObjectPos.y,
                        AbsZ = relativeObjectPos.z,
                        SortX = ownerObject.SortX + exactRelRotX,
                        SortY = ownerObject.SortY + exactRelRotY,
                        SortZ = relativeObjectPos.z,
                        YawDegrees = MathFunctions.NormalizeDegrees((int)(ownerObject.YawDegrees + relativeObjectRotationDegrees)),
                        IsChildObject = false
                    });
                    id++;
                }

                // Process any sub-objects in the ubr file for this relative object (e.g. bfr_door within bfr_building ubr)
                foreach (var meshItem in mesh.PortalSystem.MeshItems)
                {
                    if (!_objectsWithGuids.Contains(meshItem.AssetName)) continue;

                    var subObjectRotationDegrees = MathFunctions.TransformToRotationDegrees(meshItem.Transform);

                    // Orientation the same way as for the mesh children of a placement (see ProcessObject), with the placement's rotation being the owner's
                    // rotation plus the row's rotation (the same composition that positions the children, which matches the client).
                    var relativeParentRotationClockwise = 360 - (ownerObject.YawDegrees + relativeObjectRotationDegrees);
                    var subObjectYaw = relativeParentRotationClockwise + (360 - (180 - subObjectRotationDegrees));

                    var (exactSubRotX, exactSubRotY) = MathFunctions.RotateXY(meshItem.Transform[12], meshItem.Transform[13], MathFunctions.DegreesToRadians(ownerObject.ExactYawDegrees + exactRelativeObjectRotationDegrees));
                    var (subObjectRotX, subObjectRotY) = MathFunctions.RotateXY(meshItem.Transform[12], meshItem.Transform[13], MathFunctions.DegreesToRadians(ownerObject.YawDegrees + relativeObjectRotationDegrees));
                    identifiableObjects.Add(new PlanetSideObject
                    {
                        Id = id,
                        ObjectName = meshItem.AssetName,
                        ObjectType = meshItem.AssetName,
                        Owner = ownerObject.Id,
                        AbsX = relativeObjectPos.x + subObjectRotX,
                        AbsY = relativeObjectPos.y + subObjectRotY,
                        AbsZ = relativeObjectPos.z + meshItem.Transform[14],
                        SortX = ownerObject.SortX + exactRelRotX + exactSubRotX,
                        SortY = ownerObject.SortY + exactRelRotY + exactSubRotY,
                        SortZ = relativeObjectPos.z + meshItem.Transform[14],
                        YawDegrees = MathFunctions.NormalizeDegrees((int)subObjectYaw),
                        IsChildObject = true
                    });
                    id++;
                }
            }
        }

        private static void RemoveUnusedLluSockets(List<PlanetSideObject> identifiableObjects, string mapNumber)
        {
            // Remove any LLU sockets from non-CTF bases. Technically all bases have these, but they are hidden and unused by the client.
            var objectTypesToCheck = new List<string>
            {
                "amp_station",
                "cryo_facility",
                "comm_station",
                "comm_station_dsp",
                "tech_plant"
            };

            var socketsToRemove = new List<PlanetSideObject>();
            foreach (var obj in identifiableObjects.Where(x => objectTypesToCheck.Contains(x.ObjectType.ToLower())))
            { 
                var isCtfBase = objReader.GetByObjectName($"map{mapNumber}").Any(x => x[2].StartsWith("building_ctf_") && x[3].ToLower() == obj.ObjectName.ToLower());

                if (!isCtfBase)
                {
                    var socket = identifiableObjects.SingleOrDefault(x => x.ObjectType == "llm_socket" && x.Owner == obj.Id);
                    if (socket != null)
                    {
                        socketsToRemove.Add(socket);
                    }
                }
            }

            identifiableObjects.RemoveAll(x => socketsToRemove.Select(y => y.Id).Contains(x.Id));
        }
    }
}
