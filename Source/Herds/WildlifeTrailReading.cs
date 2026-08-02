using System;
using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Herds
{
    public enum WildlifeTrailState
    {
        Ambient,
        LiveQuarry,
        Pursuit,
        BeyondMap,
        Cold
    }

    public sealed class WildlifeTrailLead : IExposable
    {
        public ThingDef species;
        public Pawn tracker;
        public Pawn targetAnimal;
        public List<IntVec3> evidenceCells = new List<IntVec3>();
        public List<int> evidenceTicks = new List<int>();
        public IntVec3 startCell;
        public IntVec3 predictedCell;
        public WildlifeSignKind dominantKind;
        public int groupSize = 1;
        public int createdTick;
        public int expiresTick;
        public float confidence;
        public bool predator;
        public bool marked = true;
        public string direction;
        public WildlifeTrailState state;
        public IntVec3 departureCell;
        public int failedSearches;
        public bool viableLead;
        public string lastOutcome;

        public float UncertaintyRadius => Mathf.Lerp(22f, 4f, Mathf.Clamp01(confidence));

        public void ExposeData()
        {
            Scribe_Defs.Look(ref species, "species");
            Scribe_References.Look(ref tracker, "tracker");
            Scribe_References.Look(ref targetAnimal, "targetAnimal");
            Scribe_Collections.Look(ref evidenceCells, "evidenceCells", LookMode.Value);
            Scribe_Collections.Look(ref evidenceTicks, "evidenceTicks", LookMode.Value);
            Scribe_Values.Look(ref startCell, "startCell");
            Scribe_Values.Look(ref predictedCell, "predictedCell");
            Scribe_Values.Look(ref dominantKind, "dominantKind");
            Scribe_Values.Look(ref groupSize, "groupSize", 1);
            Scribe_Values.Look(ref createdTick, "createdTick");
            Scribe_Values.Look(ref expiresTick, "expiresTick");
            Scribe_Values.Look(ref confidence, "confidence");
            Scribe_Values.Look(ref predator, "predator");
            Scribe_Values.Look(ref marked, "marked", true);
            Scribe_Values.Look(ref direction, "direction");
            Scribe_Values.Look(ref state, "state", WildlifeTrailState.Ambient);
            Scribe_Values.Look(ref departureCell, "departureCell");
            Scribe_Values.Look(ref failedSearches, "failedSearches");
            Scribe_Values.Look(ref viableLead, "viableLead");
            Scribe_Values.Look(ref lastOutcome, "lastOutcome");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                evidenceCells = evidenceCells ?? new List<IntVec3>();
                evidenceTicks = evidenceTicks ?? new List<int>();
            }
        }
    }

    public sealed class WildlifeTrailMapComponent : MapComponent
    {
        private List<WildlifeTrailLead> leads = new List<WildlifeTrailLead>();
        private int nextCleanupTick;

        public List<WildlifeTrailLead> TrailLeads => leads;

        public WildlifeTrailMapComponent(Map map) : base(map) { }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref leads, "wildlifeTrailLeads", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                leads = leads ?? new List<WildlifeTrailLead>();
        }

        public override void MapComponentTick()
        {
            if (HerdsMod.Settings?.enableTrailReading != true) return;
            int now = Find.TickManager.TicksGame;
            if (now < nextCleanupTick) return;
            nextCleanupTick = now + 600;
            for (int i = 0; i < leads.Count; i++)
            {
                WildlifeTrailLead lead = leads[i];
                if (lead?.targetAnimal == null || lead.state == WildlifeTrailState.BeyondMap)
                    continue;
                if (lead.targetAnimal.Dead)
                {
                    lead.state = WildlifeTrailState.Cold;
                    lead.lastOutcome = "The tracked animal died.";
                    lead.expiresTick = Mathf.Min(lead.expiresTick, now + 2500);
                }
                else if (!lead.targetAnimal.Spawned)
                {
                    RoamingAnimalRecord roaming = map.GetComponent<RegionalWildlifeMapComponent>()?
                        .RoamingAnimals.FirstOrDefault(value =>
                            value?.animal == lead.targetAnimal &&
                            value.state != RoamingAnimalState.Dead);
                    if (roaming != null)
                    {
                        lead.state = WildlifeTrailState.BeyondMap;
                        lead.lastOutcome = "The trail continues into the surrounding region.";
                        lead.expiresTick = Mathf.Max(lead.expiresTick, now + 60000);
                    }
                }
            }
            leads.RemoveAll(value => value == null || value.species == null || value.expiresTick <= now);
        }

        public WildlifeTrailLead LeadFor(ThingDef species)
        {
            if (HerdsMod.Settings?.enableTrailReading != true || species == null) return null;
            int now = Find.TickManager.TicksGame;
            return leads.Where(value => value?.species == species && value.expiresTick > now)
                .OrderByDescending(value => value.createdTick).FirstOrDefault();
        }

        public WildlifeTrailLead LeadFor(Pawn animal)
        {
            if (HerdsMod.Settings?.enableTrailReading != true || animal == null) return null;
            int now = Find.TickManager.TicksGame;
            return leads.Where(value => value?.targetAnimal == animal && value.expiresTick > now)
                .OrderByDescending(value => value.createdTick).FirstOrDefault();
        }

        public WildlifeTrailLead Analyze(WildlifeSign seed, Pawn tracker)
        {
            if (HerdsMod.Settings?.enableTrailReading != true || seed?.Spawned != true ||
                seed.species == null || tracker == null || !RepresentsDepartedAnimal(seed))
                return null;
            int now = Find.TickManager.TicksGame;
            List<WildlifeSign> evidence = map.listerThings.ThingsOfDef(HerdsDefOf.Herds_WildlifeSign)
                .OfType<WildlifeSign>()
                .Where(value => value.sourceAnimal == seed.sourceAnimal &&
                    value.species == seed.species && now - value.createdTick <= 18000 &&
                    value.Position.DistanceToSquared(seed.Position) <= 8100)
                .OrderByDescending(value => value.createdTick).Take(12)
                .OrderBy(value => value.createdTick).ToList();
            if (evidence.Count == 0) evidence.Add(seed);

            int skill = tracker.skills?.GetSkill(SkillDefOf.Animals)?.Level ?? 0;
            int knowledge = map.GetComponent<HuntingKnowledgeMapComponent>()?.Level(tracker, seed.species) ?? 0;
            int newestTick = evidence.Max(value => value.createdTick);
            float freshness = Mathf.Clamp01(1f - (now - newestTick) / 18000f);
            float confidence = Mathf.Clamp(0.12f + Mathf.Min(0.34f, evidence.Count * 0.055f) +
                skill * 0.018f + knowledge * 0.055f + freshness * 0.12f, 0.12f, 0.96f);

            Vector2 movement = Vector2.zero;
            for (int i = 0; i < evidence.Count; i++)
                movement += new Vector2(evidence[i].travelTo.x - evidence[i].travelFrom.x,
                    evidence[i].travelTo.z - evidence[i].travelFrom.z);
            if (movement.sqrMagnitude < 0.01f)
                movement = new Vector2(seed.travelTo.x - seed.travelFrom.x,
                    seed.travelTo.z - seed.travelFrom.z);
            if (movement.sqrMagnitude < 0.01f) movement = Vector2.up;
            movement.Normalize();

            WildlifeSign newest = evidence[evidence.Count - 1];
            int projection = Mathf.RoundToInt(Mathf.Lerp(5f, 15f, confidence));
            IntVec3 predicted = (newest.travelTo + new IntVec3(
                Mathf.RoundToInt(movement.x * projection), 0,
                Mathf.RoundToInt(movement.y * projection))).ClampInsideMap(map);
            if (!predicted.Walkable(map))
                predicted = CellFinder.RandomClosewalkCellNear(predicted, map, 6);

            Pawn sourceAnimal = newest.sourceAnimal ?? seed.sourceAnimal;
            Pawn target = sourceAnimal;
            RoamingAnimalRecord roaming = sourceAnimal == null ? null :
                map.GetComponent<RegionalWildlifeMapComponent>()?.RoamingAnimals
                    .FirstOrDefault(value => value?.animal == sourceAnimal &&
                        value.state != RoamingAnimalState.Dead);
            if (target == null && roaming != null) target = sourceAnimal;
            bool beyondMap = target?.Spawned != true && roaming != null;
            if (beyondMap)
                predicted = newest.travelTo;

            WildlifeTrailLead lead = LeadFor(sourceAnimal);
            if (lead == null)
            {
                lead = new WildlifeTrailLead { species = seed.species };
                leads.Add(lead);
            }
            lead.tracker = tracker;
            lead.targetAnimal = target;
            lead.evidenceCells = evidence.Select(value => value.travelTo).Distinct().ToList();
            lead.evidenceTicks = evidence.GroupBy(value => value.travelTo)
                .Select(group => group.Max(value => value.createdTick)).ToList();
            lead.startCell = evidence[0].travelFrom;
            lead.predictedCell = predicted;
            lead.dominantKind = evidence.GroupBy(value => value.signKind)
                .OrderByDescending(group => group.Count()).First().Key;
            lead.groupSize = Mathf.Max(1, Mathf.RoundToInt((float)evidence.Average(value => value.groupSize)));
            lead.createdTick = now;
            lead.expiresTick = int.MaxValue;
            lead.confidence = confidence;
            lead.predator = evidence.Any(value => value.predator);
            lead.direction = DirectionLabel(movement);
            lead.failedSearches = 0;
            lead.viableLead = beyondMap;
            lead.state = WildlifeTrailState.BeyondMap;
            lead.departureCell = newest.travelTo;
            lead.lastOutcome = "The originating animal is roaming beyond the local map; the trail reaches the boundary.";
            if (HerdsMod.Settings.enableSpeciesKnowledgeProgression)
                WildlifeKnowledgeAdapter.Observe(tracker, seed.species, WildlifeKnowledgeObservation.Tracks, map, true,
                    Mathf.Clamp(0.7f + evidence.Count * 0.1f + confidence * 0.5f, 0.2f, 2f),
                    "A reconstructed trail established movement toward " + lead.direction.ToLowerInvariant() + ".",
                    "wildlife:tracks:analysis:" + seed.thingIDNumber + ":" + tracker.thingIDNumber + ":" + newestTick);
            map.GetComponent<HuntingExpeditionMapComponent>()?
                .TryCreateTrailHuntOpportunity(tracker, seed.species, confidence, target);
            SetMarked(lead, true);
            WildlifeExperience.Record("Trail Reading", tracker.LabelShortCap + " reconstructed a " +
                ConfidenceLabel(confidence).ToLowerInvariant() + " " + seed.species.label +
                " trail moving " + lead.direction.ToLowerInvariant() + ".", seed);
            if (WildlifeTestLog.Enabled)
                WildlifeTestLog.Write("TrailRead", "species=" + seed.species.defName +
                    " evidence=" + evidence.Count + " confidence=" + confidence.ToString("0.00") +
                    " direction=" + lead.direction + " prediction=" + predicted, tracker, seed);
            return lead;
        }

        public bool RepresentsDepartedAnimal(WildlifeSign sign)
        {
            Pawn animal = sign?.sourceAnimal;
            if (animal == null || animal.Spawned) return false;
            return map.GetComponent<RegionalWildlifeMapComponent>()?.RoamingAnimals.Any(value =>
                value?.animal == animal && value.state != RoamingAnimalState.Present &&
                value.state != RoamingAnimalState.Dead) == true;
        }

        public List<WildlifeSign> AvailableLeadSigns()
        {
            if (HerdsDefOf.Herds_WildlifeSign == null) return new List<WildlifeSign>();
            int now = Find.TickManager.TicksGame;
            return map.listerThings.ThingsOfDef(HerdsDefOf.Herds_WildlifeSign)
                .OfType<WildlifeSign>()
                .Where(value => value.species != null && now - value.createdTick <= 18000 &&
                    RepresentsDepartedAnimal(value))
                .ToList();
        }

        public int UrgentLeadCount => CountUrgentLeads(AvailableLeadSigns());

        internal static int CountUrgentLeads(IEnumerable<WildlifeSign> signs) =>
            signs?.Where(value => value != null && (value.predator ||
                    value.signKind == WildlifeSignKind.BloodTrail))
                .Select(value => value.sourceAnimal).Where(value => value != null)
                .Distinct().Count() ?? 0;

        public void Follow(Pawn pawn, WildlifeTrailLead lead, WildlifeSign source)
        {
            IntVec3 destination = SafeFollowDestination(pawn, lead);
            if (pawn?.Spawned != true || lead == null || source == null ||
                !destination.IsValid ||
                !pawn.CanReach(destination, PathEndMode.OnCell, Danger.Some))
            {
                Messages.Message("That colonist cannot safely reach the predicted trail area.",
                    MessageTypeDefOf.RejectInput, false);
                return;
            }
            Job job = JobMaker.MakeJob(HerdsDefOf.Herds_FollowWildlifeTrail,
                destination, source);
            job.playerForced = true;
            if (!pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc))
                Messages.Message(pawn.LabelShortCap + " could not begin following the trail.",
                    pawn, MessageTypeDefOf.RejectInput, false);
        }

        public void ResolveFollow(Pawn pawn, ThingDef species, IntVec3 searchedCell)
        {
            WildlifeTrailLead lead = LeadFor(species);
            if (lead == null || pawn?.Spawned != true) return;
            if (lead.state == WildlifeTrailState.BeyondMap)
            {
                Messages.Message("The " + species.LabelCap +
                    " trail reaches the map edge and continues through the surrounding region.",
                    pawn, MessageTypeDefOf.NeutralEvent, false);
                Find.WindowStack.Add(new Window_RegionalWildlife(map));
                return;
            }
            float radius = lead.UncertaintyRadius + 8f;
            Pawn found = lead.targetAnimal?.Spawned == true &&
                !lead.targetAnimal.Dead &&
                (lead.targetAnimal.Position.DistanceToSquared(searchedCell) <= radius * radius ||
                 pawn.Position.DistanceToSquared(lead.targetAnimal.Position) <= 2025 &&
                 GenSight.LineOfSight(pawn.Position, lead.targetAnimal.Position, map))
                    ? lead.targetAnimal
                    : map.mapPawns.AllPawnsSpawned
                .Where(value => value?.Dead == false && value.def == species && value.Faction != Faction.OfPlayer &&
                    value.Position.DistanceToSquared(searchedCell) <= radius * radius)
                .OrderBy(value => value.Position.DistanceToSquared(searchedCell)).FirstOrDefault();
            if (found != null)
            {
                lead.targetAnimal = found;
                lead.confidence = Mathf.Min(0.98f, lead.confidence + 0.16f);
                lead.predictedCell = found.Position;
                lead.expiresTick = Find.TickManager.TicksGame + 30000;
                lead.failedSearches = 0;
                lead.state = WildlifeTrailState.LiveQuarry;
                lead.lastOutcome = pawn.LabelShortCap + " confirmed living quarry at the end of the trail.";
                SetMarked(lead, true);
                if (HerdsMod.Settings.enableSpeciesKnowledgeProgression)
                    WildlifeKnowledgeAdapter.Observe(pawn, species, WildlifeKnowledgeObservation.TrailCompletion, map, true,
                        1.6f, "A successful trail follow located living quarry.",
                        "wildlife:trail-completion:" + map.uniqueID + ":" + searchedCell.x + ":" + searchedCell.z +
                        ":" + pawn.thingIDNumber + ":" + found.thingIDNumber);
                WildlifeExperience.Record("Trail Followed", pawn.LabelShortCap + " successfully followed a " +
                    species.label + " trail.", found);
                Messages.Message(pawn.LabelShortCap + " found the " + species.LabelCap +
                    " trail. Its likely location is now much clearer.", found,
                    MessageTypeDefOf.PositiveEvent, false);
                if (WildlifeTestLog.Enabled)
                    WildlifeTestLog.Write("TrailFollow", "result=found species=" + species.defName +
                        " confidence=" + lead.confidence.ToString("0.00"), pawn, found);
            }
            else
            {
                lead.failedSearches++;
                RoamingAnimalRecord roaming = lead.targetAnimal == null ? null :
                    map.GetComponent<RegionalWildlifeMapComponent>()?.RoamingAnimals
                        .FirstOrDefault(value => value?.animal == lead.targetAnimal &&
                            value.state != RoamingAnimalState.Dead);
                if (roaming != null && lead.targetAnimal?.Spawned != true)
                {
                    lead.state = WildlifeTrailState.BeyondMap;
                    lead.lastOutcome = "The quarry left the local map; its trail continues " +
                        roaming.direction.ToLowerInvariant() + " through the region.";
                    lead.expiresTick = Find.TickManager.TicksGame + 60000;
                    Messages.Message(lead.lastOutcome, pawn,
                        MessageTypeDefOf.NeutralEvent, false);
                }
                else if (UsableTarget(lead.targetAnimal, species, true))
                {
                    int correction = lead.failedSearches >= 2 ? 2 :
                        Mathf.Max(3, Mathf.RoundToInt(lead.UncertaintyRadius * 0.45f));
                    lead.predictedCell = CellFinder.RandomClosewalkCellNear(
                        lead.targetAnimal.Position, map, correction);
                    lead.confidence = Mathf.Max(0.25f, lead.confidence - 0.06f);
                    lead.expiresTick = Find.TickManager.TicksGame + 18000;
                    lead.state = WildlifeTrailState.Pursuit;
                    lead.lastOutcome = "The first search missed, but fresher movement corrected the likely area.";
                    Messages.Message(pawn.LabelShortCap + " did not sight the " +
                        species.LabelCap + ", but fresher movement corrected the trail.",
                        pawn, MessageTypeDefOf.NeutralEvent, false);
                }
                else
                {
                    lead.confidence = Mathf.Max(0.08f, lead.confidence - 0.14f);
                    lead.expiresTick = Mathf.Min(lead.expiresTick,
                        Find.TickManager.TicksGame + 9000);
                    lead.state = WildlifeTrailState.Cold;
                    lead.lastOutcome = "No living quarry or regional continuation could be confirmed.";
                    Messages.Message(pawn.LabelShortCap + " found no fresh " +
                        species.LabelCap + " sign. The trail is going cold.", pawn,
                        MessageTypeDefOf.NeutralEvent, false);
                }
                WildlifeExperience.Record("Trail Followed", pawn.LabelShortCap +
                    " tested a " + species.label + " trail: " + lead.lastOutcome, pawn,
                    lead.state == WildlifeTrailState.Cold);
                if (WildlifeTestLog.Enabled)
                    WildlifeTestLog.Write("TrailFollow", "result=cold species=" + species.defName +
                        " state=" + lead.state + " confidence=" +
                        lead.confidence.ToString("0.00"), pawn);
            }
        }

        public IntVec3 SafeFollowDestination(Pawn pawn, WildlifeTrailLead lead)
        {
            if (pawn?.Spawned != true || lead == null) return IntVec3.Invalid;
            Pawn target = lead.targetAnimal;
            if (target?.Spawned != true || target.Dead) return lead.predictedCell;
            Vector2 observerSide = new Vector2(pawn.Position.x - target.Position.x,
                pawn.Position.z - target.Position.z);
            if (observerSide.sqrMagnitude < 0.01f) observerSide = Vector2.up;
            observerSide.Normalize();
            IntVec3 desired = target.Position + new IntVec3(
                Mathf.RoundToInt(observerSide.x * 34f), 0,
                Mathf.RoundToInt(observerSide.y * 34f));
            IntVec3 bestCell = IntVec3.Invalid;
            float best = float.MaxValue;
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(
                desired.ClampInsideMap(map), 6f, true))
            {
                float targetDistance = cell.DistanceToSquared(target.Position);
                if (!cell.InBounds(map) || !cell.Walkable(map) ||
                    targetDistance < 900f || targetDistance > 2025f ||
                    !pawn.CanReach(cell, PathEndMode.OnCell, Danger.Some)) continue;
                Vector2 side = new Vector2(cell.x - target.Position.x,
                    cell.z - target.Position.z).normalized;
                if (Vector2.Dot(side, observerSide) < 0.45f) continue;
                float score = cell.DistanceToSquared(pawn.Position);
                if (score >= best) continue;
                best = score;
                bestCell = cell;
            }
            return bestCell;
        }

        public bool Retains(Pawn animal)
        {
            if (HerdsMod.Settings?.enableTrailReading != true || animal == null) return false;
            int now = Find.TickManager.TicksGame;
            return leads.Any(value => value?.targetAnimal == animal &&
                value.expiresTick > now && value.state != WildlifeTrailState.Cold &&
                value.state != WildlifeTrailState.BeyondMap);
        }

        public void NotifyAnimalDeparture(Pawn animal, IntVec3 edge)
        {
            if (animal == null) return;
            int now = Find.TickManager.TicksGame;
            foreach (WildlifeTrailLead lead in leads.Where(value =>
                value?.targetAnimal == animal && value.expiresTick > now))
            {
                lead.state = WildlifeTrailState.BeyondMap;
                lead.departureCell = edge;
                lead.predictedCell = edge;
                lead.expiresTick = now + 60000;
                lead.lastOutcome = "The animal left the local map. Its identity and trail remain available in Local Wildlife.";
                lead.marked = true;
            }
        }

        public override void MapComponentDraw()
        {
            base.MapComponentDraw();
            if (HerdsMod.Settings?.enableTrailReading != true || Find.CurrentMap != map ||
                leads.Count == 0) return;
            int now = Find.TickManager.TicksGame;
            bool unified = Prefs.DevMode && WildlifeDevMaster.CompleteOverlayEnabled;
            int drawn = 0;
            for (int i = leads.Count - 1; i >= 0 && drawn < 16; i--)
            {
                WildlifeTrailLead lead = leads[i];
                if (lead == null || lead.expiresTick <= now ||
                    (!unified && !lead.marked)) continue;
                DrawTrail(lead, false);
                drawn++;
            }
        }

        public void SetMarked(WildlifeTrailLead lead, bool marked)
        {
            if (lead == null) return;
            if (marked)
                for (int i = 0; i < leads.Count; i++)
                    if (leads[i] != null && leads[i] != lead) leads[i].marked = false;
            lead.marked = marked;
        }

        public void Forget(WildlifeTrailLead lead)
        {
            if (lead == null || !leads.Remove(lead)) return;
            map.GetComponent<HuntingExpeditionMapComponent>()?
                .ForgetTrailOpportunity(lead.targetAnimal);
        }

        public void DrawSelectedTrail(WildlifeSign sign)
        {
            WildlifeTrailLead lead = LeadFor(sign?.species);
            if (lead != null && !lead.marked) DrawTrail(lead, true);
        }

        private void DrawTrail(WildlifeTrailLead lead, bool selected)
        {
            if (lead.evidenceCells == null || lead.evidenceCells.Count == 0) return;
            Color color = NaturalTrailColor(lead);
            Color prediction = lead.state == WildlifeTrailState.BeyondMap
                ? new Color(0.38f, 0.42f, 0.25f, 0.72f)
                : new Color(0.62f, 0.48f, 0.23f, 0.68f);
            int markBudget = selected ? 64 : 48;
            IntVec3 previous = lead.startCell.IsValid ? lead.startCell : lead.evidenceCells[0];
            for (int i = 0; i < lead.evidenceCells.Count; i++)
            {
                IntVec3 cell = lead.evidenceCells[i];
                DrawNaturalSegment(previous, cell, color, false, ref markBudget);
                GenDraw.DrawRadiusRing(cell, selected ? 0.58f : 0.40f,
                    new Color(color.r, color.g, color.b, selected ? 0.82f : 0.62f));
                previous = cell;
            }
            DrawNaturalSegment(previous, lead.predictedCell, prediction, true, ref markBudget);
            GenDraw.DrawRadiusRing(lead.predictedCell, lead.UncertaintyRadius,
                new Color(prediction.r, prediction.g, prediction.b, 0.20f));
            GenDraw.DrawRadiusRing(lead.predictedCell, selected ? 0.86f : 0.68f,
                new Color(prediction.r, prediction.g, prediction.b, 0.76f));
        }

        private static Color NaturalTrailColor(WildlifeTrailLead lead)
        {
            if (lead.dominantKind == WildlifeSignKind.BloodTrail)
                return new Color(0.50f, 0.16f, 0.10f, 0.74f);
            if (lead.dominantKind == WildlifeSignKind.Browse)
                return new Color(0.31f, 0.40f, 0.18f, 0.72f);
            if (lead.dominantKind == WildlifeSignKind.Droppings)
                return new Color(0.34f, 0.25f, 0.14f, 0.74f);
            if (lead.dominantKind == WildlifeSignKind.TerritoryMark || lead.predator)
                return new Color(0.49f, 0.29f, 0.13f, 0.76f);
            return new Color(0.42f, 0.34f, 0.20f, 0.72f);
        }

        public static bool NaturalPaletteSelfTest()
        {
            Color tracks = NaturalTrailColor(new WildlifeTrailLead
                { dominantKind = WildlifeSignKind.Tracks });
            Color browse = NaturalTrailColor(new WildlifeTrailLead
                { dominantKind = WildlifeSignKind.Browse });
            Color blood = NaturalTrailColor(new WildlifeTrailLead
                { dominantKind = WildlifeSignKind.BloodTrail });
            return tracks.maxColorComponent < 0.75f &&
                browse.g > browse.b && tracks.r > tracks.b &&
                blood.r > blood.g * 2f && tracks != browse && browse != blood;
        }

        private static void DrawNaturalSegment(IntVec3 from, IntVec3 to, Color color,
            bool uncertain, ref int budget)
        {
            if (!from.IsValid || !to.IsValid || budget <= 0) return;
            float distance = from.DistanceTo(to);
            if (distance < 0.8f) return;
            float spacing = uncertain ? 3.1f : 2.0f;
            int count = Mathf.Min(budget, Mathf.Max(1,
                Mathf.FloorToInt(distance / spacing)));
            Vector3 start = from.ToVector3Shifted();
            Vector3 end = to.ToVector3Shifted();
            for (int i = 1; i <= count; i++)
            {
                float t = i / (count + 1f);
                Vector3 point = Vector3.Lerp(start, end, t);
                IntVec3 cell = new IntVec3(Mathf.RoundToInt(point.x), 0,
                    Mathf.RoundToInt(point.z));
                float radius = uncertain ? 0.12f : (i % 2 == 0 ? 0.16f : 0.12f);
                float alpha = uncertain ? 0.38f : (i % 3 == 0 ? 0.66f : 0.52f);
                GenDraw.DrawRadiusRing(cell, radius,
                    new Color(color.r, color.g, color.b, alpha));
            }
            budget -= count;
        }

        public List<string> DebugOverviewLines()
        {
            int now = Find.TickManager.TicksGame;
            List<string> result = new List<string>
            {
                "TRAILS active=" + leads.Count(value => value?.expiresTick > now) +
                " marked=" + leads.Count(value => value?.expiresTick > now && value.marked)
            };
            result.AddRange(leads.Where(value => value?.expiresTick > now).Take(12).Select(value =>
                "TRAIL species=" + value.species?.defName + " evidence=" + value.evidenceCells.Count +
                " confidence=" + value.confidence.ToString("0.00") + " direction=" + value.direction +
                " predicted=" + value.predictedCell + " state=" + value.state +
                " target=" + (value.targetAnimal?.thingIDNumber ?? -1) +
                " viable=" + value.viableLead + " misses=" + value.failedSearches));
            return result;
        }

        public string DebugCreateTrail()
        {
            Pawn animal = map.mapPawns.AllPawnsSpawned.FirstOrDefault(value =>
                value?.Dead == false && value.RaceProps?.Animal == true &&
                value.Faction != Faction.OfPlayer);
            Pawn tracker = map.mapPawns.FreeColonistsSpawned.OrderByDescending(value =>
                value.skills?.GetSkill(SkillDefOf.Animals)?.Level ?? 0).FirstOrDefault();
            if (animal == null || tracker == null) return "missing animal or colonist";
            List<WildlifeSign> signs = map.listerThings.ThingsOfDef(HerdsDefOf.Herds_WildlifeSign)
                .OfType<WildlifeSign>().Where(value => value.species == animal.def)
                .OrderBy(value => value.createdTick).ToList();
            if (signs.Count < 3)
            {
                IntVec3 step = IntVec3.East * 4;
                for (int i = signs.Count; i < 4; i++)
                {
                    IntVec3 desired = (animal.Position - step * (4 - i)).ClampInsideMap(map);
                    IntVec3 cell = CellFinder.RandomClosewalkCellNear(desired, map, 3);
                    WildlifeSign sign = (WildlifeSign)ThingMaker.MakeThing(HerdsDefOf.Herds_WildlifeSign);
                    sign.species = animal.def;
                    sign.sourceAnimal = animal;
                    sign.createdTick = Find.TickManager.TicksGame - (4 - i) * 500;
                    sign.travelFrom = (cell - step).ClampInsideMap(map);
                    sign.travelTo = cell;
                    sign.predator = WildlifeSpeciesClassification.IsPredator(animal.def);
                    sign.groupSize = map.GetComponent<HerdMapComponent>()?.HerdFor(animal)?.members.Count ?? 1;
                    sign.signKind = i == 2 && sign.predator
                        ? WildlifeSignKind.TerritoryMark : WildlifeSignKind.Tracks;
                    GenSpawn.Spawn(sign, cell, map);
                    signs.Add(sign);
                }
            }
            WildlifeTrailLead lead = Analyze(signs.OrderByDescending(value =>
                value.createdTick).First(), tracker);
            WildlifeSign source = signs.OrderByDescending(value => value.createdTick).First();
            return lead == null ? "trail creation failed" :
                animal.def.defName + " evidence=" + lead.evidenceCells.Count +
                " confidence=" + lead.confidence.ToString("0.00") +
                " predicted=" + lead.predictedCell + " signId=" + source.thingIDNumber;
        }

        public static string ConfidenceLabel(float confidence) =>
            confidence < 0.35f ? "Tentative" : confidence < 0.60f ? "Plausible" :
            confidence < 0.82f ? "Strong" : "Very Strong";

        public static string StatusLabel(WildlifeTrailLead lead)
        {
            if (lead == null) return "Unavailable";
            return "Animal departed map";
        }

        private Pawn BestLiveTarget(ThingDef species, Pawn tracker, bool requireViable)
        {
            IEnumerable<Pawn> candidates = map.mapPawns.AllPawnsSpawned.Where(value =>
                UsableTarget(value, species, true) && value.CurJob?.exitMapOnArrival != true);
            if (requireViable)
                candidates = candidates.Where(value => EdgeDistance(value.Position) >= 18);
            return candidates.OrderBy(value => value.Position.DistanceToSquared(
                tracker?.Position ?? map.Center)).FirstOrDefault();
        }

        private bool UsableTarget(Pawn value, ThingDef species, bool requireInterior)
        {
            if (value?.Spawned != true || value.Dead || value.Downed ||
                value.InMentalState || value.def != species ||
                value.Faction == Faction.OfPlayer) return false;
            return !requireInterior || EdgeDistance(value.Position) >= 10;
        }

        private int EdgeDistance(IntVec3 cell) =>
            Math.Min(Math.Min(cell.x, cell.z),
                Math.Min(map.Size.x - 1 - cell.x, map.Size.z - 1 - cell.z));

        private static string DirectionLabel(Vector2 value)
        {
            string vertical = value.y > 0.35f ? "North" : value.y < -0.35f ? "South" : "";
            string horizontal = value.x > 0.35f ? "East" : value.x < -0.35f ? "West" : "";
            string result = vertical + horizontal;
            return result.NullOrEmpty() ? "Nearby" : result;
        }
    }

    public sealed class JobDriver_FollowWildlifeTrail : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
            Toil search = Toils_General.Wait(420, TargetIndex.None);
            search.socialMode = RandomSocialMode.Off;
            yield return search;
            Toil finish = ToilMaker.MakeToil("ResolveWildlifeTrail");
            finish.initAction = () =>
            {
                WildlifeSign sign = job.targetB.Thing as WildlifeSign;
                ThingDef species = sign?.species;
                if (species != null)
                    pawn.Map?.GetComponent<WildlifeTrailMapComponent>()?
                        .ResolveFollow(pawn, species, job.targetA.Cell);
            };
            finish.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return finish;
        }
    }

    public sealed class Window_WildlifeTrail : Window
    {
        private readonly Map map;
        private readonly WildlifeTrailLead lead;
        private Vector2 scroll;

        public override Vector2 InitialSize => new Vector2(760f, 620f);

        public Window_WildlifeTrail(Map map, WildlifeTrailLead lead)
        {
            this.map = map;
            this.lead = lead;
            doCloseX = true;
            absorbInputAroundWindow = true;
            resizeable = true;
        }

        public override void DoWindowContents(Rect rect)
        {
            if (map == null || lead?.species == null)
            {
                Widgets.Label(rect, "This trail is no longer available.");
                return;
            }
            Color accent = lead.predator ? new Color(0.78f, 0.28f, 0.18f) :
                new Color(0.20f, 0.60f, 0.48f);
            Rect header = new Rect(0f, 0f, rect.width, 112f);
            Widgets.DrawMenuSection(header);
            Widgets.DrawBoxSolid(new Rect(0f, 0f, 6f, header.height), accent);
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(18f, 12f, rect.width - 36f, 30f),
                lead.species.LabelCap + " Trail");
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(18f, 45f, rect.width - 36f, 24f),
                WildlifeTrailMapComponent.StatusLabel(lead) + "  •  " +
                (lead.predator ? "Predator movement — approach with care" :
                "Reconstructed from physical wildlife signs"));
            Rect bar = new Rect(18f, 78f, rect.width - 210f, 17f);
            Widgets.FillableBar(bar, lead.confidence);
            Widgets.Label(new Rect(rect.width - 178f, 74f, 160f, 26f),
                WildlifeTrailMapComponent.ConfidenceLabel(lead.confidence) + " confidence");
            TooltipHandler.TipRegion(bar, "Confidence combines the tracker’s Animals Skill, personal Animal Knowledge, evidence count, and freshness. The outer map ring shows the remaining uncertainty.");

            float top = 126f;
            float gap = 8f;
            float width = (rect.width - gap * 3f) / 4f;
            DrawMetric(new Rect(0f, top, width, 58f), "Direction", lead.direction);
            DrawMetric(new Rect(width + gap, top, width, 58f), "Group", "About " + lead.groupSize);
            DrawMetric(new Rect((width + gap) * 2f, top, width, 58f), "Evidence",
                lead.evidenceCells.Count.ToString());
            DrawMetric(new Rect((width + gap) * 3f, top, width, 58f), "Freshness", Freshness());
            TooltipHandler.TipRegion(new Rect(0f, top, rect.width, 58f),
                lead.lastOutcome.NullOrEmpty()
                    ? "The trail has not yet produced an outcome."
                    : lead.lastOutcome);
            TrailHuntOpportunity opportunity = map.GetComponent<HuntingExpeditionMapComponent>()?
                .ActiveTrailHuntOpportunity(lead.species, map.Biome);
            if (opportunity != null)
                TooltipHandler.TipRegion(new Rect(0f, top, rect.width, 58f),
                    "Hunt opportunity active for " +
                    (opportunity.expiresTick - Find.TickManager.TicksGame).ToStringTicksToPeriod() +
                    ". A matching hunt expedition receives improved encounter, success, and safety conditions.");

            Rect interpretation = new Rect(0f, top + 70f, rect.width, 88f);
            Widgets.DrawMenuSection(interpretation);
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(14f, interpretation.y + 9f, rect.width - 28f, 28f), "Field Reading");
            Text.Font = GameFont.Small;
            string behavior = lead.dominantKind == WildlifeSignKind.BloodTrail ? "The trail includes blood; the animal may be wounded." :
                lead.dominantKind == WildlifeSignKind.TerritoryMark ? "Repeated markings suggest an actively defended range." :
                lead.dominantKind == WildlifeSignKind.Browse ? "Feeding sign suggests the group slowed or lingered here." :
                lead.dominantKind == WildlifeSignKind.Droppings ? "Droppings help establish the group’s recent pace and size." :
                "Tracks establish a recent line of travel.";
            Widgets.Label(new Rect(14f, interpretation.y + 39f, rect.width - 28f, 42f),
                behavior + " The likely area lies " + lead.direction.ToLowerInvariant() +
                (lead.state == WildlifeTrailState.BeyondMap
                    ? ", where it reaches the map boundary and continues through the region."
                    : ", within roughly " + lead.UncertaintyRadius.ToString("0") + " cells."));

            Rect evidenceOuter = new Rect(0f, interpretation.yMax + 10f, rect.width, rect.height - interpretation.yMax - 66f);
            float viewHeight = Mathf.Max(evidenceOuter.height,
                36f + lead.evidenceCells.Count * 34f);
            Rect view = new Rect(0f, 0f, evidenceOuter.width - 18f, viewHeight);
            Widgets.BeginScrollView(evidenceOuter, ref scroll, view);
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, view.width, 28f), "Evidence Trail");
            Text.Font = GameFont.Small;
            for (int i = 0; i < lead.evidenceCells.Count; i++)
            {
                Rect row = new Rect(0f, 32f + i * 34f, view.width, 30f);
                if (i % 2 == 0) Widgets.DrawLightHighlight(row);
                int age = Find.TickManager.TicksGame -
                    (i < lead.evidenceTicks.Count ? lead.evidenceTicks[i] : lead.createdTick);
                Widgets.Label(new Rect(8f, row.y + 5f, row.width - 16f, 22f),
                    (i + 1) + ". " + age.ToStringTicksToPeriod() + " ago" +
                    (i == lead.evidenceCells.Count - 1 ? "  —  latest sign" : ""));
                if (Widgets.ButtonInvisible(row))
                    WildlifeUI.Focus(lead.evidenceCells[i], map);
                TooltipHandler.TipRegion(row, "Click to focus this piece of evidence on the map.");
            }
            Widgets.EndScrollView();

            float buttonY = rect.height - 42f;
            if (Widgets.ButtonText(new Rect(0f, buttonY, 210f, 36f), "Send Expedition"))
                SendExpedition();
            if (Widgets.ButtonText(new Rect(220f, buttonY, 170f, 36f), "Forget Trail"))
                ConfirmForget();
        }

        private void DrawMetric(Rect rect, string label, string value)
        {
            Widgets.DrawMenuSection(rect);
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.72f, 0.78f, 0.72f);
            Widgets.Label(new Rect(rect.x + 9f, rect.y + 6f, rect.width - 18f, 18f), label.ToUpperInvariant());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(rect.x + 9f, rect.y + 27f, rect.width - 18f, 24f), value);
        }

        private string Freshness()
        {
            int newest = lead.evidenceTicks.Count > 0 ? lead.evidenceTicks.Max() : lead.createdTick;
            int age = Mathf.Max(0, Find.TickManager.TicksGame - newest);
            return age < 2500 ? "Fresh" : age < 7500 ? "Recent" : "Old";
        }

        private bool QuarryAtTrailEnd()
        {
            if (lead.targetAnimal?.Spawned != true || lead.targetAnimal.Dead ||
                lead.evidenceCells == null || lead.evidenceCells.Count == 0 ||
                lead.targetAnimal.Position.Fogged(map)) return false;
            IntVec3 latest = lead.evidenceCells[lead.evidenceCells.Count - 1];
            return latest.DistanceToSquared(lead.targetAnimal.Position) <= 576;
        }

        private void ChooseFollower()
        {
            WildlifeSign source = map.listerThings.ThingsOfDef(HerdsDefOf.Herds_WildlifeSign)
                .OfType<WildlifeSign>().Where(value => value.species == lead.species)
                .OrderByDescending(value => value.createdTick).FirstOrDefault();
            if (source == null)
            {
                Messages.Message("The physical trail has faded.", MessageTypeDefOf.RejectInput, false);
                return;
            }
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned
                .OrderByDescending(value => value.skills?.GetSkill(SkillDefOf.Animals)?.Level ?? 0))
            {
                Pawn selected = pawn;
                int skill = selected.skills?.GetSkill(SkillDefOf.Animals)?.Level ?? 0;
                string label = selected.LabelShortCap + " — Animals Skill " + skill;
                if (selected.Downed || selected.InMentalState ||
                    !selected.CanReach(lead.predictedCell, PathEndMode.OnCell, Danger.Some))
                    options.Add(new FloatMenuOption(label + " (unavailable)", null));
                else
                    options.Add(new FloatMenuOption(label, () =>
                    {
                        map.GetComponent<WildlifeTrailMapComponent>()?.Follow(selected, lead, source);
                        Close();
                    }));
            }
            if (options.Count == 0)
                options.Add(new FloatMenuOption("No colonists are available.", null));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void OpenBeyondMapOptions()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>
            {
                new FloatMenuOption("View in Local Wildlife", () =>
                {
                    Find.WindowStack.Add(new Window_RegionalWildlife(map));
                    Close();
                })
            };
            HuntingExpeditionMapComponent expeditions =
                map.GetComponent<HuntingExpeditionMapComponent>();
            if (WildlifeProgression.Unlocked(WildlifeCapability.HuntingExpedition) &&
                expeditions != null)
            {
                TrailHuntOpportunity opportunity = expeditions.ActiveTrailHuntOpportunity(
                    lead.species, map.Biome);
                options.Add(new FloatMenuOption("Send Wildlife Expedition", () =>
                {
                    Window_WildlifeExpeditions list =
                        new Window_WildlifeExpeditions(map);
                    WildlifeWorldMapController.BeginNewExpeditionSelection(expeditions, list);
                    Close();
                }));
                if (opportunity != null)
                    options.Add(new FloatMenuOption("Plan Improved Hunt Expedition", () =>
                    {
                        ExpeditionDestination destination = expeditions.Destinations()
                            .OrderByDescending(value => value.biome == opportunity.biome)
                            .ThenBy(value => value.distance).FirstOrDefault();
                        Find.WindowStack.Add(new Window_HuntingExpeditionSetup(map, destination,
                            lead.species, ExpeditionObjective.Hunt));
                        Close();
                    }));
            }

            else
                options.Add(new FloatMenuOption("Send Wildlife Expedition (research required)", null));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void SendExpedition()
        {
            HuntingExpeditionMapComponent expeditions = map.GetComponent<HuntingExpeditionMapComponent>();
            TrailHuntOpportunity opportunity = expeditions?.TrailHuntOpportunities
                .FirstOrDefault(value => value?.targetAnimal == lead.targetAnimal);
            ExpeditionDestination destination = expeditions?.NearbyTrailDestination(lead.targetAnimal);
            if (opportunity == null || destination == null)
            {
                Messages.Message("This trail no longer has a valid nearby expedition target.",
                    MessageTypeDefOf.RejectInput, false);
                return;
            }
            Find.WindowStack.Add(new Window_HuntingExpeditionSetup(map, destination,
                lead.species, ExpeditionObjective.Hunt, lead.targetAnimal));
            Close();
        }

        private void ConfirmForget()
        {
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "Permanently forget this trail?", () =>
                {
                    map.GetComponent<WildlifeTrailMapComponent>()?.Forget(lead);
                    Close(false);
                }, destructive: true, title: "Forget Trail"));
        }
    }

    public sealed class Window_WildlifeTrailBoard : Window
    {
        private sealed class TrailCandidate
        {
            public ThingDef species;
            public List<WildlifeSign> signs;
            public WildlifeTrailLead lead;
            public WildlifeSign Latest => signs.OrderByDescending(value => value.createdTick).FirstOrDefault();
            public bool Urgent => signs.Any(value => value.predator ||
                value.signKind == WildlifeSignKind.BloodTrail);
        }

        private readonly Map map;
        private Vector2 scroll;
        private List<TrailCandidate> cachedCandidates;
        private int nextCandidateRefreshTick;

        public override Vector2 InitialSize => new Vector2(900f, 720f);

        public Window_WildlifeTrailBoard(Map map)
        {
            this.map = map;
            doCloseX = true;
            absorbInputAroundWindow = true;
            resizeable = true;
        }

        public override void DoWindowContents(Rect rect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, rect.width, 32f), "Trail Leads");
            Text.Font = GameFont.Small;
            GUI.color = new Color(0.72f, 0.78f, 0.72f);
            Widgets.Label(new Rect(0f, 31f, rect.width, 24f),
                "Study signs left by departed animals, then send an exact-animal expedition or forget the trail.");
            GUI.color = Color.white;
            if (map == null || HerdsDefOf.Herds_WildlifeSign == null)
            {
                Widgets.Label(new Rect(0f, 64f, rect.width, 30f),
                    "Trail information is unavailable.");
                return;
            }

            int now = Find.TickManager.TicksGame;
            if (cachedCandidates == null || now >= nextCandidateRefreshTick)
            {
                cachedCandidates = Candidates();
                nextCandidateRefreshTick = now + 60;
            }
            List<TrailCandidate> candidates = cachedCandidates;
            int urgent = candidates.Count(value => value.Urgent);
            int reconstructed = candidates.Count(value => value.lead != null);
            float metricWidth = (rect.width - 16f) / 3f;
            DrawSummary(new Rect(0f, 62f, metricWidth, 62f), "Available Leads",
                candidates.Count.ToString(), new Color(0.25f, 0.62f, 0.51f),
                "Species with physical signs still present on this map.");
            DrawSummary(new Rect(metricWidth + 8f, 62f, metricWidth, 62f), "Urgent",
                urgent.ToString(), new Color(0.75f, 0.31f, 0.21f),
                "Leads containing predator signs or blood trails.");
            DrawSummary(new Rect((metricWidth + 8f) * 2f, 62f, metricWidth, 62f),
                "Reconstructed", reconstructed.ToString(), new Color(0.64f, 0.53f, 0.24f),
                "Trails interpreted by a colonist and available as map overlays.");

            Rect outer = new Rect(0f, 136f, rect.width, rect.height - 136f);
            float cardHeight = 126f;
            Rect view = new Rect(0f, 0f, outer.width - 18f,
                Mathf.Max(outer.height, candidates.Count * (cardHeight + 8f)));
            Widgets.BeginScrollView(outer, ref scroll, view);
            if (candidates.Count == 0)
            {
                Widgets.DrawMenuSection(new Rect(0f, 0f, view.width, 104f));
                Text.Font = GameFont.Medium;
                Widgets.Label(new Rect(14f, 12f, view.width - 28f, 28f),
                    "No Current Trail Leads");
                Text.Font = GameFont.Small;
                Widgets.Label(new Rect(14f, 47f, view.width - 28f, 42f),
                    "Wildlife will leave tracks, feeding sign, droppings, territory marks, and blood trails as animals move.");
            }
            int bestSkill = map.GetComponent<WildlifeFieldcraftMapComponent>()?.BestTrackerSkill ?? 0;
            for (int i = 0; i < candidates.Count; i++)
                DrawCandidate(new Rect(0f, i * (cardHeight + 8f), view.width, cardHeight),
                    candidates[i], bestSkill, i);
            Widgets.EndScrollView();
        }

        private List<TrailCandidate> Candidates()
        {
            WildlifeTrailMapComponent trails = map.GetComponent<WildlifeTrailMapComponent>();
            List<TrailCandidate> candidates = (trails?.AvailableLeadSigns() ??
                    new List<WildlifeSign>())
                .GroupBy(value => value.sourceAnimal)
                .Select(group => new TrailCandidate
                {
                    species = group.First().species,
                    signs = group.OrderByDescending(value => value.createdTick).ToList(),
                    lead = trails?.LeadFor(group.Key)
                })
                .OrderByDescending(value => value.Urgent)
                .ThenByDescending(value => value.lead != null)
                .ThenByDescending(value => value.Latest?.createdTick ?? value.lead?.createdTick ?? 0)
                .ToList();
            foreach (WildlifeTrailLead lead in trails?.TrailLeads ?? new List<WildlifeTrailLead>())
                if (lead?.species != null && !candidates.Any(candidate => candidate.lead == lead))
                    candidates.Add(new TrailCandidate
                    {
                        species = lead.species,
                        signs = new List<WildlifeSign>(),
                        lead = lead
                    });
            return candidates.OrderByDescending(value => value.Urgent)
                .ThenByDescending(value => value.lead != null)
                .ThenByDescending(value => value.Latest?.createdTick ?? value.lead?.createdTick ?? 0)
                .ToList();
        }

        private void DrawCandidate(Rect card, TrailCandidate candidate, int bestSkill, int index)
        {
            Widgets.DrawMenuSection(card);
            Color accent = candidate.Urgent ? new Color(0.84f, 0.28f, 0.18f) :
                candidate.lead != null ? new Color(0.24f, 0.69f, 0.56f) :
                new Color(0.51f, 0.52f, 0.35f);
            Widgets.DrawBoxSolid(new Rect(card.x, card.y, 5f, card.height), accent);
            string species = bestSkill >= 4 ? candidate.species.LabelCap.ToString() :
                "Unidentified Trail " + (index + 1);
            int age = Mathf.Max(0, Find.TickManager.TicksGame -
                (candidate.Latest?.createdTick ?? candidate.lead?.createdTick ?? 0));
            int group = Mathf.Max(1, Mathf.RoundToInt((float)candidate.signs.Average(value =>
                value.groupSize)));
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(card.x + 14f, card.y + 10f, card.width - 410f, 28f),
                species);
            Text.Font = GameFont.Small;
            string status = candidate.lead == null ? "Uninterpreted" :
                WildlifeTrailMapComponent.ConfidenceLabel(candidate.lead.confidence) +
                " confidence • " + candidate.lead.direction + " • " +
                WildlifeTrailMapComponent.StatusLabel(candidate.lead);
            Widgets.Label(new Rect(card.x + 14f, card.y + 41f, card.width - 410f, 24f),
                status);
            GUI.color = new Color(0.72f, 0.78f, 0.72f);
            Widgets.Label(new Rect(card.x + 14f, card.y + 69f, card.width - 410f, 42f),
                candidate.signs.Count + " visible clue" + (candidate.signs.Count == 1 ? "" : "s") +
                " • about " + group + " animal" + (group == 1 ? "" : "s") +
                " • latest " + age.ToStringTicksToPeriod() + " ago" +
                (candidate.Urgent ? "\nUrgent: predator sign or blood detected." : ""));
            GUI.color = Color.white;

            float buttonX = card.xMax - 388f;
            if (candidate.lead == null && Widgets.ButtonText(
                new Rect(buttonX, card.y + 18f, 376f, 34f), "Study"))
                ShowTrackerMenu(candidate);
            if (candidate.lead != null && Widgets.ButtonText(
                new Rect(buttonX, card.y + 18f, 120f, 34f), "Focus"))
            {
                if (candidate.Latest != null) WildlifeUI.Show(candidate.Latest);
                else WildlifeUI.Focus(candidate.lead.departureCell.IsValid
                    ? candidate.lead.departureCell : candidate.lead.predictedCell, map);
            }
            if (candidate.lead != null && Widgets.ButtonText(
                new Rect(buttonX + 128f, card.y + 18f, 120f, 34f), "Send Expedition"))
                SendExpedition(candidate.lead);
            if (candidate.lead != null && Widgets.ButtonText(
                new Rect(buttonX + 256f, card.y + 18f, 120f, 34f), "Forget Trail"))
                ConfirmForget(candidate.lead);

            Rect hint = new Rect(buttonX, card.y + 62f, 376f, 48f);
            Widgets.DrawHighlight(hint);
            Widgets.Label(hint.ContractedBy(7f), candidate.lead == null
                ? "Study the sign before any trail action becomes available."
                : "The animal has left the map. Send an exact-animal expedition or forget the trail.");
            TooltipHandler.TipRegion(card, candidate.Urgent
                ? "Urgent leads are sorted first. Predator signs can reveal a defended range; blood may indicate wounded quarry or recent violence."
                : candidate.lead?.lastOutcome.NullOrEmpty() == false
                    ? candidate.lead.lastOutcome
                    : "Trail leads are grouped by species so repeated physical signs become one readable activity pattern.");
        }

        private void ShowTrackerMenu(TrailCandidate candidate)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            List<WildlifeSign> usableSigns = candidate.signs.Where(sign => sign?.Spawned == true)
                .OrderByDescending(sign => sign.createdTick).ToList();
            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned.OrderByDescending(value =>
                value.skills?.GetSkill(SkillDefOf.Animals)?.Level ?? 0))
            {
                Pawn tracker = pawn;
                WildlifeSign sign = usableSigns.FirstOrDefault(value =>
                    !value.studiedBy.Contains(tracker));
                int skill = tracker.skills?.GetSkill(SkillDefOf.Animals)?.Level ?? 0;
                string label = tracker.LabelShortCap + " — Animals Skill " + skill;
                if (sign == null)
                {
                    options.Add(new FloatMenuOption(label + " (all clues studied)", null));
                    continue;
                }
                WildlifeSign selectedSign = sign;
                if (tracker.Downed || tracker.InMentalState ||
                    !tracker.CanReserveAndReach(selectedSign, PathEndMode.Touch, Danger.Some))
                    options.Add(new FloatMenuOption(label + " (unavailable)", null));
                else
                    options.Add(new FloatMenuOption(label, () =>
                    {
                        Job study = JobMaker.MakeJob(HerdsDefOf.Herds_StudyWildlifeSign,
                            selectedSign);
                        study.playerForced = true;
                        if (tracker.jobs.TryTakeOrderedJob(study, JobTag.Misc))
                        {
                            WildlifeUI.Show(selectedSign);
                        }
                        else Messages.Message(tracker.LabelShortCap +
                            " could not begin studying the wildlife sign.", tracker,
                            MessageTypeDefOf.RejectInput, false);
                    }));
            }
            if (options.Count == 0)
                options.Add(new FloatMenuOption("No colonists are available.", null));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void ConfirmForget(WildlifeTrailLead lead)
        {
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "Permanently forget this trail?", () =>
                {
                    map.GetComponent<WildlifeTrailMapComponent>()?.Forget(lead);
                    cachedCandidates = null;
                }, destructive: true, title: "Forget Trail"));
        }

        private void SendExpedition(WildlifeTrailLead lead)
        {
            HuntingExpeditionMapComponent expeditions = map.GetComponent<HuntingExpeditionMapComponent>();
            TrailHuntOpportunity opportunity = expeditions?.TrailHuntOpportunities
                .FirstOrDefault(value => value?.targetAnimal == lead.targetAnimal);
            ExpeditionDestination destination = expeditions?.NearbyTrailDestination(lead.targetAnimal);
            if (opportunity == null || destination == null)
            {
                Messages.Message("This trail no longer has a valid nearby expedition target.",
                    MessageTypeDefOf.RejectInput, false);
                return;
            }
            Find.WindowStack.Add(new Window_HuntingExpeditionSetup(map, destination,
                lead.species, ExpeditionObjective.Hunt, lead.targetAnimal));
        }

        private static void DrawSummary(Rect rect, string title, string value, Color accent,
            string tooltip)
        {
            Widgets.DrawMenuSection(rect);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, 5f, rect.height), accent);
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.72f, 0.78f, 0.72f);
            Widgets.Label(new Rect(rect.x + 12f, rect.y + 7f, rect.width - 20f, 18f),
                title.ToUpperInvariant());
            GUI.color = Color.white;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x + 12f, rect.y + 26f, rect.width - 20f, 28f), value);
            Text.Font = GameFont.Small;
            TooltipHandler.TipRegion(rect, tooltip);
        }
    }

    public static class WildlifeTrailDebugActions
    {
        [DebugAction("Wildlife", "Create test wildlife trail",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void CreateTestTrail()
        {
            Map map = Find.CurrentMap;
            string result = map?.GetComponent<WildlifeTrailMapComponent>()?.DebugCreateTrail() ??
                "Trail component unavailable.";
            Messages.Message("Trail test: " + result, MessageTypeDefOf.NeutralEvent, false);
        }
    }
}
