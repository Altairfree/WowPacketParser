﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using WowPacketParser.Enums;
using WowPacketParser.Hotfix;
using WowPacketParser.Misc;
using WowPacketParser.Store;
using WowPacketParser.Store.Objects;

namespace WowPacketParser.SQL.Builders
{
    [BuilderClass]
    public static class UnitMisc
    {
        [BuilderMethod(Units = true)]
        public static string CreatureTemplateAddon(Dictionary<WowGuid, Unit> units)
        {
            if (units.Count == 0)
                return string.Empty;

            if (!Settings.SQLOutputFlag.HasAnyFlagBit(SQLOutput.creature_template_addon))
                return string.Empty;

            var addons = new DataBag<CreatureTemplateAddon>();
            foreach (var unit in units)
            {
                var npc = unit.Value;

                if (Settings.AreaFilters.Length > 0)
                    if (!(npc.Area.ToString(CultureInfo.InvariantCulture).MatchesFilters(Settings.AreaFilters)))
                        continue;

                if (Settings.MapFilters.Length > 0)
                    if (!(npc.Map.ToString(CultureInfo.InvariantCulture).MatchesFilters(Settings.MapFilters)))
                        continue;

                var auras = string.Empty;
                var commentAuras = string.Empty;
                if (npc.Auras != null && npc.Auras.Count != 0)
                {
                    foreach (var aura in npc.Auras.Where(aura =>
                        aura != null &&
                        (ClientVersion.AddedInVersion(ClientType.MistsOfPandaria) ?
                            aura.AuraFlags.HasAnyFlag(AuraFlagMoP.NoCaster) :
                            aura.AuraFlags.HasAnyFlag(AuraFlag.NotCaster))))
                    {
                        auras += aura.SpellId + " ";
                        commentAuras += StoreGetters.GetName(StoreNameType.Spell, (int) aura.SpellId, false) + ", ";
                    }
                    auras = auras.TrimEnd(' ');
                    commentAuras = commentAuras.TrimEnd(',', ' ');
                }

                var addon = new CreatureTemplateAddon
                {
                    Entry = unit.Key.GetEntry(),
                    PathID = 0,
                    MountID = (uint)npc.UnitData.MountDisplayID,
                    Bytes1 = npc.Bytes1,
                    Bytes2 = npc.Bytes2,
                    Emote = 0,
                    AIAnimKit = npc.AIAnimKit.GetValueOrDefault(0),
                    MovementAnimKit = npc.MovementAnimKit.GetValueOrDefault(0),
                    MeleeAnimKit = npc.MeleeAnimKit.GetValueOrDefault(0),
                    Auras = auras,
                    CommentAuras = commentAuras
                };

                if (addons.ContainsKey(addon))
                    continue;

                addons.Add(addon);
            }

            var addonsDb = SQLDatabase.Get(addons);
            return SQLUtil.Compare(addons, addonsDb,
                addon =>
                {
                    var comment = StoreGetters.GetName(StoreNameType.Unit, (int)addon.Entry.GetValueOrDefault());
                    if (!string.IsNullOrEmpty(addon.CommentAuras))
                        comment += " - " + addon.CommentAuras;
                    return comment;
                });
        }

        public static Dictionary<uint, Tuple<int, int>> GetScalingDeltaLevels(Dictionary<WowGuid, Unit> units)
        {
            if (units.Count == 0)
                return null;

            var entries = units.GroupBy(unit => unit.Key.GetEntry());
            var list = new Dictionary<uint, List<int>>();

            foreach (var pair in entries.SelectMany(entry => entry))
            {
                if (list.ContainsKey(pair.Key.GetEntry()))
                    list[pair.Key.GetEntry()].Add(pair.Value.UnitData.ScalingLevelDelta);
                else
                    list.Add(pair.Key.GetEntry(), new List<int> { pair.Value.UnitData.ScalingLevelDelta });
            }

            var result = list.ToDictionary(pair => pair.Key, pair => Tuple.Create(pair.Value.Min(), pair.Value.Max()));

            return result.Count == 0 ? null : result;
        }

        [BuilderMethod(true, Units = true)]
        public static string CreatureTemplateScalingData(Dictionary<WowGuid, Unit> units)
        {
            if (units.Count == 0)
                return string.Empty;

            if (!Settings.SQLOutputFlag.HasAnyFlagBit(SQLOutput.creature_template_scaling))
                return string.Empty;

            var scalingdeltalevels = GetScalingDeltaLevels(units);

            foreach (var unit in units)
            {
                if (Storage.CreatureTemplateScalings.Any(creature => creature.Item1.Entry == unit.Key.GetEntry()))
                    continue;

                var npc = unit.Value;

                var minLevel = (uint)npc.UnitData.ScalingLevelMin;
                var maxLevel = (uint)npc.UnitData.ScalingLevelMax;
                var contentTuningID = npc.UnitData.ContentTuningID;

                if (minLevel != 0 || maxLevel != 0 || contentTuningID != 0)
                {
                    Storage.CreatureTemplateScalings.Add(new CreatureTemplateScaling
                    {
                        Entry = unit.Key.GetEntry(),
                        DifficultyID = npc.DifficultyID,
                        LevelScalingMin = minLevel,
                        LevelScalingMax = maxLevel,
                        LevelScalingDeltaMin = scalingdeltalevels[unit.Key.GetEntry()].Item1,
                        LevelScalingDeltaMax = scalingdeltalevels[unit.Key.GetEntry()].Item2,
                        ContentTuningID = contentTuningID
                    });
                }
            }

            var templatesDb = SQLDatabase.Get(Storage.CreatureTemplateScalings);

            return SQLUtil.Compare(Settings.SQLOrderByKey ? Storage.CreatureTemplateScalings.OrderBy(x => x.Item1.Entry).ToArray() : Storage.CreatureTemplateScalings.ToArray(), templatesDb, x => string.Empty);
        }

        [BuilderMethod(Units = true)]
        public static string ModelData(Dictionary<WowGuid, Unit> units)
        {
            if (units.Count == 0)
                return string.Empty;

            if (!Settings.SQLOutputFlag.HasAnyFlagBit(SQLOutput.creature_model_info))
                return string.Empty;

            var models = new DataBag<ModelData>();
            foreach (var npc in units.Select(unit => unit.Value))
            {
                if (Settings.AreaFilters.Length > 0)
                    if (!(npc.Area.ToString(CultureInfo.InvariantCulture).MatchesFilters(Settings.AreaFilters)))
                        continue;

                if (Settings.MapFilters.Length > 0)
                    if (!(npc.Map.ToString(CultureInfo.InvariantCulture).MatchesFilters(Settings.MapFilters)))
                        continue;

                uint modelId = (uint)npc.UnitData.DisplayID;
                if (modelId == 0)
                    continue;

                var model = new ModelData
                {
                    DisplayID = modelId
                };

                //if (models.ContainsKey(model))
                if (models.Any(modelInfo => modelInfo.Item1.DisplayID == modelId))
                    continue;

                var scale = npc.ObjectData.Scale;
                model.BoundingRadius = npc.UnitData.BoundingRadius / scale;
                model.CombatReach = npc.UnitData.CombatReach / scale;
                model.Gender = (Gender)npc.UnitData.Sex;

                models.Add(model);
            }

            var modelsDb = SQLDatabase.Get(models);
            return SQLUtil.Compare(models, modelsDb, StoreNameType.None);
        }

        [BuilderMethod]
        public static string NpcTrainer()
        {
            if (Storage.NpcTrainers.IsEmpty())
                return string.Empty;

            if (!Settings.SQLOutputFlag.HasAnyFlagBit(SQLOutput.npc_trainer))
                return string.Empty;

            var templatesDb = SQLDatabase.Get(Storage.NpcTrainers);

            return SQLUtil.Compare(Storage.NpcTrainers, templatesDb, StoreNameType.Unit);
        }

        [BuilderMethod]
        public static string Trainer()
        {
            if (Storage.Trainers.IsEmpty())
                return string.Empty;

            if (!Settings.SQLOutputFlag.HasAnyFlagBit(SQLOutput.trainer))
                return string.Empty;

            var templatesDb = SQLDatabase.Get(Storage.Trainers);

            return SQLUtil.Compare(Storage.Trainers, templatesDb, StoreNameType.None);
        }

        [BuilderMethod]
        public static string TrainerSpell()
        {
            if (Storage.TrainerSpells.IsEmpty())
                return string.Empty;

            if (!Settings.SQLOutputFlag.HasAnyFlagBit(SQLOutput.trainer))
                return string.Empty;

            foreach (var trainerSpell in Storage.TrainerSpells)
                trainerSpell.Item1.ConvertToDBStruct();

            var templatesDb = SQLDatabase.Get(Storage.TrainerSpells);

            return SQLUtil.Compare(Storage.TrainerSpells, templatesDb, t => t.FactionHelper);
        }

        [BuilderMethod]
        public static string CreatureTrainer()
        {
            if (Storage.CreatureTrainers.IsEmpty())
                return string.Empty;

            if (!Settings.SQLOutputFlag.HasAnyFlagBit(SQLOutput.trainer))
                return string.Empty;

            return SQLUtil.Compare(Storage.CreatureTrainers, SQLDatabase.Get(Storage.CreatureTrainers), StoreNameType.None);
        }

        [BuilderMethod]
        public static string NpcVendor()
        {
            if (Storage.NpcVendors.IsEmpty())
                return string.Empty;

            if (!Settings.SQLOutputFlag.HasAnyFlagBit(SQLOutput.npc_vendor))
                return string.Empty;

            var templatesDb = SQLDatabase.Get(Storage.NpcVendors);

            return SQLUtil.Compare(Storage.NpcVendors, templatesDb,
                vendor => StoreGetters.GetName(vendor.Type <= 1 ? StoreNameType.Item : StoreNameType.Currency, vendor.Item.GetValueOrDefault(), false));
        }

        [BuilderMethod(Units = true)]
        public static string CreatureEquip(Dictionary<WowGuid, Unit> units)
        {
            if (units.Count == 0)
                return string.Empty;

            if (!Settings.SQLOutputFlag.HasAnyFlagBit(SQLOutput.creature_equip_template))
                return string.Empty;

            var equips = new DataBag<CreatureEquipment>();
            foreach (var npc in units)
            {
                if (Settings.AreaFilters.Length > 0)
                    if (!(npc.Value.Area.ToString(CultureInfo.InvariantCulture).MatchesFilters(Settings.AreaFilters)))
                        continue;

                if (Settings.MapFilters.Length > 0)
                    if (!(npc.Value.Map.ToString(CultureInfo.InvariantCulture).MatchesFilters(Settings.MapFilters)))
                        continue;

                var equipment = npc.Value.UnitData.VirtualItems;
                if (equipment.Length != 3)
                    continue;

                if (equipment[0].ItemID == 0 && equipment[1].ItemID == 0 && equipment[2].ItemID == 0)
                    continue;

                var equip = new CreatureEquipment
                {
                    CreatureID = npc.Key.GetEntry(),
                    ItemID1 = (uint)equipment[0].ItemID,
                    ItemID2 = (uint)equipment[1].ItemID,
                    ItemID3 = (uint)equipment[2].ItemID,

                    AppearanceModID1 = equipment[0].ItemAppearanceModID,
                    AppearanceModID2 = equipment[1].ItemAppearanceModID,
                    AppearanceModID3 = equipment[2].ItemAppearanceModID,

                    ItemVisual1 = equipment[0].ItemVisual,
                    ItemVisual2 = equipment[1].ItemVisual,
                    ItemVisual3 = equipment[2].ItemVisual
                };


                if (equips.Contains(equip))
                    continue;

                for (uint i = 1;; i++)
                {
                    equip.ID = i;
                    if (!equips.ContainsKey(equip))
                        break;
                }

                equips.Add(equip);
            }

            var equipsDb = SQLDatabase.Get(equips);
            return SQLUtil.Compare(equips, equipsDb, StoreNameType.Unit);
        }

        [BuilderMethod]
        public static string PointsOfInterest()
        {
            if (Storage.GossipPOIs.IsEmpty())
                return string.Empty;

            if (!Settings.SQLOutputFlag.HasAnyFlagBit(SQLOutput.points_of_interest))
                return string.Empty;

            if (Settings.DBEnabled)
                return SQLUtil.Compare(Storage.GossipPOIs, SQLDatabase.Get(Storage.GossipPOIs), StoreNameType.None);
            else
            {
                uint count = 0;
                var rows = new RowList<PointsOfInterest>();

                foreach (var pointOfInterest in Storage.GossipPOIs)
                {
                    Row<PointsOfInterest> row = new Row<PointsOfInterest>();

                    Type t = pointOfInterest.Item1.ID.GetType();
                    if (t.Equals(typeof(int)))
                        row.Data.ID = pointOfInterest.Item1.ID;
                    else
                        row.Data.ID = "@PID+" + count;

                    row.Data.PositionX = pointOfInterest.Item1.PositionX;
                    row.Data.PositionY = pointOfInterest.Item1.PositionY;
                    row.Data.PositionZ = pointOfInterest.Item1.PositionZ;
                    row.Data.Icon = pointOfInterest.Item1.Icon;
                    row.Data.Flags = pointOfInterest.Item1.Flags;
                    row.Data.Importance = pointOfInterest.Item1.Importance;
                    row.Data.Name = pointOfInterest.Item1.Name;
                    row.Data.VerifiedBuild = pointOfInterest.Item1.VerifiedBuild;

                    ++count;

                    rows.Add(row);
                }

                StringBuilder result = new StringBuilder();
                // delete query for GUIDs
                var delete = new SQLDelete<PointsOfInterest>(Tuple.Create("@PID+0", "@PID+" + --count));
                result.Append(delete.Build());

                var sql = new SQLInsert<PointsOfInterest>(rows, false);
                result.Append(sql.Build());

                return result.ToString();
            }
        }

        [BuilderMethod]
        public static string Gossip()
        {
            if (Storage.Gossips.IsEmpty() && Storage.GossipMenuOptions.IsEmpty())
                return string.Empty;

            var result = "";

            // `gossip_menu`
            if (Settings.SQLOutputFlag.HasAnyFlagBit(SQLOutput.gossip_menu))
                result += SQLUtil.Compare(Storage.Gossips, SQLDatabase.Get(Storage.Gossips),
                    t => StoreGetters.GetName(StoreNameType.Unit, (int)t.ObjectEntry)); // BUG: GOs can send gossips too

            // `gossip_menu_option`
            if (Settings.SQLOutputFlag.HasAnyFlagBit(SQLOutput.gossip_menu_option))
            {
                result += SQLUtil.Compare(Storage.GossipMenuOptions, SQLDatabase.Get(Storage.GossipMenuOptions), t => t.BroadcastTextIDHelper);
                result += SQLUtil.Compare(Storage.GossipMenuOptionActions, SQLDatabase.Get(Storage.GossipMenuOptionActions), StoreNameType.None);
                result += SQLUtil.Compare(Storage.GossipMenuOptionBoxes, SQLDatabase.Get(Storage.GossipMenuOptionBoxes), t => t.BroadcastTextIdHelper);
            }

            return result;
        }

        //                      entry, <minlevel, maxlevel>
        public static Dictionary<uint, ValueTuple<int, int>> GetLevels(Dictionary<WowGuid, Unit> units)
        {
            if (units.Count == 0)
                return null;

            var entries = units.GroupBy(unit => unit.Key.GetEntry());
            var list = new Dictionary<uint, List<int>>();

            foreach (var pair in entries.SelectMany(entry => entry))
            {
                if (list.ContainsKey(pair.Key.GetEntry()))
                    list[pair.Key.GetEntry()].Add(pair.Value.UnitData.Level);
                else
                    list.Add(pair.Key.GetEntry(), new List<int> { pair.Value.UnitData.Level });
            }

            var result = list.ToDictionary(pair => pair.Key, pair => ValueTuple.Create(pair.Value.Min(), pair.Value.Max()));

            return result.Count == 0 ? null : result;
        }

        public static readonly HashSet<string> ProfessionTrainers = new HashSet<string>
        {
            "Alchemy Trainer", "Armorsmith Trainer", "Armorsmithing Trainer", "Blacksmith Trainer",
            "Blacksmithing Trainer", "Blacksmithing Trainer & Supplies", "Cold Weather Flying Trainer",
            "Cooking Trainer", "Cooking Trainer & Supplies", "Dragonscale Leatherworking Trainer",
            "Elemental Leatherworking Trainer", "Enchanting Trainer", "Engineering Trainer",
            "First Aid Trainer", "Fishing Trainer", "Fishing Trainer & Supplies",
            "Gnome Engineering Trainer", "Gnomish Engineering Trainer", "Goblin Engineering Trainer",
            "Grand Master Alchemy Trainer", "Grand Master Blacksmithing Trainer",
            "Grand Master Cooking Trainer", "Grand Master Enchanting Trainer",
            "Grand Master Engineering Trainer", "Grand Master First Aid Trainer",
            "Grand Master Fishing Trainer", "Grand Master Fishing Trainer & Supplies",
            "Grand Master Herbalism Trainer", "Grand Master Inscription Trainer",
            "Grand Master Jewelcrafting Trainer", "Grand Master Leatherworking Trainer",
            "Grand Master Mining Trainer", "Grand Master Skinning Trainer",
            "Grand Master Tailoring Trainer", "Herbalism Trainer",
            "Herbalism Trainer & Supplies", "Inscription Trainer",
            "Jewelcrafting Trainer", "Leatherworking Trainer",
            "Master Alchemy Trainer", "Master Blacksmithing Trainer",
            "Master Enchanting Trainer", "Master Engineering Trainer",
            "Master Fishing Trainer", "Master Herbalism Trainer",
            "Master Inscription Trainer", "Master Jewelcrafting Trainer",
            "Master Leatherworking Trainer", "Master Mining Trainer",
            "Master Skinning Trainer", "Master Tailoring Trainer",
            "Mining Trainer", "Skinning Trainer", "Tailor Trainer", "Tailoring Trainer",
            "Tribal Leatherworking Trainer", "Weaponsmith Trainer", "Weaponsmithing Trainer",
            "Horse Riding Trainer", "Ram Riding Trainer", "Raptor Riding Trainer",
            "Tiger Riding Trainer", "Wolf Riding Trainer", "Mechastrider Riding Trainer",
            "Riding Trainer", "Undead Horse Riding Trainer"
        };

        public static readonly HashSet<string> ClassTrainers = new HashSet<string>
        {
            "Druid Trainer", "Portal Trainer", "Portal: Darnassus Trainer",
            "Portal: Ironforge Trainer", "Portal: Orgrimmar Trainer",
            "Portal: Stormwind Trainer", "Portal: Thunder Bluff Trainer",
            "Portal: Undercity Trainer", "Deathknight Trainer",
            "Hunter Trainer", "Mage Trainer", "Paladin Trainer",
            "Priest Trainer", "Shaman Trainer", "Warlock Trainer",
            "Warrior Trainer"
        };

        private static string GetSubName(int entry, bool withEntry)
        {
            string name = StoreGetters.GetName(StoreNameType.Unit, entry, withEntry);
            int firstIndex = name.LastIndexOf('<');
            int lastIndex = name.LastIndexOf('>');
            if (firstIndex != -1 && lastIndex != -1)
                return name.Substring(firstIndex + 1, lastIndex - firstIndex - 1);

            return "";
        }

        private static NPCFlags ProcessNpcFlags(string subName)
        {
            if (ProfessionTrainers.Contains(subName))
                return NPCFlags.ProfessionTrainer;
            if (ClassTrainers.Contains(subName))
                return NPCFlags.ClassTrainer;

            return 0;
        }

        [BuilderMethod(true, Units = true)]
        public static string CreatureTemplateNonWDB(Dictionary<WowGuid, Unit> units)
        {
            if (units.Count == 0)
                return string.Empty;

            if (!Settings.SQLOutputFlag.HasAnyFlagBit(SQLOutput.creature_template))
                return string.Empty;

            var levels = GetLevels(units);
            var usesCurrentExpansionLevels = new Dictionary<uint, long>();
            var expansionBaseLevel = 0;
            if (Settings.TargetedDatabase >= TargetedDatabase.WarlordsOfDraenor && Settings.DBEnabled)
            {
                usesCurrentExpansionLevels = SQLDatabase.GetDict<uint, long>($"SELECT entry, 1 FROM {Settings.TDBDatabase}.creature_template WHERE HealthScalingExpansion = -1");
                switch (Settings.TargetedDatabase)
                {
                    case TargetedDatabase.WarlordsOfDraenor:
                        expansionBaseLevel = 100;
                        break;
                    case TargetedDatabase.Legion:
                        expansionBaseLevel = 110;
                        break;
                    case TargetedDatabase.BattleForAzeroth:
                        expansionBaseLevel = 120;
                        break;
                    case TargetedDatabase.Shadowlands:
                        expansionBaseLevel = 60;
                        break;
                }
            }

            Func<uint, (int MinLevel, int MaxLevel)> getLevel = (uint id) =>
            {
                CreatureTemplate template;
                if ((Storage.CreatureTemplates.TryGetValue(id, out template) && template.HealthScalingExpansion == ClientType.Current)
                    || usesCurrentExpansionLevels.ContainsKey(id))
                    return (levels[id].Item1 - expansionBaseLevel, levels[id].Item2 - expansionBaseLevel);

                return levels[id];
            };

            foreach (var unit in units)
            {
                if (Storage.CreatureTemplatesNonWDB.Any(creature => creature.Item1.Entry == unit.Key.GetEntry()))
                    continue;

                var npc = unit.Value;
                var minMaxLevel = getLevel(unit.Key.GetEntry());

                var template = new CreatureTemplateNonWDB
                {
                    Entry = unit.Key.GetEntry(),
                    GossipMenuId = npc.GossipId,
                    MinLevel = minMaxLevel.MinLevel,
                    MaxLevel = minMaxLevel.MaxLevel,
                    Faction = (uint)npc.UnitData.FactionTemplate,
                    NpcFlag = (NPCFlags)Utilities.MAKE_PAIR64(npc.UnitData.NpcFlags[0], npc.UnitData.NpcFlags[1]),
                    SpeedRun = npc.Movement.RunSpeed,
                    SpeedWalk = npc.Movement.WalkSpeed,
                    BaseAttackTime = npc.UnitData.AttackRoundBaseTime[0],
                    RangedAttackTime = npc.UnitData.RangedAttackRoundBaseTime,
                    UnitClass = npc.UnitData.ClassId,
                    UnitFlags = (UnitFlags)npc.UnitData.Flags,
                    UnitFlags2 = (UnitFlags2)npc.UnitData.Flags2,
                    UnitFlags3 = (UnitFlags3)npc.UnitData.Flags3,
                    DynamicFlags = npc.DynamicFlags.GetValueOrDefault(UnitDynamicFlags.None),
                    DynamicFlagsWod = npc.DynamicFlagsWod.GetValueOrDefault(UnitDynamicFlagsWOD.None),
                    VehicleID = npc.Movement.VehicleId,
                    HoverHeight = npc.UnitData.HoverHeight
                };

                if (Settings.UseDBC)
                {
                    var creatureDiff = DBC.DBC.CreatureDifficulty.Where(diff => diff.Value.CreatureID == unit.Key.GetEntry());
                    if (creatureDiff.Any())
                    {
                        template.MinLevel = creatureDiff.Select(lv => lv.Value.MinLevel).First();
                        template.MaxLevel = creatureDiff.Select(lv => lv.Value.MaxLevel).First();
                        template.Faction  = creatureDiff.Select(lv => lv.Value.FactionTemplateID).First();
                    }
                }

                if (template.Faction == 1 || template.Faction == 2 || template.Faction == 3 ||
                    template.Faction == 4 || template.Faction == 5 || template.Faction == 6 ||
                    template.Faction == 115 || template.Faction == 116 || template.Faction == 1610 ||
                    template.Faction == 1629 || template.Faction == 2203 || template.Faction == 2204 ||
                    template.Faction == 2395 || template.Faction == 2401 || template.Faction == 2402) // player factions
                    template.Faction = 35;

                template.UnitFlags &= ~UnitFlags.IsInCombat;
                template.UnitFlags &= ~UnitFlags.PetIsAttackingTarget;
                template.UnitFlags &= ~UnitFlags.PlayerControlled;
                template.UnitFlags &= ~UnitFlags.Silenced;
                template.UnitFlags &= ~UnitFlags.PossessedByPlayer;

                if (!ClientVersion.AddedInVersion(ClientType.WarlordsOfDraenor))
                {
                    template.DynamicFlags &= ~UnitDynamicFlags.Lootable;
                    template.DynamicFlags &= ~UnitDynamicFlags.Tapped;
                    template.DynamicFlags &= ~UnitDynamicFlags.TappedByPlayer;
                    template.DynamicFlags &= ~UnitDynamicFlags.TappedByAllThreatList;
                }
                else
                {
                    template.DynamicFlagsWod &= ~UnitDynamicFlagsWOD.Lootable;
                    template.DynamicFlagsWod &= ~UnitDynamicFlagsWOD.Tapped;
                    template.DynamicFlagsWod &= ~UnitDynamicFlagsWOD.TappedByPlayer;
                    template.DynamicFlagsWod &= ~UnitDynamicFlagsWOD.TappedByAllThreatList;
                }

                // has trainer flag but doesn't have prof nor class trainer flag
                if ((template.NpcFlag & NPCFlags.Trainer) != 0 &&
                    ((template.NpcFlag & NPCFlags.ProfessionTrainer) == 0 ||
                     (template.NpcFlag & NPCFlags.ClassTrainer) == 0))
                {
                    var subname = GetSubName((int)unit.Key.GetEntry(), false); // Fall back
                    CreatureTemplate entry;
                    if (Storage.CreatureTemplates.TryGetValue(unit.Key.GetEntry(), out entry))
                    {
                        var sub = entry.SubName;
                        if (sub.Length > 0)
                        {
                            template.NpcFlag |= ProcessNpcFlags(sub);
                            Trace.WriteLine($"Entry: { unit.Key.GetEntry() } NpcFlag: { template.NpcFlag }");
                        }
                        else // If the SubName doesn't exist or is cached, fall back to DB method
                            template.NpcFlag |= ProcessNpcFlags(subname);
                    }
                    else // In case we have NonWDB data which doesn't have an entry in CreatureTemplates
                        template.NpcFlag |= ProcessNpcFlags(subname);
                }

                Storage.CreatureTemplatesNonWDB.Add(template);
            }

            var templatesDb = SQLDatabase.Get(Storage.CreatureTemplatesNonWDB);
            return SQLUtil.Compare(Storage.CreatureTemplatesNonWDB, templatesDb, StoreNameType.Unit);
        }

        static UnitMisc()
        {
            HotfixStoreMgr.OnRecordReceived += (hash, recordKey, added) =>
            {
                if (!added || hash != DB2Hash.BroadcastText)
                    return;

                var record = HotfixStoreMgr.GetStore(hash).GetRecord(recordKey) as IBroadcastTextEntry;
                if (record == null)
                    return;

                if (!SQLDatabase.BroadcastTexts.ContainsKey(record.Text))
                    SQLDatabase.BroadcastTexts[record.Text] = new List<int>();
                SQLDatabase.BroadcastTexts[record.Text].Add(recordKey);

                if (!SQLDatabase.BroadcastText1s.ContainsKey(record.Text1))
                    SQLDatabase.BroadcastText1s[record.Text1] = new List<int>();
                SQLDatabase.BroadcastText1s[record.Text1].Add(recordKey);
            };
        }

        [BuilderMethod]
        public static string CreatureText()
        {
            if (Storage.CreatureTexts.IsEmpty() || !Settings.SQLOutputFlag.HasAnyFlagBit(SQLOutput.creature_text))
                return string.Empty;

            // For each sound and emote, if the time they were send is in the +1/-1 seconds range of
            // our texts, add that sound and emote to our Storage.CreatureTexts

            foreach (var text in Storage.CreatureTexts)
            {
                // For each text
                foreach (var textValue in text.Value)
                {
                    // For each emote
                    var text1 = text;
                    var value1 = textValue;
                    foreach (var emoteValue in from emote in Storage.Emotes where emote.Key.GetEntry() == text1.Key from emoteValue in emote.Value let timeSpan = value1.Item2 - emoteValue.Item2 where timeSpan != null && timeSpan.Value.Duration() <= TimeSpan.FromSeconds(1) select emoteValue)
                        textValue.Item1.Emote = emoteValue.Item1;

                    // For each sound
                    var value = textValue;
                    foreach (var sound in from sound in Storage.Sounds let timeSpan = value.Item2 - sound.Item2 where timeSpan != null && timeSpan.Value.Duration() <= TimeSpan.FromSeconds(1) select sound)
                        textValue.Item1.Sound = sound.Item1;

                    List<int> textList;
                    if (SQLDatabase.BroadcastTexts.TryGetValue(textValue.Item1.Text, out textList) ||
                        SQLDatabase.BroadcastText1s.TryGetValue(textValue.Item1.Text, out textList))
                    {
                        if (textList.Count == 1)
                            textValue.Item1.BroadcastTextID = (uint)textList.First();
                        else
                        {
                            textValue.Item1.BroadcastTextID = "PLEASE_SET_A_BROADCASTTEXT_ID";
                            textValue.Item1.BroadcastTextIDHelper = "BroadcastTextID: ";
                            textValue.Item1.BroadcastTextIDHelper += string.Join(" - ", textList);
                        }

                    }

                    // Set comment
                    string from = null, to = null;
                    if (!textValue.Item1.SenderGUID.IsEmpty())
                    {
                        if (textValue.Item1.SenderGUID.GetObjectType() == ObjectType.Player)
                            from = "Player";
                        else
                            from = !string.IsNullOrEmpty(textValue.Item1.SenderName) ? textValue.Item1.SenderName : StoreGetters.GetName(StoreNameType.Unit, (int)textValue.Item1.SenderGUID.GetEntry(), false);
                    }

                    if (!textValue.Item1.ReceiverGUID.IsEmpty())
                    {
                        if (textValue.Item1.ReceiverGUID.GetObjectType() == ObjectType.Player)
                            to = "Player";
                        else
                            to = !string.IsNullOrEmpty(textValue.Item1.ReceiverName) ? textValue.Item1.ReceiverName : StoreGetters.GetName(StoreNameType.Unit, (int)textValue.Item1.ReceiverGUID.GetEntry(), false);
                    }

                    Trace.Assert(text.Key == textValue.Item1.SenderGUID.GetEntry() ||
                        text.Key == textValue.Item1.ReceiverGUID.GetEntry());

                    if (from != null && to != null)
                        textValue.Item1.Comment = from + " to " + to;
                    else if (from != null)
                        textValue.Item1.Comment = from;
                    else
                        Trace.Assert(false);
                }
            }

            /* can't use compare DB without knowing values of groupid or id
            var entries = Storage.CreatureTexts.Keys.ToList();
            var creatureTextDb = SQLDatabase.GetDict<uint, CreatureText>(entries);
            */

            var rows = new RowList<CreatureText>();
            Dictionary<uint, uint> entryCount = new Dictionary<uint, uint>();

            foreach (var text in Storage.CreatureTexts.OrderBy(t => t.Key))
            {
                foreach (var textValue in text.Value)
                {
                    var count = entryCount.ContainsKey(text.Key) ? entryCount[text.Key] : 0;

                    if (rows.Where(text2 => text2.Data.Entry == text.Key && text2.Data.Text == textValue.Item1.Text).Count() != 0)
                        continue;

                    var row = new Row<CreatureText>
                    {
                        Data = new CreatureText
                        {
                            Entry = text.Key,
                            GroupId = "@GROUP_ID+" + count,
                            ID = "@ID+",
                            Text = textValue.Item1.Text,
                            Type = textValue.Item1.Type,
                            Language = textValue.Item1.Language,
                            Probability = 100.0f,
                            Emote = (textValue.Item1.Emote != null ? textValue.Item1.Emote : 0),
                            Duration = 0,
                            Sound = (textValue.Item1.Sound != null ? textValue.Item1.Sound : 0),
                            BroadcastTextID = textValue.Item1.BroadcastTextID,
                            Comment = textValue.Item1.Comment
                        },

                        Comment = textValue.Item1.BroadcastTextIDHelper
                    };

                    if (!entryCount.ContainsKey(text.Key))
                        entryCount.Add(text.Key, count + 1);
                    else
                        entryCount[text.Key] = count + 1;

                    rows.Add(row);
                }
            }

            return new SQLInsert<CreatureText>(rows, false).Build();
        }

        [BuilderMethod]
        public static string VehicleAccessory()
        {
            if (Storage.VehicleTemplateAccessories.IsEmpty() || !Settings.SQLOutputFlag.HasAnyFlagBit(SQLOutput.vehicle_template_accessory))
                return string.Empty;

            var rows = new RowList<VehicleTemplateAccessory>();
            foreach (var accessory in Storage.VehicleTemplateAccessories)
            {
                if (accessory.Item1.SeatId < 0 || accessory.Item1.SeatId > 7)
                    continue;

                // ReSharper disable once UseObjectOrCollectionInitializer
                var row = new Row<VehicleTemplateAccessory>();
                row.Comment = StoreGetters.GetName(StoreNameType.Unit, (int)accessory.Item1.Entry.GetValueOrDefault(), false) + " - ";
                row.Comment += StoreGetters.GetName(StoreNameType.Unit, (int)accessory.Item1.AccessoryEntry.GetValueOrDefault(), false);
                accessory.Item1.Description = row.Comment;
                row.Data = accessory.Item1;

                rows.Add(row);
            }

            return new SQLInsert<VehicleTemplateAccessory>(rows, false).Build();
        }

        [BuilderMethod]
        public static string NpcSpellClick()
        {
            if (Storage.NpcSpellClicks.IsEmpty() || !Settings.SQLOutputFlag.HasAnyFlagBit(SQLOutput.npc_spellclick_spells))
                return string.Empty;

            var rows = new RowList<NpcSpellClick>();

            foreach (var npcSpellClick in Storage.NpcSpellClicks)
            {
                foreach (var spellClick in Storage.SpellClicks)
                {
                    var row = new Row<NpcSpellClick>();

                    if (spellClick.Item1.CasterGUID.GetObjectType() == ObjectType.Unit && spellClick.Item1.TargetGUID.GetObjectType() == ObjectType.Unit)
                        spellClick.Item1.CastFlags = 0x0;
                    if (spellClick.Item1.CasterGUID.GetObjectType() == ObjectType.Player && spellClick.Item1.TargetGUID.GetObjectType() == ObjectType.Unit)
                        spellClick.Item1.CastFlags = 0x1;
                    if (spellClick.Item1.CasterGUID.GetObjectType() == ObjectType.Unit && spellClick.Item1.TargetGUID.GetObjectType() == ObjectType.Player)
                        spellClick.Item1.CastFlags = 0x2;
                    if (spellClick.Item1.CasterGUID.GetObjectType() == ObjectType.Player && spellClick.Item1.TargetGUID.GetObjectType() == ObjectType.Player)
                        spellClick.Item1.CastFlags = 0x3;

                    spellClick.Item1.Entry = npcSpellClick.Item1.GetEntry();
                    row.Data = spellClick.Item1;

                    var timeSpan = spellClick.Item2 - npcSpellClick.Item2;
                    if (timeSpan != null && timeSpan.Value.Duration() <= TimeSpan.FromSeconds(1))
                        rows.Add(row);
                }
            }

            return new SQLInsert<NpcSpellClick>(rows, false).Build();
        }

        [BuilderMethod(Units = true)]
        public static string NpcSpellClickMop(Dictionary<WowGuid, Unit> units)
        {
            if (units.Count == 0 || !Settings.SQLOutputFlag.HasAnyFlagBit(SQLOutput.npc_spellclick_spells))
                return string.Empty;

            var rows = new RowList<NpcSpellClick>();

            foreach (var unit in units)
            {
                var row = new Row<NpcSpellClick>();

                var npc = unit.Value;
                if (npc.UnitData.InteractSpellID == 0)
                    continue;

                if (Settings.AreaFilters.Length > 0)
                    if (!npc.Area.ToString(CultureInfo.InvariantCulture).MatchesFilters(Settings.AreaFilters))
                        continue;

                if (Settings.MapFilters.Length > 0)
                    if (!npc.Map.ToString(CultureInfo.InvariantCulture).MatchesFilters(Settings.MapFilters))
                        continue;

                row.Data.Entry = unit.Key.GetEntry();
                row.Data.SpellID = (uint)npc.UnitData.InteractSpellID;

                rows.Add(row);
            }

            return new SQLInsert<NpcSpellClick>(rows, false).Build();
        }
    }
}
