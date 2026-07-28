using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Herds
{
    [StaticConstructorOnStartup]
    public static class HerdsStartup
    {
        static HerdsStartup()
        {
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                AddTab(DefDatabase<ThingDef>.GetNamedSilentFail("PenMarker"), typeof(ITab_PenHerds));
                foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
                {
                    if (PreyProfileDatabase.IsEligible(def)) AddTab(def, typeof(ITab_Herd));
                    if (def.race?.Animal == true) AddTab(def, typeof(ITab_AnimalMemory));
                    if (def.plant?.IsTree == true) def.selectable = true;
                    if (def.plant != null || def.comps?.Any(properties => properties is CompProperties_HidingRefuge) == true) AddTab(def, typeof(ITab_HidingRefuge));
                    if (def.race?.Animal == true) ReorderAnimalTabs(def);
                }
                WildlifeProgression.RefreshDefGates();
                HerdsMod.Harmony.PatchAll(Assembly.GetExecutingAssembly());
                ProgressionEducationKnowledgeCompatibility.Initialize();
            });
        }

        private static void AddTab(ThingDef def, Type tabType)
        {
            if (def == null) return;
            if (def.inspectorTabs == null) def.inspectorTabs = new List<Type>();
            if (!def.inspectorTabs.Contains(tabType)) def.inspectorTabs.Add(tabType);
            if (def.inspectorTabsResolved != null && !def.inspectorTabsResolved.Any(tab => tab.GetType() == tabType))
                def.inspectorTabsResolved.Add(InspectTabManager.GetSharedInstance(tabType));
        }

        public static void ReorderAnimalTabs(ThingDef def)
        {
            if (def?.race?.Animal != true) return;
            if (def.inspectorTabs == null) def.inspectorTabs = new List<Type>();
            if (!def.inspectorTabs.Contains(typeof(ITab_AnimalMemory)))
                def.inspectorTabs.Add(typeof(ITab_AnimalMemory));
            if (PreyProfileDatabase.IsEligible(def) && !def.inspectorTabs.Contains(typeof(ITab_Herd)))
                def.inspectorTabs.Add(typeof(ITab_Herd));
            Type packTab = AccessTools.TypeByName("Packs.ITab_Pack");
            if (def.race.predator && packTab != null && !def.inspectorTabs.Contains(packTab))
                def.inspectorTabs.Add(packTab);
            string[] order =
            {
                "RimWorld.ITab_Pawn_Needs",
                "Herds.ITab_AnimalMemory",
                "RimWorld.ITab_Pawn_Health",
                "RimWorld.ITab_Pawn_Social",
                "RimWorld.ITab_Pawn_Training",
                "Herds.ITab_Herd",
                "Packs.ITab_Pack",
                "RimWorld.ITab_Pawn_Log"
            };
            List<Type> original = def.inspectorTabs.Distinct().ToList();
            List<Type> ordered = new List<Type>();
            for (int i = 0; i < order.Length; i++)
                ordered.AddRange(original.Where(type => type.FullName == order[i]));
            ordered.AddRange(original.Where(type => !ordered.Contains(type)));
            def.inspectorTabs = ordered;
            if (def.inspectorTabsResolved != null)
                def.inspectorTabsResolved = ordered.Select(InspectTabManager.GetSharedInstance).ToList();
        }
    }

    [HarmonyPatch(typeof(Thing), "GetInspectTabs")]
    public static class AnimalInspectTabReconciliationPatch
    {
        public static void Postfix(Thing __instance, ref IEnumerable<InspectTabBase> __result)
        {
            if (__instance is not Pawn pawn || pawn.RaceProps?.Animal != true) return;
            List<InspectTabBase> tabs = (__result ?? Enumerable.Empty<InspectTabBase>())
                .Where(tab => tab != null).GroupBy(tab => tab.GetType()).Select(group => group.First()).ToList();
            bool tamed = pawn.Faction == Faction.OfPlayer;
            tabs.RemoveAll(tab =>
                (tab.GetType().FullName == "RimWorld.ITab_Pawn_Needs" &&
                 (!tamed || pawn.needs == null)) ||
                (tab.GetType().FullName == "RimWorld.ITab_Pawn_Training" &&
                 (!tamed || pawn.training == null)) ||
                (tab.GetType().FullName == "RimWorld.ITab_Pawn_Social" &&
                 pawn.relations == null));
            if (HerdsMod.Settings?.enableAnimalMemory == true) Add(tabs, typeof(ITab_AnimalMemory));
            if (HerdsMod.Settings?.enablePreyAndHerds == true &&
                HerdsMod.Settings.enableWildlifeKnowledge && PreyProfileDatabase.IsEligible(pawn.def))
                Add(tabs, typeof(ITab_Herd));
            string[] order =
            {
                "RimWorld.ITab_Pawn_Needs", "Herds.ITab_AnimalMemory", "RimWorld.ITab_Pawn_Health",
                "RimWorld.ITab_Pawn_Social", "RimWorld.ITab_Pawn_Training", "Herds.ITab_Herd",
                "Packs.ITab_Pack", "RimWorld.ITab_Pawn_Log"
            };
            __result = tabs.OrderBy(tab =>
            {
                int index = Array.IndexOf(order, tab.GetType().FullName);
                return index < 0 ? order.Length : index;
            }).ToList();
        }

        private static void Add(List<InspectTabBase> tabs, Type type)
        {
            if (type != null && !tabs.Any(tab => tab.GetType() == type))
                tabs.Add(InspectTabManager.GetSharedInstance(type));
        }
    }

    [HarmonyPatch(typeof(ITab_Pawn_Needs), nameof(ITab_Pawn_Needs.IsVisible), MethodType.Getter)]
    public static class AnimalNeedsTabStaleSelectionGuard
    {
        public static bool Prefix(ref bool __result)
        {
            if (Find.Selector?.SingleSelectedThing is Pawn pawn && pawn.needs != null) return true;
            __result = false;
            return false;
        }
    }

    public static class HerdDefenseAPI
    {
        public static void NotifyThreat(Pawn herdMember, Thing threat, int durationTicks = 900)
        {
            if (HerdsMod.Settings?.enablePreyAndHerds != true) return;
            herdMember?.Map?.GetComponent<HerdMapComponent>()?.NotifyThreat(herdMember, threat, durationTicks);
        }
    }

    public static class PreyDefenseAPI
    {
        public static void NotifyThreat(Pawn prey, Thing predator, int durationTicks = 900)
        {
            if (HerdsMod.Settings?.enablePreyAndHerds != true) return;
            prey?.Map?.GetComponent<HerdMapComponent>()?.NotifyThreat(prey, predator, durationTicks);
        }

        public static bool IsHidden(Pawn prey)
        {
            if (HerdsMod.Settings?.enablePreyAndHerds != true) return false;
            if (prey == null) return false;
            if (prey.Spawned) return false;
            for (int i = 0; i < Find.Maps.Count; i++)
                if (Find.Maps[i].GetComponent<HerdMapComponent>()?.IsHidden(prey) == true) return true;
            return false;
        }

        public static float VigilanceFor(Pawn prey)
        {
            if (HerdsMod.Settings?.enablePreyAndHerds != true) return 0.5f;
            if (prey == null) return 0.5f;
            if (prey.Spawned) return prey.Map.GetComponent<HerdMapComponent>()?.VigilanceFor(prey) ?? 0.5f;
            return Mathf.Clamp(PreyProfileDatabase.For(prey.def)?.vigilanceChance ?? 0.5f, 0.05f, 0.95f);
        }

        public static float DetectionModifierFor(Pawn prey)
        {
            if (HerdsMod.Settings?.enablePreyAndHerds != true) return 0f;
            PreyDefenseStrategy strategy = PreyProfileDatabase.For(prey?.def)?.defenseStrategy ?? PreyDefenseStrategy.Flight;
            if (strategy == PreyDefenseStrategy.Freeze) return -0.12f;
            if (strategy == PreyDefenseStrategy.Hide) return -0.04f;
            if (strategy == PreyDefenseStrategy.StandGround) return 0.05f;
            return 0f;
        }

        public static bool IsBird(Pawn animal) => PreyProfileDatabase.IsBird(animal?.def);

        public static Thing HomeFor(Pawn prey)
        {
            if (HerdsMod.Settings?.enablePreyAndHerds != true) return null;
            if (prey == null) return null;
            if (prey.Spawned) return prey.Map.GetComponent<HerdMapComponent>()?.HomeFor(prey);
            for (int i = 0; i < Find.Maps.Count; i++)
            {
                Thing home = Find.Maps[i].GetComponent<HerdMapComponent>()?.HomeFor(prey);
                if (home != null) return home;
            }
            return null;
        }

        public static void NotifyThreatEnded(Pawn prey, Thing predator = null)
        {
            if (HerdsMod.Settings?.enablePreyAndHerds != true) return;
            if (prey == null) return;
            if (prey.Spawned)
            {
                prey.Map.GetComponent<HerdMapComponent>()?.NotifyThreatEnded(prey, predator);
                return;
            }
            for (int i = 0; i < Find.Maps.Count; i++) Find.Maps[i].GetComponent<HerdMapComponent>()?.NotifyThreatEnded(prey, predator);
        }
    }

    [HarmonyPatch(typeof(JobGiver_GetRest), "TryGiveJob")]
    public static class HomeRestPatch
    {
        public static void Postfix(Pawn pawn, ref Job __result)
        {
            if (HerdsMod.Settings?.enablePreyAndHerds != true || pawn?.Spawned != true || __result?.def != JobDefOf.LayDown || __result.targetA.HasThing || !PreyProfileDatabase.IsEligible(pawn.def)) return;
            HerdMapComponent component = pawn.Map.GetComponent<HerdMapComponent>();
            if (component == null || !component.TryGetHomeRestCell(pawn, out IntVec3 cell)) return;
            Job homeRest = JobMaker.MakeJob(JobDefOf.LayDown, cell);
            homeRest.forceSleep = __result.forceSleep;
            __result = homeRest;
            if (WildlifeTestLog.Enabled) WildlifeTestLog.WriteTransition("home-rest:" + pawn.thingIDNumber, "HomeRest", "cell=" + cell + " forceSleep=" + homeRest.forceSleep, pawn, component.HomeFor(pawn));
        }
    }

    [HarmonyPatch]
    public static class PacksPursuitEscapePatch
    {
        private sealed class EscapeMemory
        {
            public Pawn prey;
            public int nextCheckTick;
            public float lastDistance;
        }

        private static readonly ConditionalWeakTable<object, Dictionary<int, EscapeMemory>> MemoryByComponent = new ConditionalWeakTable<object, Dictionary<int, EscapeMemory>>();
        private static FieldInfo packIdField;
        private static FieldInfo packPreyField;
        private static FieldInfo packMembersField;

        public static bool Prepare() => AccessTools.TypeByName("Packs.PackMapComponent") != null;

        public static MethodBase TargetMethod()
        {
            Type type = AccessTools.TypeByName("Packs.PackMapComponent");
            return type == null ? null : AccessTools.Method(type, "ShouldAbandonHunt");
        }

        public static void Postfix(object __instance, object pack, int now, ref bool __result)
        {
            if (__result || pack == null || HerdsMod.Settings?.enablePredatorEscapeChance != true) return;
            Type packType = pack.GetType();
            packIdField = packIdField ?? AccessTools.Field(packType, "id");
            packPreyField = packPreyField ?? AccessTools.Field(packType, "prey");
            packMembersField = packMembersField ?? AccessTools.Field(packType, "members");
            if (packIdField == null || packPreyField == null || packMembersField == null) return;

            int packId = (int)packIdField.GetValue(pack);
            Pawn prey = packPreyField.GetValue(pack) as Pawn;
            if (prey?.Spawned != true || prey.Dead || prey.Downed) return;
            IEnumerable<Pawn> members = packMembersField.GetValue(pack) as IEnumerable<Pawn>;
            if (members == null) return;

            int activeHunterCount = 0;
            float closestDistance = float.MaxValue;
            float fastestHunter = 0.1f;
            foreach (Pawn member in members)
            {
                if (member?.Spawned != true || member.Dead || member.Downed || member.CurJob == null || !member.CurJob.targetA.HasThing || member.CurJob.targetA.Thing != prey) continue;
                if (member.CurJobDef != JobDefOf.PredatorHunt && member.CurJobDef != JobDefOf.AttackMelee) continue;
                activeHunterCount++;
                closestDistance = Mathf.Min(closestDistance, member.Position.DistanceTo(prey.Position));
                fastestHunter = Mathf.Max(fastestHunter, member.GetStatValue(StatDefOf.MoveSpeed));
            }
            if (activeHunterCount == 0) return;

            Dictionary<int, EscapeMemory> componentMemory = MemoryByComponent.GetOrCreateValue(__instance);
            if (!componentMemory.TryGetValue(packId, out EscapeMemory memory) || memory.prey != prey)
            {
                componentMemory[packId] = new EscapeMemory { prey = prey, nextCheckTick = now + 180, lastDistance = closestDistance };
                return;
            }
            if (now < memory.nextCheckTick) return;
            memory.nextCheckTick = now + Mathf.Max(120, HerdsMod.Settings.predatorEscapeCheckIntervalTicks);

            float preySpeed = prey.GetStatValue(StatDefOf.MoveSpeed);
            float speedAdvantage = (preySpeed - fastestHunter) / fastestHunter;
            float chance = HerdsMod.Settings.basePredatorEscapeChance;
            chance += Mathf.Clamp(speedAdvantage * 0.28f, -0.16f, 0.24f);
            chance += Mathf.InverseLerp(5f, 30f, closestDistance) * 0.18f;
            chance -= Mathf.Min(0.18f, (activeHunterCount - 1) * 0.035f);
            if (closestDistance > memory.lastDistance + 1.5f) chance += 0.1f;
            else if (closestDistance + 1.5f < memory.lastDistance) chance -= 0.07f;
            chance *= Mathf.Lerp(0.35f, 1f, prey.health.summaryHealth.SummaryHealthPercent);
            memory.lastDistance = closestDistance;
            if (Rand.Chance(Mathf.Clamp(chance, 0.02f, 0.6f))) __result = true;
        }
    }

    [HarmonyPatch]
    public static class PacksHuntEndedPatch
    {
        public static bool Prepare() => AccessTools.TypeByName("Packs.PackMapComponent") != null;

        public static MethodBase TargetMethod()
        {
            Type type = AccessTools.TypeByName("Packs.PackMapComponent");
            return type == null ? null : AccessTools.Method(type, "AbandonHunt");
        }

        public static void Prefix(object pack, ref Pawn __state)
        {
            if (pack == null) return;
            __state = AccessTools.Field(pack.GetType(), "prey")?.GetValue(pack) as Pawn;
        }

        public static void Postfix(Pawn __state)
        {
            if (__state != null) PreyDefenseAPI.NotifyThreatEnded(__state);
        }
    }

    public sealed class JobGiver_HerdDefense : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            return HerdDefenseJobs.MakeJob(pawn, pawn?.Map?.GetComponent<HerdMapComponent>()?.DefenseOrderFor(pawn));
        }
    }

    internal static class HerdDefenseJobs
    {
        internal static Job MakeJob(Pawn pawn, HerdDefenseOrder order)
        {
            if (pawn == null || order == null || !order.destination.IsValid) return null;
            if (order.mode == HerdDefenseMode.Hide && !order.treeWaypoint && order.refuge?.Spawned == true)
            {
                Job hide = JobMaker.MakeJob(HerdsDefOf.Herds_Hide, order.refuge, order.threat);
                hide.expiryInterval = 600;
                hide.checkOverrideOnExpire = true;
                hide.locomotionUrgency = LocomotionUrgency.Sprint;
                if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("DefenseJob", "job=Hide refuge=" + order.refuge.thingIDNumber + " expiry=" + hide.expiryInterval, pawn, order.threat);
                return hide;
            }
            if (order.guardian && order.threat is Pawn threatPawn && threatPawn.Spawned && !threatPawn.Dead &&
                pawn.Position.InHorDistOf(threatPawn.Position, 4.5f) && !pawn.WorkTagIsDisabled(WorkTags.Violent))
            {
                Job attack = JobMaker.MakeJob(JobDefOf.AttackMelee, threatPawn);
                attack.maxNumMeleeAttacks = 1;
                attack.expiryInterval = 120;
                attack.checkOverrideOnExpire = true;
                if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("DefenseJob", "job=AttackMelee guardian=true expiry=" + attack.expiryInterval, pawn, threatPawn);
                return attack;
            }
            if (order.mode == HerdDefenseMode.Freeze || order.mode == HerdDefenseMode.StandGround)
            {
                Job wait = JobMaker.MakeJob(JobDefOf.Wait);
                wait.expiryInterval = 120;
                wait.checkOverrideOnExpire = true;
                if (WildlifeTestLog.Enabled) WildlifeTestLog.WriteTransition("defense-job:" + pawn.thingIDNumber, "DefenseJob", "job=Wait mode=" + order.mode, pawn, order.threat);
                return wait;
            }
            if (pawn.Position == order.destination) return null;
            Job job = JobMaker.MakeJob(JobDefOf.Goto, order.destination);
            job.expiryInterval = order.exitMap ? 10000 : 180;
            job.checkOverrideOnExpire = true;
            job.locomotionUrgency = LocomotionUrgency.Sprint;
            job.exitMapOnArrival = order.exitMap;
            if (WildlifeTestLog.Enabled) WildlifeTestLog.WriteTransition("defense-job:" + pawn.thingIDNumber, "DefenseJob", "job=Goto mode=" + (order.treeWaypoint ? "TreeRoute" : order.mode.ToString()) + " destination=" + order.destination, pawn, order.threat);
            return job;
        }
    }

    [HarmonyPatch(typeof(JobGiver_AnimalFlee), "TryGiveJob")]
    public static class CoordinatedAnimalFleePatch
    {
        public static void Postfix(Pawn pawn, ref Job __result)
        {
            if (HerdsMod.Settings?.enablePreyAndHerds != true || pawn == null || !PreyProfileDatabase.IsEligible(pawn.def)) return;
            HerdMapComponent component = pawn.Map?.GetComponent<HerdMapComponent>();
            if (__result != null && __result.targetB.HasThing)
                component?.NotifyThreat(pawn, __result.targetB.Thing);
            Job coordinated = HerdDefenseJobs.MakeJob(pawn, component?.DefenseOrderFor(pawn));
            if (coordinated != null) __result = coordinated;
        }
    }

    [HarmonyPatch(typeof(JobGiver_ReactToCloseMeleeThreat), "TryGiveJob")]
    public static class CoordinatedMeleeThreatPatch
    {
        public static void Postfix(Pawn pawn, ref Job __result)
        {
            if (HerdsMod.Settings?.enablePreyAndHerds != true || pawn == null || !PreyProfileDatabase.IsEligible(pawn.def)) return;
            HerdMapComponent component = pawn.Map?.GetComponent<HerdMapComponent>();
            if (__result != null && __result.targetA.HasThing)
                component?.NotifyThreat(pawn, __result.targetA.Thing);
            Job coordinated = HerdDefenseJobs.MakeJob(pawn, component?.DefenseOrderFor(pawn));
            if (coordinated != null) __result = coordinated;
        }
    }

    [HarmonyPatch(typeof(JobGiver_WanderHerd), "GetWanderRoot")]
    public static class WanderHerdRootPatch
    {
        public static void Postfix(Pawn pawn, ref IntVec3 __result)
        {
            if (HerdsMod.Settings?.enablePreyAndHerds != true) return;
            __result = pawn?.Map?.GetComponent<HerdMapComponent>()?.WanderRootFor(pawn, __result) ?? __result;
        }
    }

    [HarmonyPatch(typeof(JobGiver_WanderInPen), "GetWanderRoot")]
    public static class WanderInPenRootPatch
    {
        public static void Postfix(Pawn pawn, ref IntVec3 __result)
        {
            if (HerdsMod.Settings?.enablePreyAndHerds != true) return;
            __result = pawn?.Map?.GetComponent<HerdMapComponent>()?.WanderRootFor(pawn, __result) ?? __result;
        }
    }

    [HarmonyPatch(typeof(JobGiver_WanderColony), "GetWanderRoot")]
    public static class WanderColonyRootPatch
    {
        public static void Postfix(Pawn pawn, ref IntVec3 __result)
        {
            if (HerdsMod.Settings?.enablePreyAndHerds != true) return;
            __result = pawn?.Map?.GetComponent<HerdMapComponent>()?.WanderRootFor(pawn, __result) ?? __result;
        }
    }

    public sealed class ITab_Herd : ITab
    {
        private Vector2 scroll;

        public ITab_Herd()
        {
            size = new Vector2(560f, 480f);
            labelKey = "Herds_WildlifeKnowledge";
        }

        public override bool IsVisible
        {
            get
            {
                Pawn pawn = SelThing as Pawn;
                return HerdsMod.Settings.enablePreyAndHerds && HerdsMod.Settings.enableWildlifeKnowledge &&
                    pawn?.Spawned == true && PreyProfileDatabase.IsEligible(pawn.def);
            }
        }

        protected override void FillTab()
        {
            Pawn selected = SelThing as Pawn;
            HerdMapComponent component = selected?.Map?.GetComponent<HerdMapComponent>();
            HerdSnapshot herd = component?.HerdFor(selected);
            if (herd == null)
            {
                DrawIndependentAnimal(selected);
                return;
            }
            PreyProfile profile = herd.profile ?? PreyProfileDatabase.For(selected.def);
            bool soloBird = profile?.socialType == PreySocialType.Flock && herd.members.Count <= 1;
            bool solitary = profile?.socialType == PreySocialType.Solitary || soloBird;
            bool flock = profile?.socialType == PreySocialType.Flock && !soloBird;
            bool observed = component.IsObserved(selected);
            Rect rect = new Rect(0f, 0f, size.x, size.y).ContractedBy(12f);
            int knowledge = HuntingKnowledgeMapComponent.ColonyLevel(selected.def);
            float knowledgeXp = HuntingKnowledgeMapComponent.ColonyExperience(selected.def);
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 32f), selected.LabelShortCap);
            Text.Font = GameFont.Small;
            Widgets.FillableBar(new Rect(rect.x, rect.y + 32f, rect.width, 7f), Mathf.Clamp01(knowledgeXp / 1200f));
            string identity = soloBird ? "Solo bird" : solitary ? "Solitary" :
                herd.Label + "  •  " + herd.members.Count + " visible";
            Widgets.Label(new Rect(rect.x, rect.y + 43f, rect.width, 22f), identity + "  •  " + HuntingKnowledgeMapComponent.LevelLabel(knowledge) + "  •  " + (observed ? "Observed" : "Not observed"));
            if (!observed)
            {
                Rect notice = new Rect(rect.x, rect.y + 70f, rect.width, 62f);
                Widgets.DrawMenuSection(notice);
                Widgets.Label(notice.ContractedBy(9f), solitary
                    ? "Observe this animal from nearby or from an active observation post to reveal its survival strategy, refuge preference, vigilance, home, and behavior."
                    : "Observe this group from nearby or from an active observation post to reveal its leadership, vigilance, home, and behavior.");
                DrawMembers(new Rect(rect.x, rect.y + 140f, rect.width, rect.height - 140f), herd.members, ref scroll, selected);
                return;
            }
            float top = rect.y + 70f;
            Rect social = new Rect(rect.x, top, rect.width * 0.49f, 106f);
            Rect behavior = new Rect(rect.x + rect.width * 0.51f, top, rect.width * 0.49f, 106f);
            Widgets.DrawMenuSection(social); Widgets.DrawMenuSection(behavior);
            if (solitary)
            {
                DrawSectionTitle(social, soloBird ? "Individual Bird" : "Individual Profile");
                DrawValueRow(new Rect(social.x + 8f, social.y + 29f, social.width - 16f, 22f), "Lifestyle",
                    soloBird ? "Currently alone" : "Solitary",
                    soloBird
                        ? "This bird is currently moving independently. If it joins compatible birds, flock leadership and rotating lookout behavior will become relevant."
                        : "This species normally lives and moves alone rather than relying on group leadership or sentinels.");
                DrawValueRow(new Rect(social.x + 8f, social.y + 51f, social.width - 16f, 22f), "Survival", DefenseStrategyLabel(profile.defenseStrategy),
                    "The animal's preferred response when it detects a serious threat.");
                DrawValueRow(new Rect(social.x + 8f, social.y + 73f, social.width - 16f, 22f), "Refuge", RefugePreferenceLabel(profile.refugePreference),
                    "The kind of cover or home this animal prefers when hiding or resting.");
            }
            else
            {
                DrawSectionTitle(social, flock ? "Flock" : "Social Group");
                DrawThingLink(new Rect(social.x + 8f, social.y + 31f, social.width - 16f, 27f), flock ? "Lead Bird" : "Leader", herd.leader,
                    flock ? "The bird currently at the front of the flock's shifting formation." :
                    "The group member that anchors cohesion and guides ordinary group movement.");
                DrawThingLink(new Rect(social.x + 8f, social.y + 61f, social.width - 16f, 27f), flock ? "Lookout" : "Sentinel", herd.sentinel,
                    flock ? "Flocks rotate their lookout. An alert lookout can trigger a rapid group scatter." :
                    "The member currently watching for danger. Its vigilance helps determine how quickly the group detects and reacts to threats.");
            }
            DrawSectionTitle(behavior, "Current Behavior");
            Rect stateRect = new Rect(behavior.x + 9f, behavior.y + 32f, behavior.width - 18f, 24f);
            Rect vigilanceRect = new Rect(behavior.x + 9f, behavior.y + 55f, behavior.width - 18f, 24f);
            Rect personalityRect = new Rect(behavior.x + 9f, behavior.y + 78f, behavior.width - 18f, 24f);
            Widgets.Label(stateRect, StateLabel(herd));
            Widgets.Label(vigilanceRect, "Vigilance  " + component.VigilanceFor(selected).ToStringPercent());
            Widgets.Label(personalityRect, "Personality  " + WildlifeLifeUtility.PersonalityLabel(selected));
            TooltipHandler.TipRegion(stateRect, solitary
                ? "This animal's current response. Calm animals follow normal routines; threatened animals may flee, hide, freeze, or stand their ground."
                : "The group's current coordinated response. Calm groups follow normal routines; threatened groups flee, scatter, hide, freeze, protect young, or stand their ground.");
            TooltipHandler.TipRegion(vigilanceRect, "The chance that this animal or its group quickly detects a new threat. Species, group size, experience, concealment, scent masking, and wind can affect the actual detection result.");
            TooltipHandler.TipRegion(personalityRect, WildlifeLifeUtility.PersonalityDescription(selected));

            Rect habitat = new Rect(rect.x, top + 114f, rect.width, 132f);
            Widgets.DrawMenuSection(habitat);
            DrawSectionTitle(habitat, "Home and Safety");
            Thing home = component.HomeFor(selected);
            string homeLabel = home is Plant ? "Tree home" : home?.TryGetComp<CompHidingRefuge>() != null ? "Den / refuge" : "Home";
            DrawThingLink(new Rect(habitat.x + 9f, habitat.y + 31f, habitat.width * 0.48f, 28f), homeLabel, home);
            DrawThingLink(new Rect(habitat.x + habitat.width * 0.51f, habitat.y + 31f, habitat.width * 0.47f, 28f), "Threat", herd.defenseThreat);
            DrawThingLink(new Rect(habitat.x + 9f, habitat.y + 62f, habitat.width * 0.48f, 27f), "Enclosure", herd.pen?.parent);
            DrawValueRow(new Rect(habitat.x + habitat.width * 0.51f, habitat.y + 62f,
                habitat.width * 0.47f, 27f), "Season", selected.Map
                    .GetComponent<WildlifeLivesMapComponent>()?.Lifecycle(selected) ?? "Unknown",
                "The animal's current seasonal lifecycle. Nesting, breeding, rearing, migration preparation, and winter sheltering affect its wider population and ordinary movement.");
            DrawValueRow(new Rect(habitat.x + 9f, habitat.y + 92f,
                habitat.width - 18f, 27f), "Landscape",
                WildlifeLandscapeAPI.RoleSummary(selected),
                WildlifeLandscapeAPI.RoleTooltip(selected));

            float membersTop = top + 254f;
            if (HerdsMod.Settings.enableWildlifeSignalCulture)
            {
                WildlifeSignalCultureMapComponent signals =
                    selected.Map.GetComponent<WildlifeSignalCultureMapComponent>();
                Rect signal = new Rect(rect.x, membersTop, rect.width, 72f);
                Widgets.DrawMenuSection(signal);
                DrawSectionTitle(signal, "Local Signals");
                Rect signalRow = new Rect(signal.x + 9f, signal.y + 30f,
                    signal.width - 18f, 30f);
                Widgets.DrawHighlightIfMouseover(signalRow);
                Widgets.Label(signalRow, signals?.SignalSummary(selected.def) ??
                    "No local signals recorded.");
                TooltipHandler.TipRegion(signalRow, signals?.SignalTooltip(selected.def) ??
                    "No local signal information.");
                membersTop += 80f;
            }
            if (HerdsMod.Settings.enableAnimalMemory)
            {
                Rect memory = new Rect(rect.x, membersTop, rect.width, 72f);
                Widgets.DrawMenuSection(memory);
                DrawSectionTitle(memory, "Individual Memory");
                string summary = selected.Map.GetComponent<WildlifeMemoryMapComponent>()?.Summary(selected) ??
                    "No lasting memories of colonists.";
                string bonds = selected.Map.GetComponent<WildlifeLivesMapComponent>()?.RelationshipSummary(selected);
                if (!bonds.NullOrEmpty()) summary = bonds + "\n" + summary;
                Widgets.Label(new Rect(memory.x + 9f, memory.y + 29f, memory.width - 18f, 38f), summary);
                TooltipHandler.TipRegion(memory, "Individual animals remember who studied, called, tended, hunted, or harmed them. Trust reduces avoidance; fear and hostility increase it.");
                membersTop += 80f;
            }
            if (Prefs.DevMode)
            {
                Rect dev = new Rect(rect.x, membersTop, rect.width, 38f);
                Widgets.DrawBoxSolid(dev, new Color(0.12f, 0.22f, 0.28f, 0.28f));
                if (Widgets.ButtonText(new Rect(dev.x + 6f, dev.y + 5f, dev.width * 0.48f - 9f, 28f), "DEV: Jump to Center")) CameraJumper.TryJump(herd.center, selected.Map);
                if (Widgets.ButtonText(new Rect(dev.x + dev.width * 0.5f + 3f, dev.y + 5f, dev.width * 0.48f - 9f, 28f), "DEV: Jump to Movement Target")) CameraJumper.TryJump(herd.movementTarget, selected.Map);
                membersTop += 44f;
            }
            Widgets.Label(new Rect(rect.x + 4f, membersTop, rect.width, 24f), solitary ? "Current Activity" : "Group Members");
            DrawMembers(new Rect(rect.x, membersTop + 26f, rect.width, rect.yMax - membersTop - 26f), herd.members, ref scroll, selected);
        }

        private void DrawIndependentAnimal(Pawn animal)
        {
            if (animal == null) return;
            PreyProfile profile = PreyProfileDatabase.For(animal.def);
            Rect rect = new Rect(0f, 0f, size.x, size.y).ContractedBy(12f);
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 32f), animal.LabelShortCap);
            Text.Font = GameFont.Small;
            Widgets.DrawMenuSection(new Rect(rect.x, rect.y + 42f, rect.width, 126f));
            string status = animal.Faction == Faction.OfPlayer ? "Colony animal" :
                profile?.socialType == PreySocialType.Flock ? "Solo bird" : "Independent animal";
            Widgets.Label(new Rect(rect.x + 12f, rect.y + 54f, rect.width - 24f, 24f), status);
            Widgets.Label(new Rect(rect.x + 12f, rect.y + 82f, rect.width - 24f, 72f),
                "Lifestyle: " + (profile?.socialType.ToString() ?? "Unknown") +
                "\nSurvival: " + DefenseStrategyLabel(profile?.defenseStrategy ?? PreyDefenseStrategy.Flight) +
                "\nRefuge: " + RefugePreferenceLabel(profile?.refugePreference ?? PreyRefugePreference.None));
            TooltipHandler.TipRegion(new Rect(rect.x, rect.y + 42f, rect.width, 126f),
                animal.Faction == Faction.OfPlayer
                    ? "This animal follows colony care and sleeping assignments, so no wild group, den, or wildlife home is assigned."
                    : "This animal is not currently attached to a visible social group. Group information will appear if it joins one.");
        }

        private static void DrawSectionTitle(Rect section, string title)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.72f, 0.82f, 0.9f);
            Widgets.Label(new Rect(section.x + 9f, section.y + 7f, section.width - 18f, 20f), title.ToUpperInvariant());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private static void DrawThingLink(Rect row, string label, Thing thing, string tooltip = null)
        {
            if (!tooltip.NullOrEmpty()) TooltipHandler.TipRegion(row, tooltip + (thing?.Spawned == true ? "\n\nClick to select " + thing.LabelShortCap + "." : string.Empty));
            if (thing?.Spawned != true)
            {
                GUI.color = Color.gray;
                Widgets.Label(row, label + "  —  None");
                GUI.color = Color.white;
                return;
            }
            Widgets.DrawHighlightIfMouseover(row);
            Widgets.Label(new Rect(row.x + 3f, row.y + 2f, row.width * 0.38f, row.height - 4f), label);
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = new Color(0.55f, 0.82f, 1f);
            Widgets.Label(new Rect(row.x + row.width * 0.38f, row.y, row.width * 0.59f, row.height), thing.LabelShortCap);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            if (Widgets.ButtonInvisible(row)) SelectAndJump(thing);
            if (tooltip.NullOrEmpty()) TooltipHandler.TipRegion(row, "Click to select " + thing.LabelShortCap + ".");
        }

        private static void DrawValueRow(Rect row, string label, string value, string tooltip)
        {
            Widgets.DrawHighlightIfMouseover(row);
            Widgets.Label(new Rect(row.x + 3f, row.y, row.width * 0.42f, row.height), label);
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = new Color(0.82f, 0.88f, 0.92f);
            Widgets.Label(new Rect(row.x + row.width * 0.42f, row.y, row.width * 0.55f, row.height), value);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            TooltipHandler.TipRegion(row, tooltip);
        }

        private static string DefenseStrategyLabel(PreyDefenseStrategy strategy)
        {
            switch (strategy)
            {
                case PreyDefenseStrategy.ProtectYoung: return "Protect Young";
                case PreyDefenseStrategy.StandGround: return "Stand Ground";
                default: return strategy.ToString();
            }
        }

        private static string RefugePreferenceLabel(PreyRefugePreference preference)
        {
            switch (preference)
            {
                case PreyRefugePreference.TreesAndVegetation: return "Trees / Vegetation";
                case PreyRefugePreference.TreesAndDens: return "Trees / Dens";
                default: return preference.ToString();
            }
        }

        private static void SelectAndJump(Thing thing)
        {
            if (thing?.Spawned != true) return;
            Find.Selector.ClearSelection();
            Find.Selector.Select(thing);
            CameraJumper.TryJump(thing);
        }

        internal static string DefenseLabel(HerdDefenseMode mode)
        {
            switch (mode)
            {
                case HerdDefenseMode.ProtectYoung: return "Protecting Young";
                case HerdDefenseMode.Flight: return "Fleeing";
                case HerdDefenseMode.Scatter: return "Scattering";
                case HerdDefenseMode.Hide: return "Seeking Refuge";
                case HerdDefenseMode.Freeze: return "Freezing";
                case HerdDefenseMode.StandGround: return "Standing Ground";
                default: return "Inactive";
            }
        }

        internal static string StateLabel(HerdSnapshot herd)
        {
            if (herd.defenseMode != HerdDefenseMode.None)
                return "State: " + (herd.simulatedHunt ? "Hunted - " : "Threatened - ") + DefenseLabel(herd.defenseMode);
            if (herd.profile?.socialType == PreySocialType.Flock && herd.groundFeeding) return "State: Ground Feeding";
            return herd.profile?.socialType == PreySocialType.Flock ? "State: Flocking" : "State: Calm";
        }

        internal static void DrawMembers(Rect outRect, List<Pawn> members, ref Vector2 scroll, Pawn selected = null)
        {
            Rect view = new Rect(0f, 0f, outRect.width - 16f, Mathf.Max(outRect.height, members.Count * 32f));
            Widgets.BeginScrollView(outRect, ref scroll, view);
            for (int i = 0; i < members.Count; i++)
            {
                Pawn pawn = members[i];
                Rect row = new Rect(0f, i * 32f, view.width, 28f);
                if (pawn == selected) Widgets.DrawHighlightSelected(row); else Widgets.DrawHighlightIfMouseover(row);
                Widgets.Label(new Rect(8f, row.y + 3f, view.width * 0.56f, 24f), pawn.LabelShortCap);
                string status = pawn.Downed ? "Downed" : pawn.InMentalState ? pawn.MentalStateDef.LabelCap : pawn.CurJobDef?.LabelCap ?? "Idle";
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(new Rect(view.width * 0.58f, row.y, view.width * 0.39f, 28f), status);
                Text.Anchor = TextAnchor.UpperLeft;
                if (Widgets.ButtonInvisible(row) && pawn.Spawned)
                {
                    Find.Selector.ClearSelection();
                    Find.Selector.Select(pawn);
                    CameraJumper.TryJump(pawn);
                }
            }
            Widgets.EndScrollView();
        }
    }

    public sealed class ITab_PenHerds : ITab
    {
        private Vector2 scroll;

        public ITab_PenHerds()
        {
            size = new Vector2(620f, 480f);
            labelKey = "Herds_PreyGroups";
        }

        public override bool IsVisible => SelThing?.TryGetComp<CompAnimalPenMarker>() != null;

        protected override void FillTab()
        {
            CompAnimalPenMarker pen = SelThing?.TryGetComp<CompAnimalPenMarker>();
            IReadOnlyList<HerdSnapshot> herds = pen?.parent?.Map?.GetComponent<HerdMapComponent>()?.HerdsFor(pen) ?? Array.Empty<HerdSnapshot>();
            Rect rect = new Rect(0f, 0f, size.x, size.y).ContractedBy(12f);
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 32f), "Prey groups in " + (pen?.RenamableLabel ?? "pen"));
            Text.Font = GameFont.Small;
            if (herds.Count == 0)
            {
                Widgets.Label(new Rect(rect.x, rect.y + 42f, rect.width, 40f), "No eligible prey are currently present.");
                return;
            }
            float height = 0f;
            for (int i = 0; i < herds.Count; i++) height += 54f + herds[i].members.Count * 28f;
            Rect outRect = new Rect(rect.x, rect.y + 40f, rect.width, rect.height - 40f);
            Rect view = new Rect(0f, 0f, outRect.width - 16f, Mathf.Max(outRect.height, height));
            Widgets.BeginScrollView(outRect, ref scroll, view);
            float y = 0f;
            for (int i = 0; i < herds.Count; i++)
            {
                HerdSnapshot herd = herds[i];
                float sectionHeight = 48f + herd.members.Count * 28f;
                Rect section = new Rect(0f, y, view.width, sectionHeight);
                Widgets.DrawMenuSection(section);
                Widgets.Label(new Rect(10f, y + 6f, view.width * 0.52f, 26f), herd.Label + " (" + herd.members.Count + ")");
                string summary = herd.defenseMode == HerdDefenseMode.None ? "State: Calm" : ITab_Herd.StateLabel(herd);
                Widgets.Label(new Rect(view.width * 0.54f, y + 6f, view.width * 0.43f, 26f), summary);
                for (int memberIndex = 0; memberIndex < herd.members.Count; memberIndex++)
                {
                    Pawn pawn = herd.members[memberIndex];
                    Rect row = new Rect(8f, y + 38f + memberIndex * 28f, view.width - 16f, 25f);
                    Widgets.DrawHighlightIfMouseover(row);
                    Widgets.Label(new Rect(row.x + 6f, row.y + 2f, row.width * 0.58f, 22f), pawn.LabelShortCap);
                    Text.Anchor = TextAnchor.MiddleRight;
                    string status = pawn.Downed ? "Downed" : pawn.CurJobDef != null ? pawn.CurJobDef.LabelCap.ToString() : "Idle";
                    Widgets.Label(new Rect(row.x + row.width * 0.60f, row.y, row.width * 0.37f, 25f), status);
                    Text.Anchor = TextAnchor.UpperLeft;
                    if (Widgets.ButtonInvisible(row) && pawn.Spawned)
                    {
                        Find.Selector.ClearSelection(); Find.Selector.Select(pawn); CameraJumper.TryJump(pawn);
                    }
                }
                y += sectionHeight + 6f;
            }
            Widgets.EndScrollView();
        }
    }

    [HarmonyPatch(typeof(StatsReportUtility), "StatsToDraw", new[] { typeof(Thing) })]
    public static class SpeciesKnowledgeStatsPatch
    {
        public static void Postfix(Thing thing, ref IEnumerable<StatDrawEntry> __result)
        {
            if (!HerdsMod.Settings.enableSpeciesKnowledgeProgression || thing is not Pawn pawn || pawn.RaceProps?.Animal != true || __result == null) return;
            __result = Filter(pawn.def, __result);
        }

        internal static IEnumerable<StatDrawEntry> Filter(ThingDef species, IEnumerable<StatDrawEntry> source)
        {
            if (species?.race?.Animal != true || source == null) return source;
            int level = HuntingKnowledgeMapComponent.ColonyLevel(species);
            List<StatDrawEntry> visible = source.Where(entry => entry.LabelCap != "Animal Knowledge" && RequiredLevel(entry) <= level).ToList();
            string tier = HuntingKnowledgeMapComponent.LevelLabel(level);
            string explanation = level >= 5
                ? "The colony has mastered this species; all known statistics are visible."
                : "Observe, track, wound, tend, or hunt this species to reveal additional statistics. Current colony knowledge: " + HuntingKnowledgeMapComponent.ColonyExperience(species).ToString("0") + " XP.";
            visible.Insert(0, new StatDrawEntry(StatCategoryDefOf.BasicsImportant, "Animal Knowledge", tier, explanation, 99999, "Animal Knowledge", null, false, true));
            return visible;
        }

        internal static int RequiredLevel(StatDrawEntry entry)
        {
            if (entry == null) return 5;
            StatCategoryDef category = entry.category;
            string name = entry.stat?.defName ?? entry.LabelCap ?? string.Empty;
            if (name.IndexOf("Leather", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Meat", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Wool", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Milk", StringComparison.OrdinalIgnoreCase) >= 0) return 4;
            if (name.Equals("Race", StringComparison.OrdinalIgnoreCase) || name.Equals("Sex", StringComparison.OrdinalIgnoreCase)) return 1;
            if (category == StatCategoryDefOf.Basics || category == StatCategoryDefOf.BasicsImportant || category == StatCategoryDefOf.BasicsPawn || category == StatCategoryDefOf.BasicsPawnImportant)
                return name.Contains("Market") || name.Contains("Beauty") ? 2 : 1;
            if (category == StatCategoryDefOf.PawnFood) return 2;
            if (category == StatCategoryDefOf.PawnCombat || name.Contains("Melee") || name.Contains("Armor") || name.Contains("Dodge")) return 3;
            if (category == StatCategoryDefOf.PawnHealth || name.Contains("Immunity") || name.Contains("Bleed") || name.Contains("Toxic")) return 3;
            if (category == StatCategoryDefOf.PawnResistances || category == StatCategoryDefOf.AnimalProductivity || name.Contains("Meat") || name.Contains("Leather") || name.Contains("Wool") || name.Contains("Milk")) return 4;
            if (category == StatCategoryDefOf.Animals)
            {
                if (name.Contains("Wildness") || name.Contains("Trainability") || name.Contains("BodySize") || name.Contains("MoveSpeed")) return 1;
                return 2;
            }
            if (category == StatCategoryDefOf.PawnMisc) return 2;
            return entry.stat == null ? 1 : 4;
        }
    }

    [HarmonyPatch(typeof(RaceProperties), nameof(RaceProperties.SpecialDisplayStats))]
    public static class SpeciesKnowledgeRaceStatsPatch
    {
        public static void Postfix(ThingDef parentDef, ref IEnumerable<StatDrawEntry> __result)
        {
            if (!HerdsMod.Settings.enableSpeciesKnowledgeProgression || parentDef?.race?.Animal != true || __result == null) return;
            int level = HuntingKnowledgeMapComponent.ColonyLevel(parentDef);
            __result = __result.Where(entry => SpeciesKnowledgeStatsPatch.RequiredLevel(entry) <= level).ToList();
        }
    }

    [HarmonyPatch(typeof(ThingDef), nameof(ThingDef.SpecialDisplayStats))]
    public static class SpeciesKnowledgeThingDefSpecialStatsPatch
    {
        public static void Postfix(ThingDef __instance, ref IEnumerable<StatDrawEntry> __result)
        {
            if (!HerdsMod.Settings.enableSpeciesKnowledgeProgression || __instance?.race?.Animal != true || __result == null) return;
            int level = HuntingKnowledgeMapComponent.ColonyLevel(__instance);
            __result = __result.Where(entry => SpeciesKnowledgeStatsPatch.RequiredLevel(entry) <= level).ToList();
        }
    }

    [HarmonyPatch(typeof(StatsReportUtility), "StatsToDraw", new[] { typeof(Def), typeof(ThingDef) })]
    public static class SpeciesKnowledgeDefStatsPatch
    {
        public static void Postfix(Def def, ref IEnumerable<StatDrawEntry> __result)
        {
            if (!HerdsMod.Settings.enableSpeciesKnowledgeProgression || def is not ThingDef species || species.race?.Animal != true || __result == null) return;
            __result = SpeciesKnowledgeStatsPatch.Filter(species, __result);
        }
    }

    [HarmonyPatch(typeof(MainTabWindow_PawnTable), "get_ExtraTopSpace")]
    public static class WildlifeMainTabKnowledgeSpacePatch
    {
        public static void Postfix(MainTabWindow_PawnTable __instance, ref float __result)
        {
            if (__instance is MainTabWindow_Wildlife) __result += WildlifeMenuRegistry.RequiredHeight();
        }
    }

    [HarmonyPatch(typeof(MainTabWindow_PawnTable), nameof(MainTabWindow_PawnTable.DoWindowContents))]
    public static class WildlifeMainTabKnowledgeButtonPatch
    {
        public static void Postfix(MainTabWindow_PawnTable __instance, Rect rect)
        {
            if (__instance is not MainTabWindow_Wildlife) return;
            WildlifeMenuRegistry.Draw(new Rect(rect.x + 4f, rect.y + 3f, rect.width - 8f,
                WildlifeMenuRegistry.RequiredHeight()));
        }
    }
}
