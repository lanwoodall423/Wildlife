using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using RimWorld;
using Verse;

namespace Herds
{
    public enum WildlifeEventKind
    {
        Sighting,
        Study,
        Tracks,
        TrailCompletion,
        Signal,
        Hunt,
        Tending,
        Taming,
        Survey,
        Expedition,
        MysteryEvidence,
        Story,
        Report,
        Documentation,
        Migration,
        PopulationChange,
        NotableAnimal,
        Memory,
        Policy,
        Forecast
    }

    /// <summary>Small, meaningful cross-system event. Simulation systems remain authoritative for their state.</summary>
    public sealed class WildlifeEvent
    {
        public int id;
        public int tick;
        public WildlifeEventKind kind;
        public Map map;
        public Pawn observer;
        public Pawn animal;
        public ThingDef species;
        public IntVec3 cell = IntVec3.Invalid;
        public string subjectId;
        public string methodId;
        public string source;
        public string sourceInstanceId;
        public string reasonId;
        public string summary;
        public float quality = 1f;
        public float confidence;
        public float amount;
        public bool success = true;
        public bool documented;
        public IReadOnlyList<Pawn> witnesses;
        public IReadOnlyDictionary<string, string> metadata;

        public WildlifeEvent Copy()
        {
            return new WildlifeEvent
            {
                id = id,
                tick = tick,
                kind = kind,
                map = map,
                observer = observer,
                animal = animal,
                species = species,
                cell = cell,
                subjectId = subjectId,
                methodId = methodId,
                source = source,
                sourceInstanceId = sourceInstanceId,
                reasonId = reasonId,
                summary = summary,
                quality = quality,
                confidence = confidence,
                amount = amount,
                success = success,
                documented = documented,
                witnesses = witnesses == null ? null : new ReadOnlyCollection<Pawn>(witnesses.Where(value => value != null).Take(32).ToList()),
                metadata = metadata == null ? null : new ReadOnlyDictionary<string, string>(metadata.ToDictionary(pair => pair.Key, pair => pair.Value))
            };
        }
    }

    /// <summary>
    /// The only bounded cross-system notification path for meaningful Wildlife outcomes.
    /// Subscribers must treat events as facts and query their owning system for current state.
    /// </summary>
    public sealed class WildlifeEventRouter
    {
        public const int HistoryLimit = 128;
        private readonly List<WildlifeEvent> history = new List<WildlifeEvent>(HistoryLimit);
        private readonly List<Action<WildlifeEvent>> subscribers = new List<Action<WildlifeEvent>>();
        private int nextId;

        public static WildlifeEventRouter Shared { get; } = new WildlifeEventRouter();

        public IReadOnlyList<WildlifeEvent> History => new ReadOnlyCollection<WildlifeEvent>(history.ToList());

        public IDisposable Subscribe(Action<WildlifeEvent> subscriber)
        {
            if (subscriber == null) return new Subscription(null, null);
            if (!subscribers.Contains(subscriber)) subscribers.Add(subscriber);
            return new Subscription(this, subscriber);
        }

        public int Publish(WildlifeEvent value)
        {
            if (value == null || value.kind == 0 && value.species == null && value.summary.NullOrEmpty()) return 0;
            value.id = ++nextId;
            value.tick = value.tick > 0 ? value.tick : Find.TickManager?.TicksGame ?? 0;
            value.quality = Clamp(value.quality, 0f, 100f, 1f);
            value.confidence = Clamp(value.confidence, 0f, 1f, 0f);
            value.amount = Clamp(value.amount, 0f, 1000000f, 0f);
            history.Add(value.Copy());
            if (history.Count > HistoryLimit) history.RemoveAt(0);

            Action<WildlifeEvent>[] handlers = subscribers.ToArray();
            for (int i = 0; i < handlers.Length; i++)
            {
                try { handlers[i](value); }
                catch (Exception exception)
                {
                    Log.ErrorOnce("A Wildlife event subscriber failed: " + exception.Message,
                        Gen.HashCombineInt(value.id, i));
                }
            }
            return value.id;
        }

        public void Clear()
        {
            history.Clear();
            nextId = 0;
        }

        private void Unsubscribe(Action<WildlifeEvent> subscriber)
        {
            if (subscriber != null) subscribers.Remove(subscriber);
        }

        private static float Clamp(float value, float minimum, float maximum, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return fallback;
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private sealed class Subscription : IDisposable
        {
            private WildlifeEventRouter router;
            private Action<WildlifeEvent> subscriber;

            public Subscription(WildlifeEventRouter router, Action<WildlifeEvent> subscriber)
            {
                this.router = router;
                this.subscriber = subscriber;
            }

            public void Dispose()
            {
                router?.Unsubscribe(subscriber);
                router = null;
                subscriber = null;
            }
        }
    }

    public static class WildlifeEventUtility
    {
        public static int Publish(WildlifeEventKind kind, Map map, Pawn observer, Pawn animal,
            ThingDef species, string source, string summary, string methodId = null,
            bool success = true, float quality = 1f, float confidence = 0f,
            float amount = 0f, bool documented = false, string sourceInstanceId = null,
            string reasonId = null, IntVec3 cell = default(IntVec3), IReadOnlyList<Pawn> witnesses = null,
            IReadOnlyDictionary<string, string> metadata = null)
        {
            return WildlifeEventRouter.Shared.Publish(new WildlifeEvent
            {
                kind = kind,
                map = map,
                observer = observer,
                animal = animal,
                species = species ?? animal?.def,
                cell = cell,
                source = source,
                sourceInstanceId = sourceInstanceId,
                methodId = methodId,
                reasonId = reasonId,
                summary = summary,
                success = success,
                quality = quality,
                confidence = confidence,
                amount = amount,
                documented = documented,
                witnesses = witnesses,
                metadata = metadata
            });
        }

        public static WildlifeEventKind KindForOutcome(string category, bool negative)
        {
            string value = category?.ToLowerInvariant() ?? string.Empty;
            if (value.Contains("sight") || value.Contains("observation")) return WildlifeEventKind.Sighting;
            if (value.Contains("trail") || value.Contains("track")) return WildlifeEventKind.Tracks;
            if (value.Contains("signal") || value.Contains("call")) return WildlifeEventKind.Signal;
            if (value.Contains("hunt")) return WildlifeEventKind.Hunt;
            if (value.Contains("tend")) return WildlifeEventKind.Tending;
            if (value.Contains("tame") || value.Contains("train")) return WildlifeEventKind.Taming;
            if (value.Contains("expedition") || value.Contains("survey")) return WildlifeEventKind.Expedition;
            if (value.Contains("mystery")) return WildlifeEventKind.MysteryEvidence;
            if (value.Contains("migration") || value.Contains("roaming")) return WildlifeEventKind.Migration;
            if (value.Contains("population") || value.Contains("habitat")) return WildlifeEventKind.PopulationChange;
            if (value.Contains("notable")) return WildlifeEventKind.NotableAnimal;
            if (value.Contains("memory")) return WildlifeEventKind.Memory;
            if (value.Contains("document")) return WildlifeEventKind.Documentation;
            if (value.Contains("report")) return WildlifeEventKind.Report;
            return negative ? WildlifeEventKind.Story : WildlifeEventKind.Story;
        }
    }
}
