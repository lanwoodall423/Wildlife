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
    public enum WildlifeSignalKind
    {
        Alarm,
        HumanDanger,
        AllClear,
        Contact,
        Food,
        Water,
        Coordination
    }

    public sealed class WildlifeDialectRecord : IExposable
    {
        public ThingDef species;
        public float credibility = 0.72f;
        public float humanTrust = 0.35f;
        public float tradition = 0.25f;
        public int trueSignals;
        public int falseSignals;
        public int lastSignalTick;
        public WildlifeSignalKind lastKind;
        public IntVec3 lastCell;

        public void ExposeData()
        {
            Scribe_Defs.Look(ref species, "species");
            Scribe_Values.Look(ref credibility, "credibility", 0.72f);
            Scribe_Values.Look(ref humanTrust, "humanTrust", 0.35f);
            Scribe_Values.Look(ref tradition, "tradition", 0.25f);
            Scribe_Values.Look(ref trueSignals, "trueSignals", 0);
            Scribe_Values.Look(ref falseSignals, "falseSignals", 0);
            Scribe_Values.Look(ref lastSignalTick, "lastSignalTick", 0);
            Scribe_Values.Look(ref lastKind, "lastKind", WildlifeSignalKind.Contact);
            Scribe_Values.Look(ref lastCell, "lastCell");
        }
    }

    public sealed class WildlifeSignalKnowledgeRecord : IExposable
    {
        public Pawn colonist;
        public ThingDef species;
        public float understanding;
        public int signalsHeard;
        public int lastHeardTick;
        public int lastNotifiedStage;

        public void ExposeData()
        {
            Scribe_References.Look(ref colonist, "colonist");
            Scribe_Defs.Look(ref species, "species");
            Scribe_Values.Look(ref understanding, "understanding", 0f);
            Scribe_Values.Look(ref signalsHeard, "signalsHeard", 0);
            Scribe_Values.Look(ref lastHeardTick, "lastHeardTick", 0);
            Scribe_Values.Look(ref lastNotifiedStage, "lastNotifiedStage", 0);
        }
    }

    public sealed class PredatorSignalKnowledgeRecord : IExposable
    {
        public ThingDef predatorSpecies;
        public ThingDef preySpecies;
        public float understanding;
        public int signalsHeard;

        public void ExposeData()
        {
            Scribe_Defs.Look(ref predatorSpecies, "predatorSpecies");
            Scribe_Defs.Look(ref preySpecies, "preySpecies");
            Scribe_Values.Look(ref understanding, "understanding", 0f);
            Scribe_Values.Look(ref signalsHeard, "signalsHeard", 0);
        }
    }

    public sealed class WildlifeActiveSignal : IExposable
    {
        public int traceId;
        public ThingDef species;
        public WildlifeSignalKind kind;
        public IntVec3 cell;
        public int startedTick;
        public int expiresTick;
        public float radius;
        public bool truthful;
        public Pawn speaker;
        public Thing subject;
        public IntVec3 subjectCell;
        public bool hasSubject;
        public bool humanImitation;
        public int listenerCount;
        public int observerCount;
        public int reactionCount;
        public bool verified;
        public bool behaviorConsistent;
        public string cause;
        public string expectedBehavior;
        public string observedBehavior;

        public void ExposeData()
        {
            Scribe_Values.Look(ref traceId, "traceId", 0);
            Scribe_Defs.Look(ref species, "species");
            Scribe_Values.Look(ref kind, "kind", WildlifeSignalKind.Contact);
            Scribe_Values.Look(ref cell, "cell");
            Scribe_Values.Look(ref startedTick, "startedTick", 0);
            Scribe_Values.Look(ref expiresTick, "expiresTick", 0);
            Scribe_Values.Look(ref radius, "radius", 30f);
            Scribe_Values.Look(ref truthful, "truthful", true);
            Scribe_References.Look(ref speaker, "speaker");
            Scribe_References.Look(ref subject, "subject");
            Scribe_Values.Look(ref subjectCell, "subjectCell");
            Scribe_Values.Look(ref hasSubject, "hasSubject", false);
            Scribe_Values.Look(ref humanImitation, "humanImitation", false);
            Scribe_Values.Look(ref listenerCount, "listenerCount", 0);
            Scribe_Values.Look(ref observerCount, "observerCount", 0);
            Scribe_Values.Look(ref reactionCount, "reactionCount", 0);
            Scribe_Values.Look(ref verified, "verified", false);
            Scribe_Values.Look(ref behaviorConsistent, "behaviorConsistent", false);
            Scribe_Values.Look(ref cause, "cause");
            Scribe_Values.Look(ref expectedBehavior, "expectedBehavior");
            Scribe_Values.Look(ref observedBehavior, "observedBehavior");
        }
    }

    public sealed class WildlifeSignalTrace : IExposable
    {
        public int traceId;
        public ThingDef species;
        public WildlifeSignalKind kind;
        public IntVec3 cell;
        public int tick;
        public float radius;
        public bool truthful;
        public bool humanImitation;
        public int listenerCount;
        public int observerCount;
        public int reactionCount;
        public bool verified;
        public bool behaviorConsistent;
        public IntVec3 subjectCell;
        public bool hasSubject;
        public string speakerLabel;
        public string subjectLabel;
        public string cause;
        public string expectedBehavior;
        public string observedBehavior;

        public void ExposeData()
        {
            Scribe_Values.Look(ref traceId, "traceId", 0);
            Scribe_Defs.Look(ref species, "species");
            Scribe_Values.Look(ref kind, "kind", WildlifeSignalKind.Contact);
            Scribe_Values.Look(ref cell, "cell");
            Scribe_Values.Look(ref tick, "tick", 0);
            Scribe_Values.Look(ref radius, "radius", 30f);
            Scribe_Values.Look(ref truthful, "truthful", true);
            Scribe_Values.Look(ref humanImitation, "humanImitation", false);
            Scribe_Values.Look(ref listenerCount, "listenerCount", 0);
            Scribe_Values.Look(ref observerCount, "observerCount", 0);
            Scribe_Values.Look(ref reactionCount, "reactionCount", 0);
            Scribe_Values.Look(ref verified, "verified", false);
            Scribe_Values.Look(ref behaviorConsistent, "behaviorConsistent", false);
            Scribe_Values.Look(ref subjectCell, "subjectCell");
            Scribe_Values.Look(ref hasSubject, "hasSubject", false);
            Scribe_Values.Look(ref speakerLabel, "speakerLabel");
            Scribe_Values.Look(ref subjectLabel, "subjectLabel");
            Scribe_Values.Look(ref cause, "cause");
            Scribe_Values.Look(ref expectedBehavior, "expectedBehavior");
            Scribe_Values.Look(ref observedBehavior, "observedBehavior");
        }
    }

    public sealed class WildlifeSignalCultureMapComponent : MapComponent
    {
        private List<WildlifeDialectRecord> dialects = new List<WildlifeDialectRecord>();
        private List<WildlifeSignalKnowledgeRecord> colonistKnowledge = new List<WildlifeSignalKnowledgeRecord>();
        private List<PredatorSignalKnowledgeRecord> predatorKnowledge = new List<PredatorSignalKnowledgeRecord>();
        private List<WildlifeActiveSignal> activeSignals = new List<WildlifeActiveSignal>();
        private List<WildlifeSignalTrace> signalHistory = new List<WildlifeSignalTrace>();
        private readonly Dictionary<int, int> lastPawnLogTicks =
            new Dictionary<int, int>();
        private int nextResourceSignalTick;
        private int nextTraceId = 1;

        private static readonly string[] DialectDescriptors =
        {
            "Broken Trill", "Low Pulse", "Rising Chatter", "Soft Whistle",
            "Three-Beat Cry", "Dry Rattle", "Falling Chorus", "Sharp Bark",
            "Long Call", "Quick Chime", "Hushed Cadence", "Echoing Note"
        };

        public WildlifeSignalCultureMapComponent(Map map) : base(map) { }

        public IReadOnlyList<WildlifeDialectRecord> Dialects => dialects;
        public IReadOnlyList<WildlifeActiveSignal> ActiveSignals => activeSignals;
        public IReadOnlyList<WildlifeSignalTrace> RecentSignals => signalHistory;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref dialects, "wildlifeDialects", LookMode.Deep);
            Scribe_Collections.Look(ref colonistKnowledge, "wildlifeSignalKnowledge", LookMode.Deep);
            Scribe_Collections.Look(ref predatorKnowledge, "predatorSignalKnowledge", LookMode.Deep);
            Scribe_Collections.Look(ref activeSignals, "activeWildlifeSignals", LookMode.Deep);
            Scribe_Collections.Look(ref signalHistory, "wildlifeSignalHistory", LookMode.Deep);
            Scribe_Values.Look(ref nextResourceSignalTick, "nextResourceSignalTick", 0);
            Scribe_Values.Look(ref nextTraceId, "nextSignalTraceId", 1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                dialects = dialects?.Where(record => record?.species?.race?.Animal == true).ToList() ??
                    new List<WildlifeDialectRecord>();
                colonistKnowledge = colonistKnowledge?.Where(record =>
                    record?.colonist != null && !record.colonist.Dead && record.species?.race?.Animal == true).ToList() ??
                    new List<WildlifeSignalKnowledgeRecord>();
                predatorKnowledge = predatorKnowledge?.Where(record =>
                    record?.predatorSpecies?.race?.Animal == true && record.preySpecies?.race?.Animal == true).ToList() ??
                    new List<PredatorSignalKnowledgeRecord>();
                activeSignals = activeSignals?.Where(signal => signal?.species?.race?.Animal == true).ToList() ??
                    new List<WildlifeActiveSignal>();
                signalHistory = signalHistory?.Where(signal => signal?.species?.race?.Animal == true)
                    .OrderByDescending(signal => signal.tick).Take(60).OrderBy(signal => signal.tick).ToList() ??
                    new List<WildlifeSignalTrace>();
                nextTraceId = Mathf.Max(nextTraceId,
                    signalHistory.Count == 0 ? 1 : signalHistory.Max(signal => signal.traceId) + 1);
            }
        }

        public override void MapComponentTick()
        {
            if (HerdsMod.Settings?.enableWildlifeSignalCulture != true) return;
            int now = Find.TickManager.TicksGame;
            if (activeSignals.Count > 0 && now % 30 == 0) VerifyActiveSignals(now);
            if (now % 60 == 0) activeSignals.RemoveAll(signal => signal == null || signal.expiresTick <= now);
            if (now >= nextResourceSignalTick)
            {
                nextResourceSignalTick = now + 2500;
                TryResourceSignal(now);
            }
        }

        public override void MapComponentDraw()
        {
            base.MapComponentDraw();
            if (HerdsMod.Settings?.enableWildlifeSignalCulture != true || Find.CurrentMap != map ||
                activeSignals.Count == 0) return;
            int now = Find.TickManager.TicksGame;
            CellRect view = Find.CameraDriver.CurrentViewRect;
            for (int i = 0; i < activeSignals.Count; i++)
            {
                WildlifeActiveSignal signal = activeSignals[i];
                if (signal.expiresTick <= now || !signal.cell.InBounds(map) || !view.Contains(signal.cell)) continue;
                float life = Mathf.InverseLerp(signal.startedTick, signal.expiresTick, now);
                float wave = Mathf.Lerp(1.2f, signal.radius, life);
                Color color = SignalColor(signal.kind);
                color.a = Mathf.Lerp(0.8f, 0.08f, life);
                GenDraw.DrawRadiusRing(signal.cell, wave, color);
                DrawSignalGrammar(signal, life, color);
                if (Prefs.DevMode && WildlifeDevMaster.CompleteOverlayEnabled)
                    GenDraw.DrawRadiusRing(signal.cell, signal.radius, new Color(color.r, color.g, color.b, 0.22f));
            }
        }

        private void DrawSignalGrammar(WildlifeActiveSignal signal, float life, Color color)
        {
            float pulse = Mathf.Lerp(2f, signal.radius * 0.72f, life);
            switch (signal.kind)
            {
                case WildlifeSignalKind.Alarm:
                case WildlifeSignalKind.HumanDanger:
                    GenDraw.DrawRadiusRing(signal.cell, Mathf.Max(1f, pulse * 0.58f),
                        new Color(color.r, color.g, color.b, color.a * 0.75f));
                    if (TrySignalSubjectCell(signal, out IntVec3 alarmTarget))
                    {
                        GenDraw.DrawLineBetween(signal.cell.ToVector3Shifted(),
                            alarmTarget.ToVector3Shifted(), SimpleColor.Red);
                        GenDraw.DrawRadiusRing(alarmTarget, 2.2f, new Color(1f, 0.15f, 0.1f, 0.7f));
                    }
                    break;
                case WildlifeSignalKind.AllClear:
                    GenDraw.DrawRadiusRing(signal.cell, Mathf.Max(1f, pulse * 0.66f),
                        new Color(color.r, color.g, color.b, color.a * 0.55f));
                    GenDraw.DrawRadiusRing(signal.cell, Mathf.Max(1f, pulse * 0.33f),
                        new Color(color.r, color.g, color.b, color.a * 0.35f));
                    break;
                case WildlifeSignalKind.Food:
                case WildlifeSignalKind.Water:
                    if (TrySignalSubjectCell(signal, out IntVec3 resourceTarget))
                    {
                        GenDraw.DrawLineBetween(signal.cell.ToVector3Shifted(),
                            resourceTarget.ToVector3Shifted(),
                            signal.kind == WildlifeSignalKind.Water ? SimpleColor.Cyan : SimpleColor.Yellow);
                        GenDraw.DrawRadiusRing(resourceTarget, Mathf.Lerp(1f, 4f, life),
                            new Color(color.r, color.g, color.b, color.a));
                    }
                    break;
                case WildlifeSignalKind.Contact:
                    GenDraw.DrawRadiusRing(signal.cell + IntVec3.East * 2,
                        Mathf.Max(1f, pulse * 0.42f), new Color(color.r, color.g, color.b, color.a * 0.65f));
                    break;
                case WildlifeSignalKind.Coordination:
                    if (TrySignalSubjectCell(signal, out IntVec3 huntTarget))
                        GenDraw.DrawLineBetween(signal.cell.ToVector3Shifted(),
                            huntTarget.ToVector3Shifted(), SimpleColor.Yellow);
                    GenDraw.DrawRadiusRing(signal.cell, Mathf.Max(1f, pulse * 0.45f),
                        new Color(color.r, color.g, color.b, color.a * 0.6f));
                    break;
            }
        }

        private bool TrySignalSubjectCell(WildlifeActiveSignal signal, out IntVec3 cell)
        {
            if (signal.subject?.Spawned == true)
            {
                cell = signal.subject.Position;
                return true;
            }
            cell = signal.subjectCell;
            return signal.hasSubject && cell.InBounds(map);
        }

        public WildlifeDialectRecord DialectFor(ThingDef species, bool create = true)
        {
            if (species?.race?.Animal != true) return null;
            WildlifeDialectRecord record = dialects.FirstOrDefault(value => value.species == species);
            if (record == null && create)
            {
                record = new WildlifeDialectRecord { species = species };
                dialects.Add(record);
            }
            return record;
        }

        public string DialectName(ThingDef species)
        {
            if (species == null) return "Unknown";
            int index = PositiveMod(species.shortHash * 37 + map.uniqueID * 11, DialectDescriptors.Length);
            return DialectDescriptors[index];
        }

        public float Understanding(Pawn colonist, ThingDef species)
        {
            if (colonist == null || species == null) return 0f;
            WildlifeSignalKnowledgeRecord record = colonistKnowledge.FirstOrDefault(value =>
                value.colonist == colonist && value.species == species);
            float learned = record?.understanding ?? 0f;
            int animalLevel = map.GetComponent<HuntingKnowledgeMapComponent>()?.Level(colonist, species) ?? 0;
            return Mathf.Clamp01(Mathf.Max(learned, animalLevel * 0.11f));
        }

        public float ColonyUnderstanding(ThingDef species)
        {
            Pawn contributor = ColonyContributor(species);
            return contributor == null ? 0f : Understanding(contributor, species);
        }

        public Pawn ColonyContributor(ThingDef species)
        {
            IReadOnlyList<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
            Pawn bestPawn = null;
            float best = 0f;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn colonist = colonists[i];
                float value = Understanding(colonist, species);
                if (value <= best) continue;
                best = value;
                bestPawn = colonist;
            }
            return bestPawn;
        }

        public string UnderstandingLabel(float value)
        {
            return value < 0.15f ? "Unfamiliar" : value < 0.4f ? "Recognized" :
                value < 0.7f ? "Interpreted" : value < 0.92f ? "Fluent" : "Mastered";
        }

        public string SignalSummary(ThingDef species)
        {
            WildlifeDialectRecord dialect = DialectFor(species);
            if (dialect == null) return "No local signals recorded.";
            float known = ColonyUnderstanding(species);
            WildlifeSignalTrace recent = signalHistory.Where(trace => trace.species == species)
                .OrderByDescending(trace => trace.tick).FirstOrDefault();
            string current = Find.TickManager.TicksGame - dialect.lastSignalTick <= 2500
                ? PlayerFacingSignal(dialect.lastKind, known, recent?.truthful ?? true,
                    recent?.verified ?? false) : "Quiet";
            return (known >= 0.15f ? DialectName(species) : "Unfamiliar dialect") +
                " • " + UnderstandingLabel(known) + " " + known.ToStringPercent() +
                " • " + current;
        }

        public string SignalTooltip(ThingDef species, Pawn observer = null)
        {
            WildlifeDialectRecord dialect = DialectFor(species);
            if (dialect == null) return "No local signal culture is known.";
            Pawn contributor = observer ?? ColonyContributor(species);
            float understanding = observer == null ? ColonyUnderstanding(species) : Understanding(observer, species);
            string text = (observer == null ? "Colony" : observer.LabelShortCap + "'s") +
                " " + species.LabelCap + " signal understanding: " +
                UnderstandingLabel(understanding) + " " + understanding.ToStringPercent() + ".";
            if (observer == null)
                text += contributor == null
                    ? "\nCurrent contributor: none."
                    : "\nCurrent contributor: " + contributor.LabelShortCap + ".";
            if (understanding < 0.15f)
                text += observer == null
                    ? "\n\nThe colony cannot yet distinguish this species' calls."
                    : "\n\nThis colonist cannot yet distinguish this species' calls.";
            else
            {
                text += "\n\nLocal dialect: " + DialectName(species) + ".";
                text += understanding < 0.4f
                    ? "\nBroad call families can be recognized."
                    : "\nThe exact intent of calls can be interpreted.";
                if (understanding >= 0.7f)
                    text += "\nSignal credibility: " + dialect.credibility.ToStringPercent() +
                        "\nHuman imitation trust: " + dialect.humanTrust.ToStringPercent() +
                        "\nTradition strength: " + dialect.tradition.ToStringPercent();
                if (understanding >= 0.92f)
                    text += "\nMistaken and deceptive calls can be identified.";
            }
            text += "\n\nUnderstanding improves when colonists hear calls, especially while manning an observation post.";
            if (observer == null)
                text += "\n\nThe colony uses its best currently present contributor for each species. " +
                    "This level can fall if that colonist leaves or dies.";
            return text;
        }

        public string PredatorSummary(ThingDef predatorSpecies)
        {
            PredatorSignalKnowledgeRecord best = predatorKnowledge.Where(record =>
                record.predatorSpecies == predatorSpecies).OrderByDescending(record => record.understanding).FirstOrDefault();
            return best == null ? "No prey calls interpreted" :
                "Reads " + best.preySpecies.LabelCap + " • " + best.understanding.ToStringPercent();
        }

        public string PredatorTooltip(ThingDef predatorSpecies)
        {
            List<PredatorSignalKnowledgeRecord> known = predatorKnowledge.Where(record =>
                record.predatorSpecies == predatorSpecies && record.understanding > 0.01f)
                .OrderByDescending(record => record.understanding).Take(5).ToList();
            if (known.Count == 0)
                return "This predator has not yet learned to exploit local prey alarm calls.";
            return "Predators learn which local calls mean alarm, confusion, or movement. This helps them anticipate " +
                "prey reactions.\n\n" + string.Join("\n", known.Select(record =>
                    record.preySpecies.LabelCap + ": " + record.understanding.ToStringPercent()));
        }

        public float PlayerImitationFactor(Pawn caller, ThingDef species)
        {
            if (HerdsMod.Settings?.enablePlayerSignalImitation != true) return 1f;
            WildlifeDialectRecord dialect = DialectFor(species);
            float understanding = Understanding(caller, species);
            return Mathf.Lerp(0.55f, 1.12f, understanding) * Mathf.Lerp(0.72f, 1.08f, dialect.humanTrust);
        }

        public float AlarmResponseFactor(ThingDef preySpecies, Thing threat)
        {
            if (HerdsMod.Settings?.enablePredatorSignalLearning != true ||
                threat is not Pawn predator ||
                !WildlifeSpeciesClassification.IsPredator(predator.def)) return 1f;
            PredatorSignalKnowledgeRecord record = predatorKnowledge.FirstOrDefault(value =>
                value.predatorSpecies == predator.def && value.preySpecies == preySpecies);
            return 1f - (record?.understanding ?? 0f) * 0.22f;
        }

        public void NotifyAnimalSignal(ThingDef species, WildlifeSignalKind kind, Pawn speaker,
            Thing subject, bool truthful, float radius = 35f)
        {
            if (HerdsMod.Settings?.enableWildlifeSignalCulture != true || species?.race?.Animal != true) return;
            IntVec3 cell = speaker?.Spawned == true ? speaker.Position :
                subject?.Spawned == true ? subject.Position : map.Center;
            Broadcast(species, kind, cell, speaker, subject, truthful, radius, false);
        }

        public void NotifyPredatorCoordination(Pawn predator, Pawn prey)
        {
            if (predator?.Spawned != true || predator.Map != map) return;
            NotifyAnimalSignal(predator.def, WildlifeSignalKind.Coordination, predator, prey, true, 22f);
            if (HerdsMod.Settings?.enablePredatorSignalLearning != true || prey?.Spawned != true) return;
            PredatorSignalKnowledgeRecord learned = predatorKnowledge.FirstOrDefault(record =>
                record.predatorSpecies == predator.def && record.preySpecies == prey.def);
            if (learned?.understanding >= 0.55f &&
                Rand.Chance(0.08f + learned.understanding * 0.14f))
                Broadcast(prey.def, WildlifeSignalKind.AllClear, predator.Position, predator,
                    prey, false, 22f, false);
        }

        public void NotifyPlayerImitation(ThingDef species, WildlifeSignalKind kind, Pawn caller,
            IntVec3 cell, bool successful, bool truthful)
        {
            if (HerdsMod.Settings?.enableWildlifeSignalCulture != true ||
                HerdsMod.Settings.enablePlayerSignalImitation != true || species == null) return;
            WildlifeDialectRecord dialect = DialectFor(species);
            float trustDelta = successful ? (truthful ? 0.018f : -0.035f) : -0.025f;
            dialect.humanTrust = Mathf.Clamp01(dialect.humanTrust + trustDelta);
            if (successful)
            {
                WildlifeSignalKnowledgeRecord knowledge = KnowledgeFor(caller, species);
                knowledge.understanding = Mathf.Clamp01(knowledge.understanding + 0.025f);
            }
            Broadcast(species, kind, cell, caller, null, truthful, 35f, true);
        }

        public bool TryPlayerSignal(ThingDef species, Building_WildlifeTool source, Pawn caller,
            WildlifeSignalKind kind)
        {
            if (HerdsMod.Settings?.enableWildlifeSignalCulture != true ||
                HerdsMod.Settings.enablePlayerSignalImitation != true || source?.Spawned != true ||
                caller?.Spawned != true || source.ManningColonist() != caller) return false;
            List<Pawn> animals = map.mapPawns.AllPawnsSpawned.Where(pawn =>
                pawn?.Spawned == true && !pawn.Dead && pawn.Faction != Faction.OfPlayer &&
                pawn.def == species && PreyProfileDatabase.IsEligible(pawn.def)).ToList();
            if (animals.Count == 0) return false;
            WildlifeFieldcraftMapComponent fieldcraft = map.GetComponent<WildlifeFieldcraftMapComponent>();
            int level = fieldcraft?.AnimalCallKnowledge(caller, species) ?? 0;
            float chance = Mathf.Clamp01((fieldcraft?.AnimalCallChance(level, caller) ?? 0.1f) *
                PlayerImitationFactor(caller, species));
            bool performed = Rand.Chance(chance);
            Thing realThreat = FindThreatNear(animals, 35f);
            bool truthful = kind == WildlifeSignalKind.Alarm || kind == WildlifeSignalKind.HumanDanger
                ? realThreat != null : kind != WildlifeSignalKind.AllClear || realThreat == null;
            if (!performed)
            {
                NotifyPlayerImitation(species, kind, caller, source.Position, false, truthful);
                Messages.Message(caller.LabelShortCap + " could not reproduce the " + DialectName(species) +
                    " call convincingly.", source, MessageTypeDefOf.NegativeEvent, false);
                return false;
            }

            HerdMapComponent herds = map.GetComponent<HerdMapComponent>();
            if (kind == WildlifeSignalKind.AllClear)
            {
                if (realThreat == null)
                    for (int i = 0; i < animals.Count; i++) herds?.NotifyThreatEnded(animals[i], null);
            }
            else
            {
                Thing perceivedThreat = realThreat ?? source;
                for (int i = 0; i < animals.Count; i++)
                    if (animals[i].Position.InHorDistOf(source.Position, 70f))
                        herds?.NotifyThreat(animals[i], perceivedThreat, 750);
            }
            NotifyPlayerImitation(species, kind, caller, source.Position, true, truthful);
            string effect = truthful ? "The call was understood." :
                "The animals responded, but learned that the call was misleading.";
            Messages.Message(caller.LabelShortCap + " gave a " + SignalLabel(kind).ToLowerInvariant() +
                " in the " + DialectName(species) + " dialect. " + effect, source,
                truthful ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.CautionInput, false);
            return true;
        }

        public List<string> BridgeLines()
        {
            List<string> lines = new List<string>
            {
                "signals=dialects:" + dialects.Count + " active:" +
                activeSignals.Count(signal => signal.expiresTick > Find.TickManager.TicksGame) +
                " traces:" + signalHistory.Count + " colonistLinks:" + colonistKnowledge.Count +
                " predatorLinks:" + predatorKnowledge.Count
            };
            lines.AddRange(dialects.OrderByDescending(record => record.lastSignalTick).Take(12).Select(record =>
                "dialect=" + record.species.defName + " name:" + DialectName(record.species).Replace(' ', '_') +
                " credibility:" + record.credibility.ToString("0.00") +
                " humanTrust:" + record.humanTrust.ToString("0.00") +
                " understood:" + ColonyUnderstanding(record.species).ToString("0.00") +
                " last:" + record.lastKind + " true:" + record.trueSignals + " false:" + record.falseSignals));
            return lines;
        }

        public List<string> TraceLines(string speciesFilter = null)
        {
            IEnumerable<WildlifeSignalTrace> traces = signalHistory;
            if (!speciesFilter.NullOrEmpty())
                traces = traces.Where(trace =>
                    trace.species?.defName.Equals(speciesFilter, StringComparison.OrdinalIgnoreCase) == true ||
                    trace.species?.label.Equals(speciesFilter, StringComparison.OrdinalIgnoreCase) == true);
            List<string> lines = new List<string>
            {
                "signalTrace=count:" + traces.Count() + " active:" + activeSignals.Count
            };
            lines.AddRange(traces.OrderByDescending(trace => trace.tick).Take(6).Select(trace =>
                "trace=id:" + trace.traceId + " species:" + trace.species.defName + " kind:" + trace.kind +
                " true:" + trace.truthful + " verified:" + trace.verified +
                " consistent:" + trace.behaviorConsistent + " listeners:" + trace.listenerCount +
                " reactions:" + trace.reactionCount + " observers:" + trace.observerCount +
                " cause:" + BridgeClean(trace.cause) + " observed:" + BridgeClean(trace.observedBehavior)));
            return lines;
        }

        private void Broadcast(ThingDef species, WildlifeSignalKind kind, IntVec3 cell, Pawn speaker,
            Thing subject, bool truthful, float radius, bool humanImitation)
        {
            WildlifeDialectRecord dialect = DialectFor(species);
            int now = Find.TickManager.TicksGame;
            dialect.lastSignalTick = now;
            dialect.lastKind = kind;
            dialect.lastCell = cell;
            if (truthful)
            {
                dialect.trueSignals++;
                dialect.credibility = Mathf.Clamp01(dialect.credibility + 0.006f);
            }
            else
            {
                dialect.falseSignals++;
                dialect.credibility = Mathf.Clamp01(dialect.credibility - 0.035f);
            }
            dialect.tradition = Mathf.Clamp01(dialect.tradition + 0.004f);
            HerdSnapshot speakerGroup = speaker?.Spawned == true
                ? map.GetComponent<HerdMapComponent>()?.HerdFor(speaker) : null;
            if (speakerGroup?.youngCount > 0)
                dialect.tradition = Mathf.Clamp01(dialect.tradition + 0.004f);
            activeSignals.RemoveAll(signal => signal.species == species && signal.kind == kind);
            float effectiveRadius = Mathf.Clamp(radius * Mathf.Lerp(0.72f, 1.12f, dialect.credibility), 12f, 70f);
            int traceId = nextTraceId++;
            WildlifeActiveSignal active = new WildlifeActiveSignal
            {
                traceId = traceId,
                species = species,
                kind = kind,
                cell = cell,
                startedTick = now,
                expiresTick = now + 210,
                radius = effectiveRadius,
                truthful = truthful,
                speaker = speaker,
                subject = subject,
                subjectCell = subject?.Spawned == true ? subject.Position : IntVec3.Invalid,
                hasSubject = subject != null,
                humanImitation = humanImitation,
                listenerCount = CountListeners(species, cell, effectiveRadius, speaker),
                cause = SignalCause(kind, speaker, subject, truthful, humanImitation),
                expectedBehavior = ExpectedBehavior(kind)
            };
            active.observerCount = TeachColonists(species, cell, radius, humanImitation);
            activeSignals.Add(active);
            signalHistory.Add(new WildlifeSignalTrace
            {
                traceId = traceId,
                species = species,
                kind = kind,
                cell = cell,
                tick = now,
                radius = effectiveRadius,
                truthful = truthful,
                humanImitation = humanImitation,
                listenerCount = active.listenerCount,
                observerCount = active.observerCount,
                subjectCell = active.subjectCell,
                hasSubject = active.hasSubject,
                speakerLabel = speaker?.LabelShortCap,
                subjectLabel = subject?.LabelShortCap,
                cause = active.cause,
                expectedBehavior = active.expectedBehavior,
                observedBehavior = "Awaiting response"
            });
            if (signalHistory.Count > 60)
                signalHistory.RemoveRange(0, signalHistory.Count - 60);
            RecordCallInPawnLog(speaker, kind, now);
            ApplyImmediateResponse(active);
            TeachPredator(species, speaker, subject, truthful);
            float known = ColonyUnderstanding(species);
            string identifiedMeaning = IdentifiedSignalMeaning(kind, known);
            if (HerdsMod.Settings?.showIdentifiedSignalText == true && !identifiedMeaning.NullOrEmpty())
                MoteMaker.ThrowText(cell.ToVector3Shifted(), map,
                    species.LabelCap + ": " + identifiedMeaning);
            if (WildlifeTestLog.Enabled)
                WildlifeTestLog.Write("WildlifeSignal", "species=" + species.defName + " dialect=" +
                    DialectName(species) + " kind=" + kind + " truthful=" + truthful +
                    " credibility=" + dialect.credibility.ToString("0.00") +
                    " human=" + humanImitation, speaker, subject);
        }

        private void RecordCallInPawnLog(Pawn speaker, WildlifeSignalKind kind, int now)
        {
            if (speaker?.RaceProps?.Animal != true || speaker.DestroyedOrNull() ||
                Find.BattleLog == null) return;
            int key = Gen.HashCombineInt(speaker.thingIDNumber, (int)kind);
            if (lastPawnLogTicks.TryGetValue(key, out int lastTick) &&
                now - lastTick < 600) return;
            RulePackDef rules = SignalLogRules(kind);
            if (rules == null) return;
            lastPawnLogTicks[key] = now;
            Find.BattleLog.Add(new BattleLogEntry_Event(speaker, rules, null));
            if (lastPawnLogTicks.Count > 256)
                foreach (int stale in lastPawnLogTicks.Where(pair =>
                    now - pair.Value > 60000).Select(pair => pair.Key).ToList())
                    lastPawnLogTicks.Remove(stale);
        }

        private static RulePackDef SignalLogRules(WildlifeSignalKind kind) =>
            kind == WildlifeSignalKind.Alarm ? HerdsDefOf.Herds_LogSignalAlarm :
            kind == WildlifeSignalKind.HumanDanger ? HerdsDefOf.Herds_LogSignalHumanDanger :
            kind == WildlifeSignalKind.AllClear ? HerdsDefOf.Herds_LogSignalAllClear :
            kind == WildlifeSignalKind.Contact ? HerdsDefOf.Herds_LogSignalContact :
            kind == WildlifeSignalKind.Food ? HerdsDefOf.Herds_LogSignalFood :
            kind == WildlifeSignalKind.Water ? HerdsDefOf.Herds_LogSignalWater :
            HerdsDefOf.Herds_LogSignalCoordination;

        private int TeachColonists(ThingDef species, IntVec3 cell, float radius, bool humanImitation)
        {
            IReadOnlyList<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
            int now = Find.TickManager.TicksGame;
            int observers = 0;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn colonist = colonists[i];
                Building_WildlifeTool post = MannedPostFor(colonist);
                bool throughPost = post != null && post.Position.InHorDistOf(cell, post.InfluenceRadius + radius);
                if (!throughPost && !colonist.Position.InHorDistOf(cell, radius)) continue;
                observers++;
                WildlifeSignalKnowledgeRecord record = KnowledgeFor(colonist, species);
                int beforeStage = KnowledgeStage(record.understanding);
                float animals = colonist.skills?.GetSkill(SkillDefOf.Animals)?.Level ?? 0;
                float gain = throughPost ? 0.045f : 0.012f;
                gain *= 0.65f + animals * 0.035f;
                if (WildlifeRoleUtility.IsMasterConservationist(colonist)) gain *= 1.35f;
                if (humanImitation) gain *= 0.65f;
                record.understanding = Mathf.Clamp01(record.understanding + gain);
                record.signalsHeard++;
                record.lastHeardTick = now;
                int afterStage = KnowledgeStage(record.understanding);
                if (afterStage > beforeStage && afterStage > record.lastNotifiedStage)
                {
                    record.lastNotifiedStage = afterStage;
                    Messages.Message(colonist.LabelShortCap + " now " +
                        StageLearningVerb(afterStage) + " the " + DialectName(species) +
                        " calls of " + species.LabelCap + ".", colonist,
                        MessageTypeDefOf.PositiveEvent, false);
                }
            }
            return observers;
        }

        private void TeachPredator(ThingDef preySpecies, Pawn speaker, Thing subject, bool truthful)
        {
            if (HerdsMod.Settings?.enablePredatorSignalLearning != true || !truthful) return;
            Pawn predator = subject as Pawn;
            if (!WildlifeSpeciesClassification.IsPredator(predator?.def) || predator.Map != map) return;
            PredatorSignalKnowledgeRecord record = predatorKnowledge.FirstOrDefault(value =>
                value.predatorSpecies == predator.def && value.preySpecies == preySpecies);
            if (record == null)
            {
                record = new PredatorSignalKnowledgeRecord
                {
                    predatorSpecies = predator.def,
                    preySpecies = preySpecies
                };
                predatorKnowledge.Add(record);
            }
            record.understanding = Mathf.Clamp01(record.understanding + (speaker?.Position.InHorDistOf(predator.Position, 30f) == true ? 0.025f : 0.01f));
            record.signalsHeard++;
        }

        private WildlifeSignalKnowledgeRecord KnowledgeFor(Pawn colonist, ThingDef species)
        {
            WildlifeSignalKnowledgeRecord record = colonistKnowledge.FirstOrDefault(value =>
                value.colonist == colonist && value.species == species);
            if (record == null)
            {
                record = new WildlifeSignalKnowledgeRecord { colonist = colonist, species = species };
                colonistKnowledge.Add(record);
            }
            return record;
        }

        private int CountListeners(ThingDef species, IntVec3 cell, float radius, Pawn speaker)
        {
            float squared = radius * radius;
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            int count = 0;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn == speaker || pawn?.Spawned != true || pawn.Dead || pawn.def != species ||
                    pawn.Faction == Faction.OfPlayer) continue;
                if (pawn.Position.DistanceToSquared(cell) <= squared) count++;
            }
            return count;
        }

        private void ApplyImmediateResponse(WildlifeActiveSignal signal)
        {
            if (!signal.truthful || signal.subject?.Spawned != true ||
                (signal.kind != WildlifeSignalKind.Food && signal.kind != WildlifeSignalKind.Water)) return;
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            int moved = 0;
            for (int i = 0; i < pawns.Count && moved < 3; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn == signal.speaker || pawn?.Spawned != true || pawn.Dead ||
                    pawn.def != signal.species || pawn.Faction == Faction.OfPlayer ||
                    !pawn.Position.InHorDistOf(signal.cell, signal.radius) ||
                    pawn.Downed || pawn.InMentalState || pawn.CurJobDef == JobDefOf.AttackMelee ||
                    pawn.CurJobDef == JobDefOf.AttackStatic) continue;
                IntVec3 destination = CellFinder.RandomClosewalkCellNear(signal.subject.Position, map, 3);
                if (!destination.IsValid || !pawn.CanReach(destination, PathEndMode.OnCell, Danger.Some)) continue;
                pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto, destination),
                    JobTag.Misc, false);
                moved++;
            }
        }

        private void VerifyActiveSignals(int now)
        {
            for (int i = 0; i < activeSignals.Count; i++)
            {
                WildlifeActiveSignal active = activeSignals[i];
                // Herd alarms deliberately include a perception/reaction delay. Verify after
                // that behavior has had time to become visible instead of reporting a false mismatch.
                if (active == null || active.verified || now - active.startedTick < 90) continue;
                EvaluateResponse(active, out int reactions, out bool consistent, out string observed);
                active.reactionCount = reactions;
                active.behaviorConsistent = consistent;
                active.observedBehavior = observed;
                active.verified = true;
                WildlifeSignalTrace trace = signalHistory.FirstOrDefault(value => value.traceId == active.traceId);
                if (trace != null)
                {
                    trace.reactionCount = reactions;
                    trace.behaviorConsistent = consistent;
                    trace.observedBehavior = observed;
                    trace.verified = true;
                }
                if (!consistent && WildlifeTestLog.Enabled)
                    WildlifeTestLog.Write("SignalMismatch", "trace=" + active.traceId +
                        " species=" + active.species.defName + " kind=" + active.kind +
                        " expected=" + active.expectedBehavior + " observed=" + observed,
                        active.speaker, active.subject);
            }
        }

        private void EvaluateResponse(WildlifeActiveSignal signal, out int reactions,
            out bool consistent, out string observed)
        {
            reactions = 0;
            HerdMapComponent herds = map.GetComponent<HerdMapComponent>();
            HerdSnapshot group = signal.speaker?.Spawned == true ? herds?.HerdFor(signal.speaker) : null;
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn?.Spawned != true || pawn.Dead || pawn.def != signal.species ||
                    pawn.Faction == Faction.OfPlayer ||
                    !pawn.Position.InHorDistOf(signal.cell, signal.radius)) continue;
                string job = pawn.CurJobDef?.defName ?? "";
                bool reacted = false;
                if (signal.kind == WildlifeSignalKind.Alarm ||
                    signal.kind == WildlifeSignalKind.HumanDanger)
                    reacted = job.IndexOf("Flee", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        job.IndexOf("Hide", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        job.IndexOf("Attack", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        job == JobDefOf.Goto.defName;
                else if ((signal.kind == WildlifeSignalKind.Food ||
                    signal.kind == WildlifeSignalKind.Water) && signal.subject?.Spawned == true)
                    reacted = pawn.Position.InHorDistOf(signal.subject.Position, 6f) ||
                        (pawn.CurJobDef == JobDefOf.Goto &&
                         pawn.CurJob?.targetA.Cell.InHorDistOf(signal.subject.Position, 6f) == true);
                else if (signal.kind == WildlifeSignalKind.Contact)
                    reacted = pawn.Position.InHorDistOf(signal.cell, 12f);
                else if (signal.kind == WildlifeSignalKind.Coordination)
                    reacted = signal.subject?.Spawned == true &&
                        (job.IndexOf("Attack", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         job.IndexOf("Hunt", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         pawn.CurJob?.targetA.Thing == signal.subject);
                if (reacted) reactions++;
            }

            switch (signal.kind)
            {
                case WildlifeSignalKind.Alarm:
                case WildlifeSignalKind.HumanDanger:
                    bool defense = HasActiveDefense(group);
                    consistent = reactions > 0 || defense;
                    if (signal.truthful && signal.subject == null) consistent = false;
                    observed = defense ? "Group entered " + group.defenseMode :
                        reactions > 0 ? reactions + " animal(s) fled, hid, or defended" :
                        "No defensive response";
                    break;
                case WildlifeSignalKind.AllClear:
                    consistent = group == null || group.defenseMode == HerdDefenseMode.None;
                    observed = consistent ? "Group returned to calm" :
                        "Group remained in " + group.defenseMode;
                    break;
                case WildlifeSignalKind.Food:
                case WildlifeSignalKind.Water:
                    consistent = signal.subject?.Spawned == true && reactions > 0;
                    observed = reactions > 0 ? reactions + " animal(s) approached the resource" :
                        "No animal approached the resource";
                    break;
                case WildlifeSignalKind.Coordination:
                    consistent = signal.subject?.Spawned == true && (reactions > 0 ||
                        WildlifeSpeciesClassification.IsPredator(signal.speaker?.def));
                    observed = reactions > 0 ? reactions + " hunter(s) coordinated on the target" :
                        consistent ? "Predator coordination remained active" : "No hunt target response";
                    break;
                default:
                    consistent = reactions > 0 || signal.listenerCount == 0;
                    observed = reactions > 0 ? reactions + " animal(s) maintained contact" :
                        signal.listenerCount == 0 ? "No nearby listener" : "No contact response";
                    break;
            }
        }

        public void Replay(WildlifeSignalTrace trace)
        {
            if (trace?.species == null || !trace.cell.InBounds(map)) return;
            int now = Find.TickManager.TicksGame;
            activeSignals.Add(new WildlifeActiveSignal
            {
                traceId = 0,
                species = trace.species,
                kind = trace.kind,
                cell = trace.cell,
                startedTick = now,
                expiresTick = now + 210,
                radius = trace.radius,
                truthful = trace.truthful,
                subjectCell = trace.subjectCell,
                hasSubject = trace.hasSubject,
                listenerCount = trace.listenerCount,
                observerCount = trace.observerCount,
                reactionCount = trace.reactionCount,
                verified = true,
                behaviorConsistent = trace.behaviorConsistent,
                cause = trace.cause,
                expectedBehavior = trace.expectedBehavior,
                observedBehavior = trace.observedBehavior
            });
        }

        public List<string> DebugSignalScenario(string kindName)
        {
            if (!Enum.TryParse(kindName, true, out WildlifeSignalKind kind))
                kind = WildlifeSignalKind.Alarm;
            Pawn speaker = map.mapPawns.AllPawnsSpawned.FirstOrDefault(pawn =>
                pawn?.Spawned == true && !pawn.Dead && pawn.Faction != Faction.OfPlayer &&
                pawn.RaceProps?.Animal == true && WildlifeSpeciesClassification.IsPrey(pawn.def));
            if (speaker == null) return new List<string> { "scenario=no_prey" };
            Thing subject = null;
            bool truthful = true;
            HerdMapComponent herds = map.GetComponent<HerdMapComponent>();
            if (kind == WildlifeSignalKind.Alarm || kind == WildlifeSignalKind.HumanDanger)
            {
                subject = kind == WildlifeSignalKind.HumanDanger
                    ? map.mapPawns.FreeColonistsSpawned.FirstOrDefault()
                    : map.mapPawns.AllPawnsSpawned.FirstOrDefault(pawn =>
                        pawn?.Spawned == true && WildlifeSpeciesClassification.IsPredator(pawn.def) &&
                        pawn.Faction != Faction.OfPlayer);
                if (subject != null) herds?.NotifyThreat(speaker, subject, 750);
                else truthful = false;
            }
            else if (kind == WildlifeSignalKind.AllClear)
                herds?.NotifyThreatEnded(speaker, null);
            else if (kind == WildlifeSignalKind.Coordination)
            {
                Pawn predator = map.mapPawns.AllPawnsSpawned.FirstOrDefault(pawn =>
                    pawn?.Spawned == true && WildlifeSpeciesClassification.IsPredator(pawn.def) &&
                    pawn.Faction != Faction.OfPlayer);
                if (predator != null)
                {
                    speaker = predator;
                    subject = map.mapPawns.AllPawnsSpawned.FirstOrDefault(pawn =>
                        pawn?.Spawned == true && !pawn.Dead && pawn != predator &&
                        pawn.RaceProps?.Animal == true && WildlifeSpeciesClassification.IsPrey(pawn.def));
                }
            }
            else if (kind == WildlifeSignalKind.Food || kind == WildlifeSignalKind.Water)
                subject = map.listerThings.AllThings.OfType<Building_WildlifeTool>().FirstOrDefault(tool =>
                    tool.Operational && (kind == WildlifeSignalKind.Food
                        ? tool.Kind == WildlifeToolKind.Bait
                        : tool.Kind == WildlifeToolKind.WaterSource));
            NotifyAnimalSignal(speaker.def, kind, speaker, subject, truthful, 35f);
            return new List<string>
            {
                "scenario=emitted kind:" + kind + " speaker:" + speaker.thingIDNumber +
                " subject:" + (subject?.thingIDNumber ?? -1) + " truthful:" + truthful,
                "followup=SIGNAL_TRACE after 30 game ticks"
            };
        }

        public bool DebugIdentifiedSignalText(Pawn animal)
        {
            if (animal?.Spawned != true || animal.Map != map || animal.RaceProps?.Animal != true)
                return false;
            Pawn observer = map.mapPawns.FreeColonistsSpawned.FirstOrDefault(pawn =>
                pawn?.Dead != true);
            if (observer == null)
            {
                Messages.Message("The test needs a living colonist to hold signal knowledge.",
                    MessageTypeDefOf.RejectInput, false);
                return false;
            }
            HerdsMod.Settings.enableWildlifeSignalCulture = true;
            HerdsMod.Settings.showIdentifiedSignalText = true;
            WildlifeSignalKnowledgeRecord knowledge = KnowledgeFor(observer, animal.def);
            knowledge.understanding = Mathf.Max(knowledge.understanding, 0.45f);
            knowledge.lastNotifiedStage = Mathf.Max(knowledge.lastNotifiedStage, 2);
            NotifyAnimalSignal(animal.def, WildlifeSignalKind.Contact, animal, null, true, 28f);
            Find.Selector.ClearSelection();
            Find.Selector.Select(animal);
            Find.CameraDriver.JumpToCurrentMapLoc(animal.Position);
            Messages.Message("Identified signal text test: a meaning label should appear beside " +
                animal.LabelShortCap + ".", animal, MessageTypeDefOf.PositiveEvent, false);
            return true;
        }

        public List<string> DebugTestAnimalLog()
        {
            Pawn speaker = map.mapPawns.AllPawnsSpawned.FirstOrDefault(pawn =>
                pawn?.Spawned == true && !pawn.Dead && pawn.RaceProps?.Animal == true);
            if (speaker == null)
                return new List<string> { "signalLogTest=FAIL reason:no_animal" };
            int before = Find.BattleLog?.Battles.Sum(battle =>
                battle.Entries.Count(entry => entry.Concerns(speaker))) ?? 0;
            int key = Gen.HashCombineInt(speaker.thingIDNumber,
                (int)WildlifeSignalKind.Contact);
            lastPawnLogTicks.Remove(key);
            NotifyAnimalSignal(speaker.def, WildlifeSignalKind.Contact,
                speaker, null, true, 24f);
            int after = Find.BattleLog?.Battles.Sum(battle =>
                battle.Entries.Count(entry => entry.Concerns(speaker))) ?? 0;
            return new List<string>
            {
                "signalLogTest=" + (after > before ? "PASS" : "FAIL") +
                    " animal:" + speaker.thingIDNumber +
                    " before:" + before + " after:" + after
            };
        }

        private void TryResourceSignal(int now)
        {
            HerdMapComponent herds = map.GetComponent<HerdMapComponent>();
            if (herds == null) return;
            List<Building_WildlifeTool> resources = map.listerThings.AllThings
                .OfType<Building_WildlifeTool>().Where(tool => tool.Operational &&
                    (tool.Kind == WildlifeToolKind.Bait || tool.Kind == WildlifeToolKind.WaterSource)).ToList();
            if (resources.Count == 0) return;
            IReadOnlyList<HerdSnapshot> groups = herds.AllHerds;
            for (int i = 0; i < groups.Count; i++)
            {
                HerdSnapshot group = groups[i];
                if (group?.leader?.Spawned != true || group.faction == Faction.OfPlayer) continue;
                WildlifeDialectRecord dialect = DialectFor(group.species);
                if (now - dialect.lastSignalTick < 6000) continue;
                Building_WildlifeTool resource = resources.FirstOrDefault(tool =>
                    tool.Position.InHorDistOf(group.center, Mathf.Min(18f, tool.InfluenceRadius)));
                if (resource == null) continue;
                NotifyAnimalSignal(group.species,
                    resource.Kind == WildlifeToolKind.Bait ? WildlifeSignalKind.Food : WildlifeSignalKind.Water,
                    group.leader, resource, true, 28f);
                break;
            }
        }

        private Thing FindThreatNear(List<Pawn> animals, float radius)
        {
            float radiusSquared = radius * radius;
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < animals.Count; i++)
                for (int j = 0; j < pawns.Count; j++)
                {
                    Pawn candidate = pawns[j];
                    if (candidate == animals[i] || candidate?.Spawned != true || candidate.Dead) continue;
                    bool danger = WildlifeSpeciesClassification.IsPredator(candidate.def) || candidate.HostileTo(animals[i]);
                    if (danger && candidate.Position.DistanceToSquared(animals[i].Position) <= radiusSquared)
                        return candidate;
                }
            return null;
        }

        private Pawn MannedObserverNear(IntVec3 cell, float radius)
        {
            IReadOnlyList<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < colonists.Count; i++)
            {
                Building_WildlifeTool post = MannedPostFor(colonists[i]);
                if (post != null && post.Position.InHorDistOf(cell, post.InfluenceRadius + radius))
                    return colonists[i];
            }
            return null;
        }

        private Building_WildlifeTool MannedPostFor(Pawn colonist)
        {
            if (colonist?.CurJobDef != HerdsDefOf.Herds_ManObservationPost)
                return null;
            Building_WildlifeTool post = colonist.CurJob?.targetA.Thing as Building_WildlifeTool;
            return post?.Kind == WildlifeToolKind.ObservationPost && post.ManningColonist() == colonist ? post : null;
        }

        public static string SignalLabel(WildlifeSignalKind kind)
        {
            switch (kind)
            {
                case WildlifeSignalKind.HumanDanger: return "Human-danger call";
                case WildlifeSignalKind.AllClear: return "All-clear call";
                case WildlifeSignalKind.Food: return "Food call";
                case WildlifeSignalKind.Water: return "Water call";
                case WildlifeSignalKind.Coordination: return "Coordination call";
                case WildlifeSignalKind.Contact: return "Contact call";
                default: return "Alarm call";
            }
        }

        public static string SignalFamily(WildlifeSignalKind kind)
        {
            if (kind == WildlifeSignalKind.Alarm || kind == WildlifeSignalKind.HumanDanger)
                return "Warning call";
            if (kind == WildlifeSignalKind.Food || kind == WildlifeSignalKind.Water)
                return "Resource call";
            if (kind == WildlifeSignalKind.Coordination) return "Hunting call";
            return "Social call";
        }

        public static string PlayerFacingSignal(WildlifeSignalKind kind, float understanding,
            bool truthful, bool verified)
        {
            if (understanding < 0.15f) return "Unfamiliar wildlife call";
            if (understanding < 0.4f) return SignalFamily(kind);
            string label = SignalLabel(kind);
            if (understanding >= 0.92f && verified && !truthful) label += " (misleading)";
            return label;
        }

        public static string SignalMeaning(WildlifeSignalKind kind)
        {
            switch (kind)
            {
                case WildlifeSignalKind.Alarm: return "Danger nearby";
                case WildlifeSignalKind.HumanDanger: return "Humans detected";
                case WildlifeSignalKind.AllClear: return "Danger has passed";
                case WildlifeSignalKind.Food: return "Food discovered";
                case WildlifeSignalKind.Water: return "Water discovered";
                case WildlifeSignalKind.Coordination: return "Coordinating a hunt";
                default: return "Maintaining contact";
            }
        }

        public static string IdentifiedSignalMeaning(WildlifeSignalKind kind, float understanding) =>
            understanding >= 0.4f ? SignalMeaning(kind) : null;

        private static int KnowledgeStage(float value)
        {
            return value < 0.15f ? 0 : value < 0.4f ? 1 :
                value < 0.7f ? 2 : value < 0.92f ? 3 : 4;
        }

        private static string StageLearningVerb(int stage)
        {
            switch (stage)
            {
                case 1: return "recognizes";
                case 2: return "interprets";
                case 3: return "fluently understands";
                case 4: return "has mastered";
                default: return "is learning";
            }
        }

        private static string SignalCause(WildlifeSignalKind kind, Pawn speaker,
            Thing subject, bool truthful, bool humanImitation)
        {
            if (humanImitation) return "A colonist imitated the local call.";
            if (!truthful) return "The call was mistaken or deliberately misleading.";
            switch (kind)
            {
                case WildlifeSignalKind.Alarm:
                    return subject == null ? "No visible danger caused this call." :
                        "A nearby threat was detected: " + subject.LabelShortCap + ".";
                case WildlifeSignalKind.HumanDanger:
                    return "A human entered the group's danger awareness.";
                case WildlifeSignalKind.AllClear:
                    return "The group could no longer detect its threat.";
                case WildlifeSignalKind.Food:
                    return "A usable food source was found.";
                case WildlifeSignalKind.Water:
                    return "A usable water source was found.";
                case WildlifeSignalKind.Coordination:
                    return "Predators coordinated their approach to prey.";
                default:
                    return "An animal called to maintain social contact.";
            }
        }

        private static string ExpectedBehavior(WildlifeSignalKind kind)
        {
            switch (kind)
            {
                case WildlifeSignalKind.Alarm:
                case WildlifeSignalKind.HumanDanger:
                    return "Listeners should flee, hide, protect young, or defend.";
                case WildlifeSignalKind.AllClear:
                    return "Listeners should return to calm behavior.";
                case WildlifeSignalKind.Food:
                    return "Nearby listeners should approach the food source.";
                case WildlifeSignalKind.Water:
                    return "Nearby listeners should approach the water source.";
                case WildlifeSignalKind.Coordination:
                    return "Hunters should coordinate on the same prey.";
                default:
                    return "Nearby animals should maintain contact.";
            }
        }

        public static bool VisualGrammarSelfTest()
        {
            HashSet<string> colors = new HashSet<string>();
            foreach (WildlifeSignalKind kind in Enum.GetValues(typeof(WildlifeSignalKind)))
            {
                Color color = SignalColor(kind);
                colors.Add(color.r.ToString("0.00") + ":" + color.g.ToString("0.00") +
                    ":" + color.b.ToString("0.00"));
                if (SignalLabel(kind).NullOrEmpty() || SignalFamily(kind).NullOrEmpty()) return false;
            }
            return colors.Count == Enum.GetValues(typeof(WildlifeSignalKind)).Length;
        }

        public static bool IdentifiedSignalTextSelfTest()
        {
            HashSet<string> meanings = new HashSet<string>();
            foreach (WildlifeSignalKind kind in Enum.GetValues(typeof(WildlifeSignalKind)))
            {
                if (IdentifiedSignalMeaning(kind, 0.399f) != null) return false;
                string meaning = IdentifiedSignalMeaning(kind, 0.4f);
                if (meaning.NullOrEmpty()) return false;
                meanings.Add(meaning);
            }
            return meanings.Count == Enum.GetValues(typeof(WildlifeSignalKind)).Length;
        }

        public static bool ResponseSafetySelfTest() => !HasActiveDefense(null);

        private static bool HasActiveDefense(HerdSnapshot group) =>
            group != null && group.defenseMode != HerdDefenseMode.None;

        public static Color SignalColor(WildlifeSignalKind kind)
        {
            switch (kind)
            {
                case WildlifeSignalKind.AllClear: return new Color(0.25f, 0.9f, 0.45f);
                case WildlifeSignalKind.Food: return new Color(0.95f, 0.78f, 0.18f);
                case WildlifeSignalKind.Water: return new Color(0.2f, 0.72f, 1f);
                case WildlifeSignalKind.Contact: return new Color(0.75f, 0.55f, 1f);
                case WildlifeSignalKind.Coordination: return new Color(1f, 0.5f, 0.12f);
                case WildlifeSignalKind.HumanDanger: return new Color(1f, 0.2f, 0.1f);
                default: return new Color(1f, 0.38f, 0.28f);
            }
        }

        private static int PositiveMod(int value, int modulus)
        {
            int result = value % modulus;
            return result < 0 ? result + modulus : result;
        }

        private static string BridgeClean(string value)
        {
            return (value ?? "none").Replace(' ', '_').Replace('|', '/').Replace('\n', ' ');
        }
    }

    public sealed class Window_WildlifeSignals : Window
    {
        private readonly Map map;
        private Pawn observer;
        private Vector2 speciesScroll;
        private Vector2 detailScroll;
        private ThingDef selectedSpecies;

        public Window_WildlifeSignals(Map map, Pawn observer, ThingDef selectedSpecies = null,
            Vector2? speciesScroll = null, Vector2? detailScroll = null)
        {
            this.map = map;
            this.observer = observer;
            this.selectedSpecies = selectedSpecies;
            this.speciesScroll = speciesScroll ?? Vector2.zero;
            this.detailScroll = detailScroll ?? Vector2.zero;
            doCloseButton = true;
            doCloseX = true;
            absorbInputAroundWindow = true;
        }

        public override Vector2 InitialSize => new Vector2(920f, 680f);

        public override void DoWindowContents(Rect inRect)
        {
            WildlifeSignalCultureMapComponent signals = map?.GetComponent<WildlifeSignalCultureMapComponent>();
            if (signals == null)
            {
                Widgets.Label(inRect, "No local wildlife signal data is available.");
                return;
            }
            List<ThingDef> species = map.mapPawns.AllPawnsSpawned.Where(pawn =>
                pawn?.RaceProps?.Animal == true && pawn.Faction != Faction.OfPlayer).Select(pawn => pawn.def)
                .Concat(signals.Dialects.Select(record => record.species))
                .Where(def => def != null).Distinct().OrderBy(def => def.label).ToList();
            if (selectedSpecies == null || !species.Contains(selectedSpecies))
                selectedSpecies = species.FirstOrDefault();

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width - 220f, 30f), "Local Wildlife Signals");
            if (Widgets.ButtonText(new Rect(inRect.xMax - 210f, inRect.y, 210f, 30f),
                observer == null ? "Viewing: Colony" : "Viewing: " + observer.LabelShortCap))
                OpenViewerMenu();
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(inRect.x, inRect.y + 32f, inRect.width, 42f),
                "Wild animals use species-specific local calls. Choose an animal to learn what its visible " +
                "signals mean, how reliable they are, and how nearby animals responded.");
            Rect listenerRect = new Rect(inRect.x, inRect.y + 77f, inRect.width, 48f);
            Widgets.DrawBoxSolid(listenerRect, observer == null
                ? new Color(0.16f, 0.15f, 0.11f, 0.9f)
                : new Color(0.1f, 0.23f, 0.19f, 0.9f));
            Widgets.Label(listenerRect.ContractedBy(7f),
                observer == null
                    ? "Colony view: Each animal uses the best understanding contributed by a present colonist."
                    : ObserverIsManning()
                        ? "Listening now: " + observer.LabelShortCap +
                          " is manning this post and learning nearby calls faster."
                        : "Personal view: Showing only " + observer.LabelShortCap +
                          "'s understanding of each animal.");
            TooltipHandler.TipRegion(listenerRect,
                "Understanding unlocks broad signal families at 15%, exact meanings at 40%, " +
                "reliability at 70%, and misleading calls at 92%.\n\nIn Colony view, knowledge can fall " +
                "when its current contributor leaves or dies.");

            Rect body = new Rect(inRect.x, inRect.y + 133f, inRect.width, inRect.height - 178f);
            Rect left = new Rect(body.x, body.y, 310f, body.height);
            Rect right = new Rect(left.xMax + 10f, body.y, body.width - left.width - 10f, body.height);
            Widgets.DrawBoxSolid(left, new Color(0.08f, 0.12f, 0.13f, 0.92f));
            Widgets.DrawBoxSolid(right, new Color(0.07f, 0.105f, 0.11f, 0.94f));
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(left.x + 12f, left.y + 9f, left.width - 24f, 28f), "Animal Dialects");
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(left.x + 12f, left.y + 36f, left.width - 24f, 34f),
                "Select an animal to inspect its calls.");
            Rect listOuter = new Rect(left.x + 6f, left.y + 72f, left.width - 12f, left.height - 78f);
            Rect listView = new Rect(0f, 0f, listOuter.width - 16f,
                Mathf.Max(listOuter.height, species.Count * 70f));
            Widgets.BeginScrollView(listOuter, ref speciesScroll, listView);
            for (int i = 0; i < species.Count; i++)
            {
                ThingDef def = species[i];
                WildlifeDialectRecord dialect = signals?.DialectFor(def);
                float understanding = observer == null ? signals?.ColonyUnderstanding(def) ?? 0f :
                    signals?.Understanding(observer, def) ?? 0f;
                Pawn contributor = observer == null ? signals?.ColonyContributor(def) : observer;
                Rect row = new Rect(0f, i * 70f, listView.width, 66f);
                if (selectedSpecies == def)
                    Widgets.DrawBoxSolid(row, new Color(0.18f, 0.31f, 0.29f, 0.9f));
                Widgets.DrawHighlightIfMouseover(row);
                if (def.uiIcon != null)
                    Widgets.DrawTextureFitted(new Rect(row.x + 7f, row.y + 7f, 38f, 38f), def.uiIcon, 1f);
                Widgets.Label(new Rect(row.x + 52f, row.y + 5f, row.width - 58f, 22f), def.LabelCap);
                GUI.color = new Color(0.72f, 0.82f, 0.78f);
                Widgets.Label(new Rect(row.x + 52f, row.y + 27f, row.width - 58f, 20f),
                    signals.UnderstandingLabel(understanding) + "  " + understanding.ToStringPercent());
                Text.Font = GameFont.Tiny;
                GUI.color = new Color(0.58f, 0.68f, 0.64f);
                Widgets.Label(new Rect(row.x + 52f, row.y + 45f, row.width - 58f, 18f),
                    observer == null
                        ? contributor == null ? "Contributor: None" : "Contributor: " + contributor.LabelShortCap
                        : "Personal understanding");
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
                Widgets.FillableBar(new Rect(row.x + 52f, row.y + 61f, row.width - 62f, 3f),
                    understanding);
                if (Widgets.ButtonInvisible(row)) selectedSpecies = def;
                TooltipHandler.TipRegion(row, signals.SignalTooltip(def, observer));
            }
            Widgets.EndScrollView();

            if (selectedSpecies != null)
                DrawSpeciesDetail(right.ContractedBy(12f), signals, selectedSpecies);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private void OpenViewerMenu()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>
            {
                new FloatMenuOption("Colony Knowledge", () => SetObserver(null))
            };
            options.AddRange(map.mapPawns.FreeColonistsSpawned
                .Where(pawn => pawn?.Dead != true)
                .OrderBy(pawn => pawn.LabelShortCap)
                .Select(pawn => new FloatMenuOption(pawn.LabelShortCap,
                    () => SetObserver(pawn))));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void SetObserver(Pawn pawn)
        {
            observer = pawn;
            speciesScroll = Vector2.zero;
            detailScroll = Vector2.zero;
        }

        private bool ObserverIsManning() =>
            observer?.CurJobDef == HerdsDefOf.Herds_ManObservationPost;

        private void DrawSpeciesDetail(Rect rect, WildlifeSignalCultureMapComponent signals, ThingDef species)
        {
            float understanding = observer == null ? signals.ColonyUnderstanding(species) :
                signals.Understanding(observer, species);
            WildlifeDialectRecord dialect = signals.DialectFor(species);
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 28f), species.LabelCap + " Signals");
            Text.Font = GameFont.Small;
            GUI.color = new Color(0.66f, 0.86f, 0.78f);
            Widgets.Label(new Rect(rect.x, rect.y + 31f, rect.width, 22f),
                (understanding >= 0.15f ? signals.DialectName(species) : "Unfamiliar dialect") +
                "  |  " + signals.UnderstandingLabel(understanding) + " " + understanding.ToStringPercent());
            GUI.color = Color.white;
            Pawn contributor = observer == null ? signals.ColonyContributor(species) : observer;
            GUI.color = new Color(0.62f, 0.72f, 0.68f);
            Widgets.Label(new Rect(rect.x, rect.y + 54f, rect.width, 20f),
                observer == null
                    ? contributor == null ? "Current contributor: None" :
                        "Current contributor: " + contributor.LabelShortCap
                    : "Personal knowledge held by " + observer.LabelShortCap);
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.x, rect.y + 78f, rect.width, 20f), "Signal Key");
            DrawVisualKey(new Rect(rect.x, rect.y + 100f, rect.width, 34f), understanding);
            Rect scrollOuter = new Rect(rect.x, rect.y + 139f, rect.width, rect.height - 139f);
            List<WildlifeSignalTrace> traces = signals.RecentSignals.Where(trace =>
                trace.species == species).OrderByDescending(trace => trace.tick).Take(8).ToList();
            float contentHeight = 310f + traces.Count * 104f;
            Rect view = new Rect(0f, 0f, scrollOuter.width - 16f,
                Mathf.Max(scrollOuter.height, contentHeight));
            Widgets.BeginScrollView(scrollOuter, ref detailScroll, view);

            Widgets.Label(new Rect(0f, 0f, view.width, 22f), "Understanding Progress");
            string[] stages = { "Recognized", "Interpreted", "Fluent", "Mastered" };
            string[] descriptions =
            {
                "Identifies whether a call concerns danger, resources, or social contact.",
                "Identifies the exact intent of the call.",
                "Reveals range, credibility, and the response animals are expected to make.",
                "Recognizes mistaken, deceptive, or behaviorally inconsistent calls."
            };
            float[] thresholds = { 0.15f, 0.4f, 0.7f, 0.92f };
            for (int i = 0; i < stages.Length; i++)
            {
                Rect stage = new Rect(0f, 27f + i * 48f, view.width, 43f);
                bool unlocked = understanding >= thresholds[i];
                Widgets.DrawBoxSolid(stage, unlocked
                    ? new Color(0.12f, 0.26f, 0.22f, 0.9f)
                    : new Color(0.12f, 0.13f, 0.14f, 0.82f));
                GUI.color = unlocked ? Color.white : new Color(0.55f, 0.57f, 0.58f);
                Widgets.Label(new Rect(stage.x + 8f, stage.y + 4f, 140f, 20f),
                    (unlocked ? "Learned: " : "Locked: ") + stages[i]);
                Widgets.Label(new Rect(stage.x + 150f, stage.y + 4f, stage.width - 158f, 36f),
                    descriptions[i]);
            }
            GUI.color = Color.white;
            Rect reliability = new Rect(0f, 226f, view.width, 42f);
            Widgets.DrawBoxSolid(reliability, new Color(0.11f, 0.17f, 0.18f, 0.9f));
            Widgets.Label(new Rect(8f, 5f + reliability.y, reliability.width - 16f, 20f),
                understanding >= 0.7f
                    ? "Local credibility: " + dialect.credibility.ToStringPercent() +
                      "   Human trust: " + dialect.humanTrust.ToStringPercent()
                    : "Reliability becomes visible at Fluent understanding.");
            TooltipHandler.TipRegion(reliability, signals.SignalTooltip(species, observer));

            Widgets.Label(new Rect(0f, 278f, view.width, 24f), "Recorded Signals");
            if (traces.Count == 0)
                Widgets.Label(new Rect(0f, 306f, view.width, 42f),
                    "No calls have been recorded. Observe this animal in the field.");
            for (int i = 0; i < traces.Count; i++)
            {
                WildlifeSignalTrace trace = traces[i];
                Rect card = new Rect(0f, 306f + i * 104f, view.width, 96f);
                bool showAccuracy = understanding >= 0.92f && trace.verified;
                Color cardColor = showAccuracy && (!trace.truthful || !trace.behaviorConsistent)
                    ? new Color(0.35f, 0.11f, 0.1f, 0.9f)
                    : new Color(0.11f, 0.18f, 0.19f, 0.92f);
                Widgets.DrawBoxSolid(card, cardColor);
                string name = WildlifeSignalCultureMapComponent.PlayerFacingSignal(trace.kind,
                    understanding, trace.truthful, trace.verified);
                if (understanding >= 0.4f)
                    name += " - " + WildlifeSignalCultureMapComponent.SignalMeaning(trace.kind);
                Widgets.Label(new Rect(card.x + 8f, card.y + 5f, card.width - 90f, 22f), name);
                if (understanding >= 0.7f)
                {
                    Widgets.Label(new Rect(card.x + 8f, card.y + 29f, card.width - 90f, 20f),
                        "Range " + trace.radius.ToString("0") + "  |  Listeners " + trace.listenerCount +
                        "  |  Reactions " + (trace.verified ? trace.reactionCount.ToString() : "..."));
                    Widgets.Label(new Rect(card.x + 8f, card.y + 51f, card.width - 90f, 36f),
                        trace.verified ? trace.observedBehavior : trace.expectedBehavior);
                }
                else
                    Widgets.Label(new Rect(card.x + 8f, card.y + 31f, card.width - 90f, 42f),
                        understanding >= 0.4f ? trace.cause : "Continue observing to interpret this call.");
                if (Widgets.ButtonText(new Rect(card.xMax - 76f, card.y + 32f, 68f, 30f), "Replay"))
                {
                    Pawn savedObserver = observer;
                    ThingDef savedSpecies = selectedSpecies;
                    Vector2 savedSpeciesScroll = speciesScroll;
                    Vector2 savedDetailScroll = detailScroll;
                    WildlifeUI.CloseMenus();
                    signals.Replay(trace);
                    Find.WindowStack.Add(new Window_WildlifeSignals(map, savedObserver,
                        savedSpecies, savedSpeciesScroll, savedDetailScroll));
                }
                TooltipHandler.TipRegion(card, understanding >= 0.7f
                    ? trace.cause + "\n\nExpected: " + trace.expectedBehavior +
                      "\nObserved: " + trace.observedBehavior
                    : "More understanding reveals what caused the call and how animals responded.");
            }
            Widgets.EndScrollView();
        }

        private static void DrawVisualKey(Rect rect, float understanding)
        {
            WildlifeSignalKind[] kinds =
            {
                WildlifeSignalKind.Alarm, WildlifeSignalKind.HumanDanger,
                WildlifeSignalKind.AllClear, WildlifeSignalKind.Contact,
                WildlifeSignalKind.Food, WildlifeSignalKind.Water,
                WildlifeSignalKind.Coordination
            };
            string[] exactNames = { "Alarm", "Human", "Safe", "Contact", "Food", "Water", "Hunt" };
            string[] familyNames = { "Danger", "Danger", "Danger", "Social", "Resource", "Resource", "Hunt" };
            string[] tips =
            {
                "A double red pulse and threat line: animals detected immediate danger.",
                "A sharp red pulse: people are the detected danger.",
                "Nested green pulses: danger has passed and the group is settling.",
                "Paired violet pulses: animals are maintaining social contact.",
                "A gold pulse and resource line: food was discovered.",
                "A blue pulse and resource line: water was discovered.",
                "Orange pulses and a target line: predators are coordinating a hunt."
            };
            float width = rect.width / kinds.Length;
            for (int i = 0; i < kinds.Length; i++)
            {
                Rect entry = new Rect(rect.x + i * width, rect.y, width - 2f, rect.height);
                Widgets.DrawBoxSolid(entry, new Color(0.1f, 0.14f, 0.15f, 0.9f));
                Color color = WildlifeSignalCultureMapComponent.SignalColor(kinds[i]);
                Widgets.DrawBoxSolid(new Rect(entry.x + 5f, entry.y + 9f, 12f, 12f), color);
                Text.Font = GameFont.Tiny;
                string label = understanding < 0.15f ? "?" :
                    understanding < 0.4f ? familyNames[i] : exactNames[i];
                Widgets.Label(new Rect(entry.x + 21f, entry.y + 7f, entry.width - 23f, 20f), label);
                TooltipHandler.TipRegion(entry, understanding < 0.15f
                    ? "The colony has not learned what this pattern means for this species."
                    : understanding < 0.4f
                        ? "The colony recognizes the broad family of this call. More observation reveals its exact meaning."
                        : tips[i]);
            }
            Text.Font = GameFont.Small;
        }
    }

    public static class WildlifeSignalCultureAPI
    {
        public static void NotifyPredatorCoordination(Pawn predator, Pawn prey) =>
            predator?.Map?.GetComponent<WildlifeSignalCultureMapComponent>()?.NotifyPredatorCoordination(predator, prey);

        public static string PredatorSummary(Pawn predator) =>
            HerdsMod.Settings?.enableWildlifeSignalCulture != true ? string.Empty :
            predator?.Map?.GetComponent<WildlifeSignalCultureMapComponent>()?.PredatorSummary(predator.def) ??
            "No prey calls interpreted";

        public static string PredatorTooltip(Pawn predator) =>
            HerdsMod.Settings?.enableWildlifeSignalCulture != true ? string.Empty :
            predator?.Map?.GetComponent<WildlifeSignalCultureMapComponent>()?.PredatorTooltip(predator.def) ??
            "No local signal information.";
    }

    public static class WildlifeSignalDebugActions
    {
        [DebugAction("Wildlife", "Emit test wildlife signal", actionType = DebugActionType.ToolMapForPawns,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void EmitSignal(Pawn animal)
        {
            if (animal?.Spawned != true || animal.RaceProps?.Animal != true) return;
            animal.Map.GetComponent<WildlifeSignalCultureMapComponent>()?.NotifyAnimalSignal(
                animal.def, WildlifeSignalKind.Alarm, animal, null, true, 35f);
        }

        [DebugAction("Wildlife", "Test identified signal text", actionType = DebugActionType.ToolMapForPawns,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void TestIdentifiedSignalText(Pawn animal)
        {
            animal?.Map?.GetComponent<WildlifeSignalCultureMapComponent>()
                ?.DebugIdentifiedSignalText(animal);
        }

        [DebugAction("Wildlife", "Open local wildlife signals", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void OpenSignals()
        {
            Find.WindowStack.Add(new Window_WildlifeSignals(Find.CurrentMap, null));
        }
    }
}
