using BepInEx;
using BepInEx.Logging;
using CustomRegions.Mod;
using MonoMod.RuntimeDetour;
using MoreSlugcats;
using RWCustom;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SlugpupDaycare
{
    [BepInPlugin(MOD_ID, MOD_TITLE, "0.2.0")]
    class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource SDLogger;

        private const string MOD_ID = "olaycolay.slugpupdaycare";
        private const string MOD_TITLE = "Slugpup Daycare";
        private const string DAYCARE_ROOMS_PATH = "daycarerooms.txt";

        public HashSet<string> daycareRooms = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> daycareRegions = new(StringComparer.OrdinalIgnoreCase);

        // Load any resources, such as sprites or sounds
        private void LoadResources(RainWorld rainWorld)
        {
        }

        // Add hooks
        public void OnEnable()
        {
            SDLogger = Logger;

            On.RainWorld.OnModsInit += Extras.WrapInit(LoadResources);

            On.OverWorld.LoadWorld_string_Name_Timeline_bool += OverWorld_LoadWorld;

            On.RainWorldGame.Win += RainWorldGame_Win;

            On.Room.ReadyForAI += Room_ReadyForAI;

            On.MiscWorldSaveData.ToString += MiscWorldSaveData_ToString;
            On.MiscWorldSaveData.FromString += MiscWorldSaveData_FromString;

            new Hook(typeof(CustomMerge).GetMethod(nameof(CustomMerge.MergeCustomFiles)), CustomMerge_MergeCustomFiles);
        }

        // Save slugpups in daycare rooms from region we are exiting
        private void OverWorld_LoadWorld(
            On.OverWorld.orig_LoadWorld_string_Name_Timeline_bool orig,
            OverWorld self,
            string worldName,
            SlugcatStats.Name playerCharacterNumber,
            SlugcatStats.Timeline time,
            bool singleRoomWorld)
        {
            World oldRegion = self.activeWorld;

            if (oldRegion != null)
            {
                string oldRegionAcronym = Region.GetVanillaEquivalentRegionAcronym(oldRegion.name);
                if (daycareRegions.Contains(oldRegionAcronym))
                {
                    MiscWorldSaveDataData sd = self.game.GetStorySession.saveState.miscWorldSaveData.SD();
                    IEnumerable<AbstractRoom> regionDaycareRooms = oldRegion.abstractRooms.Where(room => daycareRooms.Contains(room.name));
                    SaveSlugpupsInRooms(regionDaycareRooms, sd);
                }
            }

            orig(self, worldName, playerCharacterNumber, time, singleRoomWorld);
        }

        // Save slugpups in other daycare rooms if we shelter in the same region
        private void RainWorldGame_Win(On.RainWorldGame.orig_Win orig, RainWorldGame self, bool malnourished, bool fromWarpPoint)
        {
            if (self.FirstAlivePlayer != null && self.manager.upcomingProcess == null)
            {
                AbstractRoom shelterRoom = self.world.GetAbstractRoom(self.FirstAlivePlayer.pos);
                string regionAcronym = Region.GetVanillaEquivalentRegionAcronym(shelterRoom.world.name);
                if (daycareRegions.Contains(regionAcronym))
                {
                    IEnumerable<AbstractRoom> otherDaycareRoomsInRegion = shelterRoom.world.abstractRooms
                        .Where(room => daycareRooms.Contains(room.name) && room.name != shelterRoom.name);
                    SaveSlugpupsInRooms(otherDaycareRoomsInRegion, self.GetStorySession.saveState.miscWorldSaveData.SD());
                }
            }

            orig(self, malnourished, fromWarpPoint);
        }

        // Spawn saved slugpups
        private void Room_ReadyForAI(On.Room.orig_ReadyForAI orig, Room self)
        {
            if (daycareRooms.Contains(self.abstractRoom.name) && self.game != null && self.game.IsStorySession)
            {
                MiscWorldSaveDataData sd = self.game.GetStorySession.saveState.miscWorldSaveData.SD();
                if (sd.daycareSlugpups.TryGetValue(self.abstractRoom.name, out List<string> slugpupsToRespawn))
                {
                    SDLogger.LogInfo(string.Join("\t",
                    [
                        "Spawning slugpups in room",
                        self.abstractRoom.name
                    ]));
                    for (int i = 0; i < slugpupsToRespawn.Count;)
                    {
                        SDLogger.LogInfo(string.Join("\t",
                        [
                            "Checking if we can spawn saved slugpup",
                            slugpupsToRespawn[i]
                        ]));
                        AbstractCreature slugpup = SaveState.AbstractCreatureFromString(self.world, slugpupsToRespawn[i], true);
                        if (slugpup.Room.name == self.abstractRoom.name)
                        {
                            SDLogger.LogInfo(string.Join("\t",
                            [
                                "Adding entity to room",
                                slugpup.ID.number.ToString(),
                                self.abstractRoom.name
                            ]));
                            slugpup.Room.AddEntity(slugpup);
                            (slugpup.state as PlayerNPCState).foodInStomach = 3;
                            sd.daycareSlugpups[self.abstractRoom.name].Remove(slugpupsToRespawn[i]);
                        }
                        else
                        {
                            i++;
                        }
                    }

                    if (sd.daycareSlugpups[self.abstractRoom.name].Count == 0)
                    {
                        sd.daycareSlugpups.Remove(self.abstractRoom.name);
                    }
                }
            }

            orig(self);
        }

        // Add slugpups in daycare to miscWorldSave
        private string MiscWorldSaveData_ToString(On.MiscWorldSaveData.orig_ToString orig, MiscWorldSaveData self)
        {
            MiscWorldSaveDataData sd = self.SD();

            self.unrecognizedSaveStrings.RemoveAll(str => str.StartsWith("DAYCARESLUGPUPS"));

            string addToSave = "";
            if (sd.daycareSlugpups != null && sd.daycareSlugpups.Count > 0)
            {
                addToSave += "DAYCARESLUGPUPS<mwB>";
                KeyValuePair<string, List<string>>[] slugpupsToSave = [.. sd.daycareSlugpups.Where(kvp => kvp.Value.Count > 0).Distinct()];
                for (int i = 0; i < slugpupsToSave.Length; i++)
                {
                    addToSave += slugpupsToSave[i].Key + "<mwC>" + string.Join("<mwD>", slugpupsToSave[i].Value) +
                        (i < slugpupsToSave.Length - 1 ? "<mwB>" : "");
                }
                addToSave += "<mwA>";
                SDLogger.LogInfo(string.Join("\t",
                [
                    "Adding slugpups to miscWorldSave",
                    addToSave
                ]));
                self.unrecognizedSaveStrings.Add(addToSave);
            }
            else
            {
                SDLogger.LogInfo("No slugpups in daycares to save");
            }

            return orig(self);
        }

        // Retrieve slugpups in daycare from miscWorldSave
        private void MiscWorldSaveData_FromString(On.MiscWorldSaveData.orig_FromString orig, MiscWorldSaveData self, string s)
        {
            orig(self, s);

            MiscWorldSaveDataData sd = self.SD();
            sd.daycareSlugpups = [];

            //SDLogger.LogInfo(string.Join("\t",[MOD_TITLE, "Unrecognized save strings", .. self.unrecognizedSaveStrings]);
            string slugpupDaycareSave = self.unrecognizedSaveStrings.FirstOrDefault(s => s.StartsWith("DAYCARESLUGPUPS"));
            if (!slugpupDaycareSave.IsNullOrWhiteSpace())
            {
                SDLogger.LogInfo(string.Join("\t",
                [
                    "Slugpups Daycare save",
                    slugpupDaycareSave
                ]));
                IEnumerable<string> slugpupDaycareRooms = Regex.Split(slugpupDaycareSave, "<mwB>").Skip(1);
                SDLogger.LogInfo(string.Join("\t",
                [
                    "All saved slugpups",
                    .. slugpupDaycareRooms
                ]));
                foreach (string region in slugpupDaycareRooms)
                {
                    if (!region.IsNullOrWhiteSpace())
                    {
                        string[] slugpupStrings = Regex.Split(region, "<mwC>");
                        SDLogger.LogInfo(string.Join("\t",
                        [
                            "Retrieving slugpups from save for room",
                            .. slugpupStrings
                        ]));
                        sd.daycareSlugpups[slugpupStrings[0]] = [.. Regex.Split(slugpupStrings[1], "<mwD>")];
                    }
                }
            }
        }

        // Merge daycarerooms.txt between all mods
        private void CustomMerge_MergeCustomFiles(Action orig)
        {
            orig();

            string filePath = Path.Combine(Path.Combine(Custom.RootFolderDirectory(), "mergedmods"), DAYCARE_ROOMS_PATH);
            if (!File.Exists(filePath))
            {
                CustomRegionsMod.CustomLog("merging daycarerooms");
                CustomMerge.MergeSpecific(DAYCARE_ROOMS_PATH);
            }

            LoadDaycareRoomsSet();
        }

        private void LoadDaycareRoomsSet()
        {
            string path = AssetManager.ResolveFilePath(DAYCARE_ROOMS_PATH);
            if (File.Exists(path))
            {
                foreach (string text in File.ReadAllLines(path))
                {
                    if (!daycareRooms.Contains(text.Trim()))
                    {
                        daycareRooms.Add(text.Trim());
                    }
                }
            }

            daycareRooms = [.. daycareRooms.Distinct()];
            daycareRegions = [.. daycareRooms.Select(s => s.Split('_')[0]).Distinct()];

            SDLogger.LogInfo(string.Join("\t",
            [
                "Daycare rooms:",
                .. daycareRooms.ToArray()
            ]));
        }

        private void SaveSlugpupsInRooms(IEnumerable<AbstractRoom> daycareRooms, MiscWorldSaveDataData sd)
        {
            foreach (AbstractRoom daycareRoom in daycareRooms)
            {
                IEnumerable<AbstractCreature> slugpupsInRoom = daycareRoom.creatures
                            .Where(creature => creature.Room == daycareRoom && creature.creatureTemplate.TopAncestor().type == MoreSlugcatsEnums.CreatureTemplateType.SlugNPC);
                foreach (AbstractCreature slugpup in slugpupsInRoom)
                {
                    (slugpup.state as PlayerNPCState).foodInStomach = 3;
                }

                List<string> slugpupsInRoomStrings = [.. slugpupsInRoom.Select(SaveState.AbstractCreatureToStringStoryWorld)];
                if (slugpupsInRoomStrings.Count > 0)
                {
                    SDLogger.LogInfo(string.Join("\t",
                    [
                            "Adding slugpup(s) to be saved",
                            .. slugpupsInRoomStrings
                    ]));
                    sd.daycareSlugpups[daycareRoom.name] = slugpupsInRoomStrings;
                }
            }
        }
    }
}