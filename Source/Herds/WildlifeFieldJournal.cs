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
    public enum WildlifeOpportunityKind
    {
        Migration,
        NestingSeason,
        InjuredAnimal,
        PredatorIncursion,
        RareSighting,
        DiseaseConcern,
        PredatorStalk,
        LookoutRotation,
        SocialBond,
        AnimalRivalry,
        HomeDiscovery,
        SignalResponse
    }

    public enum WildlifeMomentResponse
    {
        None,
        Observe,
        Track,
        Protect,
        Hunt,
        Ignore
    }

    public enum WildlifeStewardProjectKind
    {
        RestoreSpecies,
        MigrationCorridor,
        PopulationControl,
        RanchDefense,
        ProtectMigration,
        SuppressPredators,
        AttractRareBirds
    }

    public sealed class WildlifeJournalEntry : IExposable
    {
        public ThingDef species;
        public bool completionRewardGranted;

        public void ExposeData()
        {
            Scribe_Defs.Look(ref species, "species");
            Scribe_Values.Look(ref completionRewardGranted, "completionRewardGranted");
        }
    }

    public sealed class WildlifeOpportunityRecord : IExposable
    {
        public WildlifeOpportunityKind kind;
        public ThingDef species;
        public Pawn animal;
        public int startedTick;
        public int availableUntilTick;
        public int expiresTick;
        public bool accepted;
        public string description;
        public Pawn otherAnimal;
        public Pawn responder;
        public Thing evidence;
        public WildlifeMomentResponse response;
        public IntVec3 focusCell;
        public string eventKey;
        public int responseStartedTick;
        public bool continuedAsTrail;
        public string failureReason;
        public int wildlifeWitnesses;
        public bool protectionDeclared;

        public void ExposeData()
        {
            Scribe_Values.Look(ref kind, "kind");
            Scribe_Defs.Look(ref species, "species");
            Scribe_References.Look(ref animal, "animal");
            Scribe_Values.Look(ref startedTick, "startedTick");
            Scribe_Values.Look(ref availableUntilTick, "availableUntilTick");
            Scribe_Values.Look(ref expiresTick, "expiresTick");
            Scribe_Values.Look(ref accepted, "accepted");
            Scribe_Values.Look(ref description, "description");
            Scribe_References.Look(ref otherAnimal, "otherAnimal");
            Scribe_References.Look(ref responder, "responder");
            Scribe_References.Look(ref evidence, "evidence");
            Scribe_Values.Look(ref response, "response", WildlifeMomentResponse.None);
            Scribe_Values.Look(ref focusCell, "focusCell");
            Scribe_Values.Look(ref eventKey, "eventKey");
            Scribe_Values.Look(ref responseStartedTick, "responseStartedTick");
            Scribe_Values.Look(ref continuedAsTrail, "continuedAsTrail");
            Scribe_Values.Look(ref failureReason, "failureReason");
            Scribe_Values.Look(ref wildlifeWitnesses, "wildlifeWitnesses");
            Scribe_Values.Look(ref protectionDeclared, "protectionDeclared");
        }
    }

    public sealed class WildlifeMomentKeyRecord : IExposable
    {
        public string key;
        public int expiresTick;
        public void ExposeData()
        {
            Scribe_Values.Look(ref key, "key");
            Scribe_Values.Look(ref expiresTick, "expiresTick");
        }
    }

    public sealed class WildlifeMomentSentinelRecord : IExposable
    {
        public int groupId;
        public Pawn sentinel;
        public void ExposeData()
        {
            Scribe_Values.Look(ref groupId, "groupId");
            Scribe_References.Look(ref sentinel, "sentinel");
        }
    }

    public sealed class WildlifeMomentOutcomeRecord : IExposable
    {
        public WildlifeOpportunityKind kind;
        public WildlifeMomentResponse response;
        public ThingDef species;
        public int tick;
        public bool success;
        public string text;
        public void ExposeData()
        {
            Scribe_Values.Look(ref kind, "kind");
            Scribe_Values.Look(ref response, "response");
            Scribe_Defs.Look(ref species, "species");
            Scribe_Values.Look(ref tick, "tick");
            Scribe_Values.Look(ref success, "success");
            Scribe_Values.Look(ref text, "text");
        }
    }

    public sealed class WildlifeStewardProjectRecord : IExposable
    {
        public WildlifeStewardProjectKind kind;
        public ThingDef species;
        public float progress;
        public int startedTick;

        public void ExposeData()
        {
            Scribe_Values.Look(ref kind, "kind");
            Scribe_Defs.Look(ref species, "species");
            Scribe_Values.Look(ref progress, "progress");
            Scribe_Values.Look(ref startedTick, "startedTick");
        }
    }

    public sealed class WildlifeFieldJournalMapComponent : MapComponent
    {
        private List<WildlifeJournalEntry> entries = new List<WildlifeJournalEntry>();
        private WildlifeOpportunityRecord opportunity;
        private WildlifeStewardProjectRecord project;
        private int completedProjects;
        private int nextUpdateTick;
        private int nextOpportunityTick;
        private int wildlifeMomentVersion;
        private int lastSignalTraceId;
        private List<WildlifeMomentKeyRecord> recentMomentKeys = new List<WildlifeMomentKeyRecord>();
        private List<WildlifeMomentSentinelRecord> sentinelStates = new List<WildlifeMomentSentinelRecord>();
        private List<WildlifeMomentOutcomeRecord> momentHistory = new List<WildlifeMomentOutcomeRecord>();
        private const int MomentHourTicks = 2500;
        private const int ResponseCompletionTicks = 75000;

        public WildlifeFieldJournalMapComponent(Map map) : base(map) { }
        public IReadOnlyList<WildlifeJournalEntry> Entries => entries;
        public WildlifeOpportunityRecord Opportunity => opportunity;
        public IReadOnlyList<WildlifeMomentOutcomeRecord> MomentHistory => momentHistory;
        public WildlifeStewardProjectRecord Project => project;
        public int CompletedEntries => entries.Count(entry => entry.completionRewardGranted);
        public int CompletedProjects => completedProjects;
        public float OutcomeBonus => Mathf.Min(0.10f, CompletedEntries * 0.005f + completedProjects * 0.015f);
        public float HuntingSkillBonus => Mathf.Min(2f, CompletedEntries * 0.10f + completedProjects * 0.25f);

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref entries, "wildlifeJournalEntries", LookMode.Deep);
            Scribe_Deep.Look(ref opportunity, "wildlifeOpportunity");
            Scribe_Deep.Look(ref project, "wildlifeStewardProject");
            Scribe_Values.Look(ref completedProjects, "completedWildlifeProjects");
            Scribe_Values.Look(ref nextUpdateTick, "nextWildlifeJournalUpdate");
            Scribe_Values.Look(ref nextOpportunityTick, "nextWildlifeOpportunity");
            Scribe_Values.Look(ref wildlifeMomentVersion, "wildlifeMomentVersion", 0);
            Scribe_Values.Look(ref lastSignalTraceId, "lastWildlifeMomentSignalTrace", 0);
            Scribe_Collections.Look(ref recentMomentKeys, "recentWildlifeMomentKeys", LookMode.Deep);
            Scribe_Collections.Look(ref sentinelStates, "wildlifeMomentSentinels", LookMode.Deep);
            Scribe_Collections.Look(ref momentHistory, "wildlifeMomentHistory", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                entries = entries?.Where(entry => entry?.species?.race?.Animal == true).ToList() ?? new List<WildlifeJournalEntry>();
                recentMomentKeys = recentMomentKeys?.Where(value => value != null && !value.key.NullOrEmpty()).ToList() ??
                    new List<WildlifeMomentKeyRecord>();
                sentinelStates = sentinelStates?.Where(value => value != null).ToList() ??
                    new List<WildlifeMomentSentinelRecord>();
                momentHistory = momentHistory?.Where(value => value?.species?.race?.Animal == true)
                    .OrderByDescending(value => value.tick).Take(20).ToList() ??
                    new List<WildlifeMomentOutcomeRecord>();
                if (project != null && !ValidProject(project)) project = null;
                if (wildlifeMomentVersion < 1)
                {
                    wildlifeMomentVersion = 1;
                    nextOpportunityTick = (Find.TickManager?.TicksGame ?? 0) + 2500;
                    opportunity = null;
                }
                if (wildlifeMomentVersion < 2)
                {
                    wildlifeMomentVersion = 2;
                    if (opportunity != null)
                    {
                        opportunity.availableUntilTick = opportunity.startedTick +
                            MomentAvailabilityTicks(opportunity.kind,
                                opportunity.animal?.thingIDNumber ?? 0,
                                opportunity.startedTick);
                        if (opportunity.response == WildlifeMomentResponse.None)
                            opportunity.expiresTick = opportunity.availableUntilTick;
                    }
                }
            }
        }

        public override void MapComponentTick()
        {
            HerdsSettings settings = HerdsMod.Settings;
            if (settings == null || (!settings.enableFieldJournal && !settings.enableDynamicWildlifeOpportunities && !settings.enableStewardProjects)) return;
            int now = Find.TickManager.TicksGame;
            if (now < nextUpdateTick) return;
            nextUpdateTick = now + 2500;
            if (settings.enableFieldJournal) UpdateJournal();
            if (settings.enableDynamicWildlifeOpportunities)
            {
                UpdateOpportunity(now);
                if (opportunity != null)
                {
                    int deadline = opportunity.response == WildlifeMomentResponse.None
                        ? AvailabilityDeadline(opportunity) : opportunity.expiresTick;
                    if (deadline > now) nextUpdateTick = Mathf.Min(nextUpdateTick, deadline);
                }
            }
            else opportunity = null;
            if (settings.enableStewardProjects) FinishProjectIfReady();
            else project = null;
        }

        public override void MapComponentDraw()
        {
            base.MapComponentDraw();
            if (HerdsMod.Settings?.enableDynamicWildlifeOpportunities != true ||
                Find.CurrentMap != map || opportunity == null) return;
            IntVec3 cell = opportunity.animal?.Spawned == true
                ? opportunity.animal.Position : opportunity.focusCell;
            if (!cell.IsValid || !cell.InBounds(map) ||
                !Find.CameraDriver.CurrentViewRect.Contains(cell)) return;
            float pulse = 2.2f + Mathf.Sin(Find.TickManager.TicksGame * 0.045f) * 0.7f;
            Color color = MomentColor(opportunity.kind);
            color.a = opportunity.response == WildlifeMomentResponse.None ? 0.62f : 0.9f;
            GenDraw.DrawRadiusRing(cell, pulse, color);
            if (opportunity.otherAnimal?.Spawned == true)
                GenDraw.DrawLineBetween(cell.ToVector3Shifted(),
                    opportunity.otherAnimal.Position.ToVector3Shifted(),
                    opportunity.kind == WildlifeOpportunityKind.PredatorStalk
                        ? SimpleColor.Red : SimpleColor.Yellow);
            if (opportunity.evidence?.Spawned == true)
                GenDraw.DrawLineBetween(cell.ToVector3Shifted(),
                    opportunity.evidence.Position.ToVector3Shifted(), SimpleColor.Cyan);
        }

        public int JournalStage(ThingDef species)
        {
            int level = HuntingKnowledgeMapComponent.ColonyLevel(species);
            return level <= 0 ? 0 : level == 1 ? 1 : level == 2 ? 2 : 5;
        }

        public string JournalStageLabel(ThingDef species)
        {
            int stage = JournalStage(species);
            return stage <= 0 ? "Unknown" : stage == 1 ? "Identified" : stage == 2 ? "Tracks documented" :
                stage == 3 ? "Habitat understood" : stage == 4 ? "Seasonal behavior known" : "Entry complete";
        }

        public string JournalTooltip(ThingDef species)
        {
            int level = HuntingKnowledgeMapComponent.ColonyLevel(species);
            return species.LabelCap + "\n\n" + JournalStageLabel(species) +
                "\n\nEntry discoveries:" +
                "\n• Identity and basic behavior: " + Mark(level >= 1) +
                "\n• Tracks and signs: " + Mark(level >= 2) +
                "\n• Habitat and feeding preferences: " + Mark(level >= 3) +
                "\n• Seasonal activity and preferred bait: " + Mark(level >= 3) +
                "\n• Complete field entry: " + Mark(level >= 3) +
                "\n\nCompleted entries permanently improve colony hunting and wildlife field outcomes.";
        }

        public void AcceptOpportunity()
        {
            ChooseMomentResponse(WildlifeMomentResponse.Observe);
        }

        public bool ResponseAvailable(WildlifeMomentResponse response, out string reason)
        {
            reason = null;
            if (opportunity == null)
            {
                reason = "There is no active Wildlife Moment.";
                return false;
            }
            if (opportunity.response != WildlifeMomentResponse.None)
            {
                reason = "The colony has already chosen a response.";
                return false;
            }
            if ((Find.TickManager?.TicksGame ?? 0) >= AvailabilityDeadline(opportunity))
            {
                reason = "This Wildlife Moment has passed.";
                return false;
            }
            Pawn target = ResponseTarget(response);
            bool usableTrail = response == WildlifeMomentResponse.Track &&
                opportunity.evidence is WildlifeSign evidence && evidence.Spawned;
            if (response != WildlifeMomentResponse.Ignore &&
                !usableTrail &&
                (target?.Spawned != true || target.Dead))
            {
                reason = "The animal is no longer present.";
                return false;
            }
            if (response == WildlifeMomentResponse.Track &&
                (!ResearchAllowsResponse(response) ||
                 HerdsMod.Settings.enableTrackingSigns != true))
            {
                reason = !ResearchAllowsResponse(response)
                    ? "Tracking Wildlife Moments requires additional research."
                    : "Fading Tracks and Wildlife Signs are disabled.";
                return false;
            }
            if (response == WildlifeMomentResponse.Hunt &&
                !ResearchAllowsResponse(response))
            {
                reason = "Hunting a Wildlife Moment requires Organized Hunting.";
                return false;
            }
            if (response == WildlifeMomentResponse.Protect &&
                HerdsMod.Settings.enableNotableAnimals != true)
            {
                reason = "Notable Animals are disabled.";
                return false;
            }
            if (response == WildlifeMomentResponse.Protect && opportunity.protectionDeclared)
            {
                reason = "This animal is already protected. It can still be observed or tracked.";
                return false;
            }
            return true;
        }

        public void ChooseMomentResponse(WildlifeMomentResponse response)
        {
            if (!ResponseAvailable(response, out string reason))
            {
                Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (response == WildlifeMomentResponse.Ignore)
            {
                if (opportunity.protectionDeclared)
                {
                    opportunity.response = WildlifeMomentResponse.Protect;
                    ResolveOpportunity(true);
                }
                else
                {
                    opportunity.response = response;
                    ResolveOpportunity(false, true);
                }
                return;
            }
            if (response == WildlifeMomentResponse.Observe ||
                response == WildlifeMomentResponse.Track)
            {
                ShowResponderMenu(response);
                return;
            }
            StartMomentResponse(response, null);
        }

        private void ShowResponderMenu(WildlifeMomentResponse response)
        {
            Pawn target = ResponseTarget(response);
            Thing destination = response == WildlifeMomentResponse.Track &&
                opportunity?.evidence?.Spawned == true ? opportunity.evidence : target;
            WildlifeFieldcraftMapComponent fieldcraft =
                map.GetComponent<WildlifeFieldcraftMapComponent>();
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            IReadOnlyList<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn colonist = colonists[i];
                string disabled = colonist.Downed ? "Downed" :
                    colonist.InMentalState ? "In a mental state" :
                    destination == null ? "The trail is unavailable" :
                    response == WildlifeMomentResponse.Observe
                        ? (!TryFindObservationCell(colonist, target, out _)
                            ? "No safe observation position" : null) :
                    response == WildlifeMomentResponse.Track &&
                        opportunity?.evidence?.Spawned != true
                        ? (fieldcraft?.CanSafelyTrack(target, colonist) != true
                            ? "No safely approachable sign" : null) :
                    !colonist.CanReach(destination, PathEndMode.Touch, Danger.Some)
                        ? "Cannot reach the evidence" : null;
                if (disabled != null)
                    options.Add(new FloatMenuOption(colonist.LabelShortCap + " (" + disabled + ")", null));
                else
                {
                    Pawn chosen = colonist;
                    options.Add(new FloatMenuOption(colonist.LabelShortCap + " - Animals " +
                        (colonist.skills?.GetSkill(SkillDefOf.Animals)?.Level ?? 0),
                        () => StartMomentResponse(response, chosen)));
                }
            }
            if (options.Count == 0)
                options.Add(new FloatMenuOption("No available colonist", null));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void StartMomentResponse(WildlifeMomentResponse response, Pawn responder)
        {
            if (opportunity == null) return;
            Pawn target = ResponseTarget(response);
            if (response == WildlifeMomentResponse.Protect)
            {
                NotableWildlifeMapComponent notables = map.GetComponent<NotableWildlifeMapComponent>();
                NotableAnimalRecord record = notables?.MakeNotable(target, true);
                if (record == null)
                {
                    Messages.Message("The focal animal could not be declared protected.",
                        MessageTypeDefOf.RejectInput, false);
                    return;
                }
                notables.SetIntent(record, NotableAnimalIntent.Protect);
                opportunity.protectionDeclared = true;
                opportunity.response = WildlifeMomentResponse.None;
                opportunity.accepted = false;
                opportunity.responder = null;
                WildlifeExperience.Record("Wildlife Moment", "The colony declared " +
                    target.LabelShortCap + " protected while continuing to observe the moment.", target);
                Messages.Message(target.LabelShortCap +
                    " is now protected. The Wildlife Moment remains open for observation or tracking.",
                    target, MessageTypeDefOf.PositiveEvent, false);
                return;
            }
            opportunity.response = response;
            opportunity.accepted = true;
            opportunity.responder = responder;
            opportunity.responseStartedTick = Find.TickManager.TicksGame;
            opportunity.expiresTick = opportunity.responseStartedTick + ResponseCompletionTicks;
            WildlifeExperience.Record("Wildlife Moment", "The colony chose to " +
                response.ToString().ToLowerInvariant() + " during " +
                OpportunityLabel(opportunity.kind).ToLowerInvariant() + ".", target);
            if (response == WildlifeMomentResponse.Observe)
            {
                if (!TryFindObservationCell(responder, target, out IntVec3 observationCell))
                {
                    opportunity.response = WildlifeMomentResponse.None;
                    opportunity.accepted = false;
                    opportunity.responder = null;
                    Messages.Message("No safe observation position is reachable.",
                        responder, MessageTypeDefOf.RejectInput, false);
                    return;
                }
                Job job = JobMaker.MakeJob(HerdsDefOf.Herds_ObserveWildlifeMoment,
                    target, observationCell);
                job.playerForced = true;
                responder.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                Messages.Message(responder.LabelShortCap + " is moving to observe " +
                    OpportunityLabel(opportunity.kind).ToLowerInvariant() + ".",
                    responder, MessageTypeDefOf.NeutralEvent, false);
            }
            else if (response == WildlifeMomentResponse.Track)
            {
                WildlifeSign sign = opportunity.evidence as WildlifeSign;
                if (sign?.Spawned != true)
                    sign = map.GetComponent<WildlifeFieldcraftMapComponent>()?
                        .CreateSafeTrackingSign(target, responder);
                if (sign == null)
                {
                    opportunity.response = WildlifeMomentResponse.None;
                    opportunity.accepted = false;
                    opportunity.responder = null;
                    Messages.Message("No usable sign could be found.", MessageTypeDefOf.RejectInput, false);
                    return;
                }
                opportunity.evidence = sign;
                Job study = JobMaker.MakeJob(HerdsDefOf.Herds_StudyWildlifeSign, sign);
                study.playerForced = true;
                responder.jobs.TryTakeOrderedJob(study, JobTag.Misc);
            }
            else if (response == WildlifeMomentResponse.Hunt)
            {
                Building_HuntingSpot spot = map.listerThings
                    .ThingsOfDef(HerdsDefOf.Herds_HuntingSpot)
                    .OfType<Building_HuntingSpot>().FirstOrDefault(building =>
                        building.Spawned && map.GetComponent<WildlifeHuntCoordinator>()?
                            .TryGetSpotStatus(building, out _, out _, out _, out _) != true);
                if (spot != null && HerdsMod.Settings.enableHuntingChanges &&
                    WildlifeProgression.Unlocked(WildlifeCapability.BasicHunting))
                {
                    Find.WindowStack.Add(new Window_FieldcraftHuntSetup(spot, prey: target));
                    Messages.Message("Plan the Wildlife Moment hunt from " + spot.LabelShortCap + ".",
                        spot, MessageTypeDefOf.NeutralEvent, false);
                }
                else
                {
                    Designation existing = map.designationManager.DesignationOn(target, DesignationDefOf.Hunt);
                    if (existing == null)
                        map.designationManager.AddDesignation(new Designation(target, DesignationDefOf.Hunt));
                    Messages.Message(target.LabelShortCap + " has been designated for hunting.",
                        target, MessageTypeDefOf.CautionInput, false);
                }
            }
        }

        public void CompleteMomentObservation(Pawn observer, Pawn animal)
        {
            if (opportunity?.response != WildlifeMomentResponse.Observe ||
                opportunity.responder != observer || ResponseTarget(WildlifeMomentResponse.Observe) != animal) return;
            opportunity.wildlifeWitnesses = RecordObservationWitnesses(observer, animal);
            ResolveOpportunity(true);
        }

        private int RecordObservationWitnesses(Pawn observer, Pawn focalAnimal)
        {
            if (HerdsMod.Settings?.enableAnimalMemory != true ||
                observer?.Spawned != true) return 0;
            int witnessed = 0;
            IEnumerable<Pawn> witnesses = map.mapPawns.AllPawnsSpawned.Where(value =>
                value?.RaceProps?.Animal == true && value.Faction != Faction.OfPlayer &&
                !value.Dead && !value.Downed && value != focalAnimal &&
                value.Position.DistanceToSquared(observer.Position) <= 1225 &&
                GenSight.LineOfSight(value.Position, observer.Position, map))
                .OrderBy(value => value.Position.DistanceToSquared(observer.Position))
                .Take(16);
            foreach (Pawn witness in witnesses)
            {
                float distance = witness.Position.DistanceTo(observer.Position);
                float strength = Mathf.Lerp(0.65f, 0.25f,
                    Mathf.InverseLerp(8f, 35f, distance));
                if (witness == opportunity?.otherAnimal) strength += 0.20f;
                WildlifeMemoryUtility.Remember(witness, observer,
                    AnimalMemoryKind.QuietObservation, strength);
                witnessed++;
            }
            if (witnessed > 0)
            {
                WildlifeExperience.Record("Wildlife Witnesses",
                    witnessed + " nearby wild animal" + (witnessed == 1 ? "" : "s") +
                    " noticed " + observer.LabelShortCap + "'s quiet observation.",
                    focalAnimal);
                WildlifeTestLog.Count("memory.observationWitnesses");
                if (WildlifeTestLog.Enabled)
                    WildlifeTestLog.Write("ObservationWitnesses",
                        "observer=" + observer.thingIDNumber + " witnesses=" + witnessed,
                        focalAnimal, observer);
            }
            return witnessed;
        }

        public void CompleteMomentTracking(Pawn tracker, WildlifeSign sign)
        {
            if (opportunity?.response != WildlifeMomentResponse.Track ||
                opportunity.responder != tracker || opportunity.evidence != sign) return;
            ResolveOpportunity(true);
        }

        public bool ReferencesAnimal(Pawn animal) =>
            opportunity != null && animal != null &&
            (opportunity.animal == animal || opportunity.otherAnimal == animal);

        public void NotifyAnimalDeparture(Pawn animal, IntVec3 edge)
        {
            if (!ReferencesAnimal(animal) || opportunity == null) return;
            int now = Find.TickManager.TicksGame;
            if (animal != opportunity.animal)
            {
                opportunity.failureReason = "A second animal involved in the moment left the area.";
                opportunity.expiresTick = now + 1;
                return;
            }
            WildlifeSign sign = opportunity.evidence as WildlifeSign;
            if (sign?.Spawned != true)
                sign = map.GetComponent<WildlifeFieldcraftMapComponent>()?.DebugCreateSign(animal);
            if (sign != null)
            {
                sign.sourceAnimal = animal;
                opportunity.evidence = sign;
            }
            opportunity.focusCell = edge;
            if (opportunity.response != WildlifeMomentResponse.None)
                opportunity.expiresTick = Mathf.Max(opportunity.expiresTick, now + 30000);
            opportunity.continuedAsTrail = sign != null;
            opportunity.description += "\n\nThe animal left the local map, but fresh evidence reaches the boundary.";

            if (opportunity.response == WildlifeMomentResponse.Observe &&
                opportunity.responder?.Spawned == true && sign != null)
            {
                opportunity.response = WildlifeMomentResponse.Track;
                opportunity.responseStartedTick = now;
                Job study = JobMaker.MakeJob(HerdsDefOf.Herds_StudyWildlifeSign, sign);
                study.playerForced = true;
                opportunity.responder.jobs.TryTakeOrderedJob(study, JobTag.Misc);
                Messages.Message(opportunity.responder.LabelShortCap +
                    " lost sight of the animal, but the observation has become a trackable trail.",
                    sign, MessageTypeDefOf.CautionInput, false);
            }
            else if (opportunity.response == WildlifeMomentResponse.Track && sign != null)
            {
                Messages.Message("The animal left the map, but its active trail remains usable.",
                    sign, MessageTypeDefOf.NeutralEvent, false);
            }
            else if (opportunity.response == WildlifeMomentResponse.None && sign != null)
            {
                Messages.Message("This Wildlife Moment now continues as a fresh trail at the map edge.",
                    sign, MessageTypeDefOf.NeutralEvent, false);
            }
            else if (opportunity.response != WildlifeMomentResponse.Ignore)
            {
                opportunity.failureReason = "The focal animal escaped beyond the colony map.";
            }
        }

        private Pawn ResponseTarget(WildlifeMomentResponse response)
        {
            if (opportunity == null) return null;
            return response == WildlifeMomentResponse.Hunt &&
                opportunity.kind == WildlifeOpportunityKind.PredatorStalk &&
                opportunity.otherAnimal != null
                ? opportunity.otherAnimal : opportunity.animal;
        }

        public void StartProject(WildlifeStewardProjectKind kind, ThingDef species)
        {
            project = new WildlifeStewardProjectRecord
            {
                kind = kind,
                species = species,
                progress = 0f,
                startedTick = Find.TickManager.TicksGame
            };
            WildlifeExperience.Record("Steward Project", "Started " + ProjectLabel(kind) + " for " + species.LabelCap + ".");
            Messages.Message("Wildlife project started: " + ProjectLabel(kind) + " — " + species.LabelCap + ".", MessageTypeDefOf.PositiveEvent, false);
        }

        public void CancelProject()
        {
            if (project == null) return;
            WildlifeExperience.Record("Steward Project", ProjectLabel(project.kind) + " was cancelled.", null, true);
            project = null;
        }

        public bool CanStartProject(WildlifeStewardProjectKind kind, ThingDef species,
            out string reason)
        {
            reason = null;
            if (species?.race?.Animal != true)
            {
                reason = "Choose a known animal species.";
                return false;
            }
            if (kind != WildlifeStewardProjectKind.RestoreSpecies) return true;
            RegionalSpeciesRecord record = map.GetComponent<RegionalWildlifeMapComponent>()?
                .Records.FirstOrDefault(value => value.species == species);
            bool declined = RestoreSpeciesEligible(record);
            if (declined) return true;
            reason = "Restore a Species becomes available after this species is regionally scarce, locally depleted, or has fallen by at least 25% since the previous estimate.";
            return false;
        }

        internal static bool RestoreSpeciesEligible(RegionalSpeciesRecord record) =>
            record != null && (record.consequenceState == 1 ||
                record.consequenceState == 3 || record.previousPopulation > 0f &&
                record.population <= record.previousPopulation * 0.75f);

        public bool AssignProjectWork(Pawn worker)
        {
            if (project == null || worker?.Spawned != true || worker.Downed ||
                worker.InMentalState || HerdsDefOf.Herds_PerformStewardshipProject == null)
                return false;
            IntVec3 cell = ProjectWorkCell(worker);
            if (!cell.IsValid || !worker.CanReach(cell, PathEndMode.OnCell, Danger.Some))
                return false;
            Job job = JobMaker.MakeJob(HerdsDefOf.Herds_PerformStewardshipProject, cell);
            job.playerForced = true;
            return worker.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        public void CompleteProjectWork(Pawn worker)
        {
            if (project == null || worker == null) return;
            float gain = 0.12f;
            bool HasTool(WildlifeToolKind kind) => map.listerThings.AllThings
                .OfType<Building_WildlifeTool>().Any(tool => tool.Kind == kind && tool.active);
            if (project.kind == WildlifeStewardProjectKind.RestoreSpecies &&
                (HasTool(WildlifeToolKind.HabitatRestoration) || HasTool(WildlifeToolKind.Reserve)))
                gain += 0.08f;
            else if ((project.kind == WildlifeStewardProjectKind.MigrationCorridor ||
                      project.kind == WildlifeStewardProjectKind.ProtectMigration) &&
                     HasTool(WildlifeToolKind.MigrationCorridor)) gain += 0.08f;
            else if ((project.kind == WildlifeStewardProjectKind.RanchDefense ||
                      project.kind == WildlifeStewardProjectKind.SuppressPredators) &&
                     HasTool(WildlifeToolKind.PredatorDeterrent)) gain += 0.07f;
            else if (project.kind == WildlifeStewardProjectKind.AttractRareBirds &&
                     (HasTool(WildlifeToolKind.HabitatRestoration) ||
                      HasTool(WildlifeToolKind.WaterSource))) gain += 0.07f;
            int skill = worker.skills?.GetSkill(SkillDefOf.Animals)?.Level ?? 0;
            project.progress = Mathf.Clamp01(project.progress + gain + skill * 0.003f);
            map.GetComponent<HuntingKnowledgeMapComponent>()?.Learn(worker, project.species, 8f);
            WildlifeExperience.Record("Steward Project", worker.LabelShortCap + " completed fieldwork for " +
                ProjectLabel(project.kind).ToLowerInvariant() + ".", worker);
            FinishProjectIfReady();
        }

        private IntVec3 ProjectWorkCell(Pawn worker)
        {
            Pawn animal = map.mapPawns.AllPawnsSpawned.FirstOrDefault(value =>
                value?.def == project.species && !value.Dead);
            if (animal != null)
                return CellFinder.RandomClosewalkCellNear(animal.Position, map, 8);
            Building_WildlifeTool tool = map.listerThings.AllThings.OfType<Building_WildlifeTool>()
                .Where(value => value.active).OrderBy(value =>
                    value.Position.DistanceToSquared(worker.Position)).FirstOrDefault();
            return tool?.Position ?? CellFinder.RandomClosewalkCellNear(map.Center, map, 20);
        }

        public void NotifyHuntKill(ThingDef species)
        {
            if (project?.kind == WildlifeStewardProjectKind.PopulationControl && project.species == species)
                project.progress = Mathf.Clamp01(project.progress + 0.20f);
        }

        public void ResolveLocalHuntReward(IEnumerable<Pawn> hunters, Pawn prey)
        {
            if (HerdsMod.Settings.enableHuntRewards != true || prey == null) return;
            List<Pawn> participants = hunters?.Where(pawn => pawn != null).ToList() ?? new List<Pawn>();
            int proficiency = participants.Select(pawn =>
                map.GetComponent<HuntingKnowledgeMapComponent>()?.WildlifeProficiencyLevel(pawn) ?? 0).DefaultIfEmpty(0).Max();
            float skill = participants.Select(pawn => ColonistHuntingUtility.HuntingSkill(pawn, prey.def)).DefaultIfEmpty(0f).Max();
            if (!Rand.Chance(Mathf.Clamp(0.06f + proficiency * 0.04f + skill * 0.008f + OutcomeBonus, 0.06f, 0.35f))) return;
            if (HerdsDefOf.Herds_WildlifeTrophy != null && Rand.Chance(0.45f) && prey.PositionHeld.IsValid)
            {
                Thing trophy = ThingMaker.MakeThing(HerdsDefOf.Herds_WildlifeTrophy);
                GenPlace.TryPlaceThing(trophy, prey.PositionHeld, map, ThingPlaceMode.Near);
                Messages.Message("The hunters recovered a wildlife trophy from " + prey.LabelShortCap + ".", trophy, MessageTypeDefOf.PositiveEvent, false);
                WildlifeExperience.Record("Hunt Reward", "A wildlife trophy was recovered.", trophy);
            }
            else
            {
                HuntingKnowledgeMapComponent knowledge = map.GetComponent<HuntingKnowledgeMapComponent>();
                for (int i = 0; i < participants.Count; i++) knowledge?.Learn(participants[i], prey.def, 18f, true);
                Messages.Message("The hunters preserved useful specimens from " + prey.LabelShortCap + ".", prey, MessageTypeDefOf.PositiveEvent, false);
                WildlifeExperience.Record("Hunt Reward", "Useful wildlife specimens improved Animal Knowledge.", prey);
            }
        }

        public List<string> DebugOverviewLines() => new List<string>
        {
            "JOURNAL entries=" + entries.Count + " complete=" + CompletedEntries + " bonus=" + OutcomeBonus.ToString("0.000"),
                "MOMENT " + (opportunity == null ? "none" : opportunity.kind +
                " response=" + opportunity.response + " animal=" +
                (opportunity.animal?.thingIDNumber ?? -1) + " expires=" +
                opportunity.expiresTick + " availableUntil=" +
                AvailabilityDeadline(opportunity) + " trail=" + opportunity.continuedAsTrail +
                " witnesses=" + opportunity.wildlifeWitnesses +
                " protected=" + opportunity.protectionDeclared),
            "MOMENTS history=" + momentHistory.Count + " cooldowns=" + recentMomentKeys.Count,
            "PROJECT " + (project == null ? "none" : project.kind + " progress=" + project.progress.ToString("0.00"))
        };

        public List<string> MomentBridgeLines()
        {
            List<string> lines = new List<string>
            {
                "moments=active:" + (opportunity != null ? 1 : 0) +
                " history:" + momentHistory.Count + " cooldowns:" + recentMomentKeys.Count
            };
            if (opportunity != null)
                lines.Add("moment=kind:" + opportunity.kind + " species:" +
                    (opportunity.species?.defName ?? "none") + " animal:" +
                    (opportunity.animal?.thingIDNumber ?? -1) + " other:" +
                    (opportunity.otherAnimal?.thingIDNumber ?? -1) + " response:" +
                    opportunity.response + " expires:" + opportunity.expiresTick +
                    " availableUntil:" + AvailabilityDeadline(opportunity) +
                    " trail:" + opportunity.continuedAsTrail +
                    " witnesses:" + opportunity.wildlifeWitnesses +
                    " protected:" + opportunity.protectionDeclared);
            lines.AddRange(momentHistory.Take(5).Select(value =>
                "momentHistory=kind:" + value.kind + " species:" + value.species.defName +
                " response:" + value.response + " success:" + value.success +
                " tick:" + value.tick));
            return lines;
        }

        public List<string> DebugForceMoment()
        {
            if (opportunity != null)
                return new List<string> { "moment=already_active kind:" + opportunity.kind };
            int now = Find.TickManager.TicksGame;
            opportunity = DetectWildlifeMoment(now);
            if (opportunity == null)
            {
                Pawn animal = map.mapPawns.AllPawnsSpawned.FirstOrDefault(pawn =>
                    pawn?.Spawned == true && !pawn.Dead && pawn.RaceProps?.Animal == true &&
                    pawn.Faction != Faction.OfPlayer);
                opportunity = CreateMoment(WildlifeOpportunityKind.RareSighting,
                    animal, null, "dev:" + now,
                    animal == null ? null : animal.LabelShortCap +
                    " is displaying ordinary behavior worth documenting.", now);
            }
            if (opportunity == null) return new List<string> { "moment=no_wildlife" };
            nextOpportunityTick = now + 60000;
            return new List<string>
            {
                "moment=forced kind:" + opportunity.kind + " animal:" +
                (opportunity.animal?.thingIDNumber ?? -1)
            }.Concat(MomentBridgeLines()).ToList();
        }

        private void UpdateJournal()
        {
            List<ThingDef> known = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(def => def?.race?.Animal == true && HuntingKnowledgeMapComponent.ColonyExperience(def) > 0f).ToList();
            for (int i = 0; i < known.Count; i++)
            {
                WildlifeJournalEntry entry = entries.FirstOrDefault(item => item.species == known[i]);
                if (entry == null)
                {
                    entry = new WildlifeJournalEntry { species = known[i] };
                    entries.Add(entry);
                }
                if (entry.completionRewardGranted || JournalStage(known[i]) < 5) continue;
                entry.completionRewardGranted = true;
                string text = "The colony completed its field-journal entry for " + known[i].LabelCap + ".";
                Messages.Message(text, MessageTypeDefOf.PositiveEvent, false);
                WildlifeExperience.Record("Field Journal", text);
                WildlifeMemoryUtility.Folklore(map, known[i].LabelCap + " Field Notes",
                    "The colony completed its field record of " + known[i].label + ".",
                    species: known[i]);
            }
        }

        private void UpdateOpportunity(int now)
        {
            recentMomentKeys.RemoveAll(value => value == null || value.expiresTick <= now);
            if (opportunity != null)
            {
                if (opportunity.accepted && opportunity.responder != null &&
                    now - opportunity.responseStartedTick > 300)
                {
                    JobDef expected = opportunity.response == WildlifeMomentResponse.Observe
                        ? HerdsDefOf.Herds_ObserveWildlifeMoment
                        : opportunity.response == WildlifeMomentResponse.Track
                            ? HerdsDefOf.Herds_StudyWildlifeSign : null;
                    if (expected != null && opportunity.responder.CurJobDef != expected)
                    {
                        Messages.Message(opportunity.responder.LabelShortCap +
                            "'s Wildlife Moment response was interrupted. Another response can be chosen.",
                            opportunity.responder, MessageTypeDefOf.CautionInput, false);
                        opportunity.accepted = false;
                        opportunity.response = WildlifeMomentResponse.None;
                        opportunity.responder = null;
                    }
                }
                if (OpportunityCompleted())
                {
                    ResolveOpportunity(true);
                    return;
                }
                Pawn target = ResponseTarget(opportunity.response);
                if (opportunity.response != WildlifeMomentResponse.Hunt &&
                    (target == null || target.Dead || !target.Spawned) &&
                    !(opportunity.continuedAsTrail &&
                      opportunity.evidence?.Spawned == true &&
                      (opportunity.response == WildlifeMomentResponse.None ||
                       opportunity.response == WildlifeMomentResponse.Track)))
                {
                    ResolveOpportunity(false);
                    return;
                }
                int deadline = opportunity.response == WildlifeMomentResponse.None
                    ? AvailabilityDeadline(opportunity) : opportunity.expiresTick;
                if (now >= deadline)
                {
                    if (opportunity.protectionDeclared)
                    {
                        opportunity.response = WildlifeMomentResponse.Protect;
                        ResolveOpportunity(true);
                    }
                    else ResolveOpportunity(false);
                }
                return;
            }
            if (nextOpportunityTick == 0) nextOpportunityTick = now + 5000;
            if (now < nextOpportunityTick) return;
            opportunity = DetectWildlifeMoment(now);
            nextOpportunityTick = now + (opportunity == null ? 7500 : Rand.Range(30000, 60000));
            if (opportunity == null) return;
            recentMomentKeys.Add(new WildlifeMomentKeyRecord
            {
                key = opportunity.eventKey,
                expiresTick = now + 600000
            });
            string title = "Wildlife Moment: " + OpportunityLabel(opportunity.kind);
            if (HerdsMod.Settings.enableWildlifeAlerts)
                Find.LetterStack.ReceiveLetter(title,
                    opportunity.description + "\n\nChoose how the colony responds within " +
                    MomentTimeRemaining(opportunity) + " in the Wildlife Journal Field Log.",
                    LetterDefOf.NeutralEvent, opportunity.animal);
            else
                Messages.Message(title, opportunity.animal, MessageTypeDefOf.NeutralEvent, false);
            WildlifeExperience.Record("Wildlife Moment", opportunity.description, opportunity.animal);
        }

        private bool OpportunityCompleted()
        {
            if (opportunity?.accepted != true) return false;
            if (opportunity.response == WildlifeMomentResponse.Track)
                return opportunity.evidence is WildlifeSign sign &&
                    opportunity.responder != null && sign.studiedBy.Contains(opportunity.responder);
            if (opportunity.response == WildlifeMomentResponse.Hunt)
            {
                Pawn target = ResponseTarget(WildlifeMomentResponse.Hunt);
                return target == null || target.Dead;
            }
            return false;
        }

        private void ResolveOpportunity(bool success, bool ignored = false)
        {
            if (opportunity == null) return;
            WildlifeOpportunityRecord completed = opportunity;
            Pawn target = ResponseTarget(completed.response) ?? completed.animal;
            string text;
            int storyTick = -1;
            string storyTitle = null;
            if (success)
            {
                float populationDelta = completed.response == WildlifeMomentResponse.Hunt ? -0.35f : 0f;
                map.GetComponent<RegionalWildlifeMapComponent>()?.ApplyExpeditionImpact(
                    target?.def ?? completed.species, populationDelta, 0.07f);
                Pawn learner = completed.responder ?? map.mapPawns.FreeColonistsSpawned.OrderByDescending(pawn =>
                    pawn.skills?.GetSkill(SkillDefOf.Animals)?.Level ?? 0).FirstOrDefault();
                float knowledge = completed.response == WildlifeMomentResponse.Observe ? 28f :
                    completed.response == WildlifeMomentResponse.Track ? 22f : 14f;
                map.GetComponent<HuntingKnowledgeMapComponent>()?.Learn(learner,
                    completed.species, knowledge, true);
                if (completed.animal != null && learner != null && !completed.animal.Dead &&
                    !(completed.response == WildlifeMomentResponse.Protect &&
                      completed.protectionDeclared))
                    WildlifeMemoryUtility.Remember(completed.animal, learner,
                        completed.response == WildlifeMomentResponse.Protect
                            ? AnimalMemoryKind.Protected : AnimalMemoryKind.Studied);
                text = OpportunityLabel(completed.kind) + " became a recorded colony story through " +
                    completed.response.ToString().ToLowerInvariant() + "." +
                    (completed.protectionDeclared &&
                     completed.response != WildlifeMomentResponse.Protect
                        ? " The focal animal also remains protected by colony policy."
                        : "") +
                    (completed.response == WildlifeMomentResponse.Observe &&
                     completed.wildlifeWitnesses > 0
                        ? " " + completed.wildlifeWitnesses + " nearby wild animal" +
                          (completed.wildlifeWitnesses == 1 ? "" : "s") +
                          " also remember seeing " +
                          (learner != null ? learner.LabelShortCap.ToString() : "the observer") + "."
                        : "");
                if (completed.kind == WildlifeOpportunityKind.PredatorStalk ||
                    completed.kind == WildlifeOpportunityKind.SocialBond ||
                    completed.kind == WildlifeOpportunityKind.AnimalRivalry ||
                    completed.kind == WildlifeOpportunityKind.SignalResponse)
                {
                    storyTitle = OpportunityLabel(completed.kind) + ": " + completed.species.LabelCap;
                    storyTick = Find.TickManager.TicksGame;
                    WildlifeMemoryUtility.Folklore(map, storyTitle,
                        text, completed.animal, completed.response != WildlifeMomentResponse.Hunt,
                        new[] { completed.responder ?? learner }, completed.focusCell,
                        completed.species);
                }
            }
            else text = !completed.failureReason.NullOrEmpty()
                ? OpportunityLabel(completed.kind) + " ended: " + completed.failureReason
                : OpportunityLabel(completed.kind) +
                    (ignored ? " was deliberately left undisturbed." :
                    " passed before the colony completed its response.");
            momentHistory.Insert(0, new WildlifeMomentOutcomeRecord
            {
                kind = completed.kind,
                response = completed.response,
                species = completed.species,
                tick = Find.TickManager.TicksGame,
                success = success,
                text = text
            });
            if (momentHistory.Count > 20) momentHistory.RemoveRange(20, momentHistory.Count - 20);
            if (storyTick >= 0 && HerdsDefOf.Herds_WildlifeStory != null)
            {
                ChoiceLetter_WildlifeStory letter = LetterMaker.MakeLetter(
                    "Wildlife Moment became a Colony Story", text,
                    HerdsDefOf.Herds_WildlifeStory, completed.animal) as ChoiceLetter_WildlifeStory;
                if (letter != null)
                {
                    letter.map = map;
                    letter.storyTick = storyTick;
                    letter.storyTitle = storyTitle;
                    Find.LetterStack.ReceiveLetter(letter);
                }
            }
            else Messages.Message(text, completed.animal,
                success ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.NeutralEvent, false);
            WildlifeExperience.Record("Wildlife Moment", text, completed.animal, !success && !ignored);
            opportunity = null;
        }

        private WildlifeOpportunityRecord DetectWildlifeMoment(int now)
        {
            Func<int, WildlifeOpportunityRecord>[] detectors =
            {
                DetectPredatorStalk, DetectSignalMoment, DetectSocialMoment,
                DetectLookoutMoment, DetectInjuryMoment, DetectNotableMoment,
                DetectHomeMoment, DetectYoungMoment
            };
            for (int i = 0; i < detectors.Length; i++)
            {
                WildlifeOpportunityRecord candidate = detectors[i](now);
                if (candidate != null && CanFeature(candidate.eventKey, now)) return candidate;
            }
            return null;
        }

        private WildlifeOpportunityRecord DetectPredatorStalk(int now)
        {
            MapComponent packs = map.components.FirstOrDefault(component =>
                component.GetType().FullName == "Packs.PackMapComponent");
            object result = packs?.GetType().GetMethod("WildlifeMomentHuntPair")?.Invoke(packs, null);
            List<Pawn> pair = (result as IEnumerable<Pawn>)?.Where(pawn => pawn != null).Take(2).ToList();
            if (pair?.Count != 2 || pair[0].Spawned != true || pair[1].Spawned != true) return null;
            Pawn predator = pair[0];
            Pawn prey = pair[1];
            return CreateMoment(WildlifeOpportunityKind.PredatorStalk, prey, predator,
                "hunt:" + predator.thingIDNumber + ":" + prey.thingIDNumber,
                prey.LabelShortCap + " is being stalked by " + predator.LabelShortCap +
                ". Observe the hunt, track the prey, protect it, or hunt the predator.", now);
        }

        private WildlifeOpportunityRecord DetectSignalMoment(int now)
        {
            IReadOnlyList<WildlifeSignalTrace> traces =
                map.GetComponent<WildlifeSignalCultureMapComponent>()?.RecentSignals;
            WildlifeSignalTrace trace = (traces ?? Array.Empty<WildlifeSignalTrace>())
                .Where(value => value != null && value.traceId > lastSignalTraceId &&
                    value.verified && value.behaviorConsistent && now - value.tick <= 7500)
                .OrderByDescending(value => value.tick).FirstOrDefault();
            if (trace == null) return null;
            lastSignalTraceId = trace.traceId;
            Pawn animal = map.mapPawns.AllPawnsSpawned.Where(pawn =>
                pawn?.Spawned == true && !pawn.Dead && pawn.def == trace.species &&
                pawn.Faction != Faction.OfPlayer).OrderBy(pawn =>
                pawn.Position.DistanceToSquared(trace.cell)).FirstOrDefault();
            if (animal == null) return null;
            WildlifeSignalCultureMapComponent signals = map.GetComponent<WildlifeSignalCultureMapComponent>();
            float understanding = signals?.ColonyUnderstanding(trace.species) ?? 0f;
            string signalDescription = WildlifeSignalPresentation.Description(trace.kind, understanding,
                trace.truthful, trace.verified, trace.behaviorConsistent, animal, trace.species,
                trace.radius, trace.expectedBehavior, trace.observedBehavior, map);
            return CreateMoment(WildlifeOpportunityKind.SignalResponse, animal, null,
                "signal:" + trace.traceId,
                signalDescription + " This is a chance to connect the call with real behavior.", now, trace.cell);
        }

        private WildlifeOpportunityRecord DetectSocialMoment(int now)
        {
            IReadOnlyList<AnimalSocialMemory> memories =
                map.GetComponent<WildlifeMemoryMapComponent>()?.SocialMemories;
            var recentSocial = (memories ?? Array.Empty<AnimalSocialMemory>())
                .Where(value => value?.animal?.Spawned == true && value.otherAnimal?.Spawned == true)
                .SelectMany(value => value.events.Where(entry =>
                    entry != null && now - entry.tick <= 7500).Select(entry => new { value, entry }))
                .Where(value => value.entry.kind == AnimalSocialMemoryKind.MateBond ||
                    value.entry.kind == AnimalSocialMemoryKind.ParentCare ||
                    value.entry.kind == AnimalSocialMemoryKind.Reunited ||
                    value.entry.kind == AnimalSocialMemoryKind.Rivalry ||
                    value.entry.kind == AnimalSocialMemoryKind.Fought)
                .OrderByDescending(value => value.entry.tick).FirstOrDefault();
            if (recentSocial == null) return null;
            AnimalSocialMemory social = recentSocial.value;
            AnimalSocialMemoryEvent recent = recentSocial.entry;
            int low = Mathf.Min(social.animal.thingIDNumber, social.otherAnimal.thingIDNumber);
            int high = Mathf.Max(social.animal.thingIDNumber, social.otherAnimal.thingIDNumber);
            bool rivalry = recent.kind == AnimalSocialMemoryKind.Rivalry ||
                recent.kind == AnimalSocialMemoryKind.Fought;
            WildlifeOpportunityKind kind = rivalry
                ? WildlifeOpportunityKind.AnimalRivalry : WildlifeOpportunityKind.SocialBond;
            return CreateMoment(kind, social.animal, social.otherAnimal,
                "social:" + low + ":" + high + ":" + recent.kind + ":" + recent.tick,
                social.animal.LabelShortCap + " and " + social.otherAnimal.LabelShortCap +
                " are " + WildlifeMemoryMapComponent.SocialEventLabel(recent.kind) +
                ". The relationship can be witnessed as it forms.", now);
        }

        private WildlifeOpportunityRecord DetectLookoutMoment(int now)
        {
            HerdMapComponent herds = map.GetComponent<HerdMapComponent>();
            if (herds == null) return null;
            WildlifeOpportunityRecord found = null;
            IReadOnlyList<HerdSnapshot> groups = herds.AllHerds;
            for (int i = 0; i < groups.Count; i++)
            {
                HerdSnapshot group = groups[i];
                if (group?.sentinel?.Spawned != true || group.faction == Faction.OfPlayer) continue;
                WildlifeMomentSentinelRecord state = sentinelStates.FirstOrDefault(value =>
                    value.groupId == group.id);
                if (state == null)
                {
                    sentinelStates.Add(new WildlifeMomentSentinelRecord
                        { groupId = group.id, sentinel = group.sentinel });
                    continue;
                }
                if (state.sentinel == group.sentinel) continue;
                Pawn previous = state.sentinel;
                state.sentinel = group.sentinel;
                if (previous != null && found == null)
                    found = CreateMoment(WildlifeOpportunityKind.LookoutRotation,
                        group.sentinel, previous,
                        "lookout:" + group.id + ":" + group.sentinel.thingIDNumber + ":" + now / 2500,
                        group.Label + " has rotated its lookout. " + group.sentinel.LabelShortCap +
                        " is taking over vigilance for the group.", now);
            }
            return found;
        }

        private WildlifeOpportunityRecord DetectInjuryMoment(int now)
        {
            Pawn animal = map.mapPawns.AllPawnsSpawned.Where(pawn =>
                pawn?.Spawned == true && !pawn.Dead && pawn.RaceProps?.Animal == true &&
                pawn.Faction != Faction.OfPlayer &&
                pawn.health.summaryHealth.SummaryHealthPercent < 0.72f &&
                CanFeature("injury:" + pawn.thingIDNumber, now))
                .OrderBy(pawn => pawn.health.summaryHealth.SummaryHealthPercent).FirstOrDefault();
            return animal == null ? null : CreateMoment(WildlifeOpportunityKind.InjuredAnimal,
                animal, null, "injury:" + animal.thingIDNumber,
                animal.LabelShortCap + " is visibly injured. Its recovery, trail, and response to danger can be followed.", now);
        }

        private WildlifeOpportunityRecord DetectNotableMoment(int now)
        {
            IReadOnlyList<NotableAnimalRecord> notableRecords =
                map.GetComponent<NotableWildlifeMapComponent>()?.Records;
            NotableAnimalRecord record = (notableRecords ?? Array.Empty<NotableAnimalRecord>())
                .Where(value => value?.animal?.Spawned == true && !value.animal.Dead &&
                    value.studies == 0 && CanFeature("notable:" + value.animal.thingIDNumber, now))
                .OrderByDescending(value => value.discoveredTick).FirstOrDefault();
            return record == null ? null : CreateMoment(WildlifeOpportunityKind.RareSighting,
                record.animal, null, "notable:" + record.animal.thingIDNumber,
                record.title + ", " + record.species.LabelCap + ", is displaying " +
                record.distinction.ToLowerInvariant() + ". The behavior can be documented.", now);
        }

        private WildlifeOpportunityRecord DetectHomeMoment(int now)
        {
            HerdMapComponent herds = map.GetComponent<HerdMapComponent>();
            Pawn animal = map.mapPawns.AllPawnsSpawned.FirstOrDefault(pawn =>
                pawn?.Spawned == true && !pawn.Dead && pawn.RaceProps?.Animal == true &&
                pawn.Faction != Faction.OfPlayer && herds?.HomeFor(pawn)?.Spawned == true &&
                CanFeature("home:" + herds.HomeFor(pawn).thingIDNumber, now));
            Thing home = animal == null ? null : herds.HomeFor(animal);
            return home == null ? null : CreateMoment(WildlifeOpportunityKind.HomeDiscovery,
                animal, null, "home:" + home.thingIDNumber,
                animal.LabelShortCap + " has revealed a regularly used " +
                home.LabelShort.ToLowerInvariant() + ". Observing from a distance can document how it is used.", now,
                home.Position, home);
        }

        private WildlifeOpportunityRecord DetectYoungMoment(int now)
        {
            IReadOnlyList<HerdSnapshot> groups = map.GetComponent<HerdMapComponent>()?.AllHerds;
            HerdSnapshot group = (groups ?? Array.Empty<HerdSnapshot>())
                .FirstOrDefault(value => value?.youngCount > 0 && value.leader?.Spawned == true &&
                    value.faction != Faction.OfPlayer && CanFeature("young:" + value.id, now));
            return group == null ? null : CreateMoment(WildlifeOpportunityKind.NestingSeason,
                group.leader, null, "young:" + group.id,
                group.Label + " is sheltering young. Its protective formation and movement can be documented.", now);
        }

        private WildlifeOpportunityRecord CreateMoment(WildlifeOpportunityKind kind, Pawn animal,
            Pawn other, string key, string description, int now, IntVec3? cell = null, Thing evidence = null)
        {
            if (!MomentTargetViable(animal) ||
                other != null && !MomentTargetViable(other)) return null;
            WildlifeOpportunityRecord value = new WildlifeOpportunityRecord
            {
                kind = kind,
                species = animal.def,
                animal = animal,
                otherAnimal = other,
                evidence = evidence,
                startedTick = now,
                availableUntilTick = now + MomentAvailabilityTicks(kind,
                    animal.thingIDNumber, now),
                description = description,
                focusCell = cell ?? animal.Position,
                eventKey = key
            };
            value.expiresTick = value.availableUntilTick;
            return value;
        }

        private static int AvailabilityDeadline(WildlifeOpportunityRecord value) =>
            value?.availableUntilTick > 0 ? value.availableUntilTick :
            value?.expiresTick ?? 0;

        private static int MomentAvailabilityTicks(WildlifeOpportunityKind kind,
            int animalId, int startedTick)
        {
            int seed = unchecked(animalId * 397 ^ (int)kind * 7919 ^
                startedTick / MomentHourTicks);
            return (1 + ((seed & int.MaxValue) % 3)) * MomentHourTicks;
        }

        public static string MomentTimeRemaining(WildlifeOpportunityRecord value)
        {
            if (value == null) return "no time";
            int deadline = value.response == WildlifeMomentResponse.None
                ? AvailabilityDeadline(value) : value.expiresTick;
            return Mathf.Max(0, deadline - (Find.TickManager?.TicksGame ?? 0))
                .ToStringTicksToPeriod();
        }

        public static bool MomentAvailabilitySelfTest()
        {
            HashSet<int> durations = new HashSet<int>();
            foreach (WildlifeOpportunityKind kind in
                (WildlifeOpportunityKind[])Enum.GetValues(typeof(WildlifeOpportunityKind)))
            {
                for (int animalId = 1; animalId <= 12; animalId++)
                {
                    int duration = MomentAvailabilityTicks(kind, animalId, 150000);
                    if (duration < MomentHourTicks || duration > MomentHourTicks * 3 ||
                        duration % MomentHourTicks != 0 ||
                        duration != MomentAvailabilityTicks(kind, animalId, 150000))
                        return false;
                    durations.Add(duration);
                }
            }
            return durations.Count == 3;
        }

        private bool MomentTargetViable(Pawn animal)
        {
            if (animal?.Spawned != true || animal.Dead ||
                animal.CurJob?.exitMapOnArrival == true) return false;
            IntVec3 cell = animal.Position;
            int edge = Math.Min(Math.Min(cell.x, cell.z),
                Math.Min(map.Size.x - 1 - cell.x, map.Size.z - 1 - cell.z));
            return edge >= 16;
        }

        private bool TryFindObservationCell(Pawn observer, Pawn target, out IntVec3 result)
        {
            result = IntVec3.Invalid;
            if (observer?.Spawned != true || target?.Spawned != true) return false;
            Vector2 wind = WildlifeFieldcraftMapComponent.WindVector(map);
            Vector2 observerSide = new Vector2(observer.Position.x - target.Position.x,
                observer.Position.z - target.Position.z).normalized;
            float bestScore = float.MinValue;
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(target.Position, 28f, true))
            {
                float distance = cell.DistanceTo(target.Position);
                if (distance < 18f || distance > 28f || !cell.InBounds(map) ||
                    !cell.Walkable(map) || !observer.CanReach(cell, PathEndMode.OnCell, Danger.Some) ||
                    !GenSight.LineOfSight(cell, target.Position, map)) continue;
                Vector2 offset = new Vector2(cell.x - target.Position.x,
                    cell.z - target.Position.z).normalized;
                if (Vector2.Dot(observerSide, offset) < 0.15f) continue;
                float windSafety = Vector2.Dot(wind, offset);
                float cover = cell.GetPlant(map)?.Growth > 0.35f ? 18f : 0f;
                float score = windSafety * 45f + cover -
                    cell.DistanceToSquared(observer.Position) * 0.0025f;
                if (score <= bestScore) continue;
                bestScore = score;
                result = cell;
            }
            return result.IsValid;
        }

        private bool CanFeature(string key, int now) =>
            !key.NullOrEmpty() && !recentMomentKeys.Any(value =>
                value.key == key && value.expiresTick > now);

        private void FinishProjectIfReady()
        {
            if (!ProjectReady(project))
            {
                if (project != null && !ValidProject(project)) project = null;
                return;
            }
            completedProjects++;
            map.GetComponent<RegionalWildlifeMapComponent>()?.ApplyExpeditionImpact(project.species,
                project.kind == WildlifeStewardProjectKind.PopulationControl ||
                project.kind == WildlifeStewardProjectKind.SuppressPredators ? -1f : 1f, 0.18f);
            string text = ProjectLabel(project.kind) + " for " + project.species.LabelCap + " was completed.";
            Messages.Message(text, MessageTypeDefOf.PositiveEvent, false);
            WildlifeExperience.Record("Steward Project", text);
            WildlifeMemoryUtility.Folklore(map, ProjectLabel(project.kind),
                "The colony completed " + ProjectLabel(project.kind).ToLowerInvariant() +
                " for " + project.species.label + ".", species: project.species);
            WildlifeIdeologyUtility.Notify(map, WildlifeIdeologyEvent.Protect);
            project = null;
        }

        internal static bool ValidProject(WildlifeStewardProjectRecord value) =>
            value?.species?.race?.Animal == true;

        internal static bool ProjectReady(WildlifeStewardProjectRecord value) =>
            ValidProject(value) && value.progress >= 1f;

        public static string OpportunityLabel(WildlifeOpportunityKind kind) =>
            kind == WildlifeOpportunityKind.Migration ? "Passing Migration" :
            kind == WildlifeOpportunityKind.NestingSeason ? "Nesting Season" :
            kind == WildlifeOpportunityKind.InjuredAnimal ? "Injured Animal" :
            kind == WildlifeOpportunityKind.PredatorIncursion ? "Local Predator Encounter" :
            kind == WildlifeOpportunityKind.RareSighting ? "Rare Sighting" :
            kind == WildlifeOpportunityKind.PredatorStalk ? "The Stalk" :
            kind == WildlifeOpportunityKind.LookoutRotation ? "Changing of the Watch" :
            kind == WildlifeOpportunityKind.SocialBond ? "A Bond Forming" :
            kind == WildlifeOpportunityKind.AnimalRivalry ? "A Rivalry Emerging" :
            kind == WildlifeOpportunityKind.HomeDiscovery ? "A Hidden Home" :
            kind == WildlifeOpportunityKind.SignalResponse ? "A Call Answered" :
            "Disease Concern";

        public static string MomentResponseTooltip(WildlifeMomentResponse response) =>
            response == WildlifeMomentResponse.Observe
                ? "Send a colonist to a safe vantage point. Success records Animal Knowledge, regional confidence, memories, and important stories."
                : response == WildlifeMomentResponse.Track
                    ? "Study physical evidence from the event. If the animal leaves, the trail continues to the map edge and into Local Wildlife."
                    : response == WildlifeMomentResponse.Protect
                        ? "Recognize the focal animal and commit the colony to protecting it. " +
                          "Protection is immediate and the moment remains open for observation or tracking."
                        : response == WildlifeMomentResponse.Hunt
                            ? "Designate the relevant animal for hunting. During a stalk, this targets the predator."
                            : "Leave the moment undisturbed. No penalty is applied.";

        public static bool ResearchAllowsResponse(WildlifeMomentResponse response) =>
            response == WildlifeMomentResponse.Track
                ? WildlifeProgression.Unlocked(WildlifeCapability.Telemetry)
                : response == WildlifeMomentResponse.Hunt
                    ? WildlifeProgression.Unlocked(WildlifeCapability.Fieldcraft)
                    : true;

        public static bool ProtectionAllowsFollowupSelfTest()
        {
            WildlifeOpportunityRecord value = new WildlifeOpportunityRecord
            {
                protectionDeclared = true,
                response = WildlifeMomentResponse.None,
                accepted = false
            };
            return value.protectionDeclared &&
                value.response == WildlifeMomentResponse.None && !value.accepted &&
                ResearchAllowsResponse(WildlifeMomentResponse.Observe);
        }

        public static string ProjectLabel(WildlifeStewardProjectKind kind) =>
            kind == WildlifeStewardProjectKind.RestoreSpecies ? "Restore a Species" :
            kind == WildlifeStewardProjectKind.MigrationCorridor ? "Establish a Migration Corridor" :
            kind == WildlifeStewardProjectKind.PopulationControl ? "Control Overpopulation" :
            kind == WildlifeStewardProjectKind.RanchDefense ? "Protect Wildlife Habitat" :
            kind == WildlifeStewardProjectKind.ProtectMigration ? "Protect a Migration" :
            kind == WildlifeStewardProjectKind.SuppressPredators ? "Respond to Predator Encounters" :
            "Attract Rare Birds";

        public static string ProjectDescription(WildlifeStewardProjectKind kind) =>
            kind == WildlifeStewardProjectKind.RestoreSpecies
                ? "Restore a significantly declined species through repeated colonist fieldwork. Active reserves or habitat restoration improve each work session."
                : kind == WildlifeStewardProjectKind.MigrationCorridor
                    ? "Survey and maintain a safer migration route. An active migration corridor improves each work session."
                    : kind == WildlifeStewardProjectKind.PopulationControl
                        ? "Perform surveys and controlled fieldwork to reduce an overabundant population responsibly."
                        : kind == WildlifeStewardProjectKind.RanchDefense
                            ? "Protect wildlife habitat near colony territory through hands-on monitoring and deterrent work."
                            : kind == WildlifeStewardProjectKind.ProtectMigration
                                ? "Support a vulnerable migration through repeated patrol and route-maintenance work."
                                : kind == WildlifeStewardProjectKind.SuppressPredators
                                    ? "Monitor repeated local predator encounters. An active Predator Deterrent discourages ordinary predators."
                                    : "Prepare habitat and water access for rare birds through repeated field surveys and maintenance.";

        private static string OpportunityDescription(WildlifeOpportunityKind kind, ThingDef species)
        {
            string animal = species?.LabelCap.ToString() ?? "Wildlife";
            if (kind == WildlifeOpportunityKind.Migration) return animal + " are moving through the region. An active migration corridor can guide them safely.";
            if (kind == WildlifeOpportunityKind.NestingSeason) return animal + " have entered a vulnerable breeding period. Protection or a reserve can improve the outcome.";
            if (kind == WildlifeOpportunityKind.InjuredAnimal) return "An injured " + animal + " has been reported. Rescue, recovery, capture, or death will conclude the event.";
            if (kind == WildlifeOpportunityKind.PredatorIncursion) return "A " + animal + " is ranging close to colony territory. Remove it or activate a Predator Deterrent.";
            if (kind == WildlifeOpportunityKind.RareSighting) return "An unusual " + animal + " has been sighted. Complete a close study before it leaves.";
            return animal + " show signs that may indicate disease. Maintain an observation post" +
                (WildlifeProgression.Unlocked(WildlifeCapability.Telemetry)
                    ? " or telemetry station" : "") + " to document the concern.";
        }

        private static string Mark(bool complete) => complete ? "Recorded" : "Undiscovered";

        private static Color MomentColor(WildlifeOpportunityKind kind)
        {
            if (kind == WildlifeOpportunityKind.PredatorStalk ||
                kind == WildlifeOpportunityKind.AnimalRivalry)
                return new Color(0.95f, 0.32f, 0.22f);
            if (kind == WildlifeOpportunityKind.SignalResponse)
                return new Color(0.72f, 0.48f, 1f);
            if (kind == WildlifeOpportunityKind.HomeDiscovery ||
                kind == WildlifeOpportunityKind.NestingSeason)
                return new Color(0.34f, 0.82f, 0.42f);
            if (kind == WildlifeOpportunityKind.LookoutRotation)
                return new Color(0.2f, 0.76f, 0.9f);
            return new Color(0.95f, 0.75f, 0.22f);
        }
    }

    public sealed class JobDriver_ObserveWildlifeMoment : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);
            int proficiency = pawn.Map?.GetComponent<HuntingKnowledgeMapComponent>()?
                .WildlifeProficiencyLevel(pawn) ?? 0;
            Toil observe = Toils_General.Wait(Mathf.RoundToInt(900f *
                (1f - proficiency * 0.08f)), TargetIndex.A);
            observe.socialMode = RandomSocialMode.Off;
            observe.WithProgressBarToilDelay(TargetIndex.A);
            observe.AddFailCondition(() => job.targetA.Pawn?.Spawned != true ||
                !pawn.Position.InHorDistOf(job.targetA.Pawn.Position, 28f));
            yield return observe;
            Toil finish = ToilMaker.MakeToil("CompleteWildlifeMomentObservation");
            finish.initAction = () =>
            {
                Pawn animal = job.targetA.Pawn;
                pawn.Map?.GetComponent<WildlifeFieldJournalMapComponent>()?
                    .CompleteMomentObservation(pawn, animal);
            };
            finish.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return finish;
        }
    }

    public sealed class JobDriver_PerformStewardshipProject : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
            Toil work = Toils_General.Wait(2500, TargetIndex.A);
            work.socialMode = RandomSocialMode.Off;
            work.WithProgressBarToilDelay(TargetIndex.A);
            yield return work;
            Toil finish = ToilMaker.MakeToil("CompleteStewardshipProjectWork");
            finish.initAction = () => pawn.Map?.GetComponent<WildlifeFieldJournalMapComponent>()?
                .CompleteProjectWork(pawn);
            finish.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return finish;
        }
    }

    public sealed class Window_WildlifeFieldJournal : Window
    {
        private readonly Map map;
        private int tab;
        private Vector2 scroll;
        public override Vector2 InitialSize => new Vector2(860f, 700f);

        private readonly int focusedStoryTick = -1;
        private bool positionedStory;

        public Window_WildlifeFieldJournal(Map map, int initialTab = 0,
            int focusedStoryTick = -1)
        {
            this.map = map;
            tab = Mathf.Clamp(initialTab, 0, 5);
            this.focusedStoryTick = focusedStoryTick;
            doCloseX = true;
            resizeable = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect rect)
        {
            WildlifeFieldJournalMapComponent component = map?.GetComponent<WildlifeFieldJournalMapComponent>();
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, rect.width, 34f), "Wildlife Field Journal");
            Text.Font = GameFont.Small;
            if (component == null) { Widgets.Label(new Rect(0f, 40f, rect.width, 30f), "Journal information is unavailable."); return; }
            List<int> pages = new List<int>();
            List<string> labels = new List<string>();
            List<string> tips = new List<string>();
            void AddPage(int page, string label, string tip)
            {
                pages.Add(page);
                labels.Add(label);
                tips.Add(tip);
            }
            if (HerdsMod.Settings.enableFieldJournal)
                AddPage(0, "Field Guide",
                    "Review species knowledge and completed field entries.");
            if (HerdsMod.Settings.enableDynamicWildlifeOpportunities)
                AddPage(2, component.Opportunity == null
                        ? "Wildlife Moments" : "Wildlife Moments • Active",
                    "Respond to brief, real behaviors currently unfolding on the map.");
            if (HerdsMod.Settings.enableWildlifeMysteries)
                AddPage(1, "Mysteries",
                    "Investigate unusual wildlife patterns and choose a resolution.");
            if (HerdsMod.Settings.enableStewardProjects &&
                HerdsMod.Settings.enableWildlifeManagementGoals)
                AddPage(3, "Stewardship",
                    "Set long-term population, habitat, migration, and ranch objectives.");
            if (HerdsMod.Settings.enableNotableAnimals)
                AddPage(4, "Notable Animals",
                    "Review individual animals that have become part of the colony's story.");
            if (HerdsMod.Settings.enableWildlifeFolklore)
                AddPage(5, "Folklore",
                    "Review wildlife stories preserved and retold by the colony.");
            if (pages.Count == 0)
            {
                Widgets.Label(new Rect(0f, 42f, rect.width, 44f),
                    "No Field Journal features are enabled in Wildlife settings.");
                return;
            }
            if (!pages.Contains(tab)) tab = pages[0];
            float tabWidth = (rect.width - 12f) / 3f;
            for (int i = 0; i < pages.Count; i++)
            {
                int selected = pages[i];
                Rect button = new Rect((i % 3) * (tabWidth + 6f),
                    42f + (i / 3) * 36f, tabWidth, 32f);
                if (Widgets.ButtonText(button, labels[i], active: tab != selected))
                {
                    tab = selected;
                    scroll = Vector2.zero;
                }
                TooltipHandler.TipRegion(button, tips[i]);
            }
            int tabRows = Mathf.CeilToInt(pages.Count / 3f);
            float contentY = 50f + tabRows * 36f;
            Rect content = new Rect(0f, contentY, rect.width, rect.height - contentY);
            GUI.BeginGroup(content);
            Rect local = new Rect(0f, 0f, content.width, content.height);
            if (tab == 0) DrawJournal(local, component);
            else if (tab == 1) DrawMysteries(local);
            else if (tab == 2) DrawOpportunities(local, component);
            else if (tab == 3) DrawProjects(local, component);
            else if (tab == 4) DrawNotables(local);
            else DrawFolklore(local);
            GUI.EndGroup();
        }

        private void DrawMysteries(Rect rect)
        {
            if (!HerdsMod.Settings.enableWildlifeMysteries)
            {
                Widgets.Label(rect, "Living Wildlife Mysteries are disabled in settings.");
                return;
            }
            WildlifeMysteryMapComponent component = map.GetComponent<WildlifeMysteryMapComponent>();
            List<WildlifeMysteryRecord> mysteries = component?.Mysteries
                .OrderBy(value => value.Resolved).ThenByDescending(value => value.startedTick).ToList() ??
                new List<WildlifeMysteryRecord>();
            if (mysteries.Count == 0)
            {
                Widgets.DrawMenuSection(new Rect(0f, 0f, rect.width, 112f));
                Text.Font = GameFont.Medium;
                Widgets.Label(new Rect(14f, 12f, rect.width - 28f, 28f), "No Unexplained Pattern");
                Text.Font = GameFont.Small;
                Widgets.Label(new Rect(14f, 47f, rect.width - 28f, 52f),
                "The journal watches real migration, population, tradition, predator, and family-line behavior" +
                (WildlifeProgression.Unlocked(WildlifeCapability.Telemetry)
                    ? ", including telemetry" : "") +
                ". An investigation begins only when those systems produce an unusual pattern with a real cause.");
                return;
            }
            Rect outer = new Rect(0f, 0f, rect.width, rect.height);
            float totalHeight = mysteries.Sum(value => value == component.Active ? 300f +
                Mathf.Min(4, value.evidence.Count) * 38f : 82f);
            Rect view = new Rect(0f, 0f, outer.width - 18f, Mathf.Max(outer.height, totalHeight));
            Widgets.BeginScrollView(outer, ref scroll, view);
            float y = 0f;
            foreach (WildlifeMysteryRecord mystery in mysteries)
            {
                bool expanded = mystery == component.Active;
                float height = expanded ? 294f + Mathf.Min(4, mystery.evidence.Count) * 38f : 74f;
                Rect card = new Rect(0f, y, view.width, height);
                Widgets.DrawMenuSection(card);
                Color accent = mystery.Resolved ? new Color(0.40f, 0.62f, 0.40f) :
                    mystery.Solved ? new Color(0.36f, 0.72f, 0.55f) : new Color(0.62f, 0.38f, 0.68f);
                Widgets.DrawBoxSolid(new Rect(card.x, card.y, 5f, card.height), accent);
                Text.Font = GameFont.Medium;
                Widgets.Label(new Rect(card.x + 14f, card.y + 10f, card.width - 190f, 28f), mystery.title);
                Text.Font = GameFont.Small;
                Widgets.Label(new Rect(card.x + 14f, card.y + 40f, card.width - 190f, 24f),
                    mystery.species.LabelCap + "  •  " +
                    (mystery.Resolved ? WildlifeMysteryMapComponent.ResolutionLabel(mystery.resolution) :
                    mystery.Solved ? "Cause Discovered" : mystery.progress.ToStringPercent() + " understood"));
                if (!expanded)
                {
                    y += height + 8f;
                    continue;
                }
                Widgets.Label(new Rect(card.x + 14f, card.y + 72f, card.width - 28f, 50f),
                    mystery.Solved ? mystery.explanation : mystery.anomaly);
                Widgets.FillableBar(new Rect(card.x + 14f, card.y + 128f, card.width - 224f, 18f),
                    Mathf.Clamp01(mystery.progress));
                if (!mystery.Solved)
                {
                    if (Widgets.ButtonText(new Rect(card.xMax - 198f, card.y + 119f, 184f, 32f), "Review Current Evidence"))
                        component.ReviewEvidence(mystery);
                    if (Widgets.ButtonText(new Rect(card.xMax - 198f, card.y + 157f, 184f, 32f), "Focus Physical Evidence"))
                        component.FocusEvidence(mystery);
                }
                else if (!mystery.Resolved &&
                    Widgets.ButtonText(new Rect(card.xMax - 198f, card.y + 128f, 184f, 34f), "Choose Response"))
                    ShowMysteryResponses(component, mystery);
                Widgets.Label(new Rect(card.x + 14f, card.y + 158f, card.width - 224f, 24f),
                    "Evidence  •  " + mystery.evidence.Count + " findings");
                float evidenceY = card.y + 188f;
                foreach (WildlifeMysteryEvidence evidence in mystery.evidence.Take(4))
                {
                    Rect evidenceRect = new Rect(card.x + 14f, evidenceY, card.width - 28f, 32f);
                    Widgets.DrawHighlight(evidenceRect);
                    Widgets.Label(new Rect(evidenceRect.x + 6f, evidenceRect.y + 5f, 150f, 22f), evidence.source);
                    Widgets.Label(new Rect(evidenceRect.x + 162f, evidenceRect.y + 5f,
                        evidenceRect.width - 168f, 22f), evidence.clue);
                    TooltipHandler.TipRegion(evidenceRect, evidence.clue + "\nContribution: +" +
                        evidence.value.ToStringPercent());
                    evidenceY += 38f;
                }
                y += height + 8f;
            }
            Widgets.EndScrollView();
        }

        private static void ShowMysteryResponses(WildlifeMysteryMapComponent component,
            WildlifeMysteryRecord mystery)
        {
            List<WildlifeMysteryResolution> responses = new List<WildlifeMysteryResolution>
            {
                WildlifeMysteryResolution.ProtectDiscovery,
                WildlifeMysteryResolution.EstablishSanctuary,
                WildlifeMysteryResolution.ExploitForHunting,
                WildlifeMysteryResolution.LeaveUndisturbed
            };
            if (mystery.cause == WildlifeMysteryCause.DistortedTradition)
                responses.Insert(1, WildlifeMysteryResolution.CorrectTradition);
            Find.WindowStack.Add(new FloatMenu(responses.Select(response =>
                new FloatMenuOption(WildlifeMysteryMapComponent.ResolutionLabel(response),
                    () => component.Resolve(mystery, response))).ToList()));
        }

        private void DrawJournal(Rect rect, WildlifeFieldJournalMapComponent component)
        {
            Widgets.Label(new Rect(0f, 0f, rect.width, 44f),
                "Completed entries: " + component.CompletedEntries + "  •  Colony hunting bonus: +" +
                component.HuntingSkillBonus.ToString("0.0") + "  •  Field outcome bonus: +" + component.OutcomeBonus.ToStringPercent());
            List<WildlifeJournalEntry> entries = component.Entries.OrderByDescending(entry => component.JournalStage(entry.species))
                .ThenBy(entry => entry.species.label).ToList();
            Rect outer = new Rect(0f, 48f, rect.width, rect.height - 48f);
            Rect view = new Rect(0f, 0f, outer.width - 18f, Mathf.Max(outer.height, entries.Count * 50f));
            Widgets.BeginScrollView(outer, ref scroll, view);
            for (int i = 0; i < entries.Count; i++)
            {
                WildlifeJournalEntry entry = entries[i];
                Rect row = new Rect(0f, i * 50f, view.width, 44f);
                Widgets.DrawMenuSection(row);
                Widgets.Label(new Rect(10f, row.y + 10f, row.width * 0.48f, 24f), entry.species.LabelCap);
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(new Rect(row.width * 0.50f, row.y + 7f, row.width * 0.47f, 28f), component.JournalStageLabel(entry.species));
                Text.Anchor = TextAnchor.UpperLeft;
                TooltipHandler.TipRegion(row, component.JournalTooltip(entry.species));
            }
            Widgets.EndScrollView();
            if (entries.Count == 0) Widgets.Label(new Rect(8f, 60f, rect.width - 16f, 40f), "Observe, track, study, or hunt wildlife to begin journal entries.");
        }

        private void DrawOpportunities(Rect rect, WildlifeFieldJournalMapComponent component)
        {
            if (!HerdsMod.Settings.enableDynamicWildlifeOpportunities)
            {
                Widgets.Label(rect, "Wildlife Moments are disabled in settings.");
                return;
            }
            WildlifeOpportunityRecord value = component.Opportunity;
            float historyY;
            if (value == null)
            {
                Widgets.Label(new Rect(0f, 0f, rect.width, 44f),
                    "No active moment. Real hunts, calls, relationships, lookout changes, homes, and injuries can become moments.");
                historyY = 54f;
            }
            else
            {
                Rect card = new Rect(0f, 0f, rect.width, 260f);
                Widgets.DrawMenuSection(card);
                Widgets.DrawBoxSolid(new Rect(card.x, card.y, 6f, card.height),
                    value.kind == WildlifeOpportunityKind.PredatorStalk ||
                    value.kind == WildlifeOpportunityKind.AnimalRivalry
                        ? new Color(0.86f, 0.28f, 0.2f)
                        : new Color(0.33f, 0.65f, 0.42f));
                Text.Font = GameFont.Tiny;
                GUI.color = new Color(0.68f, 0.76f, 0.68f);
                Widgets.Label(new Rect(16f, 9f, rect.width - 190f, 18f), "CURRENT MOMENT");
                GUI.color = Color.white;
                Text.Font = GameFont.Medium;
                Widgets.Label(new Rect(16f, 29f, rect.width - 190f, 30f),
                    WildlifeFieldJournalMapComponent.OpportunityLabel(value.kind));
                Text.Font = GameFont.Small;
                Widgets.Label(new Rect(16f, 64f, rect.width - 32f, 58f), value.description);
                string targetName = value.animal?.LabelShortCap.ToString() ??
                    value.species?.LabelCap.ToString() ?? "Unknown wildlife";
                Widgets.Label(new Rect(16f, 128f, rect.width - 220f, 36f),
                    "Focal animal: " + targetName + "\n" +
                    (value.response == WildlifeMomentResponse.None
                        ? "Available for " : "Response time: ") +
                    WildlifeFieldJournalMapComponent.MomentTimeRemaining(value));
                Rect focus = new Rect(rect.width - 174f, 128f, 158f, 34f);
                if (Widgets.ButtonText(focus,
                    value.animal?.Spawned == true ? "Focus Animal" : "Focus Evidence"))
                {
                    Thing focusThing = value.animal?.Spawned == true
                        ? value.animal : value.evidence?.Spawned == true ? value.evidence : null;
                    if (focusThing != null) WildlifeUI.Show(focusThing);
                    else if (value.focusCell.IsValid) WildlifeUI.Focus(value.focusCell, map);
                }
                TooltipHandler.TipRegion(focus,
                    "Center the map on the focal animal or its remaining physical evidence.");
                if (value.response == WildlifeMomentResponse.None)
                {
                    List<WildlifeMomentResponse> responses = new List<WildlifeMomentResponse>
                    {
                        WildlifeMomentResponse.Observe
                    };
                    if (WildlifeFieldJournalMapComponent.ResearchAllowsResponse(
                        WildlifeMomentResponse.Track))
                        responses.Add(WildlifeMomentResponse.Track);
                    if (HerdsMod.Settings.enableNotableAnimals && !value.protectionDeclared)
                        responses.Add(WildlifeMomentResponse.Protect);
                    if (WildlifeFieldJournalMapComponent.ResearchAllowsResponse(
                        WildlifeMomentResponse.Hunt))
                        responses.Add(WildlifeMomentResponse.Hunt);
                    responses.Add(WildlifeMomentResponse.Ignore);
                    Text.Font = GameFont.Tiny;
                    GUI.color = new Color(0.68f, 0.76f, 0.68f);
                    Widgets.Label(new Rect(16f, 174f, rect.width - 32f, 18f),
                        value.protectionDeclared
                            ? "PROTECTED • CHOOSE AN ADDITIONAL RESPONSE"
                            : "CHOOSE A RESPONSE");
                    GUI.color = Color.white;
                    Text.Font = GameFont.Small;
                    float width = (rect.width - 16f - (responses.Count - 1) * 4f) /
                        responses.Count;
                    for (int i = 0; i < responses.Count; i++)
                    {
                        WildlifeMomentResponse response = responses[i];
                        Rect button = new Rect(16f + i * (width + 4f), 197f, width, 40f);
                        bool available = component.ResponseAvailable(response, out string reason);
                        string label = response == WildlifeMomentResponse.Ignore
                            ? "Leave Alone" : response.ToString();
                        if (Widgets.ButtonText(button, label, active: available))
                            component.ChooseMomentResponse(response);
                        TooltipHandler.TipRegion(button, available
                            ? WildlifeFieldJournalMapComponent.MomentResponseTooltip(response)
                            : reason);
                    }
                }
                else
                {
                    Widgets.DrawBoxSolid(new Rect(16f, 181f, rect.width - 32f, 56f),
                        new Color(0.11f, 0.2f, 0.19f, 0.86f));
                    Text.Font = GameFont.Tiny;
                    GUI.color = new Color(0.68f, 0.82f, 0.70f);
                    Widgets.Label(new Rect(26f, 188f, rect.width - 52f, 18f),
                        "RESPONSE UNDERWAY • " + value.response.ToString().ToUpperInvariant());
                    GUI.color = Color.white;
                    Text.Font = GameFont.Small;
                    Widgets.Label(new Rect(26f, 208f, rect.width - 52f, 24f),
                        value.continuedAsTrail
                            ? "The animal departed; the response continues through its physical trail."
                            : value.responder == null ? "The colony response is underway." :
                            value.responder.LabelShortCap + " is carrying out the response.");
                }
                historyY = 276f;
            }

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, historyY, rect.width, 28f), "Recent Moments");
            Text.Font = GameFont.Small;
            IReadOnlyList<WildlifeMomentOutcomeRecord> history = component.MomentHistory;
            if (history.Count == 0)
                Widgets.Label(new Rect(0f, historyY + 34f, rect.width, 34f),
                    "No Wildlife Moments have been resolved yet.");
            int visible = Mathf.Min(history.Count,
                Mathf.FloorToInt((rect.height - historyY - 34f) / 46f));
            for (int i = 0; i < visible; i++)
            {
                WildlifeMomentOutcomeRecord entry = history[i];
                Rect row = new Rect(0f, historyY + 34f + i * 46f, rect.width, 40f);
                Widgets.DrawHighlightIfMouseover(row);
                GUI.color = entry.success ? new Color(0.62f, 0.92f, 0.65f) :
                    new Color(1f, 0.48f, 0.42f);
                Widgets.Label(new Rect(row.x + 8f, row.y + 5f, row.width - 16f, 30f),
                    WildlifeFieldJournalMapComponent.OpportunityLabel(entry.kind) +
                    " • " + entry.species.LabelCap + " • " + entry.response);
                GUI.color = Color.white;
                TooltipHandler.TipRegion(row, entry.text);
            }
        }

        private void DrawProjects(Rect rect, WildlifeFieldJournalMapComponent component)
        {
            if (!HerdsMod.Settings.enableStewardProjects || !HerdsMod.Settings.enableWildlifeManagementGoals)
            {
                Widgets.Label(rect, "Wildlife Steward Projects are disabled in settings.");
                return;
            }
            WildlifeStewardProjectRecord value = component.Project;
            if (value != null)
            {
                Widgets.DrawMenuSection(new Rect(0f, 0f, rect.width, 120f));
                Text.Font = GameFont.Medium;
                Widgets.Label(new Rect(14f, 12f, rect.width - 190f, 28f), WildlifeFieldJournalMapComponent.ProjectLabel(value.kind));
                Text.Font = GameFont.Small;
                Widgets.Label(new Rect(14f, 45f, rect.width - 28f, 24f), value.species.LabelCap + "  •  " + value.progress.ToStringPercent() + " complete");
                Widgets.FillableBar(new Rect(14f, 78f, rect.width - 210f, 18f), Mathf.Clamp01(value.progress));
                if (Widgets.ButtonText(new Rect(rect.width - 354f, 72f, 164f, 32f), "Assign Fieldwork"))
                    ShowProjectWorkerMenu(component);
                if (Widgets.ButtonText(new Rect(rect.width - 180f, 72f, 164f, 32f), "Cancel Project")) component.CancelProject();
                TooltipHandler.TipRegion(new Rect(14f, 8f, rect.width - 28f, 98f),
                    WildlifeFieldJournalMapComponent.ProjectDescription(value.kind));
                return;
            }
            Widgets.Label(new Rect(0f, 0f, rect.width, 40f), "Choose a long-term objective. Progress is earned through matching structures and wildlife actions.");
            WildlifeStewardProjectKind[] kinds = (WildlifeStewardProjectKind[])Enum.GetValues(typeof(WildlifeStewardProjectKind));
            for (int i = 0; i < kinds.Length; i++)
            {
                WildlifeStewardProjectKind kind = kinds[i];
                Rect button = new Rect(0f, 52f + i * 48f, 330f, 38f);
                if (Widgets.ButtonText(button, WildlifeFieldJournalMapComponent.ProjectLabel(kind)))
                    ChooseProjectSpecies(component, kind);
                TooltipHandler.TipRegion(button,
                    WildlifeFieldJournalMapComponent.ProjectDescription(kind));
            }
        }

        private void ShowProjectWorkerMenu(WildlifeFieldJournalMapComponent component)
        {
            List<FloatMenuOption> options = map.mapPawns.FreeColonistsSpawned
                .OrderByDescending(pawn => pawn.skills?.GetSkill(SkillDefOf.Animals)?.Level ?? 0)
                .Select(pawn => new FloatMenuOption(pawn.LabelShortCap,
                    () =>
                    {
                        if (component.AssignProjectWork(pawn)) WildlifeUI.Focus(pawn);
                        else Messages.Message(pawn.LabelShortCap + " cannot reach suitable project fieldwork.",
                            pawn, MessageTypeDefOf.RejectInput, false);
                    })).ToList();
            if (options.Count == 0) options.Add(new FloatMenuOption("No colonists are available.", null));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void ChooseProjectSpecies(WildlifeFieldJournalMapComponent component, WildlifeStewardProjectKind kind)
        {
            List<ThingDef> species = component.Entries.Select(entry => entry.species).Where(def => def != null)
                .OrderBy(def => def.label).ToList();
            if (kind == WildlifeStewardProjectKind.SuppressPredators)
                species = species.Where(WildlifeSpeciesClassification.IsPredator).ToList();
            else if (kind == WildlifeStewardProjectKind.MigrationCorridor ||
                kind == WildlifeStewardProjectKind.ProtectMigration)
                species = species.Where(HuntingExpeditionMapComponent.IsHerdSpecies).ToList();
            else if (kind == WildlifeStewardProjectKind.AttractRareBirds)
                species = species.Where(PreyProfileDatabase.IsBird).ToList();
            species = species.Where(def => component.CanStartProject(kind, def, out _)).ToList();
            if (species.Count == 0)
            {
                Messages.Message(kind == WildlifeStewardProjectKind.RestoreSpecies
                        ? "No known species has declined enough to require restoration."
                        : "The colony must identify suitable wildlife before beginning this project.",
                    MessageTypeDefOf.RejectInput, false);
                return;
            }
            Find.WindowStack.Add(new FloatMenu(species.Select(def =>
                new FloatMenuOption(def.LabelCap, () => component.StartProject(kind, def))).ToList()));
        }

        private void DrawNotables(Rect rect)
        {
            List<NotableAnimalRecord> records = map.GetComponent<NotableWildlifeMapComponent>()?.Records
                .OrderByDescending(record => record.discoveredTick).ToList() ?? new List<NotableAnimalRecord>();
            Rect view = new Rect(0f, 0f, rect.width - 18f, Mathf.Max(rect.height, records.Count * 58f));
            Widgets.BeginScrollView(rect, ref scroll, view);
            for (int i = 0; i < records.Count; i++)
            {
                NotableAnimalRecord record = records[i];
                Rect row = new Rect(0f, i * 58f, view.width, 52f);
                Widgets.DrawMenuSection(row);
                Widgets.Label(new Rect(10f, row.y + 6f, row.width * 0.42f, 24f), record.title);
                Widgets.Label(new Rect(row.width * 0.43f, row.y + 6f, row.width * 0.35f, 38f), record.species.LabelCap + " • " + record.distinction);
                if (Widgets.ButtonText(new Rect(row.xMax - 130f, row.y + 10f, 120f, 30f), "Open Story"))
                    Find.WindowStack.Add(new Window_NotableAnimalStory(map.GetComponent<NotableWildlifeMapComponent>(), record));
            }
            Widgets.EndScrollView();
            if (records.Count == 0) Widgets.Label(new Rect(8f, 8f, rect.width - 16f, 40f), "No notable animal stories have been recorded.");
        }

        private void DrawFolklore(Rect rect)
        {
            if (!HerdsMod.Settings.enableWildlifeFolklore)
            {
                Widgets.Label(rect, "Wildlife Folklore is disabled in settings.");
                return;
            }
            WildlifeMemoryMapComponent component = map.GetComponent<WildlifeMemoryMapComponent>();
            List<WildlifeFolkloreRecord> stories = component?.Folklore
                .OrderByDescending(value => value.createdTick).ToList() ?? new List<WildlifeFolkloreRecord>();
            int focusedIndex = focusedStoryTick < 0 ? -1 :
                stories.FindIndex(value => value.createdTick == focusedStoryTick);
            if (!positionedStory && focusedIndex >= 0)
            {
                scroll.y = focusedIndex * 86f;
                positionedStory = true;
            }
            Rect header = new Rect(0f, 0f, rect.width, 104f);
            Widgets.DrawMenuSection(header);
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(12f, 10f, rect.width - 210f, 28f), "Living Folklore");
            Text.Font = GameFont.Small;
            int regionalLegends = stories.Count(value => value.reach >= 2);
            Widgets.Label(new Rect(12f, 42f, rect.width - 220f, 48f),
                stories.Sum(value => value.retellings) + " retellings  •  " + regionalLegends +
                " regional legends\nStories teach younger colonists and spread through visitors and other colonies.");
            bool ceremonyReady = HerdsMod.Settings.enableWildlifeCeremonies &&
                component?.CeremonyCooldownTicks == 0 && component.CeremonyGathering == false;
            if (Widgets.ButtonText(new Rect(rect.width - 194f, 12f, 180f, 34f), "Hold Ceremony", active: ceremonyReady))
                ShowCeremonyMenu(component, stories);
            if (!ceremonyReady)
                TooltipHandler.TipRegion(new Rect(rect.width - 194f, 12f, 180f, 34f),
                    !HerdsMod.Settings.enableWildlifeCeremonies ? "Wildlife Ceremonies are disabled." :
                    component.CeremonyGathering ? "A wildlife ceremony is currently gathering." :
                    "Another ceremony can be held in " + component.CeremonyCooldownTicks.ToStringTicksToPeriod() + ".");
            WildlifeLegendQuestRecord quest = component?.LegendQuest;
            if (quest != null)
            {
                Rect questRect = new Rect(rect.width - 280f, 53f, 266f, 40f);
                Widgets.DrawHighlightIfMouseover(questRect);
                Widgets.Label(new Rect(questRect.x + 5f, questRect.y, questRect.width - 70f, 38f),
                    quest.title + "\n" + quest.objective);
                if (Widgets.ButtonText(new Rect(questRect.xMax - 64f, questRect.y + 4f, 60f, 30f), "Focus") &&
                    quest.animal?.Spawned == true)
                    WildlifeUI.Show(quest.animal);
                TooltipHandler.TipRegion(questRect, component.LegendQuestDescription(quest) + "\nExpires in " +
                    Mathf.Max(0, quest.expiresTick - Find.TickManager.TicksGame).ToStringTicksToPeriod() + ".");
            }
            Rect outer = new Rect(0f, 112f, rect.width, rect.height - 112f);
            Rect view = new Rect(0f, 0f, outer.width - 18f, Mathf.Max(outer.height, stories.Count * 86f));
            Widgets.BeginScrollView(outer, ref scroll, view);
            for (int i = 0; i < stories.Count; i++)
            {
                WildlifeFolkloreRecord story = stories[i];
                Rect row = new Rect(0f, i * 86f, view.width, 80f);
                if (i == focusedIndex)
                    Widgets.DrawBoxSolid(row, new Color(0.22f, 0.34f, 0.20f, 0.9f));
                Widgets.DrawMenuSection(row);
                GUI.color = story.positive ? new Color(0.72f, 0.92f, 0.68f) : new Color(1f, 0.62f, 0.58f);
                Widgets.Label(new Rect(10f, row.y + 7f, row.width - 20f, 24f), story.title);
                GUI.color = Color.white;
                Widgets.Label(new Rect(10f, row.y + 31f, row.width - 20f, 38f), story.story);
                Text.Anchor = TextAnchor.LowerRight;
                GUI.color = Color.gray;
                Widgets.Label(new Rect(row.xMax - 190f, row.yMax - 25f, 180f, 20f),
                    story.retellings + (story.retellings == 1 ? " retelling" : " retellings") +
                    (story.reach >= 2 ? "  •  regional legend" : story.reach == 1 ? "  •  shared" : ""));
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
                if (story.animal?.Spawned == true && Widgets.ButtonInvisible(row))
                    WildlifeUI.Show(story.animal);
                TooltipHandler.TipRegion(row, story.animal?.Spawned == true
                    ? "Click to select " + story.animal.LabelShortCap + ". Stories are retold over time while colonists recreate."
                    : "Stories are retold over time while colonists recreate.");
            }
            Widgets.EndScrollView();
            if (stories.Count == 0) Widgets.Label(new Rect(8f, 122f, rect.width - 16f, 40f),
                "Major encounters with notable wildlife, studies, rescues, and remarkable hunts will become colony folklore.");
        }

        private void ShowCeremonyMenu(WildlifeMemoryMapComponent component, List<WildlifeFolkloreRecord> stories)
        {
            bool unrestricted = !ModsConfig.IdeologyActive;
            bool reverence = unrestricted || map.mapPawns.FreeColonists.Any(pawn =>
                pawn.Ideo?.HasPrecept(HerdsDefOf.Herds_WildlifeEthic_Reverence) == true);
            bool stewardship = unrestricted || map.mapPawns.FreeColonists.Any(pawn =>
                pawn.Ideo?.HasPrecept(HerdsDefOf.Herds_WildlifeEthic_Stewardship) == true);
            bool tradition = unrestricted || map.mapPawns.FreeColonists.Any(pawn =>
                pawn.Ideo?.HasPrecept(HerdsDefOf.Herds_WildlifeEthic_Tradition) == true);
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            if (tradition && stories.Any(value => value.title.IndexOf("hunt", StringComparison.OrdinalIgnoreCase) >= 0))
                options.Add(new FloatMenuOption(WildlifeMemoryMapComponent.CeremonyLabel(WildlifeCeremonyKind.FirstHunt),
                    () => component.PerformCeremony(WildlifeCeremonyKind.FirstHunt)));
            if (reverence || stewardship)
                options.Add(new FloatMenuOption(WildlifeMemoryMapComponent.CeremonyLabel(WildlifeCeremonyKind.MigrationWatch),
                    () => component.PerformCeremony(WildlifeCeremonyKind.MigrationWatch)));
            if ((reverence || stewardship) && stories.Any(value => !value.positive))
                options.Add(new FloatMenuOption(WildlifeMemoryMapComponent.CeremonyLabel(WildlifeCeremonyKind.Memorial),
                    () => component.PerformCeremony(WildlifeCeremonyKind.Memorial)));
            List<Pawn> releasable = map.mapPawns.SpawnedColonyAnimals.Where(pawn => !pawn.Downed).ToList();
            if ((reverence || stewardship) && releasable.Count > 0)
                options.Add(new FloatMenuOption(WildlifeMemoryMapComponent.CeremonyLabel(WildlifeCeremonyKind.CeremonialRelease),
                    () => Find.WindowStack.Add(new FloatMenu(releasable.Select(animal =>
                        new FloatMenuOption(animal.LabelShortCap, () =>
                            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                                "Release " + animal.LabelShortCap + " back into the wild? This animal will leave colony control.",
                                () => component.PerformCeremony(WildlifeCeremonyKind.CeremonialRelease, animal))))).ToList()))));
            if (options.Count == 0) options.Add(new FloatMenuOption(
                "No ceremony currently matches the colony's wildlife ethic or recorded stories", null));
            Find.WindowStack.Add(new FloatMenu(options));
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class WildlifeMomentAnimalGizmoPatch
    {
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> values, Pawn __instance)
        {
            foreach (Gizmo value in values) yield return value;
            if (HerdsMod.Settings?.enableDynamicWildlifeOpportunities != true ||
                __instance?.Spawned != true || __instance.RaceProps?.Animal != true) yield break;
            WildlifeOpportunityRecord moment =
                __instance.Map.GetComponent<WildlifeFieldJournalMapComponent>()?.Opportunity;
            if (moment == null || moment.animal != __instance &&
                moment.otherAnimal != __instance) yield break;
            yield return new Command_Action
            {
                defaultLabel = "Wildlife Moment",
                defaultDesc = WildlifeFieldJournalMapComponent.OpportunityLabel(moment.kind) +
                    "\n\n" + moment.description +
                    "\n\nAvailable for " +
                    WildlifeFieldJournalMapComponent.MomentTimeRemaining(moment) + "." +
                    "\n\nOpen the Wildlife Journal Field Log to choose or review the colony response.",
                icon = TexCommand.GatherSpotActive,
                action = () => Find.WindowStack.Add(
                    new Window_WildlifeFieldJournal(__instance.Map, 2))
            };
        }
    }

    public static class WildlifeMomentDebugActions
    {
        [LudeonTK.DebugAction("Wildlife", "Force Wildlife Moment",
            actionType = LudeonTK.DebugActionType.Action,
            allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        public static void ForceMoment()
        {
            List<string> result = Find.CurrentMap?.GetComponent<WildlifeFieldJournalMapComponent>()?
                .DebugForceMoment();
            Messages.Message(result == null ? "No map." : string.Join(" ", result.Take(2)),
                MessageTypeDefOf.NeutralEvent, false);
        }
    }
}
