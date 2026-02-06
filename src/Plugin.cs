using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BepInEx;
using MoreSlugcats;
using RWCustom;

namespace SlugpupDaycare
{
    [BepInPlugin(MOD_ID, MOD_TITLE, "0.1.0")]
    class Plugin : BaseUnityPlugin
    {
        private const string MOD_ID = "olaycolay.slugpupdaycare";
        private const string MOD_TITLE = "Slugpup Daycare";

        public string[] daycareRooms = ["DM_STOP", "OE_SEXTRA"];
        public string[] daycareRegions;

        // Load any resources, such as sprites or sounds
        private void LoadResources(RainWorld rainWorld)
        {
            daycareRegions = [.. daycareRooms.Select(s => s.Split('_')[0]).Distinct()];
        }

        // Add hooks
        public void OnEnable()
        {
            On.RainWorld.OnModsInit += Extras.WrapInit(LoadResources);

            On.OverWorld.LoadWorld_string_Name_Timeline_bool += OverWorld_LoadWorld_string_Name_Timeline_bool; ;

            On.Room.ReadyForAI += Room_ReadyForAI;

            On.MiscWorldSaveData.ToString += MiscWorldSaveData_ToString;
            On.MiscWorldSaveData.FromString += MiscWorldSaveData_FromString;
        }

        private void OverWorld_LoadWorld_string_Name_Timeline_bool(On.OverWorld.orig_LoadWorld_string_Name_Timeline_bool orig, OverWorld self, string worldName, SlugcatStats.Name playerCharacterNumber, SlugcatStats.Timeline time, bool singleRoomWorld)
        {
            World oldRegion = self.activeWorld;

            if (oldRegion != null)
            {
                string oldRegionAcronym = Region.GetVanillaEquivalentRegionAcronym(oldRegion.name);
                if (daycareRegions.Contains(oldRegionAcronym))
                {
                    MiscWorldSaveDataData sd = self.game.GetStorySession.saveState.miscWorldSaveData.SD();
                    IEnumerable<AbstractRoom> regionDaycareRooms = oldRegion.abstractRooms.Where(room => daycareRooms.Contains(room.name));
                    foreach (AbstractRoom daycareRoom in regionDaycareRooms)
                    {
                        sd.daycareSlugpups[daycareRoom.name] = [];
                        List<string> slugpupsInRoom = [.. daycareRoom.creatures
                            .Where(creature => creature.creatureTemplate.TopAncestor().type == MoreSlugcatsEnums.CreatureTemplateType.SlugNPC)
                            .Select(SaveState.AbstractCreatureToStringStoryWorld)];
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

            orig(self, worldName, playerCharacterNumber, time, singleRoomWorld);
        }

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
                            (slugpup.state as PlayerNPCState).foodInStomach = int.MaxValue;
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

        private string MiscWorldSaveData_ToString(On.MiscWorldSaveData.orig_ToString orig, MiscWorldSaveData self)
        {
            MiscWorldSaveDataData sd = self.SD();

            string addToSave = "";
            if (sd.daycareSlugpups != null)
            {
                addToSave += "DAYCARESLUGPUPS<mwB>";
                KeyValuePair<string, List<string>>[] slugpupsToSave = [.. sd.daycareSlugpups.Distinct()];
                for (int i = 0; i < slugpupsToSave.Length; i++)
                {
                    addToSave += slugpupsToSave[i].Key + "<mwC>" + string.Join("<mwD>", slugpupsToSave[i].Value) +
                        (i < slugpupsToSave.Length - 1 ? "<mwB>" : "");
                }
                addToSave += "<mwA>";
            }
            Custom.Log(
            [
                MOD_TITLE,
                "Adding slugpups to miscWorldSave",
                addToSave
            ]);

            return orig(self) + addToSave;
        }

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
                            "Retreiving slugpups from save for room",
                            .. slugpupStrings
                        ]);
                        sd.daycareSlugpups[slugpupStrings[0]] = [.. Regex.Split(slugpupStrings[1], "<mwD>")];
                    }
                }
            }
        }
    }
}