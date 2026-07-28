using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Herds
{
    public enum WildlifeSignKind { Tracks, Droppings, Browse, TerritoryMark, BloodTrail }
    public enum DomesticPredatorRole { None, HuntingCompanion, RanchGuardian, ColonyPatrol }

    public sealed class WildlifeSign : ThingWithComps
    {
        public ThingDef species;
        public Pawn sourceAnimal;
        public WildlifeSignKind signKind;
        public int createdTick;
        public IntVec3 travelFrom;
        public IntVec3 travelTo;
        public int groupSize = 1;
        public bool predator;
        public bool legendary;
        public string legendTitle;
        public List<Pawn> studiedBy = new List<Pawn>();

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref species, "species");
            Scribe_References.Look(ref sourceAnimal, "sourceAnimal");
            Scribe_Values.Look(ref signKind, "signKind");
            Scribe_Values.Look(ref createdTick, "createdTick");
            Scribe_Values.Look(ref travelFrom, "travelFrom");
            Scribe_Values.Look(ref travelTo, "travelTo");
            Scribe_Values.Look(ref groupSize, "groupSize", 1);
            Scribe_Values.Look(ref predator, "predator");
            Scribe_Values.Look(ref legendary, "legendary");
            Scribe_Values.Look(ref legendTitle, "legendTitle");
            Scribe_Collections.Look(ref studiedBy, "studiedBy", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) studiedBy = studiedBy ?? new List<Pawn>();
        }

        public override void TickRare()
        {
            base.TickRare();
            if (Find.TickManager.TicksGame - createdTick > 18000) Destroy(DestroyMode.Vanish);
        }

        public override string GetInspectString()
        {
            int skill = Map?.GetComponent<WildlifeFieldcraftMapComponent>()?.BestTrackerSkill ?? 0;
            int age = Mathf.Max(0, Find.TickManager.TicksGame - createdTick);
            string result = skill < 4 ? "Unidentified wildlife sign" : (species?.LabelCap.ToString() ?? "Unknown") + " " + SignLabel();
            result += "\nFreshness: " + (age < 2500 ? "fresh" : age < 7500 ? "recent" : "old");
            if (skill >= 7 && travelFrom != travelTo) result += "\nTravel: " + DirectionLabel(travelTo - travelFrom);
            if (skill >= 10) result += "\nAge: " + age.ToStringTicksToPeriod() + " | Group: about " + Mathf.Max(1, groupSize) + (predator ? " | Predator" : "");
            if (legendary) result += "\nLegendary sign: " + (legendTitle.NullOrEmpty() ? "Unknown legend" : legendTitle);
            WildlifeTrailLead trail = HerdsMod.Settings.enableTrailReading
                ? Map?.GetComponent<WildlifeTrailMapComponent>()?.LeadFor(species)
                : null;
            if (trail != null)
                result += "\nTrail: " + WildlifeTrailMapComponent.ConfidenceLabel(trail.confidence) +
                    " confidence | " + trail.direction + " | " +
                    WildlifeTrailMapComponent.StatusLabel(trail) +
                    (trail.marked ? " | Marked on map" : "");
            return result;
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos()) yield return gizmo;
            WildlifeTrailLead trail = HerdsMod.Settings.enableTrailReading
                ? Map?.GetComponent<WildlifeTrailMapComponent>()?.LeadFor(species)
                : null;
            if (trail != null)
                yield return new Command_Action
                {
                    defaultLabel = "Trail Map",
                    defaultDesc = "Open the reconstructed trail, review its evidence and confidence, or send a colonist to test its prediction.",
                    icon = TexCommand.GatherSpotActive,
                    action = () => Find.WindowStack.Add(new Window_WildlifeTrail(Map, trail))
                };
            if (!HerdsMod.Settings.enableSpeciesKnowledgeProgression || species == null) yield break;
            yield return new Command_Action
            {
                defaultLabel = "Study Wildlife", defaultDesc = "Choose a colonist to study this sign. They will gain animal knowledge and reconstruct a visible trail from nearby evidence. Each sign can be studied once per colonist.", icon = TexCommand.GatherSpotActive,
                action = ShowStudyMenu
            };
        }

        public void ShowStudyMenu()
        {
            if (Map == null) return;
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            foreach (Pawn pawn in Map.mapPawns.FreeColonistsSpawned.OrderByDescending(value =>
                value.skills?.GetSkill(SkillDefOf.Animals)?.Level ?? 0))
            {
                Pawn colonist = pawn;
                int skill = colonist.skills?.GetSkill(SkillDefOf.Animals)?.Level ?? 0;
                string label = colonist.LabelShortCap + " — Animals Skill " + skill;
                string unavailable = colonist.Downed ? "downed" :
                    colonist.InMentalState ? "in a mental state" :
                    studiedBy.Contains(colonist) ? "already studied this sign" :
                    !colonist.CanReserveAndReach(this, PathEndMode.Touch, Danger.Some)
                        ? "cannot reach the sign" : null;
                if (unavailable != null)
                {
                    options.Add(new FloatMenuOption(label + " (" + unavailable + ")", null));
                    continue;
                }
                options.Add(new FloatMenuOption(label, () =>
                {
                    Job study = JobMaker.MakeJob(HerdsDefOf.Herds_StudyWildlifeSign, this);
                    study.playerForced = true;
                    if (colonist.jobs.TryTakeOrderedJob(study, JobTag.Misc))
                        CameraJumper.TryJumpAndSelect(this);
                    else
                        Messages.Message(colonist.LabelShortCap +
                            " could not begin studying the wildlife sign.", colonist,
                            MessageTypeDefOf.RejectInput, false);
                }));
            }
            if (options.Count == 0)
                options.Add(new FloatMenuOption("No colonists are available.", null));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        public override void DrawExtraSelectionOverlays()
        {
            base.DrawExtraSelectionOverlays();
            if (HerdsMod.Settings.enableTrailReading)
                Map?.GetComponent<WildlifeTrailMapComponent>()?.DrawSelectedTrail(this);
        }

        private string SignLabel() => signKind == WildlifeSignKind.Tracks ? "tracks" : signKind == WildlifeSignKind.Droppings ? "droppings" : signKind == WildlifeSignKind.Browse ? "feeding sign" : signKind == WildlifeSignKind.BloodTrail ? "blood trail" : "territory mark";

        private static string DirectionLabel(IntVec3 delta)
        {
            string vertical = delta.z > 1 ? "north" : delta.z < -1 ? "south" : "";
            string horizontal = delta.x > 1 ? "east" : delta.x < -1 ? "west" : "";
            return vertical + horizontal;
        }
    }

    public sealed class JobDriver_StudyWildlifeSign : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) =>
            pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            int proficiency = pawn.Map?.GetComponent<HuntingKnowledgeMapComponent>()?.WildlifeProficiencyLevel(pawn) ?? 0;
            Toil inspect = Toils_General.Wait(Mathf.RoundToInt(600f * (1f - proficiency * 0.08f)), TargetIndex.A);
            inspect.socialMode = RandomSocialMode.Off;
            inspect.WithProgressBarToilDelay(TargetIndex.A);
            yield return inspect;
            Toil finish = ToilMaker.MakeToil("StudyWildlifeSign");
            finish.initAction = () =>
            {
                WildlifeSign sign = job.targetA.Thing as WildlifeSign;
                if (sign?.Spawned != true || sign.species == null || sign.studiedBy.Contains(pawn)) return;
                sign.studiedBy.Add(pawn);
                sign.Map.GetComponent<HuntingKnowledgeMapComponent>()?.Learn(pawn, sign.species,
                    sign.signKind == WildlifeSignKind.BloodTrail ? 22f : 12f);
                WildlifeTrailLead lead = HerdsMod.Settings.enableTrailReading
                    ? sign.Map.GetComponent<WildlifeTrailMapComponent>()?.Analyze(sign, pawn)
                    : null;
                sign.Map.GetComponent<HuntingKnowledgeMapComponent>()?.LearnBiome(pawn,
                    sign.Map.Biome, sign.signKind == WildlifeSignKind.BloodTrail ? 12f : 7f);
                bool huntOpportunity = lead != null && sign.Map
                    .GetComponent<HuntingExpeditionMapComponent>()?
                    .TryCreateTrailHuntOpportunity(pawn, sign.species, lead.confidence) == true;
                string result = pawn.LabelShortCap + " studied signs of " + sign.species.LabelCap + ".";
                if (lead != null)
                    result += " A " + WildlifeTrailMapComponent.ConfidenceLabel(lead.confidence).ToLowerInvariant() +
                        " trail now points " + lead.direction.ToLowerInvariant() + ".";
                if (huntOpportunity) result += " The trail opened a time-sensitive expedition hunt opportunity.";
                WildlifeExperience.Record("Trail Study", pawn.LabelShortCap + " gained " +
                    sign.species.label + " and " + sign.Map.Biome.label + " field experience.", sign);
                Messages.Message(result, sign, MessageTypeDefOf.PositiveEvent, false);
                sign.Map.GetComponent<WildlifeFieldJournalMapComponent>()?
                    .CompleteMomentTracking(pawn, sign);
            };
            finish.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return finish;
        }
    }

    public sealed class WildlifeCallRecord : IExposable
    {
        public ThingDef species;
        public IntVec3 cell;
        public Pawn target;
        public Pawn caller;
        public int knowledgeLevel;
        public int expiresTick;
        public void ExposeData() { Scribe_Defs.Look(ref species, "species"); Scribe_Values.Look(ref cell, "cell"); Scribe_References.Look(ref target, "target"); Scribe_References.Look(ref caller, "caller"); Scribe_Values.Look(ref knowledgeLevel, "knowledgeLevel"); Scribe_Values.Look(ref expiresTick, "expiresTick"); }
    }

    public sealed class WildlifeFieldcraftMapComponent : MapComponent
    {
        private const int MaxSigns = 180;
        private Dictionary<Pawn, int> scentMaskUntil = new Dictionary<Pawn, int>();
        private Dictionary<Pawn, IntVec3> guardianAnchors = new Dictionary<Pawn, IntVec3>();
        private Dictionary<Pawn, int> guardianRadii = new Dictionary<Pawn, int>();
        private Dictionary<Pawn, DomesticPredatorRole> domesticPredatorRoles = new Dictionary<Pawn, DomesticPredatorRole>();
        private Dictionary<Pawn, float> domesticRoleExperience = new Dictionary<Pawn, float>();
        private List<WildlifeCallRecord> calls = new List<WildlifeCallRecord>();
        private readonly Dictionary<Pawn, IntVec3> lastAnimalCells = new Dictionary<Pawn, IntVec3>();
        private readonly List<Pawn> guardiansScratch = new List<Pawn>();
        private int nextSignTick;
        private int nextGuardianTick;
        private int nextSkillTick;
        private int bestTrackerSkill;

        public int BestTrackerSkill => bestTrackerSkill;

        public WildlifeFieldcraftMapComponent(Map map) : base(map) { }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref scentMaskUntil, "scentMaskUntil", LookMode.Reference, LookMode.Value);
            Scribe_Collections.Look(ref guardianAnchors, "guardianAnchors", LookMode.Reference, LookMode.Value);
            Scribe_Collections.Look(ref guardianRadii, "guardianRadii", LookMode.Reference, LookMode.Value);
            Scribe_Collections.Look(ref domesticPredatorRoles, "domesticPredatorRoles", LookMode.Reference, LookMode.Value);
            Scribe_Collections.Look(ref domesticRoleExperience, "domesticRoleExperience", LookMode.Reference, LookMode.Value);
            Scribe_Collections.Look(ref calls, "wildlifeCalls", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                scentMaskUntil = scentMaskUntil ?? new Dictionary<Pawn, int>();
                guardianAnchors = guardianAnchors ?? new Dictionary<Pawn, IntVec3>();
                guardianRadii = guardianRadii ?? new Dictionary<Pawn, int>();
                domesticPredatorRoles = domesticPredatorRoles ?? new Dictionary<Pawn, DomesticPredatorRole>();
                domesticRoleExperience = domesticRoleExperience ?? new Dictionary<Pawn, float>();
                calls = calls ?? new List<WildlifeCallRecord>();
            }
        }

        public override void MapComponentTick()
        {
            if (!HerdsMod.Settings.enableTrackingSigns && !HerdsMod.Settings.enableWindHud && !HerdsMod.Settings.enableScentMasking && !HerdsMod.Settings.enableAnimalCalls && !HerdsMod.Settings.enableRanchGuardians && !HerdsMod.Settings.enableDomesticPredatorRoles && !HerdsMod.Settings.enableUncertainPredatorWarnings) return;
            int now = Find.TickManager.TicksGame;
            if (now >= nextSkillTick) RefreshTrackerSkill(now);
            if (HerdsMod.Settings.enableTrackingSigns && now >= nextSignTick) UpdateSigns(now);
            if (WildlifeProgression.Unlocked(WildlifeCapability.AnimalHandling) && HerdsMod.Settings.enableRanchGuardians && now >= nextGuardianTick) UpdateGuardians(now);
            else if (WildlifeProgression.Unlocked(WildlifeCapability.AnimalHandling) && HerdsMod.Settings.enableDomesticPredatorRoles && now >= nextGuardianTick) UpdateGuardians(now);
            if (now % 600 == 0)
            {
                calls.RemoveAll(call => call == null || call.expiresTick <= now);
                foreach (Pawn pawn in scentMaskUntil.Where(pair => pair.Key == null || pair.Key.Dead || pair.Value <= now).Select(pair => pair.Key).ToList()) scentMaskUntil.Remove(pawn);
            }
        }

        public override void MapComponentOnGUI()
        {
            base.MapComponentOnGUI();
            if (Find.CurrentMap != map) return;
            if (HerdsMod.Settings.enableWindHud && Find.Selector.SingleSelectedThing is Pawn pawn && pawn.Faction == Faction.OfPlayer)
            {
                Vector2 wind = WindVector(map);
                string direction = Mathf.Abs(wind.x) > Mathf.Abs(wind.y) ? (wind.x > 0 ? "E" : "W") : (wind.y > 0 ? "N" : "S");
                Rect panel = new Rect(UI.screenWidth - 190f, 142f, 178f, 52f);
                Widgets.DrawMenuSection(panel);
                Widgets.Label(panel.ContractedBy(7f), "Wind: " + direction + "\nScent: " + (IsScentMasked(pawn) ? "masked" : "exposed"));
            }
            if (Prefs.DevMode && FieldcraftDebug.GuardianOverlay)
                foreach (KeyValuePair<Pawn, IntVec3> pair in guardianAnchors) if (pair.Key?.Spawned == true) GenMapUI.DrawThingLabel(pair.Key, RoleLabel(DomesticRole(pair.Key)) + " | radius " + GuardianRadius(pair.Key) + " | anchor " + pair.Value);
        }

        public override void MapComponentDraw()
        {
            base.MapComponentDraw();
            if (!Prefs.DevMode || Find.CurrentMap != map || (!FieldcraftDebug.SignOverlay && !FieldcraftDebug.GuardianOverlay)) return;
            if (FieldcraftDebug.SignOverlay && HerdsDefOf.Herds_WildlifeSign != null)
            {
                List<Thing> signs = map.listerThings.ThingsOfDef(HerdsDefOf.Herds_WildlifeSign);
                for (int i = 0; i < signs.Count; i++) if (signs[i] is WildlifeSign sign)
                {
                    Color color = sign.signKind == WildlifeSignKind.BloodTrail ? Color.red : sign.predator ? new Color(1f, 0.45f, 0.1f) : Color.cyan;
                    GenDraw.DrawRadiusRing(sign.Position, 0.45f, color);
                    if (sign.travelFrom != sign.travelTo) GenDraw.DrawLineBetween(sign.travelFrom.ToVector3Shifted(), sign.travelTo.ToVector3Shifted(), sign.predator ? SimpleColor.Red : SimpleColor.Cyan);
                }
            }
            if (FieldcraftDebug.GuardianOverlay)
                foreach (KeyValuePair<Pawn, IntVec3> pair in guardianAnchors) if (pair.Key?.Spawned == true) { GenDraw.DrawRadiusRing(pair.Value, GuardianRadius(pair.Key), Color.yellow); GenDraw.DrawLineBetween(pair.Key.Position.ToVector3Shifted(), pair.Value.ToVector3Shifted(), SimpleColor.Yellow); }
        }

        public static Vector2 WindVector(Map targetMap)
        {
            float angle = PositiveMod(targetMap.uniqueID * 47 + Find.TickManager.TicksGame / 2500 * 29, 360) * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        public bool IsScentMasked(Pawn pawn) => HerdsMod.Settings.enableScentMasking && pawn != null && scentMaskUntil.TryGetValue(pawn, out int until) && until > Find.TickManager.TicksGame;

        public void ApplyScentMask(Pawn pawn)
        {
            if (pawn?.Spawned != true || pawn.Faction != Faction.OfPlayer) return;
            scentMaskUntil[pawn] = Find.TickManager.TicksGame + 15000;
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("ScentMask", "until=" + scentMaskUntil[pawn], pawn);
            Messages.Message(pawn.LabelShortCap + " applied scent masking for six hours.", pawn, MessageTypeDefOf.PositiveEvent, false);
        }

        public int AnimalCallKnowledge(Pawn caller, ThingDef species)
        {
            HuntingKnowledgeMapComponent knowledge = map.GetComponent<HuntingKnowledgeMapComponent>();
            return HerdsMod.Settings.enableSpeciesKnowledgeProgression
                ? knowledge?.Level(caller, species) ?? 0
                : Mathf.Clamp((caller.skills?.GetSkill(SkillDefOf.Animals)?.Level ?? 0) / 4, 0, 5);
        }

        public float AnimalCallChance(int knowledgeLevel, Pawn caller = null)
        {
            int proficiency = map.GetComponent<HuntingKnowledgeMapComponent>()?.WildlifeProficiencyLevel(caller) ?? 0;
            float role = WildlifeRoleUtility.IsMasterConservationist(caller) ? 0.10f :
                WildlifeRoleUtility.IsMasterHunter(caller) ? 0.04f : 0f;
            return Mathf.Clamp(0.08f + knowledgeLevel * 0.17f + proficiency * 0.04f + role, 0.08f, 0.97f);
        }

        public float AnimalCallDistance(int knowledgeLevel, Pawn caller = null)
        {
            int proficiency = map.GetComponent<HuntingKnowledgeMapComponent>()?.WildlifeProficiencyLevel(caller) ?? 0;
            return 8f + knowledgeLevel * 10f + proficiency * 3f;
        }

        public bool TryAnimalCall(ThingDef species, Building_WildlifeTool source, Pawn caller)
        {
            if (!HerdsMod.Settings.enableAnimalCalls || !WildlifeProgression.Unlocked(WildlifeCapability.AnimalHandling) || species?.race?.Animal != true || source?.Spawned != true || caller?.Spawned != true || source.ManningColonist() != caller) return false;
            List<Pawn> candidates = map.mapPawns.AllPawnsSpawned.Where(pawn => pawn?.Spawned == true && !pawn.Dead && pawn.Faction != Faction.OfPlayer && pawn.def == species && PreyProfileDatabase.IsEligible(pawn.def)).ToList();
            if (candidates.Count == 0) { Messages.Message("No " + species.LabelCap + " remain available to answer the call.", source, MessageTypeDefOf.RejectInput, false); return false; }
            int level = AnimalCallKnowledge(caller, species);
            float chance = AnimalCallChance(level, caller);
            WildlifeSignalCultureMapComponent signalCulture =
                map.GetComponent<WildlifeSignalCultureMapComponent>();
            chance = Mathf.Clamp01(chance * (signalCulture?.PlayerImitationFactor(caller, species) ?? 1f));
            if (HerdsMod.Settings.enableAnimalMemory)
            {
                WildlifeMemoryMapComponent memory = map.GetComponent<WildlifeMemoryMapComponent>();
                float rememberedTrust = candidates.Average(target => memory?.TrustFor(target, caller) ?? 0f);
                float rememberedFear = candidates.Average(target => memory?.FearFor(target, caller) ?? 0f);
                chance = Mathf.Clamp01(chance + rememberedTrust * 0.16f - rememberedFear * 0.10f);
            }
            if (!Rand.Chance(chance))
            {
                Pawn target = candidates.RandomElement();
                map.GetComponent<HerdMapComponent>()?.NotifyThreat(target, source, 600);
                string outcome = caller.LabelShortCap + "'s call was unconvincing and alarmed nearby wildlife.";
                Messages.Message(outcome, target, MessageTypeDefOf.NegativeEvent, false);
                WildlifeExperience.Record("Animal Call", outcome, target, true);
                WildlifeMemoryUtility.Remember(target, caller, AnimalMemoryKind.Wounded, 0.35f);
                signalCulture?.NotifyPlayerImitation(species, WildlifeSignalKind.Contact, caller,
                    source.Position, false, false);
                if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("AnimalCall", "result=failure caller=" + caller.thingIDNumber + " knowledgeLevel=" + level + " chance=" + chance.ToString("0.00"), target, source);
                return false;
            }
            int maximumResponders = Mathf.Min(candidates.Count, 1 + level / 2);
            int responderCount = Rand.RangeInclusive(1, Mathf.Max(1, maximumResponders));
            List<Pawn> responders = candidates.InRandomOrder().Take(responderCount).ToList();
            float travelDistance = AnimalCallDistance(level, caller);
            calls.RemoveAll(call => call.species == species);
            for (int i = 0; i < responders.Count; i++)
            {
                Pawn target = responders[i];
                WildlifeMemoryMapComponent memory = map.GetComponent<WildlifeMemoryMapComponent>();
                float rememberedResponse = 1f + (memory?.TrustFor(target, caller) ?? 0f) * 0.35f -
                    (memory?.FearFor(target, caller) ?? 0f) * 0.20f;
                Vector2 towardPost = new Vector2(source.Position.x - target.Position.x, source.Position.z - target.Position.z);
                float actualDistance = Mathf.Min(travelDistance * rememberedResponse, towardPost.magnitude);
                Vector2 direction = towardPost.sqrMagnitude > 0.01f ? towardPost.normalized : Vector2.zero;
                IntVec3 destination = target.Position + new IntVec3(Mathf.RoundToInt(direction.x * actualDistance), 0, Mathf.RoundToInt(direction.y * actualDistance));
                calls.Add(new WildlifeCallRecord { species = species, cell = destination.ClampInsideMap(map), target = target, caller = caller, knowledgeLevel = level, expiresTick = Find.TickManager.TicksGame + 5000 });
                WildlifeMemoryUtility.Remember(target, caller, AnimalMemoryKind.Called, 0.7f);
                if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("AnimalCallResponder", "caller=" + caller.thingIDNumber + " knowledgeLevel=" + level + " travel=" + actualDistance.ToString("0.0") + " destination=" + destination, target, source);
            }
            string result = responders.Count + " " + species.LabelCap + (responders.Count == 1 ? " has" : " have") + " responded to " + caller.LabelShortCap + "'s call.";
            Messages.Message(result, responders[0], MessageTypeDefOf.PositiveEvent, false);
            WildlifeExperience.Record("Animal Call", result, responders[0]);
            WildlifeIdeologyUtility.Notify(map, WildlifeIdeologyEvent.SuccessfulCall, responders[0], caller);
            signalCulture?.NotifyPlayerImitation(species, WildlifeSignalKind.Contact, caller,
                source.Position, true, true);
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("AnimalCall", "result=success caller=" + caller.thingIDNumber + " species=" + species.defName + " knowledgeLevel=" + level + " chance=" + chance.ToString("0.00") + " responders=" + responders.Count + " maxTravel=" + travelDistance.ToString("0") + " expires=" + calls[0].expiresTick, responders[0], source);
            return true;
        }

        public IntVec3 ActiveCallFor(Pawn animal, IntVec3 origin)
        {
            if (!HerdsMod.Settings.enableAnimalCalls || animal == null) return IntVec3.Invalid;
            int now = Find.TickManager.TicksGame;
            WildlifeCallRecord call = calls.FirstOrDefault(record => record.species == animal.def && record.expiresTick > now && record.target != null && (record.target == animal || record.target.Position.DistanceToSquared(animal.Position) <= 900));
            return call?.cell ?? IntVec3.Invalid;
        }

        public WildlifeSign DebugCreateSign(Pawn animal)
        {
            if (animal?.Spawned != true || animal.Map != map) return null;
            WildlifeSign sign = (WildlifeSign)ThingMaker.MakeThing(HerdsDefOf.Herds_WildlifeSign);
            sign.species = animal.def; sign.createdTick = Find.TickManager.TicksGame; sign.travelFrom = animal.Position - IntVec3.North; sign.travelTo = animal.Position;
            sign.sourceAnimal = animal;
            sign.predator = animal.RaceProps.predator; sign.groupSize = map.GetComponent<HerdMapComponent>()?.HerdFor(animal)?.members.Count ?? 1;
            sign.signKind = animal.health.summaryHealth.SummaryHealthPercent < 0.8f ? WildlifeSignKind.BloodTrail : animal.RaceProps.predator ? WildlifeSignKind.TerritoryMark : WildlifeSignKind.Tracks;
            GenSpawn.Spawn(sign, animal.Position, map);
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("DevWildlifeSign", "kind=" + sign.signKind + " species=" + animal.def.defName, animal, sign);
            return sign;
        }

        public bool CanSafelyTrack(Pawn animal, Pawn tracker)
        {
            if (animal?.Spawned != true || tracker?.Spawned != true ||
                animal.Map != map || tracker.Map != map) return false;
            WildlifeSign existing = SafeExistingSign(animal, tracker);
            return existing != null || TryFindSafeTrackingCell(animal, tracker, out _);
        }

        public WildlifeSign CreateSafeTrackingSign(Pawn animal, Pawn tracker)
        {
            if (!CanSafelyTrack(animal, tracker)) return null;
            WildlifeSign existing = SafeExistingSign(animal, tracker);
            if (existing != null) return existing;
            if (!TryFindSafeTrackingCell(animal, tracker, out IntVec3 cell)) return null;
            Vector2 movement = new Vector2(animal.Position.x - cell.x,
                animal.Position.z - cell.z).normalized;
            IntVec3 from = cell - new IntVec3(
                Mathf.RoundToInt(movement.x * 4f), 0,
                Mathf.RoundToInt(movement.y * 4f));
            WildlifeSign sign = (WildlifeSign)ThingMaker.MakeThing(
                HerdsDefOf.Herds_WildlifeSign);
            sign.species = animal.def;
            sign.sourceAnimal = animal;
            sign.createdTick = Find.TickManager.TicksGame;
            sign.travelFrom = from.ClampInsideMap(map);
            sign.travelTo = cell;
            sign.predator = animal.RaceProps.predator;
            sign.groupSize = map.GetComponent<HerdMapComponent>()?
                .HerdFor(animal)?.members.Count ?? 1;
            sign.signKind = animal.health.summaryHealth.SummaryHealthPercent < 0.8f
                ? WildlifeSignKind.BloodTrail : WildlifeSignKind.Tracks;
            GenSpawn.Spawn(sign, cell, map);
            if (WildlifeTestLog.Enabled)
                WildlifeTestLog.Write("SafeTrackingSign",
                    "distance=" + cell.DistanceTo(animal.Position).ToString("0.0") +
                    " tracker=" + tracker.thingIDNumber, animal, sign);
            return sign;
        }

        private WildlifeSign SafeExistingSign(Pawn animal, Pawn tracker)
        {
            int now = Find.TickManager.TicksGame;
            return map.listerThings.ThingsOfDef(HerdsDefOf.Herds_WildlifeSign)
                .OfType<WildlifeSign>().Where(sign =>
                    sign?.Spawned == true && sign.species == animal.def &&
                    (sign.sourceAnimal == null || sign.sourceAnimal == animal) &&
                    now - sign.createdTick <= 12000 &&
                    sign.Position.DistanceToSquared(animal.Position) >= 1024 &&
                    sign.Position.DistanceToSquared(animal.Position) <= 6400 &&
                    tracker.CanReserveAndReach(sign, PathEndMode.Touch, Danger.Some))
                .OrderByDescending(sign => sign.createdTick).FirstOrDefault();
        }

        private bool TryFindSafeTrackingCell(Pawn animal, Pawn tracker, out IntVec3 result)
        {
            result = IntVec3.Invalid;
            Vector2 towardTracker = new Vector2(tracker.Position.x - animal.Position.x,
                tracker.Position.z - animal.Position.z);
            if (towardTracker.sqrMagnitude < 0.01f) towardTracker = Vector2.up;
            towardTracker.Normalize();
            IntVec3 desired = animal.Position + new IntVec3(
                Mathf.RoundToInt(towardTracker.x * 36f), 0,
                Mathf.RoundToInt(towardTracker.y * 36f));
            float best = float.MaxValue;
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(
                desired.ClampInsideMap(map), 7f, true))
            {
                float animalDistance = cell.DistanceToSquared(animal.Position);
                if (!cell.InBounds(map) || !cell.Standable(map) ||
                    animalDistance < 1024f || animalDistance > 2025f ||
                    !tracker.CanReach(cell, PathEndMode.OnCell, Danger.Some)) continue;
                Vector2 side = new Vector2(cell.x - animal.Position.x,
                    cell.z - animal.Position.z).normalized;
                if (Vector2.Dot(side, towardTracker) < 0.55f) continue;
                float score = cell.DistanceToSquared(tracker.Position);
                if (score >= best) continue;
                best = score;
                result = cell;
            }
            return result.IsValid;
        }

        public void ToggleGuardian(Pawn pawn)
        {
            SetDomesticRole(pawn, IsGuardian(pawn) ? DomesticPredatorRole.None : DomesticPredatorRole.RanchGuardian);
        }

        public DomesticPredatorRole DomesticRole(Pawn pawn) => pawn != null && domesticPredatorRoles.TryGetValue(pawn, out DomesticPredatorRole role) ? role : guardianAnchors.ContainsKey(pawn) ? DomesticPredatorRole.RanchGuardian : DomesticPredatorRole.None;
        public float DomesticExperience(Pawn pawn) => pawn != null && domesticRoleExperience.TryGetValue(pawn, out float experience) ? Mathf.Clamp01(experience) : 0f;
        public string DomesticLevel(Pawn pawn) { float value = DomesticExperience(pawn); return value < 0.25f ? "Novice" : value < 0.55f ? "Trained" : value < 0.85f ? "Skilled" : "Veteran"; }
        private void GainDomesticExperience(Pawn pawn, float amount)
        {
            if (!HerdsMod.Settings.enableDomesticRoleProgression || pawn == null || amount <= 0f) return;
            domesticRoleExperience[pawn] = Mathf.Clamp01(DomesticExperience(pawn) + amount);
        }
        public bool IsGuardian(Pawn pawn) => DomesticRole(pawn) == DomesticPredatorRole.RanchGuardian;

        public void SetDomesticRole(Pawn pawn, DomesticPredatorRole role)
        {
            if (pawn?.Spawned != true || pawn.Faction != Faction.OfPlayer || pawn.RaceProps?.predator != true) return;
            domesticPredatorRoles.Remove(pawn);
            guardianAnchors.Remove(pawn);
            guardianRadii.Remove(pawn);
            Hediff existing = pawn.health?.hediffSet?.GetFirstHediffOfDef(HerdsDefOf.Herds_RanchGuardian);
            if (existing != null) pawn.health.RemoveHediff(existing);
            if (role != DomesticPredatorRole.None) domesticPredatorRoles[pawn] = role;
            if (role == DomesticPredatorRole.RanchGuardian || role == DomesticPredatorRole.ColonyPatrol)
            {
                guardianAnchors[pawn] = pawn.Position; guardianRadii[pawn] = 24;
            }
            if (role == DomesticPredatorRole.RanchGuardian && pawn.health?.hediffSet?.GetFirstHediffOfDef(HerdsDefOf.Herds_RanchGuardian) == null) pawn.health?.AddHediff(HerdsDefOf.Herds_RanchGuardian);
            Messages.Message(pawn.LabelShortCap + " role: " + RoleLabel(role) + ".", pawn, MessageTypeDefOf.NeutralEvent, false);
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("DomesticPredatorRole", "role=" + role + " anchor=" + pawn.Position, pawn);
        }

        public static string RoleLabel(DomesticPredatorRole role) => role == DomesticPredatorRole.HuntingCompanion ? "Hunting companion" : role == DomesticPredatorRole.RanchGuardian ? "Ranch guardian" : role == DomesticPredatorRole.ColonyPatrol ? "Colony patrol" : "None";

        public void SetGuardianAnchor(Pawn pawn, IntVec3 cell) { if (IsGuardian(pawn) && cell.InBounds(map)) guardianAnchors[pawn] = cell; }
        public void CycleGuardianRadius(Pawn pawn)
        {
            if (!IsGuardian(pawn)) return;
            int current = guardianRadii.TryGetValue(pawn, out int radius) ? radius : 24;
            guardianRadii[pawn] = current == 12 ? 24 : current == 24 ? 40 : 12;
        }
        public int GuardianRadius(Pawn pawn) => guardianRadii.TryGetValue(pawn, out int radius) ? radius : 24;
        public IntVec3 GuardianAnchor(Pawn pawn) => guardianAnchors.TryGetValue(pawn, out IntVec3 anchor) ? anchor : IntVec3.Invalid;

        public List<string> DebugOverviewLines()
        {
            List<string> lines = new List<string>
            {
                "FIELDCRAFT bestTrackerSkill=" + bestTrackerSkill + " calls=" + calls.Count + " scentMasked=" + scentMaskUntil.Count + " domesticRoles=" + domesticPredatorRoles.Count,
                "WIND vector=" + WindVector(map)
            };
            foreach (KeyValuePair<Pawn, DomesticPredatorRole> pair in domesticPredatorRoles) lines.Add("DOMESTIC " + (pair.Key?.LabelShortCap.ToString() ?? "missing") + " | role=" + pair.Value + " anchor=" + GuardianAnchor(pair.Key) + " radius=" + GuardianRadius(pair.Key) + " | job=" + (pair.Key?.CurJobDef?.defName ?? "none"));
            for (int i = 0; i < calls.Count; i++) lines.Add("CALL species=" + (calls[i].species?.LabelCap.ToString() ?? "missing") + " cell=" + calls[i].cell + " expires=" + calls[i].expiresTick);
            return lines;
        }

        private void RefreshTrackerSkill(int now)
        {
            nextSkillTick = now + 600;
            bestTrackerSkill = 0;
            IReadOnlyList<Pawn> pawns = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < pawns.Count; i++) bestTrackerSkill = Mathf.Max(bestTrackerSkill, pawns[i].skills?.GetSkill(SkillDefOf.Animals)?.Level ?? 0);
        }

        private void UpdateSigns(int now)
        {
            nextSignTick = now + 300;
            List<Thing> signs = map.listerThings.ThingsOfDef(HerdsDefOf.Herds_WildlifeSign);
            if (signs.Count >= MaxSigns) return;
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            int additions = Mathf.Min(6, MaxSigns - signs.Count);
            for (int i = PositiveMod(now / 300, 7); i < pawns.Count && additions > 0; i += 7)
            {
                Pawn pawn = pawns[i];
                if (pawn?.Spawned != true || pawn.Dead || pawn.RaceProps?.Animal != true || pawn.Faction == Faction.OfPlayer) continue;
                if (!lastAnimalCells.TryGetValue(pawn, out IntVec3 old)) { lastAnimalCells[pawn] = pawn.Position; continue; }
                lastAnimalCells[pawn] = pawn.Position;
                if (old.DistanceToSquared(pawn.Position) < 64 || pawn.Position.GetFirstThing(map, HerdsDefOf.Herds_WildlifeSign) != null) continue;
                WildlifeSign sign = (WildlifeSign)ThingMaker.MakeThing(HerdsDefOf.Herds_WildlifeSign);
                sign.species = pawn.def;
                sign.sourceAnimal = pawn;
                sign.createdTick = now;
                sign.travelFrom = old;
                sign.travelTo = pawn.Position;
                sign.predator = pawn.RaceProps.predator;
                sign.signKind = HerdsMod.Settings.enableWoundedTrackingAndRetreat && pawn.health.summaryHealth.SummaryHealthPercent < 0.8f ? WildlifeSignKind.BloodTrail : HerdsMod.Settings.enableTerritorialSigns && sign.predator && PositiveMod(pawn.thingIDNumber + now / 300, 4) == 0 ? WildlifeSignKind.TerritoryMark : (WildlifeSignKind)PositiveMod(pawn.thingIDNumber + now / 300, 3);
                HerdSnapshot herd = map.GetComponent<HerdMapComponent>()?.HerdFor(pawn);
                sign.groupSize = herd?.members.Count ?? 1;
                GenSpawn.Spawn(sign, pawn.Position, map);
                additions--;
            }
            foreach (Pawn stale in lastAnimalCells.Keys.Where(pawn => pawn == null || pawn.Dead || !pawn.Spawned).Take(16).ToList()) lastAnimalCells.Remove(stale);
        }

        private void UpdateGuardians(int now)
        {
            nextGuardianTick = now + 120;
            guardiansScratch.Clear();
            foreach (Pawn pawn in guardianAnchors.Keys) if (pawn?.Spawned == true && !pawn.Dead && pawn.Faction == Faction.OfPlayer) guardiansScratch.Add(pawn);
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < guardiansScratch.Count; i++)
            {
                Pawn guardian = guardiansScratch[i];
                DomesticPredatorRole role = DomesticRole(guardian);
                if ((role == DomesticPredatorRole.RanchGuardian && !HerdsMod.Settings.enableRanchGuardians) || (role != DomesticPredatorRole.RanchGuardian && !HerdsMod.Settings.enableDomesticPredatorRoles)) continue;
                IntVec3 anchor = guardianAnchors[guardian];
                int patrolRadius = GuardianRadius(guardian);
                if (HerdsMod.Settings.enableDomesticRoleProgression) patrolRadius += Mathf.RoundToInt(DomesticExperience(guardian) * 8f);
                Pawn threat = null;
                float best = patrolRadius * patrolRadius;
                for (int j = 0; j < pawns.Count; j++)
                {
                    Pawn candidate = pawns[j];
                    if (candidate?.Spawned != true || candidate.Dead || candidate.Faction == Faction.OfPlayer) continue;
                    bool validThreat = role == DomesticPredatorRole.RanchGuardian ? candidate.RaceProps.predator : role == DomesticPredatorRole.ColonyPatrol && candidate.Faction?.HostileTo(Faction.OfPlayer) == true;
                    if (!validThreat) continue;
                    float distance = candidate.Position.DistanceToSquared(anchor);
                    if (distance < best) { best = distance; threat = candidate; }
                }
                bool mayEngage = role == DomesticPredatorRole.ColonyPatrol || role == DomesticPredatorRole.RanchGuardian && HerdsMod.Settings.guardiansAttackPredators;
                if (mayEngage && threat != null && guardian.Position.DistanceToSquared(threat.Position) <= 225 && !guardian.WorkTagIsDisabled(WorkTags.Violent) && (guardian.CurJobDef == null || guardian.CurJobDef == JobDefOf.Wait_Wander))
                {
                    Job attack = JobMaker.MakeJob(JobDefOf.AttackMelee, threat);
                    attack.maxNumMeleeAttacks = 1; attack.expiryInterval = 180;
                    guardian.jobs.TryTakeOrderedJob(attack, JobTag.Misc);
                    GainDomesticExperience(guardian, 0.018f);
                }
                else if (threat == null && PositiveMod(now / 120 + guardian.thingIDNumber, 5) == 0 && (guardian.CurJobDef == null || guardian.CurJobDef == JobDefOf.Wait_Wander))
                {
                    float angle = PositiveMod(guardian.thingIDNumber * 37 + now / 120, 360) * Mathf.Deg2Rad;
                    IntVec3 desired = anchor + new IntVec3(Mathf.RoundToInt(Mathf.Cos(angle) * patrolRadius * 0.7f), 0, Mathf.RoundToInt(Mathf.Sin(angle) * patrolRadius * 0.7f));
                    IntVec3 patrolCell = CellFinder.RandomClosewalkCellNear(desired.ClampInsideMap(map), map, 5);
                    Job patrol = JobMaker.MakeJob(JobDefOf.Goto, patrolCell); patrol.expiryInterval = 300;
                    guardian.jobs.TryTakeOrderedJob(patrol, JobTag.Misc);
                    GainDomesticExperience(guardian, 0.0025f);
                }
            }
            if (!HerdsMod.Settings.enableDomesticPredatorRoles) return;
            foreach (KeyValuePair<Pawn, DomesticPredatorRole> pair in domesticPredatorRoles.ToList())
            {
                Pawn companion = pair.Key;
                if (pair.Value != DomesticPredatorRole.HuntingCompanion || companion?.Spawned != true || companion.Downed || companion.InMentalState || (companion.CurJobDef != null && companion.CurJobDef != JobDefOf.Wait_Wander && companion.CurJobDef != JobDefOf.GotoWander)) continue;
                Pawn hunter = map.mapPawns.FreeColonistsSpawned.Where(colonist => colonist.Drafted || colonist.CurJobDef == JobDefOf.Hunt || colonist.CurJobDef == HerdsDefOf.Herds_FieldcraftHunt).OrderBy(colonist => colonist.Position.DistanceToSquared(companion.Position)).FirstOrDefault();
                if (hunter == null) continue;
                Pawn quarry = hunter.CurJob?.targetA.Thing as Pawn;
                float assistRange = 144f + (HerdsMod.Settings.enableDomesticRoleProgression ? DomesticExperience(companion) * 225f : 0f);
                if ((hunter.CurJobDef == JobDefOf.Hunt || hunter.CurJobDef == JobDefOf.AttackStatic || hunter.CurJobDef == JobDefOf.AttackMelee) && quarry?.Spawned == true && quarry.RaceProps?.Animal == true && quarry.Faction != Faction.OfPlayer && companion.training?.HasLearned(TrainableDefOf.Release) == true && companion.Position.DistanceToSquared(quarry.Position) <= assistRange)
                {
                    Job assist = JobMaker.MakeJob(JobDefOf.AttackMelee, quarry); assist.maxNumMeleeAttacks = 1; assist.expiryInterval = 180;
                    companion.jobs.TryTakeOrderedJob(assist, JobTag.Misc);
                    GainDomesticExperience(companion, 0.022f);
                    continue;
                }
                if (hunter.Position.DistanceToSquared(companion.Position) <= 16) continue;
                IntVec3 follow = CellFinder.RandomClosewalkCellNear(hunter.Position, map, 3);
                if (!companion.CanReach(follow, PathEndMode.OnCell, Danger.Deadly)) continue;
                Job job = JobMaker.MakeJob(JobDefOf.Goto, follow); job.expiryInterval = 300;
                companion.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                GainDomesticExperience(companion, 0.0015f);
            }
        }

        private static int PositiveMod(int value, int modulus) { int result = value % modulus; return result < 0 ? result + modulus : result; }
    }

    public sealed class JobDriver_ManObservationPost : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            Toil watch = Toils_General.Wait(5000, TargetIndex.None);
            watch.socialMode = RandomSocialMode.Off;
            yield return watch;
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class RanchGuardianGizmoPatch
    {
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> values, Pawn __instance)
        {
            foreach (Gizmo gizmo in values) yield return gizmo;
            if ((!HerdsMod.Settings.enableRanchGuardians && !HerdsMod.Settings.enableDomesticPredatorRoles) || __instance?.Spawned != true || __instance.Faction != Faction.OfPlayer || __instance.RaceProps?.Animal != true || !__instance.RaceProps.predator) yield break;
            WildlifeFieldcraftMapComponent component = __instance.Map.GetComponent<WildlifeFieldcraftMapComponent>();
            Command_Action roleCommand = new Command_Action
            {
                defaultLabel = HerdsMod.Settings.enableDomesticPredatorRoles ? "Predator role: " + WildlifeFieldcraftMapComponent.RoleLabel(component.DomesticRole(__instance)) + (HerdsMod.Settings.enableDomesticRoleProgression && component.DomesticRole(__instance) != DomesticPredatorRole.None ? " (" + component.DomesticLevel(__instance) + ")" : "") : component.IsGuardian(__instance) ? "Clear guardian role" : "Assign ranch guardian",
                defaultDesc = HerdsMod.Settings.enableDomesticPredatorRoles ? "Assign this domesticated predator as a hunting companion, ranch guardian, colony patrol animal, or ordinary pet." : "Assign this trained predator to patrol its ranch area and confront wild predators.",
                icon = TexCommand.Attack,
                action = () =>
                {
                    if (!HerdsMod.Settings.enableDomesticPredatorRoles) { component.ToggleGuardian(__instance); return; }
                    Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                    {
                        new FloatMenuOption("None", () => component.SetDomesticRole(__instance, DomesticPredatorRole.None)),
                        new FloatMenuOption("Hunting companion", () => component.SetDomesticRole(__instance, DomesticPredatorRole.HuntingCompanion)),
                        new FloatMenuOption("Ranch guardian", () => component.SetDomesticRole(__instance, DomesticPredatorRole.RanchGuardian)),
                        new FloatMenuOption("Colony patrol", () => component.SetDomesticRole(__instance, DomesticPredatorRole.ColonyPatrol))
                    }));
                }
            };
            if (!WildlifeProgression.Unlocked(WildlifeCapability.AnimalHandling)) roleCommand.Disable(WildlifeProgression.LockReason(WildlifeCapability.AnimalHandling));
            yield return roleCommand;
            if ((component.IsGuardian(__instance) || component.DomesticRole(__instance) == DomesticPredatorRole.ColonyPatrol) && HerdsMod.Settings.enableGuardianPatrolAreas)
            {
                yield return new Command_Action { defaultLabel = "Set patrol center", defaultDesc = "Choose the center of this guardian's ranch patrol area.", icon = TexCommand.GatherSpotActive, action = () => Find.Targeter.BeginTargeting(TargetingParameters.ForCell(), target => component.SetGuardianAnchor(__instance, target.Cell)) };
                yield return new Command_Action { defaultLabel = "Patrol radius: " + component.GuardianRadius(__instance), defaultDesc = "Cycle between 12, 24, and 40-cell patrol areas.", icon = TexCommand.GatherSpotActive, action = () => component.CycleGuardianRadius(__instance) };
            }
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.DrawExtraSelectionOverlays))]
    public static class RanchGuardianOverlayPatch
    {
        public static void Postfix(Pawn __instance)
        {
            if (!HerdsMod.Settings.enableGuardianPatrolAreas || __instance?.Spawned != true || __instance.Faction != Faction.OfPlayer) return;
            WildlifeFieldcraftMapComponent component = __instance.Map.GetComponent<WildlifeFieldcraftMapComponent>();
            IntVec3 anchor = component.GuardianAnchor(__instance);
            if (anchor.IsValid) GenDraw.DrawRadiusRing(anchor, component.GuardianRadius(__instance), Color.yellow);
        }
    }
}
