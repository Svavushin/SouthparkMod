using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace SouthParkDeaths
{
    public sealed class SouthParkDeathsMod : Mod
    {
        public SouthParkDeathsMod(ModContentPack content) : base(content)
        {
            new Harmony("local.southpark.deaths").PatchAll();
        }
    }

    public sealed class SouthParkDeathsGameComponent : GameComponent
    {
        public SouthParkDeathsGameComponent(Game game)
        {
        }

        public override void GameComponentTick()
        {
            SouthParkDeathsState.Tick();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            SouthParkDeathsState.ExposeData();
        }

        public override void GameComponentOnGUI()
        {
            SouthParkDeathsState.DrawResurrectionPopup();
        }
    }

    public sealed class SouthParkDeathsMapComponent : MapComponent
    {
        public SouthParkDeathsMapComponent(Map map) : base(map)
        {
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class PawnKillPatch
    {
        public static void Postfix(Pawn __instance)
        {
            if (__instance == null || !__instance.Dead || !__instance.IsColonist)
            {
                return;
            }

            if (SouthParkDeathsState.IsMysterionPawn(__instance))
            {
                SouthParkDeathsState.NotifyMysterionKilled(__instance);
                return;
            }

            if (!SouthParkDeathsState.ShouldTriggerManInBlackCondition(__instance))
            {
                return;
            }

            SouthParkDeathsState.NotifyColonistKilled(__instance);
        }
    }

    [HarmonyPatch(typeof(BodyPartDef), nameof(BodyPartDef.GetMaxHealth))]
    public static class MysterionBodyPartMaxHealthPatch
    {
        public static void Postfix(Pawn pawn, ref float __result)
        {
            if (SouthParkDeathsState.IsMysterionPawn(pawn))
            {
                __result *= SouthParkDeathsState.MysterionHealthMultiplier;
            }
        }
    }

    [HarmonyPatch(typeof(CompTargetEffect_Resurrect), nameof(CompTargetEffect_Resurrect.DoEffectOn))]
    public static class ResurrectorSerumPatch
    {
        public static void Postfix(Pawn user, Thing target)
        {
            SouthParkDeathsState.NotifyResurrectorSerumUsed();
        }
    }

    [HarmonyPatch(typeof(IncidentWorker_WandererJoin), nameof(IncidentWorker_WandererJoin.SpawnJoiner))]
    public static class StrangerInBlackSpawnPatch
    {
        public static void Postfix(IncidentWorker_WandererJoin __instance, Map map, Pawn pawn)
        {
            if (__instance?.def?.defName != "StrangerInBlackJoin")
            {
                return;
            }

            SouthParkDeathsState.CustomizeMysterionJoiner(pawn, map);
        }
    }

    [HarmonyPatch(typeof(IncidentWorker_WandererJoin), nameof(IncidentWorker_WandererJoin.GeneratePawn))]
    public static class StrangerInBlackGeneratePawnPatch
    {
        public static void Postfix(IncidentWorker_WandererJoin __instance, Pawn __result)
        {
            if (__instance?.def?.defName != "StrangerInBlackJoin")
            {
                return;
            }

            SouthParkDeathsState.CustomizeMysterionJoiner(__result, null);
        }
    }

    public static class SouthParkDeathsState
    {
        private const int ResurrectionPopupTicks = 240;
        private const int MysterionMaxResurrections = 4;
        private const int MysterionResurrectionDelayTicks = 120;
        public const float MysterionHealthMultiplier = 2f;
        private const int MrHankeyGiftScheduleVersion = 2;
        private const int MrHankeyChristmasDayOfQuadrum = 10;
        private const int MrHankeyChristmasHourOfDay = 12;
        private const int MrHankeyGiftRetryTicks = 2500;
        private const int MrHankeySoundDurationTicks = 60000;
        private const int MrHankeySoundIntervalTicks = 10000;
        private const string MrHankeyGiftIncidentDefName = "SPD_MrHankeyGift";
        private const string MrHankeySoundDefName = "SPD_MrHankeyEvent";
        public const string MrHankeyVisitorDefName = "SPD_MrHankeyVisitor";
        public const string MrHankeyPawnKindDefName = "SPD_MrHankeyVisitorKind";
        private const string MysterionName = "Мистерио";
        private const string MysterionSuitDefName = "SPD_Apparel_MysterionSuit";
        private const string MysterionHoodDefName = "SPD_Apparel_MysterionHood";
        private const string MysterionGrenadesDefName = "Weapon_GrenadeFrag";
        private const string NimbleTraitDefName = "Nimble";
        private static int resurrectionPopupUntilTick = -1;
        private static int nextMrHankeyGiftTick = -1;
        private static int mrHankeyGiftScheduleVersion;
        private static int mrHankeySoundUntilTick = -1;
        private static int nextMrHankeySoundTick = -1;
        private static Texture2D jesusTexture;
        private static Dictionary<int, int> mysterionResurrectionsUsed = new Dictionary<int, int>();
        private static List<int> mysterionResurrectionKeys;
        private static List<int> mysterionResurrectionValues;
        private static readonly List<Pawn> pendingMysterionResurrections = new List<Pawn>();
        private static readonly HashSet<int> pendingMysterionResurrectionIds = new HashSet<int>();
        private static readonly Dictionary<int, int> pendingMysterionResurrectionDueTicks = new Dictionary<int, int>();

        public static void ExposeData()
        {
            Scribe_Collections.Look(
                ref mysterionResurrectionsUsed,
                "southParkDeathsMysterionResurrectionsUsed",
                LookMode.Value,
                LookMode.Value,
                ref mysterionResurrectionKeys,
                ref mysterionResurrectionValues);
            Scribe_Values.Look(ref nextMrHankeyGiftTick, "southParkDeathsNextMrHankeyGiftTick", -1);
            Scribe_Values.Look(ref mrHankeyGiftScheduleVersion, "southParkDeathsMrHankeyGiftScheduleVersion", 0);
            Scribe_Values.Look(ref mrHankeySoundUntilTick, "southParkDeathsMrHankeySoundUntilTick", -1);
            Scribe_Values.Look(ref nextMrHankeySoundTick, "southParkDeathsNextMrHankeySoundTick", -1);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (mysterionResurrectionsUsed == null)
                {
                    mysterionResurrectionsUsed = new Dictionary<int, int>();
                }

                if (mrHankeyGiftScheduleVersion < MrHankeyGiftScheduleVersion || nextMrHankeyGiftTick < 0)
                {
                    ScheduleNextMrHankeyGift();
                }

                mrHankeyGiftScheduleVersion = MrHankeyGiftScheduleVersion;
            }
        }

        public static bool ShouldTriggerManInBlackCondition(Pawn pawn)
        {
            Map map = pawn?.MapHeld;
            if (map == null || !map.IsPlayerHome)
            {
                return false;
            }

            IncidentDef strangerInBlack = DefDatabase<IncidentDef>.GetNamedSilentFail("StrangerInBlackJoin");
            if (strangerInBlack == null || strangerInBlack.Worker == null)
            {
                return false;
            }

            IncidentParms parms = new IncidentParms
            {
                target = map
            };

            return strangerInBlack.Worker.CanFireNow(parms);
        }

        public static bool IsMysterionPawn(Pawn pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            if (mysterionResurrectionsUsed.ContainsKey(pawn.thingIDNumber))
            {
                return true;
            }

            List<Apparel> wornApparel = pawn.apparel?.WornApparel;
            if (wornApparel != null)
            {
                for (int i = 0; i < wornApparel.Count; i++)
                {
                    string defName = wornApparel[i]?.def?.defName;
                    if (defName == MysterionSuitDefName || defName == MysterionHoodDefName)
                    {
                        return true;
                    }
                }
            }

            return pawn.Name?.ToStringShort == MysterionName;
        }

        public static bool IsMrHankeyPawn(Pawn pawn)
        {
            return pawn != null
                && (pawn is Pawn_MrHankeyVisitor
                    || pawn.def?.defName == MrHankeyVisitorDefName
                    || pawn.kindDef?.defName == MrHankeyPawnKindDefName);
        }

        public static void NotifyColonistKilled(Pawn pawn)
        {
            Corpse corpse = pawn.Corpse;
            if (corpse != null)
            {
                ReplaceCorpseApparelWithKennyOutfit(corpse);
            }

            SoundDef sound = DefDatabase<SoundDef>.GetNamedSilentFail("SPD_KennyDeath");
            Map map = pawn.MapHeld ?? corpse?.MapHeld ?? Find.CurrentMap;
            if (sound != null && map != null)
            {
                SoundStarter.PlayOneShotOnCamera(sound, map);
            }
        }

        public static void NotifyMysterionKilled(Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }

            int used = GetMysterionResurrectionsUsed(pawn);
            if (used >= MysterionMaxResurrections)
            {
                Messages.Message("SouthparkMod: Мистерио exhausted all resurrections.", pawn, MessageTypeDefOf.NegativeEvent, false);
                return;
            }

            mysterionResurrectionsUsed[pawn.thingIDNumber] = used + 1;
            QueueMysterionResurrection(pawn);
        }

        public static void NotifyResurrectorSerumUsed()
        {
            resurrectionPopupUntilTick = CurrentTick + ResurrectionPopupTicks;
        }

        public static void CustomizeMysterionJoiner(Pawn pawn, Map map)
        {
            if (pawn == null)
            {
                return;
            }

            pawn.Name = new NameSingle(MysterionName, false);
            EnsureMysterionTracked(pawn);

            if (pawn.Faction != Faction.OfPlayer)
            {
                pawn.SetFaction(Faction.OfPlayer);
            }

            if (pawn.playerSettings == null)
            {
                pawn.playerSettings = new Pawn_PlayerSettings(pawn);
            }

            if (pawn.story != null)
            {
                HeadTypeDef head = DefDatabase<HeadTypeDef>.GetNamedSilentFail("SPD_MysterionHead");
                if (head != null)
                {
                    pawn.story.headType = head;
                }

                HairDef shaved = DefDatabase<HairDef>.GetNamedSilentFail("Shaved");
                if (shaved != null)
                {
                    pawn.story.hairDef = shaved;
                }

                pawn.story.HairColor = Color.clear;
            }

            if (pawn.style != null)
            {
                pawn.style.beardDef = BeardDefOf.NoBeard;
            }

            pawn.Drawer?.renderer?.SetAllGraphicsDirty();

            pawn.playerSettings.hostilityResponse = HostilityResponseMode.Attack;
            pawn.playerSettings.selfTend = true;

            if (pawn.drafter != null)
            {
                pawn.drafter.FireAtWill = true;
            }

            SkillRecord melee = pawn.skills?.GetSkill(SkillDefOf.Melee);
            if (melee != null && !melee.TotallyDisabled)
            {
                melee.Level = 20;
                melee.passion = Passion.Major;
            }

            AddTraitIfAvailable(pawn, NimbleTraitDefName);
            ReplacePawnApparelWithOutfit(pawn, MysterionSuitDefName, MysterionHoodDefName);
            ReplacePawnEquipmentWithWeapon(pawn, MysterionGrenadesDefName);

            pawn.Drawer?.renderer?.SetAllGraphicsDirty();
        }

        public static void DebugPlayKennyDeathSound()
        {
            SoundDef sound = DefDatabase<SoundDef>.GetNamedSilentFail("SPD_KennyDeath");
            Map map = Find.CurrentMap;
            if (sound != null && map != null)
            {
                SoundStarter.PlayOneShotOnCamera(sound, map);
                return;
            }

            Messages.Message("SouthparkMod: Kenny death sound is not available.", MessageTypeDefOf.RejectInput, false);
        }

        public static void DebugReplaceCorpseApparel()
        {
            Map map = Find.CurrentMap;
            Corpse corpse = map?.listerThings?.ThingsInGroup(ThingRequestGroup.Corpse).OfType<Corpse>().FirstOrDefault();
            if (corpse == null)
            {
                Messages.Message("SouthparkMod: no corpse found on the current map.", MessageTypeDefOf.RejectInput, false);
                return;
            }

            ReplaceCorpseApparelWithKennyOutfit(corpse);
        }

        public static void DebugReplaceCorpseApparelWithMysterion()
        {
            Map map = Find.CurrentMap;
            Corpse corpse = map?.listerThings?.ThingsInGroup(ThingRequestGroup.Corpse).OfType<Corpse>().FirstOrDefault();
            if (corpse == null)
            {
                Messages.Message("SouthparkMod: no corpse found on the current map.", MessageTypeDefOf.RejectInput, false);
                return;
            }

            ReplaceCorpseApparelWithOutfit(corpse, "SPD_Apparel_MysterionSuit", "SPD_Apparel_MysterionHood");
        }

        public static void DebugTriggerMrHankeyGift()
        {
            if (!TryFireMrHankeyGiftIncident(true))
            {
                Messages.Message("SouthparkMod: Mr. Hankey gift incident could not fire.", MessageTypeDefOf.RejectInput, false);
            }
        }

        public static void Tick()
        {
            TickMysterionDownedRecoveryScheduler();
            ProcessPendingMysterionRecoveries();
            TickMrHankeyGiftScheduler();
            TickMrHankeySoundEvent();
        }

        private static void ReplaceCorpseApparelWithKennyOutfit(Corpse corpse)
        {
            ReplaceCorpseApparelWithOutfit(corpse, "SPD_Apparel_KennyParka", "SPD_Apparel_KennyHood");
        }

        private static void ReplaceCorpseApparelWithOutfit(Corpse corpse, string bodyDefName, string headDefName)
        {
            Pawn pawn = corpse?.InnerPawn;
            Map map = corpse?.MapHeld;
            if (pawn?.apparel == null || map == null)
            {
                return;
            }

            IntVec3 dropPosition = corpse.PositionHeld;
            List<Apparel> oldApparel = pawn.apparel.WornApparel.ToList();
            for (int i = 0; i < oldApparel.Count; i++)
            {
                Apparel apparel = oldApparel[i];
                pawn.apparel.Remove(apparel);
                GenPlace.TryPlaceThing(apparel, dropPosition, map, ThingPlaceMode.Near);
            }

            WearApparelIfAvailable(pawn, bodyDefName);
            WearApparelIfAvailable(pawn, headDefName);
        }

        private static void ReplacePawnApparelWithOutfit(Pawn pawn, string bodyDefName, string headDefName)
        {
            if (pawn?.apparel == null)
            {
                return;
            }

            bool hasBody = false;
            bool hasHead = false;
            List<Apparel> oldApparel = pawn.apparel.WornApparel.ToList();
            for (int i = 0; i < oldApparel.Count; i++)
            {
                Apparel apparel = oldApparel[i];
                string apparelDefName = apparel?.def?.defName;
                if (apparelDefName == bodyDefName)
                {
                    hasBody = true;
                    continue;
                }

                if (apparelDefName == headDefName)
                {
                    hasHead = true;
                    continue;
                }

                pawn.apparel.Remove(apparel);
                if (apparel != null && !apparel.Destroyed)
                {
                    apparel.Destroy(DestroyMode.Vanish);
                }
            }

            if (!hasBody)
            {
                WearApparelIfAvailable(pawn, bodyDefName);
            }

            if (!hasHead)
            {
                WearApparelIfAvailable(pawn, headDefName);
            }
        }

        private static void ReplacePawnEquipmentWithWeapon(Pawn pawn, string weaponDefName)
        {
            if (pawn?.equipment == null)
            {
                return;
            }

            List<ThingWithComps> oldEquipment = pawn.equipment.AllEquipmentListForReading.ToList();
            for (int i = 0; i < oldEquipment.Count; i++)
            {
                ThingWithComps equipment = oldEquipment[i];
                pawn.equipment.Remove(equipment);
                if (equipment != null && !equipment.Destroyed)
                {
                    equipment.Destroy(DestroyMode.Vanish);
                }
            }

            ThingDef weaponDef = DefDatabase<ThingDef>.GetNamedSilentFail(weaponDefName);
            if (weaponDef == null)
            {
                return;
            }

            ThingWithComps weapon = ThingMaker.MakeThing(weaponDef) as ThingWithComps;
            if (weapon != null)
            {
                pawn.equipment.AddEquipment(weapon);
            }
        }

        private static void AddTraitIfAvailable(Pawn pawn, string traitDefName)
        {
            if (pawn?.story?.traits == null)
            {
                return;
            }

            TraitDef traitDef = DefDatabase<TraitDef>.GetNamedSilentFail(traitDefName);
            if (traitDef == null || pawn.story.traits.HasTrait(traitDef))
            {
                return;
            }

            pawn.story.traits.GainTrait(new Trait(traitDef, 0, true), true);
        }

        private static int GetMysterionResurrectionsUsed(Pawn pawn)
        {
            if (pawn == null)
            {
                return MysterionMaxResurrections;
            }

            int used;
            if (mysterionResurrectionsUsed.TryGetValue(pawn.thingIDNumber, out used))
            {
                return used;
            }

            return 0;
        }

        private static void EnsureMysterionTracked(Pawn pawn)
        {
            if (pawn != null && !mysterionResurrectionsUsed.ContainsKey(pawn.thingIDNumber))
            {
                mysterionResurrectionsUsed[pawn.thingIDNumber] = 0;
            }
        }

        private static void QueueMysterionResurrection(Pawn pawn)
        {
            QueueMysterionRecovery(pawn);
        }

        private static void QueueMysterionRecovery(Pawn pawn)
        {
            if (pawn == null || !pendingMysterionResurrectionIds.Add(pawn.thingIDNumber))
            {
                return;
            }

            pendingMysterionResurrectionDueTicks[pawn.thingIDNumber] = CurrentTick + MysterionResurrectionDelayTicks;
            pendingMysterionResurrections.Add(pawn);
        }

        private static void TickMysterionDownedRecoveryScheduler()
        {
            List<Map> maps = Find.Maps;
            if (maps == null)
            {
                return;
            }

            for (int i = 0; i < maps.Count; i++)
            {
                IReadOnlyList<Pawn> pawns = maps[i]?.mapPawns?.AllPawnsSpawned;
                if (pawns == null)
                {
                    continue;
                }

                for (int j = 0; j < pawns.Count; j++)
                {
                    Pawn pawn = pawns[j];
                    if (pawn != null && pawn.Downed && !pawn.Dead && IsMysterionPawn(pawn))
                    {
                        QueueMysterionRecovery(pawn);
                    }
                }
            }
        }

        private static void ProcessPendingMysterionRecoveries()
        {
            if (pendingMysterionResurrections.Count == 0)
            {
                return;
            }

            for (int i = pendingMysterionResurrections.Count - 1; i >= 0; i--)
            {
                Pawn pawn = pendingMysterionResurrections[i];
                int pawnId = pawn?.thingIDNumber ?? -1;
                int dueTick;
                if (pendingMysterionResurrectionDueTicks.TryGetValue(pawnId, out dueTick) && dueTick > CurrentTick)
                {
                    continue;
                }

                pendingMysterionResurrections.RemoveAt(i);

                if (pawn != null)
                {
                    pendingMysterionResurrectionIds.Remove(pawnId);
                    pendingMysterionResurrectionDueTicks.Remove(pawnId);
                    RecoverMysterion(pawn);
                }
                else if (pawnId >= 0)
                {
                    pendingMysterionResurrectionIds.Remove(pawnId);
                    pendingMysterionResurrectionDueTicks.Remove(pawnId);
                }
            }
        }

        private static void TickMrHankeyGiftScheduler()
        {
            int now = CurrentTick;
            if (now <= 0)
            {
                return;
            }

            if (nextMrHankeyGiftTick < 0)
            {
                ScheduleNextMrHankeyGift();
                return;
            }

            if (now < nextMrHankeyGiftTick)
            {
                return;
            }

            if (TryFireMrHankeyGiftIncident(false))
            {
                ScheduleNextMrHankeyGift();
            }
            else
            {
                nextMrHankeyGiftTick = now + MrHankeyGiftRetryTicks;
            }
        }

        private static void ScheduleNextMrHankeyGift()
        {
            nextMrHankeyGiftTick = NextMrHankeyChristmasTickAfter(CurrentTick);
            mrHankeyGiftScheduleVersion = MrHankeyGiftScheduleVersion;
        }

        private static int NextMrHankeyChristmasTickAfter(int gameTick)
        {
            int absTick = GenDate.TickGameToAbs(Mathf.Max(0, gameTick));
            int yearOffset = PositiveMod(absTick, GenDate.TicksPerYear);
            int yearStartTick = absTick - yearOffset;
            int targetTick = yearStartTick + MrHankeyChristmasDayOfYear * GenDate.TicksPerDay + MrHankeyChristmasHourOfDay * GenDate.TicksPerHour;

            if (targetTick <= absTick)
            {
                targetTick += GenDate.TicksPerYear;
            }

            int targetGameTick = GenDate.TickAbsToGame(targetTick);
            return targetGameTick > gameTick ? targetGameTick : gameTick + GenDate.TicksPerYear;
        }

        private static int MrHankeyChristmasDayOfYear
        {
            get
            {
                return (int)Quadrum.Decembary * GenDate.DaysPerQuadrum + (MrHankeyChristmasDayOfQuadrum - 1);
            }
        }

        private static int PositiveMod(int value, int divisor)
        {
            int result = value % divisor;
            return result < 0 ? result + divisor : result;
        }

        public static void StartMrHankeySoundEvent()
        {
            int now = CurrentTick;
            mrHankeySoundUntilTick = now + MrHankeySoundDurationTicks;
            nextMrHankeySoundTick = now;
        }

        private static void TickMrHankeySoundEvent()
        {
            int now = CurrentTick;
            if (mrHankeySoundUntilTick <= now)
            {
                mrHankeySoundUntilTick = -1;
                nextMrHankeySoundTick = -1;
                return;
            }

            if (nextMrHankeySoundTick < 0 || now < nextMrHankeySoundTick)
            {
                return;
            }

            PlayMrHankeySound();
            nextMrHankeySoundTick += MrHankeySoundIntervalTicks;
            if (nextMrHankeySoundTick <= now)
            {
                nextMrHankeySoundTick = now + MrHankeySoundIntervalTicks;
            }
        }

        private static void PlayMrHankeySound()
        {
            SoundDef sound = DefDatabase<SoundDef>.GetNamedSilentFail(MrHankeySoundDefName);
            Map map = Find.CurrentMap ?? FindMrHankeyTargetMap();
            if (sound != null && map != null)
            {
                SoundStarter.PlayOneShotOnCamera(sound, map);
            }
        }

        private static bool TryFireMrHankeyGiftIncident(bool forced)
        {
            Map map = FindMrHankeyTargetMap();
            if (map == null)
            {
                return false;
            }

            IncidentDef incidentDef = DefDatabase<IncidentDef>.GetNamedSilentFail(MrHankeyGiftIncidentDefName);
            if (incidentDef?.Worker == null)
            {
                return false;
            }

            IncidentParms parms = new IncidentParms
            {
                target = map,
                forced = forced,
                sendLetter = true,
                points = StorytellerUtility.DefaultThreatPointsNow(map)
            };

            if (!forced && !incidentDef.Worker.CanFireNow(parms))
            {
                return false;
            }

            return incidentDef.Worker.TryExecute(parms);
        }

        private static Map FindMrHankeyTargetMap()
        {
            return Find.Maps
                .Where(map => map != null && map.IsPlayerHome && map.mapPawns.FreeColonistsSpawnedCount > 0)
                .OrderByDescending(map => map.mapPawns.FreeColonistsSpawnedCount)
                .FirstOrDefault();
        }

        private static void RecoverMysterion(Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }

            int used = GetMysterionResurrectionsUsed(pawn);
            bool wasDead = pawn.Dead;
            if (wasDead && !ResurrectionUtility.TryResurrect(pawn))
            {
                return;
            }

            HealMysterionCompletely(pawn);
            CustomizeMysterionJoiner(pawn, pawn.MapHeld);

            if (pawn.Dead)
            {
                return;
            }

            Messages.Message(
                wasDead
                    ? $"SouthparkMod: Мистерио resurrected ({used}/{MysterionMaxResurrections})."
                    : "SouthparkMod: Мистерио recovered from being downed.",
                pawn,
                MessageTypeDefOf.PositiveEvent,
                false);
        }

        private static void HealMysterionCompletely(Pawn pawn)
        {
            if (pawn?.health == null)
            {
                return;
            }

            pawn.health.forceDowned = false;

            List<Hediff> hediffs = pawn.health.hediffSet?.hediffs?.ToList();
            if (hediffs != null)
            {
                for (int i = 0; i < hediffs.Count; i++)
                {
                    Hediff hediff = hediffs[i];
                    if (hediff == null || hediff.def == null || !pawn.health.hediffSet.hediffs.Contains(hediff))
                    {
                        continue;
                    }

                    Hediff_MissingPart missingPart = hediff as Hediff_MissingPart;
                    if (missingPart != null && missingPart.Part != null)
                    {
                        pawn.health.RestorePart(missingPart.Part);
                        continue;
                    }

                    if (ShouldRemoveMysterionNegativeHediff(hediff))
                    {
                        pawn.health.RemoveHediff(hediff);
                    }
                }
            }

            pawn.mindState?.mentalStateHandler?.Reset();
            pawn.jobs?.CheckForJobOverride();
            pawn.health.CheckForStateChange(null, null);
        }

        private static bool ShouldRemoveMysterionNegativeHediff(Hediff hediff)
        {
            if (hediff == null || hediff.def == null)
            {
                return false;
            }

            return hediff is Hediff_Injury
                || hediff.def.isBad
                || hediff.def.everCurableByItem
                || hediff.def.forceRemoveOnResurrection;
        }

        private static void WearApparelIfAvailable(Pawn pawn, string defName)
        {
            ThingDef apparelDef = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (apparelDef == null)
            {
                return;
            }

            Apparel apparel = ThingMaker.MakeThing(apparelDef) as Apparel;
            if (apparel != null)
            {
                pawn.apparel.Wear(apparel, false);
            }
        }

        public static void DrawResurrectionPopup()
        {
            int now = CurrentTick;
            if (resurrectionPopupUntilTick <= now)
            {
                return;
            }

            Texture2D texture = EnsureJesusTexture();
            if (texture == null)
            {
                return;
            }

            float remaining = Mathf.Clamp01((resurrectionPopupUntilTick - now) / (float)ResurrectionPopupTicks);
            float alpha = remaining < 0.25f ? remaining / 0.25f : 1f;
            float size = Mathf.Min(UI.screenWidth, UI.screenHeight) * 0.42f;
            Rect rect = new Rect((UI.screenWidth - size) / 2f, (UI.screenHeight - size) / 2f, size, size);

            Color oldColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, true);
            GUI.color = oldColor;
        }

        private static Texture2D EnsureJesusTexture()
        {
            if (jesusTexture == null)
            {
                jesusTexture = ContentFinder<Texture2D>.Get("SouthPark/JesusResurrection", false);
            }

            return jesusTexture;
        }

        private static int CurrentTick
        {
            get
            {
                return Find.TickManager?.TicksGame ?? 0;
            }
        }
    }

    public sealed class IncidentWorker_MrHankeyGift : IncidentWorker
    {
        private const int ColonyVisitRadius = 12;

        private sealed class ItemGiftOption
        {
            public readonly string DefName;
            public readonly int Count;
            public readonly int Weight;
            public readonly string Label;

            public ItemGiftOption(string defName, int count, int weight, string label)
            {
                DefName = defName;
                Count = count;
                Weight = weight;
                Label = label;
            }
        }

        private static readonly ItemGiftOption[] ItemGiftOptions =
        {
            new ItemGiftOption("MedicineIndustrial", 3, 12, "3 аптечки"),
            new ItemGiftOption("MealSimple", 8, 12, "8 простых блюд"),
            new ItemGiftOption("Pemmican", 50, 10, "50 пеммикана"),
            new ItemGiftOption("PackagedSurvivalMeal", 6, 8, "6 сухпайков"),
            new ItemGiftOption("Chocolate", 20, 7, "20 шоколада"),
            new ItemGiftOption("Gun_Revolver", 1, 6, "револьвер"),
            new ItemGiftOption("Gun_Autopistol", 1, 5, "автопистолет"),
            new ItemGiftOption("ComponentIndustrial", 3, 8, "3 компонента"),
            new ItemGiftOption("Steel", 75, 8, "75 стали"),
            new ItemGiftOption("Cloth", 75, 6, "75 ткани")
        };

        private static readonly string[] AnimalGiftOptions =
        {
            "Chicken",
            "Cat",
            "YorkshireTerrier",
            "Pig"
        };

        protected override bool CanFireNowSub(IncidentParms parms)
        {
            Map map = parms.target as Map;
            return map != null && map.IsPlayerHome && map.mapPawns.FreeColonistsSpawnedCount > 0;
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = parms.target as Map;
            if (map == null)
            {
                return false;
            }

            return TrySpawnVisitor(map, CreateRandomPayload());
        }

        public static MrHankeyGiftPayload CreateRandomPayload()
        {
            return Rand.RangeInclusive(1, 5) == 1
                ? MakeAnimalPayload()
                : MakeItemPayload();
        }

        private static bool TrySpawnVisitor(Map map, MrHankeyGiftPayload payload)
        {
            PawnKindDef visitorKindDef = DefDatabase<PawnKindDef>.GetNamedSilentFail(SouthParkDeathsState.MrHankeyPawnKindDefName);
            if (visitorKindDef == null)
            {
                return false;
            }

            IntVec3 destination = FindColonyVisitCell(map);
            IntVec3 start;
            if (!CellFinder.TryFindRandomEdgeCellWith(
                    cell => cell.Standable(map) && !cell.Fogged(map),
                    map,
                    CellFinder.EdgeRoadChance_Friendly,
                    out start))
            {
                start = DropCellFinder.RandomDropSpot(map);
            }

            if (!start.IsValid || !destination.IsValid)
            {
                return false;
            }

            PawnGenerationRequest request = new PawnGenerationRequest(
                visitorKindDef,
                null,
                PawnGenerationContext.NonPlayer,
                map.Tile,
                forceGenerateNewPawn: true,
                canGeneratePawnRelations: false,
                allowFood: false,
                allowAddictions: false);

            Pawn_MrHankeyVisitor visitor = PawnGenerator.GeneratePawn(request) as Pawn_MrHankeyVisitor;
            if (visitor == null)
            {
                return false;
            }

            visitor.Initialize(destination, start, payload);
            visitor.Name = new NameSingle("Мистер Какашка", false);
            GenSpawn.Spawn(visitor, start, map, Rot4.South);
            SouthParkDeathsState.StartMrHankeySoundEvent();
            Messages.Message("Мистер Какашка идет к колонии с подарком.", visitor, MessageTypeDefOf.PositiveEvent, false);
            return true;
        }

        private static IntVec3 FindColonyVisitCell(Map map)
        {
            List<Pawn> colonists = map?.mapPawns?.FreeColonistsSpawned;
            if (colonists != null && colonists.Count > 0)
            {
                List<Pawn> candidates = colonists
                    .Where(pawn => pawn != null && pawn.Spawned)
                    .InRandomOrder()
                    .ToList();

                for (int i = 0; i < candidates.Count; i++)
                {
                    IntVec3 found = CellFinder.RandomClosewalkCellNear(
                        candidates[i].Position,
                        map,
                        ColonyVisitRadius,
                        cell => IsVisitCell(map, cell) && IsHomeAreaCell(map, cell));

                    if (found.IsValid)
                    {
                        return found;
                    }
                }

                for (int i = 0; i < candidates.Count; i++)
                {
                    IntVec3 found = CellFinder.RandomClosewalkCellNear(
                        candidates[i].Position,
                        map,
                        ColonyVisitRadius,
                        cell => IsVisitCell(map, cell));

                    if (found.IsValid)
                    {
                        return found;
                    }
                }
            }

            IntVec3 tradeSpot = DropCellFinder.TradeDropSpot(map);
            return tradeSpot.IsValid ? tradeSpot : DropCellFinder.RandomDropSpot(map);
        }

        private static bool IsVisitCell(Map map, IntVec3 cell)
        {
            if (!cell.InBounds(map) || cell.Fogged(map) || !cell.Standable(map))
            {
                return false;
            }

            Building_Door door = cell.GetDoor(map);
            return door == null || door.FreePassage;
        }

        private static bool IsHomeAreaCell(Map map, IntVec3 cell)
        {
            return map?.areaManager?.Home != null && map.areaManager.Home[cell];
        }

        private static MrHankeyGiftPayload MakeItemPayload()
        {
            ItemGiftOption option = RandomItemGiftOption();
            return MrHankeyGiftPayload.ForItem(option.DefName, option.Count, option.Label);
        }

        private static MrHankeyGiftPayload MakeAnimalPayload()
        {
            PawnKindDef pawnKindDef = RandomAnimalGiftKindDef();
            if (pawnKindDef == null)
            {
                return MakeItemPayload();
            }

            return MrHankeyGiftPayload.ForAnimal(pawnKindDef.defName, pawnKindDef.LabelCap);
        }

        private static ItemGiftOption RandomItemGiftOption()
        {
            int totalWeight = 0;
            for (int i = 0; i < ItemGiftOptions.Length; i++)
            {
                totalWeight += ItemGiftOptions[i].Weight;
            }

            int roll = Rand.Range(0, totalWeight);
            for (int i = 0; i < ItemGiftOptions.Length; i++)
            {
                if (roll < ItemGiftOptions[i].Weight)
                {
                    return ItemGiftOptions[i];
                }

                roll -= ItemGiftOptions[i].Weight;
            }

            return ItemGiftOptions[0];
        }

        private static PawnKindDef RandomAnimalGiftKindDef()
        {
            List<PawnKindDef> candidates = new List<PawnKindDef>();
            for (int i = 0; i < AnimalGiftOptions.Length; i++)
            {
                PawnKindDef pawnKindDef = DefDatabase<PawnKindDef>.GetNamedSilentFail(AnimalGiftOptions[i]);
                if (pawnKindDef != null)
                {
                    candidates.Add(pawnKindDef);
                }
            }

            if (candidates.Count == 0)
            {
                return null;
            }

            return candidates.RandomElement();
        }

        public static void SendGiftLetter(LookTargets lookTargets, string giftLabel)
        {
            TaggedString label = "Мистер Какашка";
            TaggedString text = $"Мистер Какашка заглянул в колонию и оставил: {giftLabel}.\n\nHowdy ho!";
            Find.LetterStack.ReceiveLetter(label, text, LetterDefOf.PositiveEvent, lookTargets);
        }
    }

    public struct MrHankeyGiftPayload
    {
        public bool IsAnimal;
        public string DefName;
        public int Count;
        public string Label;

        public static MrHankeyGiftPayload ForItem(string defName, int count, string label)
        {
            return new MrHankeyGiftPayload
            {
                IsAnimal = false,
                DefName = defName,
                Count = count,
                Label = label
            };
        }

        public static MrHankeyGiftPayload ForAnimal(string defName, string label)
        {
            return new MrHankeyGiftPayload
            {
                IsAnimal = true,
                DefName = defName,
                Count = 1,
                Label = label
            };
        }
    }

    public sealed class Graphic_MrHankey : Graphic_Multi
    {
        public override Material NodeGetMat(PawnDrawParms parms)
        {
            Pawn_MrHankeyVisitor visitor = parms.pawn as Pawn_MrHankeyVisitor;
            if (visitor != null && parms.facing.IsHorizontal)
            {
                int direction = visitor.VisualHorizontalDirection;
                if (direction < 0)
                {
                    return MatEast;
                }

                if (direction > 0)
                {
                    return MatWest;
                }
            }

            return base.NodeGetMat(parms);
        }
    }

    public sealed class Pawn_MrHankeyVisitor : Pawn
    {
        private const int ArrivalDistanceSquared = 4;
        private const int WanderAfterGiftTicks = 30000;
        private const int WanderArrivalDistanceSquared = 2;
        private const int WanderRadius = 12;
        private const int MinGiftDropRounds = 2;
        private const int MaxGiftDropRounds = 5;
        private const int MinGiftsPerRound = 1;
        private const int MaxGiftsPerRound = 3;
        private const int MinGiftDropDelayTicks = 2500;
        private const int MaxGiftDropDelayTicks = 5000;
        private int giftDropRoundsLeft;
        private int nextGiftDropTick = -1;
        private bool startedGiftDrops;

        private IntVec3 destination = IntVec3.Invalid;
        private IntVec3 exitCell = IntVec3.Invalid;
        private bool giftGiven;
        private bool wandering;
        private bool leaving;
        private IntVec3 wanderTarget = IntVec3.Invalid;
        private IntVec3 currentJobTarget = IntVec3.Invalid;
        private int wanderUntilTick = -1;
        private bool giftIsAnimal;
        private string giftDefName;
        private int giftCount;
        private string giftLabel;
        private int visualHorizontalDirection;

        public int VisualHorizontalDirection => visualHorizontalDirection;

        public void Initialize(IntVec3 destinationCell, IntVec3 exitMapCell, MrHankeyGiftPayload payload)
        {
            destination = destinationCell;
            exitCell = exitMapCell;
            giftIsAnimal = payload.IsAnimal;
            giftDefName = payload.DefName;
            giftCount = payload.Count;
            giftLabel = payload.Label;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref destination, "destination", IntVec3.Invalid);
            Scribe_Values.Look(ref exitCell, "exitCell", IntVec3.Invalid);
            Scribe_Values.Look(ref giftGiven, "giftGiven", defaultValue: false);
            Scribe_Values.Look(ref wandering, "wandering", defaultValue: false);
            Scribe_Values.Look(ref leaving, "leaving", defaultValue: false);
            Scribe_Values.Look(ref wanderTarget, "wanderTarget", IntVec3.Invalid);
            Scribe_Values.Look(ref currentJobTarget, "currentJobTarget", IntVec3.Invalid);
            Scribe_Values.Look(ref wanderUntilTick, "wanderUntilTick", -1);
            Scribe_Values.Look(ref giftIsAnimal, "giftIsAnimal", defaultValue: false);
            Scribe_Values.Look(ref giftDefName, "giftDefName");
            Scribe_Values.Look(ref giftCount, "giftCount", 1);
            Scribe_Values.Look(ref giftLabel, "giftLabel");
            Scribe_Values.Look(ref visualHorizontalDirection, "visualHorizontalDirection", 0);
            Scribe_Values.Look(ref giftDropRoundsLeft, "giftDropRoundsLeft", 0);
            Scribe_Values.Look(ref nextGiftDropTick, "nextGiftDropTick", -1);
            Scribe_Values.Look(ref startedGiftDrops, "startedGiftDrops", defaultValue: false);
        }

        public override void PreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
        {
            absorbed = true;
        }

        public override void Kill(DamageInfo? dinfo, Hediff exactCulprit = null)
        {
        }

        protected override void Tick()
        {
            base.Tick();

            if (!Spawned || Dead)
            {
                return;
            }

            KeepNonCombatPawnState();

            if (!destination.IsValid || !exitCell.IsValid)
            {
                Destroy(DestroyMode.Vanish);
                return;
            }

            if (!giftGiven && IsNear(destination))
            {
                ClearCurrentJobTarget();
                StartGiftDropSequence();
                return;
            }

            if (startedGiftDrops && giftDropRoundsLeft > 0)
            {
                ClearCurrentJobTarget();
                TickGiftDropSequence();
                return;
            }

            if (wandering)
            {
                TickWandering();
                return;
            }

            if (leaving && IsNear(exitCell))
            {
                Destroy(DestroyMode.Vanish);
                return;
            }

            IntVec3 activeTarget = leaving ? exitCell : destination;
            UpdateVisualHorizontalDirection(activeTarget);
            EnsureGotoJob(activeTarget);
        }

        private void KeepNonCombatPawnState()
        {
            if (mindState != null)
            {
                mindState.enemyTarget = null;
                mindState.meleeThreat = null;
                mindState.mentalStateHandler?.Reset();
            }

            if (skills != null)
            {
                SetSkillToZero(SkillDefOf.Melee);
                SetSkillToZero(SkillDefOf.Shooting);
            }
        }

        private void SetSkillToZero(SkillDef skillDef)
        {
            SkillRecord skill = skills.GetSkill(skillDef);
            if (skill != null && !skill.TotallyDisabled)
            {
                skill.Level = 0;
                skill.passion = Passion.None;
            }
        }

        private bool IsNear(IntVec3 cell)
        {
            return cell.IsValid && (Position - cell).LengthHorizontalSquared <= ArrivalDistanceSquared;
        }

        private void StartGiftDropSequence()
        {
            if (startedGiftDrops)
            {
                return;
            }

            giftGiven = true;
            leaving = false;
            wandering = false;
            startedGiftDrops = true;
            giftDropRoundsLeft = Rand.RangeInclusive(MinGiftDropRounds, MaxGiftDropRounds);
            nextGiftDropTick = Find.TickManager.TicksGame;
        }

        private void TickGiftDropSequence()
        {
            if (!startedGiftDrops || giftDropRoundsLeft <= 0)
            {
                return;
            }

            int now = Find.TickManager.TicksGame;
            if (nextGiftDropTick > now)
            {
                return;
            }

            GiveGiftRound();
            giftDropRoundsLeft--;

            if (giftDropRoundsLeft <= 0)
            {
                nextGiftDropTick = -1;
                StartWandering();
                return;
            }

            nextGiftDropTick = now + Rand.RangeInclusive(MinGiftDropDelayTicks, MaxGiftDropDelayTicks);
        }

        private void GiveGiftRound()
        {
            int giftsToGive = Rand.RangeInclusive(MinGiftsPerRound, MaxGiftsPerRound);
            List<string> giftLabels = new List<string>();
            LookTargets letterTargets = this;

            for (int i = 0; i < giftsToGive; i++)
            {
                MrHankeyGiftPayload payload = IncidentWorker_MrHankeyGift.CreateRandomPayload();

                LookTargets giftTarget;
                string givenLabel;
                if (TryGiveSingleGift(payload, out giftTarget, out givenLabel))
                {
                    if (giftLabels.Count == 0)
                    {
                        letterTargets = giftTarget;
                    }

                    giftLabels.Add(givenLabel);
                }
            }

            if (giftLabels.Count == 0)
            {
                Thing fallbackGift = MakeGiftItem("MedicineIndustrial", 3);
                GenPlace.TryPlaceThing(fallbackGift, Position, Map, ThingPlaceMode.Near);
                letterTargets = fallbackGift;
                giftLabels.Add("3 аптечки");
            }

            IncidentWorker_MrHankeyGift.SendGiftLetter(letterTargets, string.Join(", ", giftLabels.ToArray()));
        }

        private MrHankeyGiftPayload CurrentPayload()
        {
            if (string.IsNullOrEmpty(giftDefName))
            {
                return MrHankeyGiftPayload.ForItem("MedicineIndustrial", 3, "3 аптечки");
            }

            return giftIsAnimal
                ? MrHankeyGiftPayload.ForAnimal(giftDefName, giftLabel ?? "животное")
                : MrHankeyGiftPayload.ForItem(giftDefName, Mathf.Max(1, giftCount), giftLabel ?? "подарок");
        }

        private bool TryGiveSingleGift(MrHankeyGiftPayload payload, out LookTargets lookTargets, out string label)
        {
            if (payload.IsAnimal)
            {
                Pawn animal = MakeGiftAnimal(payload);
                if (animal != null)
                {
                    IntVec3 cell = CellFinder.RandomSpawnCellForPawnNear(Position, Map);
                    GenSpawn.Spawn(animal, cell, Map);
                    animal.SetFaction(Faction.OfPlayer);
                    lookTargets = animal;
                    label = payload.Label ?? "животное";
                    return true;
                }

                payload = MrHankeyGiftPayload.ForItem("MedicineIndustrial", 3, "3 аптечки");
            }

            Thing gift = MakeGiftItem(payload.DefName, payload.Count);
            GenPlace.TryPlaceThing(gift, Position, Map, ThingPlaceMode.Near);
            lookTargets = gift;
            label = payload.Label ?? "подарок";
            return true;
        }

        private Pawn MakeGiftAnimal(MrHankeyGiftPayload payload)
        {
            PawnKindDef pawnKindDef = DefDatabase<PawnKindDef>.GetNamedSilentFail(payload.DefName);
            if (pawnKindDef == null)
            {
                return null;
            }

            PawnGenerationRequest request = new PawnGenerationRequest(
                pawnKindDef,
                Faction.OfPlayer,
                PawnGenerationContext.NonPlayer,
                Map.Tile,
                forceGenerateNewPawn: true,
                canGeneratePawnRelations: false,
                allowFood: false,
                allowAddictions: false);

            return PawnGenerator.GeneratePawn(request);
        }

        private void StartWandering()
        {
            wandering = true;
            leaving = false;
            wanderUntilTick = Find.TickManager.TicksGame + WanderAfterGiftTicks;
            PickNewWanderTarget();
        }

        private void TickWandering()
        {
            int now = Find.TickManager.TicksGame;
            if (now >= wanderUntilTick)
            {
                wandering = false;
                leaving = true;
                wanderTarget = IntVec3.Invalid;
                ClearCurrentJobTarget();
                EnsureGotoJob(exitCell);
                return;
            }

            if (!wanderTarget.IsValid || IsNearWanderTarget(wanderTarget))
            {
                PickNewWanderTarget();
            }

            IntVec3 activeTarget = wanderTarget.IsValid ? wanderTarget : destination;
            UpdateVisualHorizontalDirection(activeTarget);
            EnsureGotoJob(activeTarget);
        }

        private bool IsNearWanderTarget(IntVec3 cell)
        {
            return cell.IsValid && (Position - cell).LengthHorizontalSquared <= WanderArrivalDistanceSquared;
        }

        private void PickNewWanderTarget()
        {
            IntVec3 anchor = RandomColonistCellNearVisitor();
            if (!anchor.IsValid)
            {
                anchor = destination.IsValid ? destination : Position;
            }

            IntVec3 found = CellFinder.RandomClosewalkCellNear(anchor, Map, WanderRadius, CanWanderInHomeArea);
            if (!found.IsValid)
            {
                found = CellFinder.RandomClosewalkCellNear(anchor, Map, WanderRadius, CanMoveTo);
            }

            if (found.IsValid)
            {
                wanderTarget = found;
                ClearCurrentJobTarget();
            }
        }

        private IntVec3 RandomColonistCellNearVisitor()
        {
            List<Pawn> colonists = Map?.mapPawns?.FreeColonistsSpawned;
            if (colonists == null || colonists.Count == 0)
            {
                return IntVec3.Invalid;
            }

            List<Pawn> nearbyColonists = new List<Pawn>();
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn colonist = colonists[i];
                if (colonist != null && colonist.Spawned && (colonist.Position - Position).LengthHorizontalSquared <= 400)
                {
                    nearbyColonists.Add(colonist);
                }
            }

            return nearbyColonists.Count > 0
                ? nearbyColonists.RandomElement().Position
                : colonists.RandomElement().Position;
        }

        private static Thing MakeGiftItem(string defName, int count)
        {
            ThingDef thingDef = DefDatabase<ThingDef>.GetNamedSilentFail(defName) ?? ThingDefOf.MedicineIndustrial;
            Thing gift = ThingMaker.MakeThing(thingDef);
            gift.stackCount = Mathf.Max(1, count);

            CompQuality quality = gift.TryGetComp<CompQuality>();
            if (quality != null)
            {
                quality.SetQuality(QualityCategory.Normal, ArtGenerationContext.Outsider);
            }

            return gift;
        }

        private void EnsureGotoJob(IntVec3 target)
        {
            if (!target.IsValid || jobs == null)
            {
                return;
            }

            Job currentJob = jobs.curJob;
            if (currentJobTarget == target
                && currentJob != null
                && currentJob.def == JobDefOf.Goto
                && currentJob.targetA.IsValid
                && currentJob.targetA.Cell == target)
            {
                return;
            }

            currentJobTarget = target;
            Job job = new Job(JobDefOf.Goto, target)
            {
                locomotionUrgency = LocomotionUrgency.Walk,
                expiryInterval = 5000,
                checkOverrideOnExpire = false
            };

            jobs.StartJob(job, JobCondition.InterruptForced, null, false, true);
        }

        private void UpdateVisualHorizontalDirection(IntVec3 target)
        {
            IntVec3 lookCell = IntVec3.Invalid;
            if (pather != null && pather.Moving && pather.nextCell.IsValid && pather.nextCell != Position)
            {
                lookCell = pather.nextCell;
            }
            else if (target.IsValid && target != Position)
            {
                lookCell = target;
            }

            if (!lookCell.IsValid)
            {
                return;
            }

            int horizontalDelta = lookCell.x - Position.x;
            if (horizontalDelta < 0)
            {
                visualHorizontalDirection = -1;
            }
            else if (horizontalDelta > 0)
            {
                visualHorizontalDirection = 1;
            }
        }

        private void ClearCurrentJobTarget()
        {
            currentJobTarget = IntVec3.Invalid;
            if (jobs?.curJob != null)
            {
                jobs.EndCurrentJob(JobCondition.InterruptForced, true, true);
            }
        }

        private bool CanMoveTo(IntVec3 cell)
        {
            if (!cell.InBounds(Map) || cell.Fogged(Map) || !cell.Standable(Map))
            {
                return false;
            }

            Building_Door door = cell.GetDoor(Map);
            return door == null || door.FreePassage;
        }

        private bool CanWanderInHomeArea(IntVec3 cell)
        {
            return CanMoveTo(cell) && Map?.areaManager?.Home != null && Map.areaManager.Home[cell];
        }
    }

    public sealed class Thing_MrHankeyVisitor : ThingWithComps
    {
        private const int MoveIntervalTicks = 12;
        private const int ArrivalDistanceSquared = 4;
        private const int WanderAfterGiftTicks = 15000;
        private const int WanderArrivalDistanceSquared = 2;
        private const int WanderRadius = 10;

        private IntVec3 destination = IntVec3.Invalid;
        private IntVec3 exitCell = IntVec3.Invalid;
        private bool giftGiven;
        private bool wandering;
        private bool leaving;
        private IntVec3 wanderTarget = IntVec3.Invalid;
        private int wanderUntilTick = -1;
        private int nextMoveTick;
        private int destroyAfterTick = -1;
        private bool giftIsAnimal;
        private string giftDefName;
        private int giftCount;
        private string giftLabel;
        private PawnPath currentPath;
        private IntVec3 currentPathTarget = IntVec3.Invalid;
        private IntVec3 drawFromCell = IntVec3.Invalid;
        private IntVec3 drawToCell = IntVec3.Invalid;
        private int moveStartTick;
        private int moveEndTick;

        public override Vector3 DrawPos
        {
            get
            {
                if (drawFromCell.IsValid && drawToCell.IsValid && moveEndTick > moveStartTick)
                {
                    int now = Find.TickManager?.TicksGame ?? moveEndTick;
                    if (now < moveEndTick)
                    {
                        float progress = Mathf.Clamp01((now - moveStartTick) / (float)(moveEndTick - moveStartTick));
                        Vector3 drawPos = Vector3.Lerp(drawFromCell.ToVector3Shifted(), drawToCell.ToVector3Shifted(), progress);
                        drawPos.y = def.altitudeLayer.AltitudeFor();
                        return drawPos;
                    }
                }

                return base.DrawPos;
            }
        }

        public void Initialize(IntVec3 destinationCell, IntVec3 exitMapCell, MrHankeyGiftPayload payload)
        {
            destination = destinationCell;
            exitCell = exitMapCell;
            giftIsAnimal = payload.IsAnimal;
            giftDefName = payload.DefName;
            giftCount = payload.Count;
            giftLabel = payload.Label;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref destination, "destination", IntVec3.Invalid);
            Scribe_Values.Look(ref exitCell, "exitCell", IntVec3.Invalid);
            Scribe_Values.Look(ref giftGiven, "giftGiven", defaultValue: false);
            Scribe_Values.Look(ref wandering, "wandering", defaultValue: false);
            Scribe_Values.Look(ref leaving, "leaving", defaultValue: false);
            Scribe_Values.Look(ref wanderTarget, "wanderTarget", IntVec3.Invalid);
            Scribe_Values.Look(ref wanderUntilTick, "wanderUntilTick", -1);
            Scribe_Values.Look(ref nextMoveTick, "nextMoveTick", 0);
            Scribe_Values.Look(ref destroyAfterTick, "destroyAfterTick", -1);
            Scribe_Values.Look(ref giftIsAnimal, "giftIsAnimal", defaultValue: false);
            Scribe_Values.Look(ref giftDefName, "giftDefName");
            Scribe_Values.Look(ref giftCount, "giftCount", 1);
            Scribe_Values.Look(ref giftLabel, "giftLabel");
            Scribe_Values.Look(ref drawFromCell, "drawFromCell", IntVec3.Invalid);
            Scribe_Values.Look(ref drawToCell, "drawToCell", IntVec3.Invalid);
            Scribe_Values.Look(ref moveStartTick, "moveStartTick", 0);
            Scribe_Values.Look(ref moveEndTick, "moveEndTick", 0);
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            ReleasePath();
            base.DeSpawn(mode);
        }

        protected override void Tick()
        {
            base.Tick();

            if (!Spawned)
            {
                return;
            }

            int now = Find.TickManager.TicksGame;
            if (destroyAfterTick > 0 && now >= destroyAfterTick)
            {
                Destroy(DestroyMode.Vanish);
                return;
            }

            if (now < nextMoveTick)
            {
                return;
            }

            nextMoveTick = now + MoveIntervalTicks;

            if (!giftGiven && IsNear(destination))
            {
                GiveGift();
                return;
            }

            if (wandering)
            {
                TickWandering(now);
                return;
            }

            if (leaving && IsNear(exitCell))
            {
                Destroy(DestroyMode.Vanish);
                return;
            }

            MoveOneStepToward(leaving ? exitCell : destination);
        }

        private bool IsNear(IntVec3 cell)
        {
            return cell.IsValid && (Position - cell).LengthHorizontalSquared <= ArrivalDistanceSquared;
        }

        private void GiveGift()
        {
            giftGiven = true;
            leaving = false;
            destroyAfterTick = -1;

            int giftsToGive = Rand.RangeInclusive(1, 3);
            List<string> giftLabels = new List<string>();
            LookTargets letterTargets = this;

            for (int i = 0; i < giftsToGive; i++)
            {
                MrHankeyGiftPayload payload = i == 0
                    ? CurrentPayload()
                    : IncidentWorker_MrHankeyGift.CreateRandomPayload();

                LookTargets giftTarget;
                string givenLabel;
                if (TryGiveSingleGift(payload, out giftTarget, out givenLabel))
                {
                    if (giftLabels.Count == 0)
                    {
                        letterTargets = giftTarget;
                    }

                    giftLabels.Add(givenLabel);
                }
            }

            if (giftLabels.Count == 0)
            {
                Thing fallbackGift = MakeGiftItem("MedicineIndustrial", 3);
                GenPlace.TryPlaceThing(fallbackGift, Position, Map, ThingPlaceMode.Near);
                letterTargets = fallbackGift;
                giftLabels.Add("3 аптечки");
            }

            StartWandering();
            IncidentWorker_MrHankeyGift.SendGiftLetter(letterTargets, string.Join(", ", giftLabels.ToArray()));
        }

        private MrHankeyGiftPayload CurrentPayload()
        {
            if (string.IsNullOrEmpty(giftDefName))
            {
                return MrHankeyGiftPayload.ForItem("MedicineIndustrial", 3, "3 аптечки");
            }

            return giftIsAnimal
                ? MrHankeyGiftPayload.ForAnimal(giftDefName, giftLabel ?? "животное")
                : MrHankeyGiftPayload.ForItem(giftDefName, Mathf.Max(1, giftCount), giftLabel ?? "подарок");
        }

        private bool TryGiveSingleGift(MrHankeyGiftPayload payload, out LookTargets lookTargets, out string label)
        {
            if (payload.IsAnimal)
            {
                Pawn animal = MakeGiftAnimal(payload);
                if (animal != null)
                {
                    IntVec3 cell = CellFinder.RandomSpawnCellForPawnNear(Position, Map);
                    GenSpawn.Spawn(animal, cell, Map);
                    animal.SetFaction(Faction.OfPlayer);
                    lookTargets = animal;
                    label = payload.Label ?? "животное";
                    return true;
                }

                payload = MrHankeyGiftPayload.ForItem("MedicineIndustrial", 3, "3 аптечки");
            }

            Thing gift = MakeGiftItem(payload.DefName, payload.Count);
            GenPlace.TryPlaceThing(gift, Position, Map, ThingPlaceMode.Near);
            lookTargets = gift;
            label = payload.Label ?? "подарок";
            return true;
        }

        private Pawn MakeGiftAnimal(MrHankeyGiftPayload payload)
        {
            PawnKindDef pawnKindDef = DefDatabase<PawnKindDef>.GetNamedSilentFail(payload.DefName);
            if (pawnKindDef == null)
            {
                return null;
            }

            PawnGenerationRequest request = new PawnGenerationRequest(
                pawnKindDef,
                Faction.OfPlayer,
                PawnGenerationContext.NonPlayer,
                Map.Tile,
                forceGenerateNewPawn: true,
                canGeneratePawnRelations: false,
                allowFood: false,
                allowAddictions: false);

            return PawnGenerator.GeneratePawn(request);
        }

        private void StartWandering()
        {
            wandering = true;
            leaving = false;
            wanderUntilTick = Find.TickManager.TicksGame + WanderAfterGiftTicks;
            PickNewWanderTarget();
        }

        private void TickWandering(int now)
        {
            if (now >= wanderUntilTick)
            {
                wandering = false;
                leaving = true;
                wanderTarget = IntVec3.Invalid;
                ReleasePath();
                MoveOneStepToward(exitCell);
                return;
            }

            if (!wanderTarget.IsValid || IsNearWanderTarget(wanderTarget))
            {
                PickNewWanderTarget();
            }

            MoveOneStepToward(wanderTarget.IsValid ? wanderTarget : destination);
        }

        private bool IsNearWanderTarget(IntVec3 cell)
        {
            return cell.IsValid && (Position - cell).LengthHorizontalSquared <= WanderArrivalDistanceSquared;
        }

        private void PickNewWanderTarget()
        {
            IntVec3 anchor = RandomColonistCellNearVisitor();
            if (!anchor.IsValid)
            {
                anchor = destination.IsValid ? destination : Position;
            }

            IntVec3 found = CellFinder.RandomClosewalkCellNear(anchor, Map, WanderRadius, CanMoveTo);
            if (found.IsValid)
            {
                wanderTarget = found;
                ReleasePath();
            }
        }

        private IntVec3 RandomColonistCellNearVisitor()
        {
            List<Pawn> colonists = Map?.mapPawns?.FreeColonistsSpawned;
            if (colonists == null || colonists.Count == 0)
            {
                return IntVec3.Invalid;
            }

            List<Pawn> nearbyColonists = new List<Pawn>();
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn colonist = colonists[i];
                if (colonist != null && colonist.Spawned && (colonist.Position - Position).LengthHorizontalSquared <= 400)
                {
                    nearbyColonists.Add(colonist);
                }
            }

            if (nearbyColonists.Count > 0)
            {
                return nearbyColonists.RandomElement().Position;
            }

            return colonists.RandomElement().Position;
        }

        private static Thing MakeGiftItem(string defName, int count)
        {
            ThingDef thingDef = DefDatabase<ThingDef>.GetNamedSilentFail(defName) ?? ThingDefOf.MedicineIndustrial;
            Thing gift = ThingMaker.MakeThing(thingDef);
            gift.stackCount = Mathf.Max(1, count);

            CompQuality quality = gift.TryGetComp<CompQuality>();
            if (quality != null)
            {
                quality.SetQuality(QualityCategory.Normal, ArtGenerationContext.Outsider);
            }

            return gift;
        }

        private void MoveOneStepToward(IntVec3 target)
        {
            if (!target.IsValid)
            {
                return;
            }

            IntVec3 nextCell;
            if (!TryFindPathStep(target, out nextCell) && !TryFindNextStep(target, out nextCell))
            {
                return;
            }

            Rot4 nextRotation = RotationForMove(nextCell - Position);
            if (nextRotation.IsValid)
            {
                Rotation = nextRotation;
            }

            drawFromCell = Position;
            drawToCell = nextCell;
            moveStartTick = Find.TickManager.TicksGame;
            moveEndTick = moveStartTick + MoveIntervalTicks;
            Position = nextCell;
        }

        private bool TryFindPathStep(IntVec3 target, out IntVec3 result)
        {
            result = Position;
            if (currentPath == null || !currentPath.Found || currentPath.Finished || currentPathTarget != target)
            {
                ReleasePath();
                currentPath = Map.pathFinder.FindPathNow(
                    Position,
                    target,
                    TraverseParms.For(TraverseMode.NoPassClosedDoors),
                    peMode: PathEndMode.OnCell);
                currentPathTarget = target;
            }

            if (currentPath == null || !currentPath.Found || currentPath.Finished || currentPath.NodesLeftCount <= 1)
            {
                return false;
            }

            result = currentPath.ConsumeNextNode();
            if (!CanMoveTo(result))
            {
                ReleasePath();
                result = Position;
                return false;
            }

            return result != Position;
        }

        private bool TryFindNextStep(IntVec3 target, out IntVec3 result)
        {
            result = Position;
            int bestDistance = (Position - target).LengthHorizontalSquared;

            for (int i = 0; i < GenAdj.AdjacentCellsAndInside.Length; i++)
            {
                IntVec3 candidate = Position + GenAdj.AdjacentCellsAndInside[i];
                if (candidate == Position || !CanMoveTo(candidate))
                {
                    continue;
                }

                int distance = (candidate - target).LengthHorizontalSquared;
                if (distance < bestDistance)
                {
                    result = candidate;
                    bestDistance = distance;
                }
            }

            return result != Position;
        }

        private bool CanMoveTo(IntVec3 cell)
        {
            if (!cell.InBounds(Map) || cell.Fogged(Map) || !cell.Standable(Map))
            {
                return false;
            }

            Building_Door door = cell.GetDoor(Map);
            return door == null || door.FreePassage;
        }

        private static Rot4 RotationForMove(IntVec3 delta)
        {
            if (Mathf.Abs(delta.x) > Mathf.Abs(delta.z))
            {
                return delta.x >= 0 ? Rot4.East : Rot4.West;
            }

            return delta.z >= 0 ? Rot4.North : Rot4.South;
        }

        private void ReleasePath()
        {
            if (currentPath != null)
            {
                currentPath.ReleaseToPool();
                currentPath = null;
                currentPathTarget = IntVec3.Invalid;
            }
        }
    }

    public static class SouthParkDeathsDebugActions
    {
        [DebugAction("SouthparkMod", "Play Kenny death sound", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void PlayKennyDeathSound()
        {
            SouthParkDeathsState.DebugPlayKennyDeathSound();
        }

        [DebugAction("SouthparkMod", "Replace corpse apparel with Kenny outfit", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ReplaceCorpseApparelWithKennyOutfit()
        {
            SouthParkDeathsState.DebugReplaceCorpseApparel();
        }

        [DebugAction("SouthparkMod", "Replace corpse apparel with Mysterion outfit", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ReplaceCorpseApparelWithMysterionOutfit()
        {
            SouthParkDeathsState.DebugReplaceCorpseApparelWithMysterion();
        }

        [DebugAction("SouthparkMod", "Show resurrection popup", allowedGameStates = AllowedGameStates.Playing)]
        public static void ShowResurrectionPopup()
        {
            SouthParkDeathsState.NotifyResurrectorSerumUsed();
        }

        [DebugAction("SouthparkMod", "Trigger Mr. Hankey gift", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void TriggerMrHankeyGift()
        {
            SouthParkDeathsState.DebugTriggerMrHankeyGift();
        }
    }
}
