using BepInEx;
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
    [BepInPlugin(MOD_ID, MOD_TITLE, "0.1.0")]
    class Plugin : BaseUnityPlugin
    {
        private const string MOD_ID = "olaycolay.slugpupdaycare";
        private const string MOD_TITLE = "Slugpup Daycare";

        public HashSet<string> daycareRooms = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> daycareRegions = new(StringComparer.OrdinalIgnoreCase);

        public string daycareRoomsPath = "daycarerooms.txt";

        // Load any resources, such as sprites or sounds
        private void LoadResources(RainWorld rainWorld)
        {
            string path = AssetManager.ResolveFilePath(daycareRoomsPath);
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

            daycareRegions = [.. daycareRooms.Select(s => s.Split('_')[0]).Distinct()];
        }

        // Add hooks
        public void OnEnable()
        {
            On.RainWorld.OnModsInit += Extras.WrapInit(LoadResources);

            On.OverWorld.LoadWorld_string_Name_Timeline_bool += OverWorld_LoadWorld_string_Name_Timeline_bool;

            On.ShelterDoor.DoorClosed += ShelterDoor_DoorClosed;

            On.Room.ReadyForAI += Room_ReadyForAI;

            On.MiscWorldSaveData.ToString += MiscWorldSaveData_ToString;
            On.MiscWorldSaveData.FromString += MiscWorldSaveData_FromString;

            new Hook(typeof(CustomMerge).GetMethod(nameof(CustomMerge.MergeCustomFiles)), CustomMerge_MergeCustomFiles);
        }

        // Save slugpups in daycare rooms from region we are exiting
        private void OverWorld_LoadWorld_string_Name_Timeline_bool(
            On.OverWorld.orig_LoadWorld_string_Name_Timeline_bool orig,
            OverWorld self,
            string worldName,
            SlugcatStats.Name playerCharacterNumber,
            SlugcatStats.Timeline time,
            bool singleRoomWorld)
        {
            Custom.Log(
            [
                MOD_TITLE,
                "Daycare rooms",
                .. daycareRooms
            ]);

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
        private void ShelterDoor_DoorClosed(On.ShelterDoor.orig_DoorClosed orig, ShelterDoor self)
        {
            string regionAcronym = Region.GetVanillaEquivalentRegionAcronym(self.room.world.name);
            if (daycareRegions.Contains(regionAcronym))
            {
                IEnumerable<AbstractRoom> otherDaycareRoomsInRegion = self.room.world.abstractRooms
                    .Where(room => daycareRooms.Contains(room.name) && room.name != self.room.abstractRoom.name);
                SaveSlugpupsInRooms(otherDaycareRoomsInRegion, self.room.game.GetStorySession.saveState.miscWorldSaveData.SD());
            }

            orig(self);
        }

        // Spawn saved slugpups
        private void Room_ReadyForAI(On.Room.orig_ReadyForAI orig, Room self)
        {
            if (daycareRooms.Contains(self.abstractRoom.name))
            {
                MiscWorldSaveDataData sd = self.game.GetStorySession.saveState.miscWorldSaveData.SD();
                if (sd.daycareSlugpups.TryGetValue(self.abstractRoom.name, out List<string> slugpupsToRespawn))
                {
                    Custom.Log(
                    [
                        MOD_TITLE,
                        "Spawning slugpups in room",
                        self.abstractRoom.name
                    ]);
                    for (int i = 0; i < slugpupsToRespawn.Count;)
                    {
                        Custom.Log([MOD_TITLE, "Checking if we can spawn saved slugpup", slugpupsToRespawn[i]]);
                        AbstractCreature slugpup = SaveState.AbstractCreatureFromString(self.world, slugpupsToRespawn[i], true);
                        if (slugpup.Room.name == self.abstractRoom.name)
                        {
                            Custom.Log(
                            [
                                MOD_TITLE,
                                "Adding entity to room",
                                slugpup.ID.number.ToString(),
                                self.abstractRoom.name
                            ]);
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
                KeyValuePair<string, List<string>>[] slugpupsToSave = [.. sd.daycareSlugpups.Distinct()];
                for (int i = 0; i < slugpupsToSave.Length; i++)
                {
                    addToSave += slugpupsToSave[i].Key + "<mwC>" + string.Join("<mwD>", slugpupsToSave[i].Value) +
                        (i < slugpupsToSave.Length - 1 ? "<mwB>" : "");
                }
                addToSave += "<mwA>";
                Custom.Log(
                [
                    MOD_TITLE,
                    "Adding slugpups to miscWorldSave",
                    addToSave
                ]);
                self.unrecognizedSaveStrings.Add(addToSave);
            }

            return orig(self);
        }

        // Retrieve slugpups in daycare from miscWorldSave
        private void MiscWorldSaveData_FromString(On.MiscWorldSaveData.orig_FromString orig, MiscWorldSaveData self, string s)
        {
            orig(self, s);

            MiscWorldSaveDataData sd = self.SD();
            sd.daycareSlugpups = [];

            //Custom.Log([MOD_TITLE, "Unrecognized save strings", .. self.unrecognizedSaveStrings]);
            string slugpupDaycareSave = self.unrecognizedSaveStrings.FirstOrDefault(s => s.StartsWith("DAYCARESLUGPUPS"));
            if (!slugpupDaycareSave.IsNullOrWhiteSpace())
            {
                Custom.Log(
                [
                    MOD_TITLE,
                    "Slugpups Daycare save",
                    slugpupDaycareSave
                ]);
                IEnumerable<string> slugpupDaycareRooms = Regex.Split(slugpupDaycareSave, "<mwB>").Skip(1);
                Custom.Log(
                [
                    MOD_TITLE,
                    "All saved slugpups",
                    .. slugpupDaycareRooms
                ]);
                foreach (string region in slugpupDaycareRooms)
                {
                    if (!region.IsNullOrWhiteSpace())
                    {
                        string[] slugpupStrings = Regex.Split(region, "<mwC>");
                        Custom.Log(
                        [
                            MOD_TITLE,
                            "Retrieving slugpups from save for room",
                            .. slugpupStrings
                        ]);
                        sd.daycareSlugpups[slugpupStrings[0]] = [.. Regex.Split(slugpupStrings[1], "<mwD>")];
                    }
                }
            }
        }

        // Merge daycarerooms.txt between all mods
        private void CustomMerge_MergeCustomFiles(Action orig)
        {
            orig();

            string filePath = Path.Combine(Path.Combine(Custom.RootFolderDirectory(), "mergedmods"), daycareRoomsPath);
            if (!File.Exists(filePath))
            {
                CustomRegionsMod.CustomLog("merging daycarerooms");
                CustomMerge.MergeSpecific(daycareRoomsPath);
            }
        }

        private void SaveSlugpupsInRooms(IEnumerable<AbstractRoom> daycareRooms, MiscWorldSaveDataData sd)
        {
            foreach (AbstractRoom daycareRoom in daycareRooms)
            {
                sd.daycareSlugpups[daycareRoom.name] = [];
                List<string> slugpupsInRoom = [.. daycareRoom.creatures
                            .Where(creature => creature.creatureTemplate.TopAncestor().type == MoreSlugcatsEnums.CreatureTemplateType.SlugNPC)
                            .Select(SaveState.AbstractCreatureToStringStoryWorld)];
                if (slugpupsInRoom.Count > 0)
                {
                    Custom.Log(
                    [
                        MOD_TITLE,
                            "Adding slugpup(s) to be saved",
                            .. slugpupsInRoom
                    ]);
                    sd.daycareSlugpups[daycareRoom.name].AddRange(slugpupsInRoom);
                }
            }
        }
    }
}