using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;
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
        private const string MysterionName = "Мистерио";
        private const string MysterionSuitDefName = "SPD_Apparel_MysterionSuit";
        private const string MysterionHoodDefName = "SPD_Apparel_MysterionHood";
        private const string MysterionGrenadesDefName = "Weapon_GrenadeFrag";
        private const string NimbleTraitDefName = "Nimble";
        private static int resurrectionPopupUntilTick = -1;
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

            if (Scribe.mode == LoadSaveMode.PostLoadInit && mysterionResurrectionsUsed == null)
            {
                mysterionResurrectionsUsed = new Dictionary<int, int>();
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
                Messages.Message("South Park Deaths: Мистерио exhausted all resurrections.", pawn, MessageTypeDefOf.NegativeEvent, false);
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

            Messages.Message("South Park Deaths: Kenny death sound is not available.", MessageTypeDefOf.RejectInput, false);
        }

        public static void DebugReplaceCorpseApparel()
        {
            Map map = Find.CurrentMap;
            Corpse corpse = map?.listerThings?.ThingsInGroup(ThingRequestGroup.Corpse).OfType<Corpse>().FirstOrDefault();
            if (corpse == null)
            {
                Messages.Message("South Park Deaths: no corpse found on the current map.", MessageTypeDefOf.RejectInput, false);
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
                Messages.Message("South Park Deaths: no corpse found on the current map.", MessageTypeDefOf.RejectInput, false);
                return;
            }

            ReplaceCorpseApparelWithOutfit(corpse, "SPD_Apparel_MysterionSuit", "SPD_Apparel_MysterionHood");
        }

        public static void Tick()
        {
            ProcessPendingMysterionResurrections();
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
            if (pawn == null || !pendingMysterionResurrectionIds.Add(pawn.thingIDNumber))
            {
                return;
            }

            pendingMysterionResurrectionDueTicks[pawn.thingIDNumber] = CurrentTick + MysterionResurrectionDelayTicks;
            pendingMysterionResurrections.Add(pawn);
        }

        private static void ProcessPendingMysterionResurrections()
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
                    ResurrectMysterion(pawn);
                }
                else if (pawnId >= 0)
                {
                    pendingMysterionResurrectionIds.Remove(pawnId);
                    pendingMysterionResurrectionDueTicks.Remove(pawnId);
                }
            }
        }

        private static void ResurrectMysterion(Pawn pawn)
        {
            if (pawn == null || !pawn.Dead)
            {
                return;
            }

            int used = GetMysterionResurrectionsUsed(pawn);
            if (ResurrectionUtility.TryResurrect(pawn) && !pawn.Dead)
            {
                CustomizeMysterionJoiner(pawn, pawn.MapHeld);
                Messages.Message(
                    $"South Park Deaths: Мистерио resurrected ({used}/{MysterionMaxResurrections}).",
                    pawn,
                    MessageTypeDefOf.PositiveEvent,
                    false);
            }
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

    public static class SouthParkDeathsDebugActions
    {
        [DebugAction("South Park Deaths", "Play Kenny death sound", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void PlayKennyDeathSound()
        {
            SouthParkDeathsState.DebugPlayKennyDeathSound();
        }

        [DebugAction("South Park Deaths", "Replace corpse apparel with Kenny outfit", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ReplaceCorpseApparelWithKennyOutfit()
        {
            SouthParkDeathsState.DebugReplaceCorpseApparel();
        }

        [DebugAction("South Park Deaths", "Replace corpse apparel with Mysterion outfit", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ReplaceCorpseApparelWithMysterionOutfit()
        {
            SouthParkDeathsState.DebugReplaceCorpseApparelWithMysterion();
        }

        [DebugAction("South Park Deaths", "Show resurrection popup", allowedGameStates = AllowedGameStates.Playing)]
        public static void ShowResurrectionPopup()
        {
            SouthParkDeathsState.NotifyResurrectorSerumUsed();
        }
    }
}
