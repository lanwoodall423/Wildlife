using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Herds
{
    public enum WildlifeInsightPrecision
    {
        Unknown,
        Qualitative,
        Approximate,
        Measured
    }

    /// <summary>
    /// A player-facing value projected from an authoritative owner. The projection
    /// deliberately carries no gameplay object and never exposes an unauthorized
    /// numeric value.
    /// </summary>
    public sealed class WildlifeInsightValue
    {
        internal WildlifeInsightValue(bool available, WildlifeInsightPrecision precision, string text,
            string certainty, string provenance, bool hasNumeric, float numeric)
        {
            Available = available;
            Precision = precision;
            Text = text ?? string.Empty;
            Certainty = certainty ?? string.Empty;
            Provenance = provenance ?? string.Empty;
            HasNumeric = hasNumeric;
            Numeric = numeric;
        }

        public readonly bool Available;
        public readonly WildlifeInsightPrecision Precision;
        public readonly string Text;
        public readonly string Certainty;
        public readonly string Provenance;
        public readonly bool HasNumeric;
        public readonly float Numeric;

        public bool IsUnknown => !Available || Precision == WildlifeInsightPrecision.Unknown;

        internal static WildlifeInsightValue Unknown(string provenance = "No authorized evidence") =>
            new WildlifeInsightValue(false, WildlifeInsightPrecision.Unknown, "Unknown", "Unknown", provenance,
                false, 0f);
    }

    /// <summary>
    /// Presentation-only projection for one species. Wildlife simulation, Knowledge
    /// Framework, regional ownership, and signal systems remain the authorities.
    /// </summary>
    public sealed class WildlifeInsightSpeciesDisclosure
    {
        internal WildlifeInsightSpeciesDisclosure(string stableId, string subjectId, bool visible,
            bool knowledgeVisible, bool fieldGuideVisible, bool directObservation, bool currentLocationKnown,
            IntVec3 currentFocusCell, WildlifeInsightValue localPopulation, WildlifeInsightValue nearbyPopulation,
            WildlifeInsightValue regionalPopulation, WildlifeInsightValue habitat, WildlifeInsightValue pressure,
            WildlifeInsightValue trend, WildlifeInsightValue forecast, WildlifeInsightValue diseaseRisk,
            WildlifeInsightValue intervention, string certainty, string provenance, string evidenceSummary,
            IEnumerable<string> allowedActions)
        {
            StableId = stableId ?? string.Empty;
            SubjectId = subjectId ?? string.Empty;
            Visible = visible;
            KnowledgeVisible = knowledgeVisible;
            FieldGuideVisible = fieldGuideVisible;
            DirectObservation = directObservation;
            CurrentLocationKnown = currentLocationKnown;
            CurrentFocusCell = currentFocusCell;
            LocalPopulation = localPopulation ?? WildlifeInsightValue.Unknown();
            NearbyPopulation = nearbyPopulation ?? WildlifeInsightValue.Unknown();
            RegionalPopulation = regionalPopulation ?? WildlifeInsightValue.Unknown();
            Habitat = habitat ?? WildlifeInsightValue.Unknown();
            Pressure = pressure ?? WildlifeInsightValue.Unknown();
            Trend = trend ?? WildlifeInsightValue.Unknown();
            Forecast = forecast ?? WildlifeInsightValue.Unknown();
            DiseaseRisk = diseaseRisk ?? WildlifeInsightValue.Unknown();
            Intervention = intervention ?? WildlifeInsightValue.Unknown();
            Certainty = certainty ?? "Unknown";
            Provenance = provenance ?? "No authorized evidence";
            EvidenceSummary = evidenceSummary ?? "No authorized evidence";
            AllowedActions = new ReadOnlyCollection<string>((allowedActions ?? Enumerable.Empty<string>())
                .Where(value => !value.NullOrEmpty()).Distinct(StringComparer.Ordinal).ToList());
        }

        public readonly string StableId;
        public readonly string SubjectId;
        public readonly bool Visible;
        public readonly bool KnowledgeVisible;
        public readonly bool FieldGuideVisible;
        public readonly bool DirectObservation;
        public readonly bool CurrentLocationKnown;
        public readonly IntVec3 CurrentFocusCell;
        public readonly WildlifeInsightValue LocalPopulation;
        public readonly WildlifeInsightValue NearbyPopulation;
        public readonly WildlifeInsightValue RegionalPopulation;
        public readonly WildlifeInsightValue Habitat;
        public readonly WildlifeInsightValue Pressure;
        public readonly WildlifeInsightValue Trend;
        public readonly WildlifeInsightValue Forecast;
        public readonly WildlifeInsightValue DiseaseRisk;
        public readonly WildlifeInsightValue Intervention;
        public readonly string Certainty;
        public readonly string Provenance;
        public readonly string EvidenceSummary;
        public readonly IReadOnlyList<string> AllowedActions;

        public bool Allows(string action) => action != null && AllowedActions.Contains(action);
    }

    /// <summary>
    /// The single disclosure policy used by Journal, Atlas, regional wildlife, and
    /// compact inspectors. It only reads owner state and returns inert display data.
    /// </summary>
    public static class WildlifeInsightDisclosure
    {
        public const string ActionFocus = "focus-current-observation";
        public const string ActionFollowTrail = "follow-trail-evidence";
        public const string ActionManage = "manage-authorized-population";
        public const string ActionSurvey = "survey-authorized-population";
        public const string ActionExpedition = "open-authorized-expedition";

        internal static WildlifeInsightSpeciesDisclosure For(Map map, WildlifeSpeciesSnapshot source)
        {
            if (source == null || source.species == null)
                return Project(new WildlifeInsightSource());

            HerdsSettings settings = HerdsMod.Settings;
            RegionalWildlifeMapComponent regional = map?.GetComponent<RegionalWildlifeMapComponent>();
            bool directObservation = IsDirectlyObserved(map, source.species);
            bool evidence = source.evidence.Count > 0 || source.signals.Count > 0 ||
                source.trails.Count > 0 || source.migrations.Count > 0;
            bool knowledgeVisible = source.stageOrder > 0 || source.knowledge > 0.001f;
            bool fieldGuideVisible = directObservation || evidence || knowledgeVisible &&
                settings?.requireObservationForDetails != true;
            bool visible = knowledgeVisible || evidence || directObservation;

            WildlifeInsightSource input = new WildlifeInsightSource
            {
                StableId = source.subjectId,
                SubjectId = source.subjectId,
                StageOrder = source.stageOrder,
                KnowledgeVisible = knowledgeVisible,
                FieldGuideVisible = fieldGuideVisible,
                DirectObservation = directObservation,
                HasEvidence = evidence,
                HasRegionalRecord = source.hasRegionalRecord,
                HasPopulationKnowledge = source.populationKnowledge > 0.001f,
                HasHabitatKnowledge = source.habitatKnowledge > 0.001f,
                CurrentFocusCell = CurrentObservedCell(map, source.species),
                LocalCount = source.localCount,
                NearbyPopulation = source.nearbyPopulation,
                RegionalPopulation = source.regionalPopulation,
                PreviousNearbyPopulation = source.previousNearbyPopulation,
                HabitatQuality = source.habitatQuality,
                Pressure = source.pressure,
                Confidence = source.confidence,
                Forecast = source.forecast,
                DiseaseRisk = source.diseaseRisk,
                Intervention = source.interventionSummary,
                Stewardship = WildlifeProgression.Unlocked(WildlifeCapability.Stewardship),
                Telemetry = settings?.enableTelemetry == true &&
                    WildlifeProgression.Unlocked(WildlifeCapability.Telemetry) &&
                    regional?.HasOperationalMonitor(WildlifeToolKind.TelemetryStation) == true,
                AppliedEcology = settings?.enableAppliedEcology == true &&
                    WildlifeProgression.Unlocked(WildlifeCapability.AppliedEcology),
                DiseaseMonitoring = settings?.enableDiseaseMonitoring == true &&
                    WildlifeProgression.Unlocked(WildlifeCapability.DiseaseMonitoring),
                HasTrailEvidence = source.trails.Count > 0,
                HasMigrationEvidence = source.migrations.Count > 0,
                HasSignals = source.signals.Count > 0,
                Visible = visible
            };
            return Project(input);
        }

        internal static WildlifeInsightValue MapHabitat(Map map, WildlifeEcologySnapshot ecology)
        {
            if (ecology?.species == null) return WildlifeInsightValue.Unknown("No authorized habitat interpretation");
            for (int i = 0; i < ecology.species.Count; i++)
            {
                WildlifeInsightSpeciesDisclosure disclosure = For(map, ecology.species[i]);
                if (disclosure.Habitat.Available && disclosure.Pressure.Available)
                    return Value(QualitativeHabitat(ecology.habitatQuality), WildlifeInsightPrecision.Qualitative,
                        disclosure.Certainty, "Applied Ecology model", false, 0f);
            }
            return WildlifeInsightValue.Unknown("Applied Ecology and habitat knowledge are unavailable");
        }

        internal static bool IsDirectlyObserved(Map map, ThingDef species)
        {
            HerdMapComponent herds = map?.GetComponent<HerdMapComponent>();
            IReadOnlyList<Pawn> pawns = map?.mapPawns?.AllPawnsSpawned;
            if (herds == null || pawns == null || species == null) return false;
            for (int i = 0; i < pawns.Count; i++)
                if (pawns[i]?.def == species && IsDirectlyObserved(pawns[i], herds)) return true;
            return false;
        }

        internal static bool IsDirectlyObserved(Pawn pawn) =>
            IsDirectlyObserved(pawn, pawn?.Map?.GetComponent<HerdMapComponent>());

        internal static bool IsPresentationVisible(Pawn pawn) =>
            pawn?.Spawned == true && !pawn.Dead &&
            (pawn.Faction == Faction.OfPlayer || IsDirectlyObserved(pawn));

        private static bool IsDirectlyObserved(Pawn pawn, HerdMapComponent herds) =>
            pawn?.Spawned == true && pawn.Faction == null && !pawn.Dead &&
            herds?.IsObserved(pawn) == true;

        internal static IntVec3 CurrentObservedCell(Map map, ThingDef species)
        {
            if (!IsDirectlyObserved(map, species)) return IntVec3.Invalid;
            HerdMapComponent herds = map?.GetComponent<HerdMapComponent>();
            IReadOnlyList<Pawn> pawns = map?.mapPawns?.AllPawnsSpawned;
            for (int i = 0; i < (pawns?.Count ?? 0); i++)
                if (pawns[i]?.def == species && IsDirectlyObserved(pawns[i], herds)) return pawns[i].Position;
            return IntVec3.Invalid;
        }

        /// <summary>Projects inspector-only behavior without exposing simulation precision.</summary>
        internal static string InspectorVigilance(Pawn animal, HerdMapComponent herds, int knowledge)
        {
            if (animal?.Spawned != true || herds?.IsObserved(animal) != true || knowledge < 1)
                return "Unknown";
            float value = Mathf.Clamp01(herds.VigilanceFor(animal));
            return value >= 0.7f ? "Watchful" : value >= 0.4f ? "Attentive" : "Slow to notice";
        }

        internal static string InspectorPersonality(Pawn animal, HerdMapComponent herds, int knowledge)
        {
            if (animal?.Spawned != true || herds?.IsObserved(animal) != true || knowledge < 2)
                return "Unknown";
            return WildlifeLifeUtility.PersonalityLabel(animal);
        }

        internal static bool CanRevealPackIdentity(ThingDef species, bool observed) =>
            observed && KnowledgeOrder(species) >= 1;

        internal static bool CanRevealPackSocial(ThingDef species, bool observed) =>
            observed && KnowledgeOrder(species) >= 1;

        internal static bool CanRevealPackBehavior(ThingDef species, bool observed) =>
            observed && KnowledgeOrder(species) >= 2;

        internal static bool CanRevealPackTerritory(ThingDef species, bool observed) =>
            observed && KnowledgeOrder(species) >= 2;

        internal static bool CanRevealPackDen(ThingDef species, bool observed) =>
            observed && KnowledgeOrder(species) >= 2;

        internal static bool CanRevealCurrentPrey(ThingDef species, bool preyObserved) =>
            preyObserved && KnowledgeOrder(species) >= 2;

        private static int KnowledgeOrder(ThingDef species) =>
            WildlifeKnowledgeAdapter.StageOrder(WildlifeKnowledgeAdapter.ColonyStage(species));

        private static WildlifeInsightSpeciesDisclosure Project(WildlifeInsightSource input)
        {
            input = input ?? new WildlifeInsightSource();
            bool visible = input.Visible && (input.KnowledgeVisible || input.FieldGuideVisible || input.HasEvidence);
            bool populationKnown = input.HasPopulationKnowledge || input.StageOrder >=
                WildlifeKnowledgeAdapter.StageOrder(WildlifeKnowledgeAdapter.StageStudied);
            bool regionalAuthorized = visible && input.HasRegionalRecord && populationKnown;
            bool approximateAuthorized = regionalAuthorized && input.Stewardship;
            bool measuredAuthorized = approximateAuthorized && input.Telemetry;
            bool ecologyAuthorized = regionalAuthorized && input.AppliedEcology;
            bool managementAuthorized = regionalAuthorized && input.Stewardship;
            bool diseaseAuthorized = ecologyAuthorized && input.DiseaseMonitoring;
            bool currentLocationKnown = visible && input.DirectObservation && input.CurrentFocusCell.IsValid;
            string certainty = Certainty(input.Confidence, input.HasEvidence || input.KnowledgeVisible);
            string provenance = Provenance(input);
            string evidenceSummary = EvidenceSummary(input);
            List<string> actions = new List<string>();
            if (currentLocationKnown) actions.Add(ActionFocus);
            if (input.HasTrailEvidence) actions.Add(ActionFollowTrail);
            if (approximateAuthorized) actions.Add(ActionSurvey);
            if (managementAuthorized) actions.Add(ActionManage);
            if (regionalAuthorized && WildlifeProgression.Unlocked(WildlifeCapability.HuntingExpedition))
                actions.Add(ActionExpedition);

            WildlifeInsightValue local = WildlifeInsightValue.Unknown("No direct observation or authorized survey");
            if (measuredAuthorized)
                local = Value("About " + Mathf.Max(0, input.LocalCount), WildlifeInsightPrecision.Measured,
                    certainty, "Telemetry station / current map census", true, input.LocalCount);
            else if (currentLocationKnown || input.DirectObservation)
                local = Value("Presence recorded", WildlifeInsightPrecision.Qualitative, certainty,
                    "Direct field observation", false, 0f);
            else if (input.HasEvidence)
                local = Value("Signs recorded", WildlifeInsightPrecision.Qualitative, certainty,
                    evidenceSummary, false, 0f);

            WildlifeInsightValue nearby = WildlifeInsightValue.Unknown("No authorized population estimate");
            if (measuredAuthorized)
                nearby = Value("About " + Mathf.Max(0, Mathf.RoundToInt(input.NearbyPopulation)),
                    WildlifeInsightPrecision.Approximate, certainty, "Telemetry and regional estimate", true,
                    input.NearbyPopulation);
            else if (approximateAuthorized)
                nearby = Value(QualitativePopulation(input.NearbyPopulation), WildlifeInsightPrecision.Approximate,
                    certainty, "Stewardship estimate", false, 0f);

            WildlifeInsightValue regional = WildlifeInsightValue.Unknown("No authorized regional estimate");
            if (measuredAuthorized)
                regional = Value(QualitativePopulation(input.RegionalPopulation), WildlifeInsightPrecision.Approximate,
                    certainty, "Telemetry-supported regional estimate", false, 0f);
            else if (approximateAuthorized)
                regional = Value(QualitativePopulation(input.RegionalPopulation), WildlifeInsightPrecision.Approximate,
                    certainty, "Regional wildlife estimate", false, 0f);
            else if (input.HasEvidence && input.HasRegionalRecord)
                regional = Value("Regional presence recorded", WildlifeInsightPrecision.Qualitative, certainty,
                    evidenceSummary, false, 0f);

            WildlifeInsightValue habitat = WildlifeInsightValue.Unknown("Habitat facet or applied ecology unavailable");
            if (input.HasHabitatKnowledge && input.AppliedEcology)
                habitat = Value(QualitativeHabitat(input.HabitatQuality), WildlifeInsightPrecision.Qualitative,
                    certainty, "Knowledge Framework habitat facet and Applied Ecology", false, 0f);
            else if (input.HasHabitatKnowledge && input.HasEvidence)
                habitat = Value("Habitat relationship recorded", WildlifeInsightPrecision.Qualitative,
                    certainty, "Knowledge Framework habitat facet", false, 0f);

            WildlifeInsightValue trend = WildlifeInsightValue.Unknown("No authorized trend estimate");
            if (approximateAuthorized)
            {
                string direction = input.NearbyPopulation > input.PreviousNearbyPopulation + 0.2f ? "Increasing" :
                    input.NearbyPopulation < input.PreviousNearbyPopulation - 0.2f ? "Declining" : "Stable";
                trend = Value(direction, WildlifeInsightPrecision.Qualitative, certainty,
                    "Regional wildlife estimate", false, 0f);
            }

            WildlifeInsightValue pressure = WildlifeInsightValue.Unknown("Applied Ecology unavailable");
            if (ecologyAuthorized)
                pressure = Value(input.Pressure >= 0.7f ? "High" : input.Pressure >= 0.4f ? "Moderate" : "Low",
                    WildlifeInsightPrecision.Qualitative, certainty, "Applied Ecology model", false, 0f);

            WildlifeInsightValue forecast = WildlifeInsightValue.Unknown("Applied Ecology or population knowledge unavailable");
            if (ecologyAuthorized && !input.Forecast.NullOrEmpty())
                forecast = Value(input.Forecast, WildlifeInsightPrecision.Qualitative, certainty,
                    "Applied Ecology model", false, 0f);

            WildlifeInsightValue disease = WildlifeInsightValue.Unknown("Disease monitoring is not authorized");
            if (diseaseAuthorized && !input.DiseaseRisk.NullOrEmpty())
                disease = Value(input.DiseaseRisk, WildlifeInsightPrecision.Qualitative, certainty,
                    "Disease monitoring interpretation", false, 0f);

            WildlifeInsightValue intervention = WildlifeInsightValue.Unknown("Management interpretation unavailable");
            if (ecologyAuthorized && !input.Intervention.NullOrEmpty())
                intervention = Value(input.Intervention, WildlifeInsightPrecision.Qualitative, certainty,
                    "Stewardship and Applied Ecology interpretation", false, 0f);

            return new WildlifeInsightSpeciesDisclosure(input.StableId, input.SubjectId, visible,
                input.KnowledgeVisible, input.FieldGuideVisible, input.DirectObservation, currentLocationKnown,
                currentLocationKnown ? input.CurrentFocusCell : IntVec3.Invalid, local, nearby, regional, habitat,
                pressure, trend, forecast, disease, intervention, certainty, provenance, evidenceSummary, actions);
        }

        /// <summary>
        /// Deterministic guard tests for the policy. These use inert source inputs,
        /// so running them cannot register, observe, or mutate Knowledge Framework state.
        /// </summary>
        public static bool SelfTest(out string failure)
        {
            List<string> failures = new List<string>();
            WildlifeInsightSource researchOnly = new WildlifeInsightSource
            {
                StableId = "species:research-only", Visible = false, Stewardship = true,
                Telemetry = true, AppliedEcology = true, DiseaseMonitoring = true,
                HasRegionalRecord = true, NearbyPopulation = 12f, RegionalPopulation = 40f,
                LocalCount = 4, Pressure = 0.8f, Forecast = "Increasing", DiseaseRisk = "High"
            };
            WildlifeInsightSpeciesDisclosure researchProjection = Project(researchOnly);
            if (researchProjection.Visible || researchProjection.NearbyPopulation.Available ||
                researchProjection.Forecast.Available || researchProjection.DiseaseRisk.Available)
                failures.Add("research without KF knowledge or evidence exposed population/ecology data");

            WildlifeInsightSource knowledgeOnly = new WildlifeInsightSource
            {
                StableId = "species:knowledge-only", SubjectId = "species:knowledge-only", Visible = true,
                KnowledgeVisible = true, StageOrder = 5, HasPopulationKnowledge = true,
                HasHabitatKnowledge = true, HasRegionalRecord = true, NearbyPopulation = 12f,
                RegionalPopulation = 40f, LocalCount = 4, Pressure = 0.8f, Forecast = "Increasing",
                DiseaseRisk = "High", Stewardship = false, AppliedEcology = false, DiseaseMonitoring = false
            };
            WildlifeInsightSpeciesDisclosure knowledgeProjection = Project(knowledgeOnly);
            if (!knowledgeProjection.Visible || knowledgeProjection.NearbyPopulation.Available ||
                knowledgeProjection.Forecast.Available || knowledgeProjection.Intervention.Available)
                failures.Add("KF knowledge without research was incorrectly coupled to unavailable research");

            WildlifeInsightSource stewardshipKnowledge = new WildlifeInsightSource
            {
                StableId = "species:stewardship-knowledge", Visible = true, KnowledgeVisible = true,
                StageOrder = 5, HasPopulationKnowledge = true, HasRegionalRecord = true,
                NearbyPopulation = 8f, RegionalPopulation = 24f, Stewardship = true,
                AppliedEcology = false, DiseaseMonitoring = false, Telemetry = false
            };
            WildlifeInsightSpeciesDisclosure stewardshipProjection = Project(stewardshipKnowledge);
            if (!stewardshipProjection.Allows(ActionManage) || stewardshipProjection.Pressure.Available ||
                stewardshipProjection.Forecast.Available || stewardshipProjection.DiseaseRisk.Available ||
                stewardshipProjection.Intervention.Available)
                failures.Add("Stewardship management capability was coupled to unavailable Applied Ecology data");

            WildlifeInsightSource spawnedWithoutEvidence = new WildlifeInsightSource
            {
                StableId = "species:unseen-spawn", Visible = false, LocalCount = 7,
                HasRegionalRecord = true, NearbyPopulation = 9f, RegionalPopulation = 20f,
                Stewardship = true, Telemetry = true
            };
            WildlifeInsightSpeciesDisclosure unseenProjection = Project(spawnedWithoutEvidence);
            if (unseenProjection.Visible || unseenProjection.LocalPopulation.Available)
                failures.Add("spawned local count without observation was exposed");

            WildlifeInsightSource hiddenPosition = new WildlifeInsightSource
            {
                StableId = "species:hidden-position", Visible = true, KnowledgeVisible = true,
                HasEvidence = true, HasRegionalRecord = true, HasPopulationKnowledge = true,
                DirectObservation = false, CurrentFocusCell = new IntVec3(8, 0, 8),
                Stewardship = true, NearbyPopulation = 4f, RegionalPopulation = 11f
            };
            WildlifeInsightSpeciesDisclosure hiddenPositionProjection = Project(hiddenPosition);
            if (hiddenPositionProjection.CurrentLocationKnown ||
                hiddenPositionProjection.CurrentFocusCell.IsValid ||
                hiddenPositionProjection.Allows(ActionFocus))
                failures.Add("hidden Pawn position became a current focus target without observation");

            WildlifeInsightSource firstPredatorEvidence = new WildlifeInsightSource
            {
                StableId = "species:predator-evidence", Visible = true, HasEvidence = true,
                HasSignals = true, Confidence = 0.25f, HasRegionalRecord = true,
                NearbyPopulation = 25f, RegionalPopulation = 80f
            };
            WildlifeInsightSpeciesDisclosure firstPredatorProjection = Project(firstPredatorEvidence);
            WildlifeInsightSource repeatedPredatorEvidence = new WildlifeInsightSource
            {
                StableId = firstPredatorEvidence.StableId, Visible = true, HasEvidence = true,
                HasSignals = true, Confidence = 0.82f, HasRegionalRecord = true,
                NearbyPopulation = 25f, RegionalPopulation = 80f
            };
            WildlifeInsightSpeciesDisclosure repeatedPredatorProjection = Project(repeatedPredatorEvidence);
            if (!firstPredatorProjection.Visible || firstPredatorProjection.EvidenceSummary != "signals" ||
                firstPredatorProjection.NearbyPopulation.Available ||
                firstPredatorProjection.NearbyPopulation.HasNumeric ||
                repeatedPredatorProjection.Certainty != "Confirmed" ||
                repeatedPredatorProjection.NearbyPopulation.Available)
                failures.Add("predator evidence did not remain ambiguous and abundance-free while certainty progressed");

            WildlifeInsightSource pureInput = new WildlifeInsightSource
            {
                StableId = "species:pure", Visible = true, KnowledgeVisible = true,
                HasEvidence = true, DirectObservation = false, HasRegionalRecord = true,
                HasPopulationKnowledge = true, Stewardship = true, NearbyPopulation = 3f,
                PreviousNearbyPopulation = 2f, RegionalPopulation = 8f, LocalCount = 1
            };
            float before = pureInput.NearbyPopulation;
            WildlifeInsightSpeciesDisclosure pureProjection = Project(pureInput);
            if (Math.Abs(pureInput.NearbyPopulation - before) > 0.001f || pureProjection == null)
                failures.Add("presentation projection mutated its source input");

            failure = string.Join("; ", failures);
            return failures.Count == 0;
        }

        private static WildlifeInsightValue Value(string text, WildlifeInsightPrecision precision,
            string certainty, string provenance, bool hasNumeric, float numeric) =>
            new WildlifeInsightValue(true, precision, text, certainty, provenance, hasNumeric, numeric);

        private static string Certainty(float confidence, bool evidence)
        {
            if (!evidence) return "Unknown";
            if (confidence >= 0.75f) return "Confirmed";
            if (confidence >= 0.45f) return "Probable";
            return "Uncertain";
        }

        private static string Provenance(WildlifeInsightSource input)
        {
            List<string> values = new List<string>();
            if (input.DirectObservation) values.Add("direct field observation");
            if (input.HasSignals) values.Add("signal evidence");
            if (input.HasTrailEvidence) values.Add("trail evidence");
            if (input.HasMigrationEvidence) values.Add("migration record");
            if (input.KnowledgeVisible) values.Add("Knowledge Framework");
            return values.Count == 0 ? "No authorized evidence" : string.Join(", ", values);
        }

        private static string EvidenceSummary(WildlifeInsightSource input)
        {
            List<string> values = new List<string>();
            if (input.DirectObservation) values.Add("direct observation");
            if (input.HasSignals) values.Add("signals");
            if (input.HasTrailEvidence) values.Add("trails");
            if (input.HasMigrationEvidence) values.Add("migration records");
            return values.Count == 0 ? "Knowledge Framework record" : string.Join(", ", values);
        }

        private static string QualitativePopulation(float value) => value < 1f ? "Sparse" :
            value < 4f ? "Some" : value < 12f ? "Many" : "Abundant";

        private static string QualitativeHabitat(float value) => value < 0.35f ? "Poor" :
            value < 0.7f ? "Adequate" : "Healthy";

        private sealed class WildlifeInsightSource
        {
            internal string StableId;
            internal string SubjectId;
            internal int StageOrder;
            internal bool Visible;
            internal bool KnowledgeVisible;
            internal bool FieldGuideVisible;
            internal bool DirectObservation;
            internal bool HasEvidence;
            internal bool HasRegionalRecord;
            internal bool HasPopulationKnowledge;
            internal bool HasHabitatKnowledge;
            internal bool Stewardship;
            internal bool Telemetry;
            internal bool AppliedEcology;
            internal bool DiseaseMonitoring;
            internal bool HasTrailEvidence;
            internal bool HasMigrationEvidence;
            internal bool HasSignals;
            internal IntVec3 CurrentFocusCell = IntVec3.Invalid;
            internal int LocalCount;
            internal float NearbyPopulation;
            internal float RegionalPopulation;
            internal float PreviousNearbyPopulation;
            internal float HabitatQuality;
            internal float Pressure;
            internal float Confidence;
            internal string Forecast;
            internal string DiseaseRisk;
            internal string Intervention;
        }
    }
}
