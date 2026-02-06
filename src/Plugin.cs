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

        public string[] daycareRegions = ["DM", "OE"];

        // Load any resources, such as sprites or sounds
        private void LoadResources(RainWorld rainWorld)
        {
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
                    sd.daycareSlugpups[oldRegionAcronym] = [];
                    for (int i = oldRegion.firstRoomIndex; i < oldRegion.firstRoomIndex + oldRegion.NumberOfRooms; i++)
                    {
                        List<AbstractCreature> creaturesInRoom = oldRegion.GetAbstractRoom(i).creatures;
                        for (int j = 0; j < creaturesInRoom.Count; j++)
                        {
                            if (creaturesInRoom[j].creatureTemplate.TopAncestor().type == MoreSlugcatsEnums.CreatureTemplateType.SlugNPC)
                            {
                                Custom.Log([MOD_TITLE, "Adding slugpup to be saved", creaturesInRoom[j].ID.number.ToString(), oldRegion.GetAbstractRoom(i).name]);
                                sd.daycareSlugpups[oldRegionAcronym].Add(SaveState.AbstractCreatureToStringStoryWorld(creaturesInRoom[j]));
                            }
                        }
                    }
                }
            }

            orig(self, worldName, playerCharacterNumber, time, singleRoomWorld);
        }

        private void Room_ReadyForAI(On.Room.orig_ReadyForAI orig, Room self)
        {
            Custom.Log([MOD_TITLE, "Spawning slugpups in room", self.abstractRoom.name]);
            string regionAcronym = Region.GetVanillaEquivalentRegionAcronym(self.world.name);
            if (daycareRegions.Contains(regionAcronym))
            {
                MiscWorldSaveDataData sd = self.game.GetStorySession.saveState.miscWorldSaveData.SD();
                if (sd.daycareSlugpups.TryGetValue(regionAcronym, out List<string> slugpupsToRespawn))
                {
                    for (int i = 0; i < slugpupsToRespawn.Count;)
                    {
                        Custom.Log([MOD_TITLE, "Checking if we can spawn saved slugpup", slugpupsToRespawn[i]]);
                        AbstractCreature slugpup = SaveState.AbstractCreatureFromString(self.world, slugpupsToRespawn[i], true);
                        if (slugpup.Room.name == self.abstractRoom.name)
                        {
                            Custom.Log([MOD_TITLE, "Adding entity to room", slugpup.ID.number.ToString(), self.abstractRoom.name]);
                            slugpup.Room.AddEntity(slugpup);
                            (slugpup.state as PlayerNPCState).foodInStomach = int.MaxValue;
                            sd.daycareSlugpups[regionAcronym].Remove(slugpupsToRespawn[i]);
                        }
                        else
                        {
                            i++;
                        }
                    }

                    if (sd.daycareSlugpups[regionAcronym].Count == 0)
                    {
                        sd.daycareSlugpups.Remove(regionAcronym);
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
            Custom.Log([MOD_TITLE, "Adding slugpups to miscWorldSave", addToSave]);

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
                Custom.Log([MOD_TITLE, "Slugpups Daycare save", slugpupDaycareSave]);
                IEnumerable<string> slugpupDaycareRegions = Regex.Split(slugpupDaycareSave, "<mwB>").Skip(1);
                Custom.Log([MOD_TITLE, "All saved slugpups", .. slugpupDaycareRegions]);
                foreach (string region in slugpupDaycareRegions)
                {
                    if (!region.IsNullOrWhiteSpace())
                    {
                        string[] slugpupStrings = Regex.Split(region, "<mwC>");
                        Custom.Log([MOD_TITLE, "Retreiving slugpups from save for region", .. slugpupStrings]);
                        sd.daycareSlugpups[slugpupStrings[0]] = [.. Regex.Split(slugpupStrings[1], "<mwD>")];
                    }
                }
            }
        }
    }
}