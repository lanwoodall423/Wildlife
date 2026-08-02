using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Herds
{
    public enum AnimalMemoryKind
    {
        Studied,
        Called,
        Tended,
        Protected,
        Wounded,
        Hunted,
        KinKilled,
        Nuzzled,
        PositiveInteraction,
        NegativeInteraction,
        Gunfire,
        TrapEscaped,
        BaitDanger,
        WarningLearned,
        QuietObservation,
        Frightened
    }
    public enum WildlifeIdeologyEvent { Study, Tend, Protect, SuccessfulCall, HuntKill, NotableKill, Folklore, ProtectedDeath }
    public enum WildlifeCeremonyKind { FirstHunt, MigrationWatch, Memorial, CeremonialRelease }
    public enum WildlifeLegendObjective { Study, Protect, Track, Hunt }
    public enum AnimalSocialMemoryKind
    {
        MateBond,
        ParentCare,
        Taught,
        ProtectedBy,
        PlayedTogether,
        TravelledTogether,
        SharedShelter,
        Reunited,
        Rivalry,
        Fought,
        PackMemberKilled
    }

    public sealed class AnimalColonistMemory : IExposable
    {
        public Pawn animal;
        public Pawn colonist;
        public float trust;
        public float fear;
        public float hostility;
        public int positiveEvents;
        public int negativeEvents;
        public int huntingEncounters;
        public int rangedEncounters;
        public int trapEncounters;
        public int lastTick;
        public string lastEvent;
        public List<AnimalMemoryEvent> events = new List<AnimalMemoryEvent>();

        public void ExposeData()
        {
            Scribe_References.Look(ref animal, "animal");
            Scribe_References.Look(ref colonist, "colonist");
            Scribe_Values.Look(ref trust, "trust");
            Scribe_Values.Look(ref fear, "fear");
            Scribe_Values.Look(ref hostility, "hostility");
            Scribe_Values.Look(ref positiveEvents, "positiveEvents");
            Scribe_Values.Look(ref negativeEvents, "negativeEvents");
            Scribe_Values.Look(ref huntingEncounters, "huntingEncounters");
            Scribe_Values.Look(ref rangedEncounters, "rangedEncounters");
            Scribe_Values.Look(ref trapEncounters, "trapEncounters");
            Scribe_Values.Look(ref lastTick, "lastTick");
            Scribe_Values.Look(ref lastEvent, "lastEvent");
            Scribe_Collections.Look(ref events, "events", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) events ??= new List<AnimalMemoryEvent>();
        }
    }

    public sealed class AnimalMemoryEvent : IExposable
    {
        public AnimalMemoryKind kind;
        public int tick;
        public float strength;
        public Pawn cause;

        public void ExposeData()
        {
            Scribe_Values.Look(ref kind, "kind");
            Scribe_Values.Look(ref tick, "tick");
            Scribe_Values.Look(ref strength, "strength");
            Scribe_References.Look(ref cause, "cause");
        }
    }

    public sealed class AnimalSocialMemoryEvent : IExposable
    {
        public AnimalSocialMemoryKind kind;
        public int tick;
        public float strength;
        public Pawn cause;

        public void ExposeData()
        {
            Scribe_Values.Look(ref kind, "kind");
            Scribe_Values.Look(ref tick, "tick");
            Scribe_Values.Look(ref strength, "strength");
            Scribe_References.Look(ref cause, "cause");
        }
    }

    public sealed class AnimalSocialMemory : IExposable
    {
        public Pawn animal;
        public Pawn otherAnimal;
        public float bond;
        public float fear;
        public float rivalry;
        public int positiveEvents;
        public int negativeEvents;
        public int lastTick;
        public string lastEvent;
        public List<AnimalSocialMemoryEvent> events = new List<AnimalSocialMemoryEvent>();

        public void ExposeData()
        {
            Scribe_References.Look(ref animal, "animal");
            Scribe_References.Look(ref otherAnimal, "otherAnimal");
            Scribe_Values.Look(ref bond, "bond");
            Scribe_Values.Look(ref fear, "fear");
            Scribe_Values.Look(ref rivalry, "rivalry");
            Scribe_Values.Look(ref positiveEvents, "positiveEvents");
            Scribe_Values.Look(ref negativeEvents, "negativeEvents");
            Scribe_Values.Look(ref lastTick, "lastTick");
            Scribe_Values.Look(ref lastEvent, "lastEvent");
            Scribe_Collections.Look(ref events, "events", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                events ??= new List<AnimalSocialMemoryEvent>();
        }
    }

    public sealed class WildlifeFolkloreRecord : IExposable
    {
        public Pawn animal;
        public ThingDef species;
        public string title;
        public string story;
        public int createdTick;
        public int retellings;
        public bool positive;
        public int outsideTellings;
        public int reach;

        public void ExposeData()
        {
            Scribe_References.Look(ref animal, "animal");
            Scribe_Defs.Look(ref species, "species");
            Scribe_Values.Look(ref title, "title");
            Scribe_Values.Look(ref story, "story");
            Scribe_Values.Look(ref createdTick, "createdTick");
            Scribe_Values.Look(ref retellings, "retellings");
            Scribe_Values.Look(ref positive, "positive");
            Scribe_Values.Look(ref outsideTellings, "outsideTellings");
            Scribe_Values.Look(ref reach, "reach");
        }
    }

    public sealed class WildlifeLegendQuestRecord : IExposable
    {
        public Pawn animal;
        public ThingDef species;
        public string title;
        public WildlifeLegendObjective objective;
        public int startedTick;
        public int expiresTick;
        public int baselineStudies;

        public void ExposeData()
        {
            Scribe_References.Look(ref animal, "animal");
            Scribe_Defs.Look(ref species, "species");
            Scribe_Values.Look(ref title, "title");
            Scribe_Values.Look(ref objective, "objective");
            Scribe_Values.Look(ref startedTick, "startedTick");
            Scribe_Values.Look(ref expiresTick, "expiresTick");
            Scribe_Values.Look(ref baselineStudies, "baselineStudies");
        }
    }

    public sealed class WildlifeMemoryMapComponent : MapComponent
    {
        private List<AnimalColonistMemory> memories = new List<AnimalColonistMemory>();
        private List<AnimalSocialMemory> socialMemories = new List<AnimalSocialMemory>();
        private List<WildlifeFolkloreRecord> folklore = new List<WildlifeFolkloreRecord>();
        private Dictionary<int, AnimalColonistMemory> cache = new Dictionary<int, AnimalColonistMemory>();
        private Dictionary<long, AnimalSocialMemory> socialCache =
            new Dictionary<long, AnimalSocialMemory>();
        private Dictionary<Pawn, List<AnimalSocialMemory>> socialByAnimal =
            new Dictionary<Pawn, List<AnimalSocialMemory>>();
        private int nextFolkloreTick;
        private int nextMemoryTick;
        private int nextLegendQuestTick;
        private int lastCeremonyTick = -600000;
        private WildlifeLegendQuestRecord legendQuest;
        private bool ceremonyGathering;
        private WildlifeCeremonyKind pendingCeremony;
        private Pawn pendingRelease;
        private List<Pawn> pendingParticipants = new List<Pawn>();
        private List<Pawn> passionAwarded = new List<Pawn>();
        private List<Pawn> traitAwarded = new List<Pawn>();

        public WildlifeMemoryMapComponent(Map map) : base(map) { }
        public IReadOnlyList<AnimalColonistMemory> Memories => memories;
        public IReadOnlyList<AnimalSocialMemory> SocialMemories => socialMemories;
        public IReadOnlyList<WildlifeFolkloreRecord> Folklore => folklore;
        public WildlifeLegendQuestRecord LegendQuest => legendQuest;
        public int CeremonyCooldownTicks => Mathf.Max(0, lastCeremonyTick + 600000 - Find.TickManager.TicksGame);
        public bool CeremonyGathering => ceremonyGathering;

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref memories, "wildlifeIndividualMemories", LookMode.Deep);
            Scribe_Collections.Look(ref socialMemories, "wildlifeSocialMemories", LookMode.Deep);
            Scribe_Collections.Look(ref folklore, "wildlifeFolklore", LookMode.Deep);
            Scribe_Values.Look(ref nextFolkloreTick, "nextWildlifeFolkloreTick");
            Scribe_Values.Look(ref nextMemoryTick, "nextWildlifeMemoryTick");
            Scribe_Values.Look(ref nextLegendQuestTick, "nextWildlifeLegendQuestTick");
            Scribe_Values.Look(ref lastCeremonyTick, "lastWildlifeCeremonyTick", -600000);
            Scribe_Deep.Look(ref legendQuest, "wildlifeLegendQuest");
            Scribe_Values.Look(ref ceremonyGathering, "wildlifeCeremonyGathering");
            Scribe_Values.Look(ref pendingCeremony, "pendingWildlifeCeremony");
            Scribe_References.Look(ref pendingRelease, "pendingWildlifeRelease");
            Scribe_Collections.Look(ref pendingParticipants, "pendingWildlifeParticipants", LookMode.Reference);
            Scribe_Collections.Look(ref passionAwarded, "wildlifePassionAwarded", LookMode.Reference);
            Scribe_Collections.Look(ref traitAwarded, "wildlifeTraitAwarded", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                memories = memories?.Where(value => value?.animal != null &&
                    (value.colonist != null || value.events?.Any(entry => entry?.kind == AnimalMemoryKind.Frightened) == true)).ToList() ??
                    new List<AnimalColonistMemory>();
                socialMemories = socialMemories?.Where(value => value?.animal != null &&
                    value.otherAnimal != null && value.animal != value.otherAnimal).ToList() ??
                    new List<AnimalSocialMemory>();
                folklore ??= new List<WildlifeFolkloreRecord>();
                pendingParticipants ??= new List<Pawn>();
                passionAwarded ??= new List<Pawn>();
                traitAwarded ??= new List<Pawn>();
                RebuildCache();
                RebuildSocialCache();
            }
        }

        public override void MapComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (HerdsMod.Settings?.enableAnimalMemory == true && now >= nextMemoryTick)
            {
                nextMemoryTick = now + 60000;
                DecayMemories(now);
            }
            if (HerdsMod.Settings?.enableWildlifeFolklore != true) return;
            if (now < nextFolkloreTick) return;
            nextFolkloreTick = now + 60000;
            if (HerdsMod.Settings.enableFolkloreRetelling) RetellStory();
            if (ceremonyGathering && !pendingParticipants.Any(pawn =>
                pawn?.CurJobDef == HerdsDefOf.Herds_WildlifeCeremonyGather))
            {
                ceremonyGathering = false;
                pendingRelease = null;
                pendingParticipants.Clear();
            }
            if (HerdsMod.Settings.enableLegendSpread) SpreadLegends();
            if (HerdsMod.Settings.enableFolkloreDisplays) InspireAtDisplays();
            if (HerdsMod.Settings.enableWildlifeLearning) DevelopWildlifeLearners();
            UpdateLegendQuest(now);
        }

        public override void MapComponentDraw()
        {
            if (!Prefs.DevMode || !WildlifeDevMaster.CompleteOverlayEnabled ||
                HerdsMod.Settings?.enableAnimalMemory != true || Find.CurrentMap != map) return;
            foreach (IGrouping<Pawn, AnimalColonistMemory> group in memories
                .Where(value => value?.animal?.Spawned == true).GroupBy(value => value.animal))
            {
                AnimalColonistMemory strongest = group.OrderByDescending(value =>
                    Mathf.Max(value.trust, Mathf.Max(value.fear, value.hostility))).First();
                Color color = strongest.trust >= strongest.fear && strongest.trust >= strongest.hostility
                    ? new Color(0.35f, 0.9f, 0.45f)
                    : strongest.hostility >= strongest.fear ? new Color(1f, 0.28f, 0.2f) : new Color(1f, 0.72f, 0.18f);
                GenDraw.DrawRadiusRing(group.Key.Position, 0.75f +
                    Mathf.Max(strongest.trust, Mathf.Max(strongest.fear, strongest.hostility)), color);
            }
            foreach (AnimalSocialMemory social in socialMemories.Where(value =>
                value?.animal?.Spawned == true && value.otherAnimal?.Spawned == true)
                .OrderByDescending(SocialStrength).Take(24))
            {
                Color color = SocialColor(social);
                GenDraw.DrawLineBetween(social.animal.Position.ToVector3Shifted(),
                    social.otherAnimal.Position.ToVector3Shifted(),
                    social.rivalry > social.bond ? SimpleColor.Red : SimpleColor.Green);
                GenDraw.DrawRadiusRing(social.otherAnimal.Position, 0.5f, color);
            }
        }

        private static int Key(Pawn animal, Pawn colonist) =>
            Gen.HashCombineInt(animal?.thingIDNumber ?? 0, colonist?.thingIDNumber ?? 0);

        private static long SocialKey(Pawn animal, Pawn otherAnimal) =>
            ((long)(uint)(animal?.thingIDNumber ?? 0) << 32) |
            (uint)(otherAnimal?.thingIDNumber ?? 0);

        private void RebuildCache()
        {
            cache.Clear();
            for (int i = 0; i < memories.Count; i++) cache[Key(memories[i].animal, memories[i].colonist)] = memories[i];
        }

        private void RebuildSocialCache()
        {
            socialCache.Clear();
            socialByAnimal.Clear();
            for (int i = 0; i < socialMemories.Count; i++)
            {
                socialCache[SocialKey(socialMemories[i].animal,
                    socialMemories[i].otherAnimal)] = socialMemories[i];
                AddSocialBucket(socialMemories[i]);
            }
            foreach (List<AnimalSocialMemory> values in socialByAnimal.Values)
                values.Sort((left, right) => SocialStrength(right)
                    .CompareTo(SocialStrength(left)));
        }

        private void AddSocialBucket(AnimalSocialMemory value)
        {
            if (value?.animal == null) return;
            if (!socialByAnimal.TryGetValue(value.animal,
                out List<AnimalSocialMemory> values))
            {
                values = new List<AnimalSocialMemory>();
                socialByAnimal[value.animal] = values;
            }
            values.Add(value);
        }

        public AnimalColonistMemory For(Pawn animal, Pawn colonist, bool create = false)
        {
            if (animal == null) return null;
            cache.TryGetValue(Key(animal, colonist), out AnimalColonistMemory value);
            if (value == null && create)
            {
                value = new AnimalColonistMemory { animal = animal, colonist = colonist };
                memories.Add(value);
                cache[Key(animal, colonist)] = value;
            }
            return value;
        }

        public AnimalSocialMemory ForSocial(Pawn animal, Pawn otherAnimal, bool create = false)
        {
            if (animal == null || otherAnimal == null || animal == otherAnimal) return null;
            socialCache.TryGetValue(SocialKey(animal, otherAnimal), out AnimalSocialMemory value);
            if (value == null && create)
            {
                value = new AnimalSocialMemory { animal = animal, otherAnimal = otherAnimal };
                socialMemories.Add(value);
                socialCache[SocialKey(animal, otherAnimal)] = value;
                AddSocialBucket(value);
            }
            return value;
        }

        public IReadOnlyList<AnimalSocialMemory> SocialFor(Pawn animal) =>
            animal != null && socialByAnimal.TryGetValue(animal,
                out List<AnimalSocialMemory> values)
                ? values : System.Array.Empty<AnimalSocialMemory>();

        public void RememberAnimal(Pawn animal, Pawn otherAnimal,
            AnimalSocialMemoryKind kind, float strength = 1f, bool reciprocal = true, Pawn cause = null)
        {
            if (HerdsMod.Settings?.enableAnimalMemory != true ||
                HerdsMod.Settings.enableAnimalSocialMemory != true ||
                animal?.RaceProps?.Animal != true || otherAnimal?.RaceProps?.Animal != true ||
                animal == otherAnimal) return;
            AnimalSocialMemory value = ForSocial(animal, otherAnimal, true);
            int now = Find.TickManager.TicksGame;
            int cooldown = kind == AnimalSocialMemoryKind.Fought ? 7500 : 30000;
            AnimalSocialMemoryEvent previous = value.events.FirstOrDefault(entry =>
                entry != null && entry.kind == kind);
            if (previous != null && now - previous.tick < cooldown) return;
            float amount = Mathf.Clamp(strength, 0.1f, 2f);
            bool positive = kind != AnimalSocialMemoryKind.Rivalry &&
                kind != AnimalSocialMemoryKind.Fought && kind != AnimalSocialMemoryKind.PackMemberKilled;
            if (positive)
            {
                float gain = kind == AnimalSocialMemoryKind.MateBond ? 0.24f :
                    kind == AnimalSocialMemoryKind.ParentCare ? 0.20f :
                    kind == AnimalSocialMemoryKind.ProtectedBy ? 0.18f : 0.11f;
                value.bond = Mathf.Clamp01(value.bond + amount * gain);
                value.fear = Mathf.Clamp01(value.fear - amount * 0.06f);
                value.rivalry = Mathf.Clamp01(value.rivalry - amount * 0.05f);
                value.positiveEvents++;
            }
            else
            {
                value.rivalry = Mathf.Clamp01(value.rivalry + amount *
                    (kind == AnimalSocialMemoryKind.Fought ? 0.20f : 0.13f));
                value.fear = Mathf.Clamp01(value.fear + amount * 0.10f);
                value.bond = Mathf.Clamp01(value.bond - amount * 0.10f);
                value.negativeEvents++;
            }
            value.lastTick = now;
            value.lastEvent = SocialEventLabel(kind);
            value.events.Insert(0, new AnimalSocialMemoryEvent
            {
                kind = kind,
                tick = now,
                strength = amount,
                cause = cause
            });
            if (value.events.Count > 24)
                value.events.RemoveRange(24, value.events.Count - 24);
            if (socialByAnimal.TryGetValue(animal,
                out List<AnimalSocialMemory> relationships))
                relationships.Sort((left, right) => SocialStrength(right)
                    .CompareTo(SocialStrength(left)));
            if (WildlifeTestLog.Enabled)
                WildlifeTestLog.Write("AnimalSocialMemory", "other=" +
                    otherAnimal.thingIDNumber + " event=" + kind + " bond=" +
                    value.bond.ToString("0.00") + " fear=" + value.fear.ToString("0.00") +
                    " rivalry=" + value.rivalry.ToString("0.00"), animal, otherAnimal);
            if (reciprocal)
                RememberAnimal(otherAnimal, animal, kind, amount * 0.85f, false);
        }

        public void RememberPackMemberKilled(Pawn deadAnimal, Pawn killer)
        {
            if (HerdsMod.Settings?.enableAnimalMemory != true ||
                HerdsMod.Settings.enableAnimalSocialMemory != true ||
                deadAnimal?.RaceProps?.Animal != true) return;
            HerdSnapshot group = map.GetComponent<HerdMapComponent>()?.HerdFor(deadAnimal);
            IReadOnlyList<Pawn> members = group?.profile?.IsSocial == true
                ? group.members : PackMembersFor(deadAnimal);
            if (members == null || members.Count < 2) return;
            for (int i = 0; i < members.Count; i++)
            {
                Pawn member = members[i];
                if (member == null || member == deadAnimal || member.Dead ||
                    member.RaceProps?.Animal != true) continue;
                RememberAnimal(member, deadAnimal, AnimalSocialMemoryKind.PackMemberKilled,
                    killer == null ? 0.9f : 1.1f, false, killer);
            }
        }

        private static IReadOnlyList<Pawn> PackMembersFor(Pawn animal)
        {
            try
            {
                MapComponent packs = animal?.Map?.components?.FirstOrDefault(component =>
                    component?.GetType().FullName == "Packs.PackMapComponent");
                if (packs == null) return null;
                MethodInfo packFor = packs.GetType().GetMethod("PackFor", BindingFlags.Instance |
                    BindingFlags.Public | BindingFlags.NonPublic);
                object snapshot = packFor?.Invoke(packs, new object[] { animal });
                return snapshot?.GetType().GetField("members", BindingFlags.Instance |
                    BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(snapshot) as IReadOnlyList<Pawn>;
            }
            catch
            {
                return null;
            }
        }

        public float SocialAffinity(Pawn animal, Pawn otherAnimal)
        {
            if (HerdsMod.Settings?.enableAnimalSocialMemory != true) return 0f;
            AnimalSocialMemory value = ForSocial(animal, otherAnimal);
            return value == null ? 0f : Mathf.Clamp(value.bond - value.rivalry -
                value.fear * 0.35f, -1f, 1f);
        }

        public string SocialRelationship(Pawn animal, Pawn otherAnimal)
        {
            AnimalSocialMemory value = ForSocial(animal, otherAnimal);
            if (value == null) return "unfamiliar";
            if (value.rivalry >= 0.55f) return "rival";
            if (value.fear >= 0.4f && value.bond < 0.25f) return "fearful";
            if (value.bond >= 0.72f) return "devoted";
            if (value.bond >= 0.42f) return "bonded";
            if (value.bond >= 0.18f) return "familiar";
            return "wary";
        }

        public IEnumerable<Pawn> RememberedPartners(Pawn animal) =>
            SocialFor(animal).Select(value => value.otherAnimal).Where(value => value != null);

        public void Remember(Pawn animal, Pawn colonist, AnimalMemoryKind kind, float strength = 1f)
        {
            RememberInternal(animal, colonist, kind, strength, true);
        }

        public void RememberFrightened(Pawn animal, Pawn cause, float strength = 1f)
        {
            RememberInternal(animal, cause, AnimalMemoryKind.Frightened, strength, false);
        }

        private void RememberInternal(Pawn animal, Pawn colonist, AnimalMemoryKind kind, float strength, bool share)
        {
            bool frightened = kind == AnimalMemoryKind.Frightened;
            if (HerdsMod.Settings?.enableAnimalMemory != true || animal?.RaceProps?.Animal != true ||
                (!frightened && (colonist?.Faction != Faction.OfPlayer || !colonist.RaceProps.Humanlike))) return;
            AnimalColonistMemory value = For(animal, colonist, true);
            int now = Find.TickManager.TicksGame;
            AnimalMemoryEvent recentFear = frightened ? value.events.FirstOrDefault(entry =>
                entry?.kind == kind && now - entry.tick < 1500) : null;
            if (recentFear != null)
            {
                if (recentFear.cause == null && colonist != null) recentFear.cause = colonist;
                return;
            }
            float amount = Mathf.Clamp(strength, 0.1f, 2f);
            bool positive = kind == AnimalMemoryKind.Studied || kind == AnimalMemoryKind.Called ||
                kind == AnimalMemoryKind.Tended || kind == AnimalMemoryKind.Protected ||
                kind == AnimalMemoryKind.Nuzzled || kind == AnimalMemoryKind.PositiveInteraction ||
                kind == AnimalMemoryKind.QuietObservation;
            amount = Mathf.Clamp(amount * WildlifeLifeUtility.MemoryFactor(animal, positive), 0.1f, 2f);
            if (positive)
            {
                value.trust = Mathf.Clamp01(value.trust + amount * (kind == AnimalMemoryKind.Tended ? 0.26f : 0.12f));
                value.fear = Mathf.Clamp01(value.fear - amount * 0.08f);
                value.positiveEvents++;
            }
            else
            {
                value.fear = Mathf.Clamp01(value.fear + amount * (frightened ? 0.24f : 0.18f));
                value.hostility = Mathf.Clamp01(value.hostility + amount * (kind == AnimalMemoryKind.KinKilled ? 0.24f : frightened ? 0.07f : 0.11f));
                value.trust = Mathf.Clamp01(value.trust - amount * 0.18f);
                value.negativeEvents++;
            }
            if (kind == AnimalMemoryKind.Hunted || kind == AnimalMemoryKind.Wounded)
                value.huntingEncounters++;
            if (kind == AnimalMemoryKind.Gunfire) value.rangedEncounters++;
            if (kind == AnimalMemoryKind.TrapEscaped) value.trapEncounters++;
            value.lastTick = now;
            value.lastEvent = EventLabel(kind);
            value.events.Insert(0, new AnimalMemoryEvent
            {
                kind = kind,
                tick = value.lastTick,
                strength = amount,
                cause = frightened ? colonist : null
            });
            if (value.events.Count > 24) value.events.RemoveRange(24, value.events.Count - 24);
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("AnimalMemory",
                "event=" + kind + " trust=" + value.trust.ToString("0.00") + " fear=" + value.fear.ToString("0.00") +
                " hostility=" + value.hostility.ToString("0.00") + " cause=" +
                (colonist?.thingIDNumber.ToString() ?? "unknown"), animal);
            if (share && !positive && !frightened) ShareWarning(animal, colonist, kind, amount);
        }

        public float AvoidanceFactor(Pawn animal, Pawn colonist)
        {
            if (HerdsMod.Settings?.enableAnimalMemory != true) return 1f;
            AnimalColonistMemory value = For(animal, colonist);
            if (value == null) return 1f;
            float learned = Mathf.Min(0.35f, value.huntingEncounters * 0.035f +
                value.rangedEncounters * 0.025f + value.trapEncounters * 0.06f);
            return Mathf.Clamp(1f - value.trust * 0.38f + value.fear * 0.42f +
                value.hostility * 0.18f + learned, 0.6f, 1.8f);
        }

        public float TrustFor(Pawn animal, Pawn colonist) => For(animal, colonist)?.trust ?? 0f;
        public float FearFor(Pawn animal, Pawn colonist) => For(animal, colonist)?.fear ?? 0f;
        public float HostilityFor(Pawn animal, Pawn colonist) => For(animal, colonist)?.hostility ?? 0f;

        public string Relationship(Pawn animal, Pawn colonist)
        {
            AnimalColonistMemory value = For(animal, colonist);
            return value == null ? "unfamiliar" : Disposition(value);
        }

        private void ShareWarning(Pawn animal, Pawn colonist, AnimalMemoryKind kind, float amount)
        {
            HerdSnapshot group = map.GetComponent<HerdMapComponent>()?.HerdFor(animal);
            if (group?.members == null || group.members.Count <= 1) return;
            int shared = 0;
            for (int i = 0; i < group.members.Count && shared < 12; i++)
            {
                Pawn listener = group.members[i];
                if (listener == animal || listener?.Spawned != true) continue;
                float teaching = listener.ageTracker?.Adult == false ? 0.42f : 0.28f;
                RememberInternal(listener, colonist, AnimalMemoryKind.WarningLearned,
                    Mathf.Clamp(amount * teaching, 0.1f, 0.65f), false);
                shared++;
            }
            if (shared > 0) WildlifeTestLog.Count("memory.warningsShared");
        }

        private void DecayMemories(int now)
        {
            for (int i = memories.Count - 1; i >= 0; i--)
            {
                AnimalColonistMemory value = memories[i];
                if (value?.animal == null || value.colonist == null &&
                    !value.events.Any(entry => entry?.kind == AnimalMemoryKind.Frightened))
                {
                    memories.RemoveAt(i);
                    continue;
                }
                bool lasting = value.events.Any(entry => entry?.kind == AnimalMemoryKind.KinKilled ||
                    entry?.kind == AnimalMemoryKind.Hunted || entry?.kind == AnimalMemoryKind.Protected ||
                    entry?.kind == AnimalMemoryKind.Tended || entry?.kind == AnimalMemoryKind.Nuzzled);
                float retention = lasting ? 0.992f : 0.965f;
                value.trust *= retention;
                value.fear *= retention;
                value.hostility *= retention;
                value.events.RemoveAll(entry => entry == null || now - entry.tick > MemoryLifetime(entry.kind, lasting));
                if (value.events.Count == 0 && Mathf.Max(value.trust,
                    Mathf.Max(value.fear, value.hostility)) < 0.025f)
                    memories.RemoveAt(i);
            }
            RebuildCache();
            for (int i = socialMemories.Count - 1; i >= 0; i--)
            {
                AnimalSocialMemory value = socialMemories[i];
                if (value?.animal == null || value.otherAnimal == null)
                {
                    socialMemories.RemoveAt(i);
                    continue;
                }
                bool lasting = value.events.Any(entry =>
                    entry?.kind == AnimalSocialMemoryKind.MateBond ||
                    entry?.kind == AnimalSocialMemoryKind.ParentCare ||
                    entry?.kind == AnimalSocialMemoryKind.ProtectedBy ||
                    entry?.kind == AnimalSocialMemoryKind.Fought ||
                    entry?.kind == AnimalSocialMemoryKind.PackMemberKilled);
                float retention = lasting ? 0.994f : 0.975f;
                value.bond *= retention;
                value.fear *= lasting ? 0.986f : 0.96f;
                value.rivalry *= lasting ? 0.991f : 0.965f;
                value.events.RemoveAll(entry => entry == null || now - entry.tick > SocialMemoryLifetime(entry.kind, lasting));
                if (value.events.Count == 0 && SocialStrength(value) < 0.025f)
                    socialMemories.RemoveAt(i);
            }
            RebuildSocialCache();
        }

        private static int MemoryLifetime(AnimalMemoryKind kind, bool lasting)
        {
            if (kind == AnimalMemoryKind.Frightened)
                return Mathf.Max(60000, HerdsMod.Settings?.frightenedMemoryLifetimeTicks ?? 900000);
            return lasting ? 3600000 : 900000;
        }

        private static int SocialMemoryLifetime(AnimalSocialMemoryKind kind, bool lasting)
        {
            if (kind == AnimalSocialMemoryKind.PackMemberKilled)
                return Mathf.Max(600000, HerdsMod.Settings?.packMemberKilledMemoryLifetimeTicks ?? 3600000);
            return lasting ? 3600000 : 1200000;
        }

        public string Summary(Pawn animal)
        {
            List<AnimalColonistMemory> values = memories.Where(value => value?.animal == animal)
                .OrderByDescending(value => value.lastTick).ToList();
            if (values.Count == 0) return "No lasting memories of people or threats.";
            return string.Join("\n", values.Take(5).Select(value =>
            {
                AnimalMemoryEvent latest = value.events.Where(entry => entry != null)
                    .OrderByDescending(entry => entry.tick).FirstOrDefault();
                Pawn cause = latest?.cause ?? value.colonist;
                return (cause?.LabelShortCap ?? "Unknown cause") + ": " + Disposition(value) +
                    " — remembers " + (value.lastEvent ?? "an event").ToLowerInvariant() + ".";
            }));
        }

        public void RecordFolklore(string title, string story, Pawn animal = null, bool positive = true,
            IEnumerable<Pawn> involvedPawns = null, IntVec3? location = null, ThingDef species = null)
        {
            if (HerdsMod.Settings?.enableWildlifeFolklore != true || title.NullOrEmpty() || story.NullOrEmpty()) return;
            ThingDef subjectSpecies = animal?.def ?? species;
            string narrative = ColonyStoryNarrative(story, animal, subjectSpecies,
                involvedPawns, location);
            if (folklore.Any(value => value.title == title && value.story == narrative)) return;
            folklore.Insert(0, new WildlifeFolkloreRecord
            {
                title = title,
                story = narrative,
                animal = animal,
                species = subjectSpecies,
                createdTick = Find.TickManager.TicksGame,
                positive = positive
            });
            if (folklore.Count > 40) folklore.RemoveRange(40, folklore.Count - 40);
            WildlifeIdeologyUtility.Notify(map, WildlifeIdeologyEvent.Folklore, animal);
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("Folklore", "title=" + title + " positive=" + positive, animal);
        }

        internal string ColonyStoryNarrative(string story, Pawn animal, ThingDef species,
            IEnumerable<Pawn> involvedPawns, IntVec3? location)
        {
            List<Pawn> participants = involvedPawns?.Where(value => value != null).Distinct().ToList() ??
                new List<Pawn>();
            if (participants.Count == 0 && animal != null)
                participants = memories.Where(value => value?.animal == animal && value.colonist != null)
                    .OrderByDescending(value => value.lastTick).Select(value => value.colonist)
                    .Distinct().Take(3).ToList();
            string animalText = animal?.LabelShortCap.ToString() ?? species?.LabelCap.ToString();
            IntVec3 cell = location ?? (animal?.SpawnedOrAnyParentSpawned == true
                ? animal.PositionHeld : IntVec3.Invalid);
            string locationText = LocationText(cell);
            return story.TrimEnd() + " " + ContextSentence(animalText,
                participants.Select(value => value.LabelShortCap.ToString()), locationText);
        }

        private string LocationText(IntVec3 cell)
        {
            if (!cell.IsValid || map == null || !cell.InBounds(map)) return null;
            Zone zone = map.zoneManager?.ZoneAt(cell);
            if (zone != null && !zone.label.NullOrEmpty()) return zone.label;
            Room room = cell.GetRoom(map);
            if (room?.PsychologicallyOutdoors == false && room.Role != null)
                return "the " + room.Role.label;
            return cell.GetTerrain(map)?.LabelCap.ToString();
        }

        internal static string ContextSentence(string animal, IEnumerable<string> pawns,
            string location)
        {
            List<string> names = pawns?.Where(value => !value.NullOrEmpty()).Distinct().ToList() ??
                new List<string>();
            string subject = animal.NullOrEmpty() ? "wildlife whose identity was not preserved" : animal;
            string witnesses = names.Count == 0 ? "colonists whose names were not preserved" :
                names.Count == 1 ? names[0] : string.Join(", ", names.Take(names.Count - 1)) +
                " and " + names.Last();
            string place = location.NullOrEmpty() ? "at an unrecorded place" : "at " + location;
            return "The telling remembers " + witnesses + " with " + subject + " " + place + ".";
        }

        public string DebugCreateSocialMemory()
        {
            List<Pawn> animals = map.mapPawns.AllPawnsSpawned.Where(pawn =>
                pawn.RaceProps?.Animal == true && !pawn.Dead).ToList();
            if (animals.Count < 2) return "socialMemory=needs_two_animals";
            Pawn first = animals.OrderBy(pawn => pawn.thingIDNumber).First();
            Pawn second = animals.Where(pawn => pawn != first)
                .OrderBy(pawn => pawn.def == first.def ? 0 : 1)
                .ThenBy(pawn => pawn.Position.DistanceToSquared(first.Position)).First();
            RememberAnimal(first, second, AnimalSocialMemoryKind.PlayedTogether, 1.5f);
            return "socialMemory=created first:" + first.thingIDNumber +
                " second:" + second.thingIDNumber;
        }

        public List<string> DebugOverviewLines() => new List<string>
        {
            "MEMORY records=" + memories.Count + " animals=" + memories.Select(value => value.animal).Distinct().Count() +
            " social=" + socialMemories.Count + " socialAnimals=" +
            socialMemories.Select(value => value.animal).Distinct().Count(),
            "FOLKLORE stories=" + folklore.Count + " retellings=" + folklore.Sum(value => value.retellings) +
            " quest=" + (legendQuest?.objective.ToString() ?? "none") + " ceremony=" +
            (ceremonyGathering ? pendingCeremony.ToString() : "none") + " roles=" +
            map.mapPawns.FreeColonists.Count(pawn => WildlifeRoleUtility.IsMasterHunter(pawn) ||
                WildlifeRoleUtility.IsMasterConservationist(pawn))
        };

        private void RetellStory()
        {
            WildlifeFolkloreRecord story = folklore.Where(value => value != null)
                .OrderBy(value => value.retellings).ThenByDescending(value => value.createdTick).FirstOrDefault();
            List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned.Where(pawn =>
                !pawn.Downed && pawn.Awake() && pawn.CurJobDef?.joyKind != null).ToList();
            if (story == null || colonists.Count < 2) return;
            Pawn narrator = colonists.OrderByDescending(pawn =>
                pawn.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0).First();
            Pawn listener = colonists.Where(pawn => pawn != narrator)
                .OrderBy(pawn => pawn.ageTracker?.AgeBiologicalTicks ?? long.MaxValue).First();
            if (HerdsMod.Settings.enablePhysicalWildlifeStories &&
                HerdsDefOf.Herds_RetellWildlifeStory != null)
            {
                if (map.mapPawns.FreeColonistsSpawned.Any(pawn =>
                    pawn.CurJobDef == HerdsDefOf.Herds_RetellWildlifeStory)) return;
                IntVec3 site = map.listerThings.AllThings.OfType<Building_FolkloreCairn>()
                    .FirstOrDefault(cairn => cairn.StoryAssigned)?.Position ?? narrator.Position;
                List<IntVec3> cells = GenRadial.RadialCellsAround(site, 4f, true)
                    .Where(cell => cell.InBounds(map) && cell.Standable(map)).Take(2).ToList();
                if (cells.Count < 2) return;
                Job narrate = JobMaker.MakeJob(HerdsDefOf.Herds_RetellWildlifeStory, cells[0]);
                narrate.count = 1;
                Job listen = JobMaker.MakeJob(HerdsDefOf.Herds_RetellWildlifeStory, cells[1]);
                narrator.jobs.TryTakeOrderedJob(narrate, JobTag.Misc);
                listener.jobs.TryTakeOrderedJob(listen, JobTag.Misc);
                return;
            }
            CompleteRetelling(narrator);
        }

        public void CompleteRetelling(Pawn narrator)
        {
            WildlifeFolkloreRecord story = folklore.Where(value => value != null)
                .OrderBy(value => value.retellings).ThenByDescending(value => value.createdTick).FirstOrDefault();
            Pawn listener = map.mapPawns.FreeColonistsSpawned.Where(pawn => pawn != narrator &&
                pawn.Position.InHorDistOf(narrator.Position, 10f)).OrderBy(pawn =>
                pawn.ageTracker?.AgeBiologicalTicks ?? long.MaxValue).FirstOrDefault();
            if (story == null || narrator == null || listener == null) return;
            story.retellings++;
            narrator.skills?.Learn(SkillDefOf.Social, 60f);
            if (story.species != null)
                map.GetComponent<HuntingKnowledgeMapComponent>()?.Learn(listener, story.species, 8f);
            map.GetComponent<HuntingKnowledgeMapComponent>()?.LearnBiome(listener, map.Biome, 4f);
            listener.needs?.mood?.thoughts?.memories?.TryGainMemory(HerdsDefOf.Herds_HeardWildlifeLegend, narrator);
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("FolkloreRetold",
                "title=" + story.title + " narrator=" + narrator.thingIDNumber + " listener=" + listener.thingIDNumber +
                " retellings=" + story.retellings);
        }

        private void SpreadLegends()
        {
            for (int i = 0; i < folklore.Count; i++)
            {
                WildlifeFolkloreRecord story = folklore[i];
                if (story == null || story.retellings < 4) continue;
                bool visitorPresent = map.mapPawns.AllPawnsSpawned.Any(pawn =>
                    pawn.RaceProps?.Humanlike == true && pawn.Faction != null && pawn.Faction != Faction.OfPlayer &&
                    !pawn.HostileTo(Faction.OfPlayer));
                if (visitorPresent) story.outsideTellings++;
                if (story.reach == 0 && story.retellings >= 5)
                {
                    story.reach = 1;
                    foreach (Map other in Current.Game.Maps.Where(other => other != map && other.IsPlayerHome))
                        other.GetComponent<WildlifeMemoryMapComponent>()?.ImportLegend(story);
                }
                if (story.reach < 2 && story.outsideTellings >= 3) story.reach = 2;
            }
        }

        private void ImportLegend(WildlifeFolkloreRecord source)
        {
            if (source == null || folklore.Any(value => value.title == source.title)) return;
            folklore.Add(new WildlifeFolkloreRecord
            {
                title = source.title,
                story = source.story,
                species = source.species,
                positive = source.positive,
                createdTick = Find.TickManager.TicksGame,
                retellings = 1,
                reach = 1
            });
        }

        private void InspireAtDisplays()
        {
            List<Building_FolkloreCairn> displays = map.listerThings.AllThings
                .OfType<Building_FolkloreCairn>().Where(building => building.StoryAssigned).ToList();
            if (displays.Count == 0) return;
            foreach (Pawn colonist in map.mapPawns.FreeColonistsSpawned)
                if (colonist.needs?.mood != null && displays.Any(display =>
                    colonist.Position.InHorDistOf(display.Position, 7f)))
                    colonist.needs.mood.thoughts.memories.TryGainMemory(HerdsDefOf.Herds_InspiredByWildlifeMemorial);
        }

        private void UpdateLegendQuest(int now)
        {
            if (!HerdsMod.Settings.enableLegendQuests)
            {
                legendQuest = null;
                return;
            }
            if (legendQuest != null)
            {
                if (LegendQuestComplete()) ResolveLegendQuest(true);
                else if (now >= legendQuest.expiresTick) ResolveLegendQuest(false);
                return;
            }
            if (nextLegendQuestTick == 0) nextLegendQuestTick = now + 180000;
            if (now < nextLegendQuestTick) return;
            nextLegendQuestTick = now + Rand.Range(240000, 420000);
            NotableAnimalRecord notable = map.GetComponent<NotableWildlifeMapComponent>()?.Records
                .Where(value => value?.animal != null && !value.animal.Dead &&
                    (value.culturalStatus != WildlifeCulturalStatus.Unremarked || value.escapes + value.studies >= 2))
                .RandomElementWithFallback();
            if (notable == null) return;
            WildlifeLegendObjective objective = notable.culturalStatus == WildlifeCulturalStatus.Sacred ||
                notable.culturalStatus == WildlifeCulturalStatus.Beloved
                    ? (Rand.Bool ? WildlifeLegendObjective.Study : WildlifeLegendObjective.Protect)
                    : notable.culturalStatus == WildlifeCulturalStatus.Feared
                        ? WildlifeLegendObjective.Hunt
                        : notable.intent == NotableAnimalIntent.Protect
                            ? WildlifeLegendObjective.Study : notable.animal.Spawned
                                ? (WildlifeLegendObjective)Rand.RangeInclusive(0, 3) : WildlifeLegendObjective.Track;
            legendQuest = new WildlifeLegendQuestRecord
            {
                animal = notable.animal,
                species = notable.species,
                title = "Legend: " + notable.title,
                objective = objective,
                startedTick = now,
                expiresTick = now + 180000,
                baselineStudies = notable.studies
            };
            Find.LetterStack.ReceiveLetter(legendQuest.title, LegendQuestDescription(legendQuest) +
                "\n\nTrack this challenge in the Wildlife Field Journal.", LetterDefOf.NeutralEvent, notable.animal);
        }

        private bool LegendQuestComplete()
        {
            if (legendQuest == null) return false;
            NotableAnimalRecord notable = map.GetComponent<NotableWildlifeMapComponent>()?.For(legendQuest.animal);
            if (legendQuest.objective == WildlifeLegendObjective.Hunt) return legendQuest.animal == null || legendQuest.animal.Dead;
            if (notable == null) return false;
            if (legendQuest.objective == WildlifeLegendObjective.Study) return notable.studies > legendQuest.baselineStudies;
            if (legendQuest.objective == WildlifeLegendObjective.Protect) return notable.intent == NotableAnimalIntent.Protect;
            return notable.intent == NotableAnimalIntent.Track ||
                legendQuest.animal?.health?.hediffSet?.GetFirstHediffOfDef(HerdsDefOf.Herds_TrackingCollar) != null;
        }

        private void ResolveLegendQuest(bool success)
        {
            WildlifeLegendQuestRecord completed = legendQuest;
            legendQuest = null;
            string text = completed.title + (success ? " was fulfilled." : " faded without resolution.");
            Messages.Message(text, completed.animal, success ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.NeutralEvent, false);
            if (!success) return;
            foreach (Pawn colonist in map.mapPawns.FreeColonistsSpawned)
                map.GetComponent<HuntingKnowledgeMapComponent>()?.Learn(colonist, completed.species, 18f, true);
            if (completed.objective != WildlifeLegendObjective.Hunt)
            {
                Pawn inspired = map.mapPawns.FreeColonistsSpawned.OrderByDescending(pawn =>
                    WildlifeRoleUtility.IsMasterConservationist(pawn) ? 100 :
                    pawn.skills?.GetSkill(SkillDefOf.Animals)?.Level ?? 0).FirstOrDefault();
                inspired?.mindState?.inspirationHandler?.TryStartInspiration(HerdsDefOf.Herds_WildlifeInsight,
                    "Resolving a wildlife legend without killing the animal revealed a deeper pattern.", true);
                map.GetComponent<RegionalWildlifeMapComponent>()?.ApplyExpeditionImpact(completed.species, 0.75f, 0.12f);
            }
            else if (HerdsDefOf.Herds_WildlifeTrophy != null && completed.animal?.PositionHeld.IsValid == true)
            {
                Thing trophy = ThingMaker.MakeThing(HerdsDefOf.Herds_WildlifeTrophy);
                GenPlace.TryPlaceThing(trophy, completed.animal.PositionHeld, map, ThingPlaceMode.Near);
            }
            RecordFolklore(completed.title + " Fulfilled", LegendQuestDescription(completed) + " The colony answered the story.", completed.animal);
        }

        public string LegendQuestDescription(WildlifeLegendQuestRecord value)
        {
            if (value == null) return string.Empty;
            string animal = value.animal?.LabelShortCap ?? value.species?.LabelCap.ToString() ?? "the animal";
            return value.objective == WildlifeLegendObjective.Study ? "Complete a close study of " + animal + "." :
                value.objective == WildlifeLegendObjective.Protect ? "Declare " + animal + " protected." :
                value.objective == WildlifeLegendObjective.Track ? "Fit " + animal + " with a tracking collar." :
                "Bring the long hunt for " + animal + " to its conclusion.";
        }

        public void PerformCeremony(WildlifeCeremonyKind kind, Pawn releasedAnimal = null)
        {
            if (!HerdsMod.Settings.enableWildlifeCeremonies || CeremonyCooldownTicks > 0 || ceremonyGathering) return;
            List<Pawn> participants = map.mapPawns.FreeColonistsSpawned.Where(pawn =>
                !pawn.Downed && pawn.needs?.mood != null).ToList();
            if (participants.Count == 0) return;
            if (kind == WildlifeCeremonyKind.CeremonialRelease)
            {
                if (releasedAnimal?.Faction != Faction.OfPlayer || releasedAnimal.RaceProps?.Animal != true) return;
            }
            if (HerdsMod.Settings.enablePhysicalWildlifeStories &&
                HerdsDefOf.Herds_WildlifeCeremonyGather != null)
            {
                IntVec3 site = map.listerThings.AllThings.OfType<Building_FolkloreCairn>().FirstOrDefault()?.Position ??
                    map.listerThings.ThingsOfDef(HerdsDefOf.Herds_ObservationPost).FirstOrDefault()?.Position ??
                    participants[0].Position;
                List<IntVec3> cells = GenRadial.RadialCellsAround(site, 6f, true)
                    .Where(cell => cell.InBounds(map) && cell.Standable(map)).Take(participants.Count).ToList();
                if (cells.Count == 0) return;
                ceremonyGathering = true;
                pendingCeremony = kind;
                pendingRelease = releasedAnimal;
                pendingParticipants = participants.Take(cells.Count).ToList();
                for (int i = 0; i < pendingParticipants.Count; i++)
                {
                    Job job = JobMaker.MakeJob(HerdsDefOf.Herds_WildlifeCeremonyGather, cells[i]);
                    job.count = i == 0 ? 1 : 0;
                    pendingParticipants[i].jobs.TryTakeOrderedJob(job, JobTag.Misc);
                }
                return;
            }
            CompleteCeremony(kind, releasedAnimal, participants);
        }

        public void CompletePendingCeremony()
        {
            if (!ceremonyGathering) return;
            WildlifeCeremonyKind kind = pendingCeremony;
            Pawn released = pendingRelease;
            List<Pawn> participants = pendingParticipants.Where(pawn => pawn?.Spawned == true).ToList();
            ceremonyGathering = false;
            pendingRelease = null;
            pendingParticipants.Clear();
            CompleteCeremony(kind, released, participants);
        }

        private void CompleteCeremony(WildlifeCeremonyKind kind, Pawn releasedAnimal, List<Pawn> participants)
        {
            if (participants.Count == 0) return;
            if (kind == WildlifeCeremonyKind.CeremonialRelease)
                releasedAnimal?.SetFaction(null);
            lastCeremonyTick = Find.TickManager.TicksGame;
            foreach (Pawn pawn in participants)
            {
                if (ModsConfig.IdeologyActive && HerdsDefOf.Herds_WildlifeCeremony != null)
                    pawn.needs.mood.thoughts.memories.TryGainMemory(HerdsDefOf.Herds_WildlifeCeremony);
                if (releasedAnimal != null) map.GetComponent<HuntingKnowledgeMapComponent>()?.Learn(pawn, releasedAnimal.def, 12f);
                map.GetComponent<HuntingKnowledgeMapComponent>()?.LearnBiome(pawn, map.Biome, 8f);
            }
            string label = CeremonyLabel(kind);
            RecordFolklore(label, participants.Count + " colonists gathered for " + label.ToLowerInvariant() +
                (releasedAnimal == null ? "." : ", releasing " + releasedAnimal.LabelShortCap + " back to the wild."), releasedAnimal);
            Messages.Message(label + " completed.", releasedAnimal, MessageTypeDefOf.PositiveEvent, false);
        }

        private void DevelopWildlifeLearners()
        {
            HuntingKnowledgeMapComponent knowledge = map.GetComponent<HuntingKnowledgeMapComponent>();
            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
            {
                if (pawn.skills == null || pawn.story?.traits == null) continue;
                int proficiency = knowledge?.WildlifeProficiencyLevel(pawn) ?? 0;
                bool childNearStory = pawn.ageTracker?.AgeBiologicalYearsFloat < 16f &&
                    (pawn.CurJobDef == HerdsDefOf.Herds_RetellWildlifeStory ||
                     map.listerThings.AllThings.OfType<Building_FolkloreCairn>().Any(cairn =>
                         cairn.StoryAssigned && pawn.Position.InHorDistOf(cairn.Position, 8f)));
                if (childNearStory)
                {
                    pawn.skills.Learn(SkillDefOf.Animals, 80f);
                    WildlifeFolkloreRecord story = folklore.Where(value => value.species != null)
                        .RandomElementWithFallback();
                    if (story?.species != null) knowledge?.Learn(pawn, story.species, 5f);
                    knowledge?.LearnBiome(pawn, map.Biome, 3f);
                }
                if (proficiency >= 1 && !passionAwarded.Contains(pawn))
                {
                    SkillRecord skill = pawn.skills.GetSkill(SkillDefOf.Animals);
                    if (skill != null && skill.passion < Passion.Major)
                    {
                        AccessTools.Field(typeof(SkillRecord), "passion").SetValue(skill,
                            skill.passion == Passion.None ? Passion.Minor : Passion.Major);
                        Messages.Message(pawn.LabelShortCap + " developed a passion for Animals through wildlife experience.",
                            pawn, MessageTypeDefOf.PositiveEvent, false);
                    }
                    passionAwarded.Add(pawn);
                }
                if (proficiency >= 2 && !traitAwarded.Contains(pawn))
                {
                    if (!pawn.story.traits.HasTrait(HerdsDefOf.Herds_WildlifeAttuned))
                        pawn.story.traits.GainTrait(new Trait(HerdsDefOf.Herds_WildlifeAttuned));
                    traitAwarded.Add(pawn);
                }
            }
        }

        public static string CeremonyLabel(WildlifeCeremonyKind kind) =>
            kind == WildlifeCeremonyKind.FirstHunt ? "First Hunt Commemoration" :
            kind == WildlifeCeremonyKind.MigrationWatch ? "Seasonal Migration Watch" :
            kind == WildlifeCeremonyKind.Memorial ? "Wildlife Memorial" : "Ceremonial Release";

        public static string EventLabel(AnimalMemoryKind kind) =>
            kind == AnimalMemoryKind.Studied ? "being calmly studied" :
            kind == AnimalMemoryKind.Called ? "answering their call" :
            kind == AnimalMemoryKind.Tended ? "being tended" :
            kind == AnimalMemoryKind.Protected ? "being protected" :
            kind == AnimalMemoryKind.Wounded ? "being wounded" :
            kind == AnimalMemoryKind.Hunted ? "being hunted" :
            kind == AnimalMemoryKind.KinKilled ? "the death of its kin" :
            kind == AnimalMemoryKind.Nuzzled ? "nuzzling affectionately" :
            kind == AnimalMemoryKind.PositiveInteraction ? "a positive interaction" :
            kind == AnimalMemoryKind.NegativeInteraction ? "a negative interaction" :
            kind == AnimalMemoryKind.Gunfire ? "gunfire and its source" :
            kind == AnimalMemoryKind.TrapEscaped ? "escaping a trap" :
            kind == AnimalMemoryKind.BaitDanger ? "danger near bait" :
            kind == AnimalMemoryKind.QuietObservation ? "quietly watching them observe wildlife" :
            kind == AnimalMemoryKind.Frightened ? "being frightened or forced to flee" :
            "a warning learned from its group";

        public static string SocialEventLabel(AnimalSocialMemoryKind kind) =>
            kind == AnimalSocialMemoryKind.MateBond ? "forming a mate bond" :
            kind == AnimalSocialMemoryKind.ParentCare ? "receiving family care" :
            kind == AnimalSocialMemoryKind.Taught ? "learning from an older animal" :
            kind == AnimalSocialMemoryKind.ProtectedBy ? "being protected by another animal" :
            kind == AnimalSocialMemoryKind.PlayedTogether ? "playing together" :
            kind == AnimalSocialMemoryKind.TravelledTogether ? "travelling together" :
            kind == AnimalSocialMemoryKind.SharedShelter ? "sharing shelter" :
            kind == AnimalSocialMemoryKind.Reunited ? "reuniting after separation" :
            kind == AnimalSocialMemoryKind.Rivalry ? "a growing rivalry" :
            kind == AnimalSocialMemoryKind.PackMemberKilled ? "remembering the death of a pack member" :
            "fighting";

        public static float SocialStrength(AnimalSocialMemory value) =>
            value == null ? 0f : Mathf.Max(value.bond, Mathf.Max(value.fear, value.rivalry));

        public static Color SocialColor(AnimalSocialMemory value) =>
            value == null ? Color.gray :
            value.rivalry >= value.bond ? new Color(0.95f, 0.28f, 0.20f) :
            value.fear > value.bond ? new Color(1f, 0.72f, 0.18f) :
            new Color(0.34f, 0.88f, 0.48f);

        private static string Disposition(AnimalColonistMemory value)
        {
            if (value.trust > value.fear + value.hostility && value.trust >= 0.35f) return "trusting";
            if (value.hostility >= 0.55f) return "hostile";
            if (value.fear >= 0.3f) return "fearful";
            return "wary";
        }
    }

    public static class WildlifeMemoryUtility
    {
        public static void Remember(Pawn animal, Pawn colonist, AnimalMemoryKind kind, float strength = 1f) =>
            animal?.MapHeld?.GetComponent<WildlifeMemoryMapComponent>()?.Remember(animal, colonist, kind, strength);

        public static void RememberFrightened(Pawn animal, Pawn cause, float strength = 1f) =>
            animal?.MapHeld?.GetComponent<WildlifeMemoryMapComponent>()?.RememberFrightened(animal, cause, strength);

        public static float AvoidanceFactor(Pawn animal, Pawn colonist) =>
            animal?.Map?.GetComponent<WildlifeMemoryMapComponent>()?.AvoidanceFactor(animal, colonist) ?? 1f;

        public static void RememberAnimal(Pawn animal, Pawn otherAnimal,
            AnimalSocialMemoryKind kind, float strength = 1f) =>
            animal?.MapHeld?.GetComponent<WildlifeMemoryMapComponent>()?
                .RememberAnimal(animal, otherAnimal, kind, strength);

        public static void RememberPackMemberKilled(Pawn deadAnimal, Pawn killer) =>
            deadAnimal?.MapHeld?.GetComponent<WildlifeMemoryMapComponent>()?
                .RememberPackMemberKilled(deadAnimal, killer);

        public static float SocialAffinity(Pawn animal, Pawn otherAnimal) =>
            animal?.MapHeld?.GetComponent<WildlifeMemoryMapComponent>()?
                .SocialAffinity(animal, otherAnimal) ?? 0f;

        public static void Folklore(Map map, string title, string story, Pawn animal = null,
            bool positive = true, IEnumerable<Pawn> involvedPawns = null,
            IntVec3? location = null, ThingDef species = null) =>
            map?.GetComponent<WildlifeMemoryMapComponent>()?.RecordFolklore(title, story,
                animal, positive, involvedPawns, location, species);
    }

    public static class WildlifeIdeologyUtility
    {
        public static void Notify(Map map, WildlifeIdeologyEvent kind, Pawn animal = null, Pawn actor = null)
        {
            if (HerdsMod.Settings?.enableWildlifeIdeology != true || !ModsConfig.IdeologyActive || map == null) return;
            IReadOnlyList<Pawn> colonists = map.mapPawns.FreeColonists;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn pawn = colonists[i];
                if (pawn?.needs?.mood?.thoughts?.memories == null || pawn.Ideo == null) continue;
                ThoughtDef thought = null;
                if (pawn.Ideo.HasPrecept(HerdsDefOf.Herds_WildlifeEthic_Reverence))
                {
                    thought = kind == WildlifeIdeologyEvent.HuntKill || kind == WildlifeIdeologyEvent.NotableKill ||
                        kind == WildlifeIdeologyEvent.ProtectedDeath
                        ? HerdsDefOf.Herds_WildlifeDishonored : HerdsDefOf.Herds_WildlifeHarmony;
                }
                else if (pawn.Ideo.HasPrecept(HerdsDefOf.Herds_WildlifeEthic_Stewardship))
                {
                    if (kind == WildlifeIdeologyEvent.Study || kind == WildlifeIdeologyEvent.Tend ||
                        kind == WildlifeIdeologyEvent.Protect || kind == WildlifeIdeologyEvent.Folklore)
                        thought = HerdsDefOf.Herds_WildlifeHarmony;
                    else if (kind == WildlifeIdeologyEvent.NotableKill ||
                        kind == WildlifeIdeologyEvent.ProtectedDeath) thought = HerdsDefOf.Herds_WildlifeDishonored;
                }
                else if (pawn.Ideo.HasPrecept(HerdsDefOf.Herds_WildlifeEthic_Tradition) &&
                    (kind == WildlifeIdeologyEvent.HuntKill || kind == WildlifeIdeologyEvent.NotableKill))
                    thought = HerdsDefOf.Herds_TraditionalHunt;
                if (thought != null) pawn.needs.mood.thoughts.memories.TryGainMemory(thought, actor);
            }
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    public static class WildlifeAnimalMemoryDamagePatch
    {
        public static void Prefix(Thing __instance, DamageInfo dinfo)
        {
            if (HerdsMod.Settings?.enableAnimalMemory != true || __instance is not Pawn animal ||
                animal.RaceProps?.Animal != true) return;
            Pawn colonist = dinfo.Instigator as Pawn;
            if (HerdsMod.Settings.enableAnimalSocialMemory &&
                colonist?.RaceProps?.Animal == true)
            {
                float socialStrength = Mathf.Clamp(Mathf.InverseLerp(1f, 25f,
                    dinfo.Amount), 0.25f, 1.5f);
                WildlifeMemoryUtility.RememberAnimal(animal, colonist,
                    AnimalSocialMemoryKind.Fought, socialStrength);
                return;
            }
            bool trap = dinfo.Instigator?.def?.defName?.IndexOf("trap",
                StringComparison.OrdinalIgnoreCase) >= 0;
            if ((colonist == null || colonist.Faction != Faction.OfPlayer) && trap)
                colonist = animal.MapHeld?.mapPawns?.FreeColonistsSpawned
                    .OrderBy(pawn => pawn.Position.DistanceToSquared(animal.PositionHeld)).FirstOrDefault();
            if (colonist?.Faction != Faction.OfPlayer) return;
            float strength = Mathf.InverseLerp(1f, 25f, dinfo.Amount);
            AnimalMemoryKind kind = colonist.CurJobDef == JobDefOf.Hunt
                ? AnimalMemoryKind.Hunted : AnimalMemoryKind.Wounded;
            if (dinfo.Weapon?.IsRangedWeapon == true ||
                colonist.equipment?.Primary?.def?.IsRangedWeapon == true) kind = AnimalMemoryKind.Gunfire;
            else if (trap) kind = AnimalMemoryKind.TrapEscaped;
            else if (animal.MapHeld?.listerThings?.ThingsOfDef(HerdsDefOf.Herds_WildlifeBait)
                ?.Any(bait => bait.Position.DistanceToSquared(animal.PositionHeld) <= 100) == true)
                kind = AnimalMemoryKind.BaitDanger;
            WildlifeMemoryUtility.Remember(animal, colonist, kind, strength);
            animal.MapHeld?.GetComponent<HerdMapComponent>()?.LearnDanger(animal,
                animal.PositionHeld, kind == AnimalMemoryKind.TrapEscaped ? 1800000 : 600000);
        }
    }

    [HarmonyPatch]
    public static class WildlifeAnimalMemoryTendPatch
    {
        public static MethodBase TargetMethod() => AccessTools.Method(typeof(TendUtility), "DoTend",
            new[] { typeof(Pawn), typeof(Pawn), typeof(Medicine) });

        public static void Postfix(Pawn doctor, Pawn patient)
        {
            if (patient?.RaceProps?.Animal != true || doctor?.Faction != Faction.OfPlayer) return;
            WildlifeMemoryUtility.Remember(patient, doctor, AnimalMemoryKind.Tended);
            WildlifeIdeologyUtility.Notify(patient.MapHeld, WildlifeIdeologyEvent.Tend, patient, doctor);
        }
    }

    public static class WildlifeMemoryDebug
    {
        [DebugAction("Wildlife", "Give animal a memory of colonist", actionType = DebugActionType.ToolMap,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void GiveMemory()
        {
            Pawn animal = UI.MouseCell().GetFirstPawn(Find.CurrentMap);
            Pawn colonist = Find.CurrentMap?.mapPawns?.FreeColonistsSpawned?.FirstOrDefault();
            if (animal?.RaceProps?.Animal != true || colonist == null)
            {
                Messages.Message("Choose an animal; the first available colonist will be remembered.", MessageTypeDefOf.RejectInput, false);
                return;
            }
            WildlifeMemoryUtility.Remember(animal, colonist, AnimalMemoryKind.Tended, 2f);
            Messages.Message(animal.LabelShortCap + " now remembers " + colonist.LabelShortCap + ".", animal, MessageTypeDefOf.PositiveEvent, false);
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.DrawExtraSelectionOverlays))]
    public static class AnimalSocialMemorySelectionOverlayPatch
    {
        public static void Postfix(Pawn __instance)
        {
            if (HerdsMod.Settings?.enableAnimalMemory != true ||
                HerdsMod.Settings.enableAnimalSocialMemory != true ||
                __instance?.Spawned != true || __instance.RaceProps?.Animal != true ||
                Find.Selector.SingleSelectedThing != __instance) return;
            WildlifeMemoryMapComponent memory =
                __instance.Map.GetComponent<WildlifeMemoryMapComponent>();
            IReadOnlyList<AnimalSocialMemory> relationships = memory?.SocialFor(__instance);
            if (relationships == null) return;
            int drawn = 0;
            for (int i = 0; i < relationships.Count && drawn < 3; i++)
            {
                AnimalSocialMemory relationship = relationships[i];
                Pawn other = relationship?.otherAnimal;
                if (other?.Spawned != true || other.Map != __instance.Map ||
                    WildlifeMemoryMapComponent.SocialStrength(relationship) < 0.12f) continue;
                SimpleColor line = relationship.rivalry >= relationship.bond
                    ? SimpleColor.Red : relationship.fear > relationship.bond
                        ? SimpleColor.Yellow : SimpleColor.Green;
                Color color = WildlifeMemoryMapComponent.SocialColor(relationship);
                GenDraw.DrawLineBetween(__instance.Position.ToVector3Shifted(),
                    other.Position.ToVector3Shifted(), line);
                GenDraw.DrawRadiusRing(other.Position,
                    0.7f + WildlifeMemoryMapComponent.SocialStrength(relationship) * 0.5f,
                    color);
                drawn++;
            }
        }
    }
}
