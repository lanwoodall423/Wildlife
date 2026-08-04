using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using UnityEngine;
using Verse;

namespace Herds
{
    public enum WildlifeHypothesisState
    {
        Open,
        Supported,
        Disputed,
        Resolved
    }

    public enum WildlifeManagementPolicy
    {
        Nonintervention,
        SeasonalHuntingRestriction,
        RefugeProtection,
        FeedingCorridor,
        ControlledCull,
        CaptureAndRelocate
    }

    public sealed class WildlifeHypothesisEvidence : IExposable
    {
        public string text;
        public string source;
        public bool contradicts;
        public float weight;
        public int tick;

        public void ExposeData()
        {
            Scribe_Values.Look(ref text, "text");
            Scribe_Values.Look(ref source, "source");
            Scribe_Values.Look(ref contradicts, "contradicts");
            Scribe_Values.Look(ref weight, "weight");
            Scribe_Values.Look(ref tick, "tick");
        }
    }

    public sealed class WildlifeHypothesisCandidate : IExposable
    {
        public string explanation;
        public float support;
        public float contradiction;

        public void ExposeData()
        {
            Scribe_Values.Look(ref explanation, "explanation");
            Scribe_Values.Look(ref support, "support");
            Scribe_Values.Look(ref contradiction, "contradiction");
        }
    }

    public sealed class WildlifeHypothesisRecord : IExposable
    {
        public int id;
        public ThingDef species;
        public string title;
        public WildlifeHypothesisState state;
        public string bestNextObservation;
        public string actingEarlyRisk;
        public float confidence;
        public int createdTick;
        public int lastUpdatedTick;
        public List<WildlifeHypothesisCandidate> candidates = new List<WildlifeHypothesisCandidate>();
        public List<WildlifeHypothesisEvidence> evidence = new List<WildlifeHypothesisEvidence>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id");
            Scribe_Defs.Look(ref species, "species");
            Scribe_Values.Look(ref title, "title");
            Scribe_Values.Look(ref state, "state", WildlifeHypothesisState.Open);
            Scribe_Values.Look(ref bestNextObservation, "bestNextObservation");
            Scribe_Values.Look(ref actingEarlyRisk, "actingEarlyRisk");
            Scribe_Values.Look(ref confidence, "confidence");
            Scribe_Values.Look(ref createdTick, "createdTick");
            Scribe_Values.Look(ref lastUpdatedTick, "lastUpdatedTick");
            Scribe_Collections.Look(ref candidates, "candidates", LookMode.Deep);
            Scribe_Collections.Look(ref evidence, "evidence", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                candidates = candidates?.Where(value => value != null).Take(4).ToList() ?? new List<WildlifeHypothesisCandidate>();
                evidence = evidence?.Where(value => value != null).Take(16).ToList() ?? new List<WildlifeHypothesisEvidence>();
            }
        }

        public WildlifeHypothesisCandidate LeadingCandidate => candidates.OrderByDescending(value => value.support - value.contradiction).FirstOrDefault();
    }

    public sealed class WildlifeNarrativeRecord : IExposable
    {
        public WildlifeEventKind kind;
        public ThingDef species;
        public Pawn animal;
        public string title;
        public string summary;
        public string interpretation;
        public int tick;
        public bool preserved;

        public void ExposeData()
        {
            Scribe_Values.Look(ref kind, "kind");
            Scribe_Defs.Look(ref species, "species");
            Scribe_References.Look(ref animal, "animal");
            Scribe_Values.Look(ref title, "title");
            Scribe_Values.Look(ref summary, "summary");
            Scribe_Values.Look(ref interpretation, "interpretation");
            Scribe_Values.Look(ref tick, "tick");
            Scribe_Values.Look(ref preserved, "preserved");
        }
    }

    public sealed class WildlifePolicyRecord : IExposable
    {
        public ThingDef species;
        public WildlifeManagementPolicy policy;
        public Pawn decidedBy;
        public int decidedTick;
        public string forecast;
        public float uncertainty;

        public void ExposeData()
        {
            Scribe_Defs.Look(ref species, "species");
            Scribe_Values.Look(ref policy, "policy", WildlifeManagementPolicy.Nonintervention);
            Scribe_References.Look(ref decidedBy, "decidedBy");
            Scribe_Values.Look(ref decidedTick, "decidedTick");
            Scribe_Values.Look(ref forecast, "forecast");
            Scribe_Values.Look(ref uncertainty, "uncertainty");
        }
    }

    /// <summary>Coordinates narrative records without owning animal, population, trail, signal, or memory simulation.</summary>
    public sealed class WildlifeNarrativeDirector : MapComponent
    {
        private List<WildlifeHypothesisRecord> hypotheses = new List<WildlifeHypothesisRecord>();
        private List<WildlifeNarrativeRecord> stories = new List<WildlifeNarrativeRecord>();
        private List<WildlifePolicyRecord> policies = new List<WildlifePolicyRecord>();
        private int nextHypothesisId = 1;
        private IDisposable subscription;

        public WildlifeNarrativeDirector(Map map) : base(map) { }

        public IReadOnlyList<WildlifeHypothesisRecord> Hypotheses => ReadOnly(hypotheses);
        public IReadOnlyList<WildlifeNarrativeRecord> Stories => ReadOnly(stories);
        public IReadOnlyList<WildlifePolicyRecord> Policies => ReadOnly(policies);
        public IReadOnlyList<WildlifeHypothesisRecord> OpenHypotheses =>
            ReadOnly(hypotheses.Where(value => value != null && value.state != WildlifeHypothesisState.Resolved));

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            subscription?.Dispose();
            subscription = WildlifeEventRouter.Shared.Subscribe(OnEvent);
            nextHypothesisId = Mathf.Max(nextHypothesisId, hypotheses.Count == 0 ? 1 : hypotheses.Max(value => value.id) + 1);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref hypotheses, "wildlifeHypotheses", LookMode.Deep);
            Scribe_Collections.Look(ref stories, "wildlifeNarrativeStories", LookMode.Deep);
            Scribe_Collections.Look(ref policies, "wildlifeManagementPolicies", LookMode.Deep);
            Scribe_Values.Look(ref nextHypothesisId, "nextWildlifeHypothesisId", 1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                hypotheses = hypotheses?.Where(value => value?.species?.race?.Animal == true).Take(24).ToList() ?? new List<WildlifeHypothesisRecord>();
                stories = stories?.Where(value => value != null && (value.species == null || value.species.race?.Animal == true)).Take(40).ToList() ?? new List<WildlifeNarrativeRecord>();
                policies = policies?.Where(value => value?.species?.race?.Animal == true).ToList() ?? new List<WildlifePolicyRecord>();
            }
        }

        public WildlifeHypothesisRecord HypothesisFor(ThingDef species, bool create = true)
        {
            if (species == null) return null;
            WildlifeHypothesisRecord existing = hypotheses.FirstOrDefault(value => value?.species == species && value.state != WildlifeHypothesisState.Resolved);
            if (existing != null || !create) return existing;
            WildlifeEcologySnapshot snapshot = WildlifeEcologySnapshots.For(map);
            WildlifeSpeciesSnapshot speciesSnapshot = snapshot?.For(species);
            if (speciesSnapshot == null || !Interesting(speciesSnapshot)) return null;
            return CreateHypothesis(speciesSnapshot, null);
        }

        public WildlifePolicyRecord PolicyFor(ThingDef species) => policies.FirstOrDefault(value => value?.species == species);

        public bool TrySetPolicy(ThingDef species, WildlifeManagementPolicy policy, Pawn decisionMaker, out string reason)
        {
            reason = null;
            if (species?.race?.Animal != true)
            {
                reason = "Choose a real wildlife species.";
                return false;
            }
            if (!WildlifeKnowledgeAdapter.CanMakePolicyDecision(species, policy, decisionMaker, out reason)) return false;
            WildlifeSpeciesSnapshot value = WildlifeEcologySnapshots.For(map)?.For(species);
            WildlifePolicyRecord record = PolicyFor(species);
            if (record == null)
            {
                record = new WildlifePolicyRecord { species = species };
                policies.Add(record);
            }
            record.policy = policy;
            record.decidedBy = decisionMaker;
            record.decidedTick = Find.TickManager?.TicksGame ?? 0;
            record.uncertainty = Mathf.Clamp01(1f - (value?.confidence ?? 0f));
            record.forecast = ForecastFor(policy, value);
            RegionalSpeciesRecord regional = map.GetComponent<RegionalWildlifeMapComponent>()?.Records.FirstOrDefault(item => item.species == species);
            if (regional != null) regional.policy = LegacyPolicyValue(policy);
            WildlifeEventUtility.Publish(WildlifeEventKind.Policy, map, decisionMaker, null, species, "Wildlife policy",
                PolicyLabel(policy) + " was chosen for " + species.LabelCap + ". " + record.forecast, "policy", true, 1f,
                value?.confidence ?? 0f, 0f, true, "policy:" + species.defName + ":" + record.decidedTick);
            return true;
        }

        public string ForecastFor(ThingDef species, WildlifeManagementPolicy policy)
        {
            return ForecastFor(policy, WildlifeEcologySnapshots.For(map)?.For(species));
        }

        public void ResolveHypothesis(WildlifeHypothesisRecord hypothesis, string explanation)
        {
            if (hypothesis == null || hypothesis.state == WildlifeHypothesisState.Resolved) return;
            WildlifeHypothesisCandidate selected = hypothesis.candidates.FirstOrDefault(value => value.explanation == explanation) ?? hypothesis.LeadingCandidate;
            if (selected == null) return;
            hypothesis.state = WildlifeHypothesisState.Resolved;
            hypothesis.lastUpdatedTick = Find.TickManager?.TicksGame ?? 0;
            hypothesis.title = "Documented: " + hypothesis.title;
            AddStory(WildlifeEventKind.Documentation, hypothesis.species, null, hypothesis.title,
                selected.explanation + ". The colony preserved the evidence and its remaining uncertainty.",
                "The leading explanation was documented rather than treated as certainty.", true);
            WildlifeEventUtility.Publish(WildlifeEventKind.Documentation, map, null, null, hypothesis.species,
                "Investigation", hypothesis.title + ": " + selected.explanation, "hypothesis", true,
                1f, hypothesis.confidence, 0f, true, "hypothesis:" + hypothesis.id);
        }

        private void OnEvent(WildlifeEvent value)
        {
            if (value?.map != map) return;
            WildlifeEcologySnapshots.Invalidate(map);
            if (value.kind == WildlifeEventKind.Story || value.kind == WildlifeEventKind.NotableAnimal || value.kind == WildlifeEventKind.Documentation)
                AddStory(value.kind, value.species ?? value.animal?.def, value.animal,
                    value.kind == WildlifeEventKind.NotableAnimal ? "A notable animal entered the record" : "A wildlife event was preserved",
                    value.summary, "The direct event is preserved; later evidence may change its interpretation.", value.documented);
            if (value.species != null && (value.kind == WildlifeEventKind.PopulationChange || value.kind == WildlifeEventKind.Signal ||
                value.kind == WildlifeEventKind.Migration || value.kind == WildlifeEventKind.MysteryEvidence))
            {
                WildlifeHypothesisRecord hypothesis = HypothesisFor(value.species);
                if (hypothesis != null) AddEventEvidence(hypothesis, value);
            }
        }

        private WildlifeHypothesisRecord CreateHypothesis(WildlifeSpeciesSnapshot value, WildlifeEvent trigger)
        {
            WildlifeHypothesisRecord hypothesis = new WildlifeHypothesisRecord
            {
                id = nextHypothesisId++,
                species = value.species,
                title = "Why has " + value.species.LabelCap + " changed its pattern?",
                state = WildlifeHypothesisState.Open,
                createdTick = Find.TickManager?.TicksGame ?? 0,
                lastUpdatedTick = Find.TickManager?.TicksGame ?? 0,
                bestNextObservation = BestNextObservation(value),
                actingEarlyRisk = ActingEarlyRisk(value),
                confidence = Mathf.Clamp01(value.confidence * 0.65f)
            };
            hypothesis.candidates.Add(new WildlifeHypothesisCandidate
            {
                explanation = value.pressure > 0.65f ? "Local habitat pressure is redirecting the population" : "Seasonal movement is changing the visible range",
                support = value.pressure > 0.65f ? 0.42f : 0.26f,
                contradiction = value.pressure < 0.25f ? 0.12f : 0.04f
            });
            hypothesis.candidates.Add(new WildlifeHypothesisCandidate
            {
                explanation = value.variations.Any() ? "A learned regional behavior is altering the usual route" : "Repeated local predator encounters may be provoking defensive behavior",
                support = value.variations.Any() ? 0.38f : 0.18f,
                contradiction = value.localCount > 0 && value.nearbyPopulation <= value.localCount ? 0.08f : 0.02f
            });
            hypothesis.candidates.Add(new WildlifeHypothesisCandidate
            {
                explanation = "The colony's sample is too narrow and the apparent change is an observation gap",
                support = Mathf.Clamp01(0.45f - value.confidence * 0.35f),
                contradiction = value.evidence.Count >= 4 ? 0.25f : 0.04f
            });
            if (trigger != null) AddEventEvidence(hypothesis, trigger);
            hypotheses.Insert(0, hypothesis);
            if (hypotheses.Count > 24) hypotheses.RemoveRange(24, hypotheses.Count - 24);
            return hypothesis;
        }

        private void AddEventEvidence(WildlifeHypothesisRecord hypothesis, WildlifeEvent value)
        {
            if (hypothesis == null || value == null || value.species != hypothesis.species) return;
            string text;
            if (value.kind == WildlifeEventKind.Signal)
            {
                string layer;
                bool playerFacing = value.metadata != null &&
                    value.metadata.TryGetValue("observationLayer", out layer) && layer == "signal";
                text = playerFacing && !value.summary.NullOrEmpty()
                    ? value.summary
                    : "Wildlife signal evidence was recorded.";
            }
            else
            {
                text = value.summary.NullOrEmpty() ? value.kind.ToString() : value.summary;
            }
            if (hypothesis.evidence.Any(item => item.text == text && item.tick == value.tick)) return;
            hypothesis.evidence.Insert(0, new WildlifeHypothesisEvidence
            {
                text = text,
                source = value.source,
                contradicts = !value.success,
                weight = Mathf.Clamp(value.quality * Mathf.Max(0.1f, value.confidence), 0.05f, 1f),
                tick = value.tick
            });
            if (hypothesis.evidence.Count > 16) hypothesis.evidence.RemoveRange(16, hypothesis.evidence.Count - 16);
            float support = hypothesis.evidence.Where(item => !item.contradicts).Sum(item => item.weight);
            float contradiction = hypothesis.evidence.Where(item => item.contradicts).Sum(item => item.weight);
            hypothesis.confidence = Mathf.Clamp01((support + hypothesis.candidates.Sum(item => item.support * 0.2f)) /
                (support + contradiction + 1f));
            hypothesis.state = contradiction > support * 0.55f ? WildlifeHypothesisState.Disputed :
                hypothesis.confidence >= 0.68f ? WildlifeHypothesisState.Supported : WildlifeHypothesisState.Open;
            hypothesis.lastUpdatedTick = value.tick;
        }

        private static bool Interesting(WildlifeSpeciesSnapshot value) =>
            value.evidence.Count >= 2 || value.trails.Count > 0 || value.signals.Count > 0 ||
            Mathf.Abs(value.nearbyPopulation - value.regionalPopulation) > 2f || value.variations.Count > 0;

        private static string BestNextObservation(WildlifeSpeciesSnapshot value)
        {
            if (value.trails.Count > 0) return "Study a fresh trail and compare its direction with the regional estimate.";
            if (value.signals.Count > 0) return "Observe the next call near the herd and test the local dialect.";
            if (value.localCount > 0) return "Repeat a quiet sighting in a different sector before acting.";
            return "Run a regional survey or expedition to widen the sample.";
        }

        private static string ActingEarlyRisk(WildlifeSpeciesSnapshot value)
        {
            float risk = Mathf.Clamp01(0.62f - value.confidence * 0.45f + value.pressure * 0.25f);
            return risk > 0.65f ? "High: an early intervention may displace the population or erase the evidence." :
                risk > 0.35f ? "Moderate: the action may help locally while hiding the wider cause." :
                "Low: the available evidence is consistent, but not complete.";
        }

        private void AddStory(WildlifeEventKind kind, ThingDef species, Pawn animal, string title,
            string summary, string interpretation, bool preserved)
        {
            if (title.NullOrEmpty() && summary.NullOrEmpty()) return;
            if (stories.Any(value => value?.title == title && value.summary == summary)) return;
            stories.Insert(0, new WildlifeNarrativeRecord
            {
                kind = kind,
                species = species,
                animal = animal,
                title = title,
                summary = summary,
                interpretation = interpretation,
                tick = Find.TickManager?.TicksGame ?? 0,
                preserved = preserved
            });
            if (stories.Count > 40) stories.RemoveRange(40, stories.Count - 40);
        }

        private static string ForecastFor(WildlifeManagementPolicy policy, WildlifeSpeciesSnapshot value)
        {
            string species = value?.species?.LabelCap ?? "the population";
            switch (policy)
            {
                case WildlifeManagementPolicy.SeasonalHuntingRestriction:
                    return "Predicted consequence: recovery should improve, but surplus may increase if habitat remains crowded.";
                case WildlifeManagementPolicy.RefugeProtection:
                    return "Predicted consequence: safer breeding ground and stronger return signals, with pressure displaced elsewhere.";
                case WildlifeManagementPolicy.FeedingCorridor:
                    return "Predicted consequence: movement may become more predictable while local disease and crowding risk rise.";
                case WildlifeManagementPolicy.ControlledCull:
                    return "Predicted consequence: immediate pressure falls, but social structure and migration confidence may be damaged.";
                case WildlifeManagementPolicy.CaptureAndRelocate:
                    return "Predicted consequence: local pressure falls while the receiving range gains uncertain social knowledge.";
                default:
                    return "Predicted consequence: " + species + " remains an unaltered reference population for continued observation.";
            }
        }

        private static int LegacyPolicyValue(WildlifeManagementPolicy policy)
        {
            return policy == WildlifeManagementPolicy.ControlledCull || policy == WildlifeManagementPolicy.CaptureAndRelocate ? -1 :
                policy == WildlifeManagementPolicy.Nonintervention ? 0 : 1;
        }

        public static string PolicyLabel(WildlifeManagementPolicy policy) =>
            policy == WildlifeManagementPolicy.SeasonalHuntingRestriction ? "Seasonal hunting restriction" :
            policy == WildlifeManagementPolicy.RefugeProtection ? "Refuge protection" :
            policy == WildlifeManagementPolicy.FeedingCorridor ? "Feeding corridor" :
            policy == WildlifeManagementPolicy.ControlledCull ? "Controlled cull" :
            policy == WildlifeManagementPolicy.CaptureAndRelocate ? "Capture and relocation" : "Nonintervention";

        private static IReadOnlyList<T> ReadOnly<T>(IEnumerable<T> values) =>
            new ReadOnlyCollection<T>((values ?? Enumerable.Empty<T>()).Where(value => value != null).ToList());
    }

    public static class WildlifeNarrativeUtility
    {
        public static WildlifeNarrativeDirector For(Map map) => map?.GetComponent<WildlifeNarrativeDirector>();

        public static WildlifeHypothesisRecord HypothesisFor(Map map, ThingDef species) => For(map)?.HypothesisFor(species);

        public static string ForecastFor(Map map, ThingDef species, WildlifeManagementPolicy policy) =>
            For(map)?.ForecastFor(species, policy);
    }
}
