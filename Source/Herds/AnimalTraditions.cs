using System;
using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;

namespace Herds
{
    public enum AnimalTraditionKind
    {
        FearedHunter,
        ThunderSticks,
        TrapWise,
        KindHands,
        SafeValley,
        EasyRanch
    }

    public sealed class AnimalTraditionRecord : IExposable
    {
        public int id;
        public AnimalTraditionKind kind;
        public ThingDef species;
        public Pawn founder;
        public Pawn subject;
        public List<Pawn> holders = new List<Pawn>();
        public string title;
        public string belief;
        public float strength;
        public float accuracy = 1f;
        public int createdTick;
        public int transmissions;
        public int generation;
        public int parentTraditionId;

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id");
            Scribe_Values.Look(ref kind, "kind");
            Scribe_Defs.Look(ref species, "species");
            Scribe_References.Look(ref founder, "founder");
            Scribe_References.Look(ref subject, "subject");
            Scribe_Collections.Look(ref holders, "holders", LookMode.Reference);
            Scribe_Values.Look(ref title, "title");
            Scribe_Values.Look(ref belief, "belief");
            Scribe_Values.Look(ref strength, "strength");
            Scribe_Values.Look(ref accuracy, "accuracy", 1f);
            Scribe_Values.Look(ref createdTick, "createdTick");
            Scribe_Values.Look(ref transmissions, "transmissions");
            Scribe_Values.Look(ref generation, "generation");
            Scribe_Values.Look(ref parentTraditionId, "parentTraditionId");
            if (Scribe.mode == LoadSaveMode.PostLoadInit) holders ??= new List<Pawn>();
        }
    }

    public sealed class AnimalTraditionMapComponent : MapComponent
    {
        private List<AnimalTraditionRecord> traditions = new List<AnimalTraditionRecord>();
        private int nextTick;
        private int lastMemoryScanTick;
        private int nextId = 1;

        public AnimalTraditionMapComponent(Map map) : base(map) { }
        public IReadOnlyList<AnimalTraditionRecord> Traditions => traditions;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref traditions, "animalTraditions", LookMode.Deep);
            Scribe_Values.Look(ref nextTick, "nextAnimalTraditionTick");
            Scribe_Values.Look(ref lastMemoryScanTick, "lastAnimalTraditionMemoryScan");
            Scribe_Values.Look(ref nextId, "nextAnimalTraditionId", 1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                traditions = traditions?.Where(value => value?.species?.race?.Animal == true)
                    .ToList() ?? new List<AnimalTraditionRecord>();
                nextId = Mathf.Max(nextId, traditions.Count == 0 ? 1 : traditions.Max(value => value.id) + 1);
            }
        }

        public override void MapComponentTick()
        {
            if (HerdsMod.Settings?.enableAnimalTraditions != true) return;
            int now = Find.TickManager.TicksGame;
            if (now < nextTick) return;
            nextTick = now + 60000;
            FormFromMemories(now);
            InheritFamilyTraditions();
            SpreadTraditions(now);
            CorrectTraditions();
            traditions.RemoveAll(value => value == null || value.species == null ||
                value.holders == null || value.holders.Count == 0);
            if (traditions.Count > 120)
                traditions = traditions.OrderByDescending(value => value.strength)
                    .ThenByDescending(value => value.transmissions).Take(120).ToList();
        }

        public override void MapComponentDraw()
        {
            if (!Prefs.DevMode || !WildlifeDevMaster.CompleteOverlayEnabled ||
                HerdsMod.Settings?.enableAnimalTraditions != true || Find.CurrentMap != map) return;
            foreach (AnimalTraditionRecord tradition in traditions)
                foreach (Pawn holder in tradition.holders.Where(pawn => pawn?.Spawned == true).Take(8))
                {
                    Color color = tradition.kind == AnimalTraditionKind.KindHands ||
                        tradition.kind == AnimalTraditionKind.SafeValley ? Color.green :
                        tradition.accuracy < 0.45f ? Color.magenta : Color.yellow;
                    GenDraw.DrawRadiusRing(holder.Position, 0.7f + tradition.strength, color);
                }
        }

        public override void MapComponentOnGUI()
        {
            base.MapComponentOnGUI();
            if (!Prefs.DevMode || !WildlifeDevMaster.CompleteOverlayEnabled ||
                HerdsMod.Settings?.enableAnimalTraditions != true || Find.CurrentMap != map) return;
            foreach (AnimalTraditionRecord tradition in traditions.Where(value => value.strength >= 0.65f))
                foreach (Pawn holder in tradition.holders.Where(pawn => pawn?.Spawned == true).Take(8))
                    GenMapUI.DrawThingLabel(holder,
                        tradition.title + (tradition.accuracy < 0.45f ? " ?" : ""));
        }

        private void FormFromMemories(int now)
        {
            WildlifeMemoryMapComponent memory = map.GetComponent<WildlifeMemoryMapComponent>();
            if (memory == null) return;
            int fromTick = lastMemoryScanTick;
            lastMemoryScanTick = now;
            foreach (AnimalColonistMemory relationship in memory.Memories)
            {
                if (relationship?.animal?.RaceProps?.Animal != true) continue;
                foreach (AnimalMemoryEvent entry in relationship.events.Where(value =>
                    value != null && value.tick > fromTick && value.tick <= now))
                {
                    AnimalTraditionKind? kind = KindFor(entry.kind);
                    Pawn subject = entry.cause ?? relationship.colonist;
                    if (!kind.HasValue || subject == null) continue;
                    float strength = Mathf.Clamp(entry.strength * 0.42f, 0.12f, 0.9f);
                    Learn(relationship.animal, kind.Value, subject, strength, 1f, null);
                }
            }
        }

        private static AnimalTraditionKind? KindFor(AnimalMemoryKind kind)
        {
            if (kind == AnimalMemoryKind.KinKilled || kind == AnimalMemoryKind.Hunted ||
                kind == AnimalMemoryKind.Wounded || kind == AnimalMemoryKind.WarningLearned)
                return AnimalTraditionKind.FearedHunter;
            if (kind == AnimalMemoryKind.Frightened) return AnimalTraditionKind.FearedHunter;
            if (kind == AnimalMemoryKind.Gunfire) return AnimalTraditionKind.ThunderSticks;
            if (kind == AnimalMemoryKind.TrapEscaped || kind == AnimalMemoryKind.BaitDanger)
                return AnimalTraditionKind.TrapWise;
            if (kind == AnimalMemoryKind.Tended || kind == AnimalMemoryKind.Protected ||
                kind == AnimalMemoryKind.Nuzzled) return AnimalTraditionKind.KindHands;
            if (kind == AnimalMemoryKind.Called || kind == AnimalMemoryKind.PositiveInteraction)
                return AnimalTraditionKind.SafeValley;
            return null;
        }

        public void Learn(Pawn animal, AnimalTraditionKind kind, Pawn subject, float amount,
            float accuracy = 1f, AnimalTraditionRecord parent = null)
        {
            if (HerdsMod.Settings?.enableAnimalTraditions != true || animal?.RaceProps?.Animal != true) return;
            AnimalTraditionRecord existing = traditions.FirstOrDefault(value =>
                value.kind == kind && value.species == animal.def && value.subject == subject &&
                value.accuracy >= 0.55f && value.holders.Contains(animal));
            if (existing != null)
            {
                existing.strength = Mathf.Clamp01(existing.strength + amount * 0.35f);
                existing.accuracy = Mathf.Clamp01(existing.accuracy + 0.04f);
                return;
            }
            AnimalTraditionRecord tradition = new AnimalTraditionRecord
            {
                id = nextId++,
                kind = kind,
                species = animal.def,
                founder = animal,
                subject = subject,
                holders = new List<Pawn> { animal },
                strength = Mathf.Clamp(amount, 0.1f, 1f),
                accuracy = Mathf.Clamp01(accuracy),
                createdTick = Find.TickManager.TicksGame,
                generation = parent == null ? 0 : parent.generation + 1,
                parentTraditionId = parent?.id ?? 0
            };
            SetWords(tradition);
            traditions.Add(tradition);
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("AnimalTradition",
                "formed=" + kind + " accuracy=" + tradition.accuracy.ToString("0.00"), animal, subject);
        }

        public void NotifyPredatorTargetsColonyAnimal(Pawn predator, Pawn prey)
        {
            if (!WildlifeSpeciesClassification.IsPredator(predator?.def) ||
                prey?.Faction != Faction.OfPlayer) return;
            Learn(predator, AnimalTraditionKind.EasyRanch, null, 0.38f, 0.85f);
        }

        private void InheritFamilyTraditions()
        {
            WildlifeRegionalStoriesMapComponent stories = map.GetComponent<WildlifeRegionalStoriesMapComponent>();
            if (stories == null) return;
            foreach (WildlifeFamilyLine line in stories.FamilyLines)
            {
                if (line?.animal == null || line.parent == null) continue;
                foreach (AnimalTraditionRecord tradition in traditions.Where(value =>
                    value.holders.Contains(line.parent) && !value.holders.Contains(line.animal)).ToList())
                    Transmit(tradition, line.animal, true);
            }
        }

        private void SpreadTraditions(int now)
        {
            foreach (AnimalTraditionRecord tradition in traditions.ToList())
            {
                tradition.holders.RemoveAll(pawn => pawn == null || pawn.Dead);
                List<Pawn> teachers = tradition.holders.Where(pawn => pawn?.Spawned == true).Take(5).ToList();
                foreach (Pawn teacher in teachers)
                {
                    Pawn listener = map.mapPawns.AllPawnsSpawned.Where(pawn =>
                        pawn != teacher && pawn.def == teacher.def && pawn.Faction == teacher.Faction &&
                        !tradition.holders.Contains(pawn) && pawn.Position.DistanceToSquared(teacher.Position) <= 225)
                        .Where(pawn => SociallyLinked(teacher, pawn))
                        .OrderBy(pawn => pawn.Position.DistanceToSquared(teacher.Position)).FirstOrDefault();
                    if (listener == null) continue;
                    float chance = listener.ageTracker?.Adult == false ? 0.52f : 0.22f;
                    if (Rand.ChanceSeeded(chance, Gen.HashCombineInt(tradition.id, now + listener.thingIDNumber)))
                        Transmit(tradition, listener, false);
                }
            }
        }

        private bool SociallyLinked(Pawn teacher, Pawn listener)
        {
            if (WildlifeSpeciesClassification.IsPredator(teacher.def)) return true;
            HerdMapComponent herds = map.GetComponent<HerdMapComponent>();
            HerdSnapshot group = herds?.HerdFor(teacher);
            return group != null && group == herds.HerdFor(listener);
        }

        private void Transmit(AnimalTraditionRecord tradition, Pawn listener, bool inherited)
        {
            tradition.transmissions++;
            float mutationChance = inherited ? 0.05f : 0.11f;
            bool mutate = Rand.ChanceSeeded(mutationChance,
                Gen.HashCombineInt(tradition.id, listener.thingIDNumber + tradition.transmissions));
            if (!mutate)
            {
                tradition.holders.Add(listener);
                tradition.strength = Mathf.Clamp01(tradition.strength + (inherited ? 0.025f : 0.012f));
                return;
            }
            Pawn changedSubject = tradition.subject;
            if (tradition.kind == AnimalTraditionKind.FearedHunter)
                changedSubject = map.mapPawns.FreeColonistsSpawned
                    .OrderBy(pawn => Mathf.Abs(pawn.thingIDNumber - listener.thingIDNumber)).FirstOrDefault();
            Learn(listener, tradition.kind, changedSubject, tradition.strength * 0.78f,
                tradition.accuracy * Rand.Range(0.38f, 0.72f), tradition);
            AnimalTraditionRecord mutation = traditions.LastOrDefault(value => value.founder == listener &&
                value.parentTraditionId == tradition.id);
            if (mutation != null)
            {
                mutation.title = "Distorted " + mutation.title;
                mutation.belief = MutatedBelief(mutation);
            }
        }

        private void CorrectTraditions()
        {
            WildlifeMemoryMapComponent memory = map.GetComponent<WildlifeMemoryMapComponent>();
            if (memory == null) return;
            foreach (AnimalTraditionRecord tradition in traditions)
            {
                if (tradition.subject == null) continue;
                float trust = tradition.holders.Where(pawn => pawn != null)
                    .Select(pawn => memory.TrustFor(pawn, tradition.subject)).DefaultIfEmpty(0f).Average();
                float fear = tradition.holders.Where(pawn => pawn != null)
                    .Select(pawn => memory.FearFor(pawn, tradition.subject)).DefaultIfEmpty(0f).Average();
                if (tradition.kind == AnimalTraditionKind.FearedHunter && trust > fear + 0.25f)
                {
                    tradition.accuracy = Mathf.Max(0f, tradition.accuracy - 0.08f);
                    tradition.strength *= 0.94f;
                }
                else if (tradition.kind == AnimalTraditionKind.KindHands && fear > trust + 0.25f)
                {
                    tradition.accuracy = Mathf.Max(0f, tradition.accuracy - 0.08f);
                    tradition.strength *= 0.94f;
                }
            }
        }

        public float AvoidanceFactor(Pawn animal, Pawn colonist)
        {
            if (HerdsMod.Settings?.enableAnimalTraditions != true || animal == null) return 1f;
            float factor = 1f;
            foreach (AnimalTraditionRecord tradition in traditions.Where(value => value.holders.Contains(animal)))
            {
                if (tradition.kind == AnimalTraditionKind.FearedHunter &&
                    (tradition.subject == null || tradition.subject == colonist))
                    factor += tradition.strength * 0.55f;
                else if (tradition.kind == AnimalTraditionKind.ThunderSticks &&
                    colonist?.equipment?.Primary?.def?.IsRangedWeapon == true)
                    factor += tradition.strength * 0.34f;
                else if (tradition.kind == AnimalTraditionKind.TrapWise)
                    factor += tradition.strength * 0.10f;
                else if (tradition.kind == AnimalTraditionKind.KindHands &&
                    (tradition.subject == null || tradition.subject == colonist))
                    factor -= tradition.strength * 0.34f;
                else if (tradition.kind == AnimalTraditionKind.SafeValley)
                    factor -= tradition.strength * 0.12f;
            }
            return Mathf.Clamp(factor, 0.55f, 1.9f);
        }

        public float PredatorHumanPreyScore(Pawn predator, Pawn human)
        {
            if (HerdsMod.Settings?.enableAnimalTraditions != true || predator == null || human == null) return 0f;
            float score = 0f;
            foreach (AnimalTraditionRecord tradition in traditions.Where(value => value.holders.Contains(predator)))
            {
                if (tradition.kind == AnimalTraditionKind.EasyRanch) score += tradition.strength * 130f;
                else if (tradition.kind == AnimalTraditionKind.FearedHunter &&
                    (tradition.subject == null || tradition.subject == human)) score -= tradition.strength * 170f;
                else if (tradition.kind == AnimalTraditionKind.ThunderSticks &&
                    human.equipment?.Primary?.def?.IsRangedWeapon == true)
                    score -= tradition.strength * 90f;
            }
            return score;
        }

        public string Summary(Pawn animal, int knowledge)
        {
            List<AnimalTraditionRecord> known = traditions.Where(value => value.holders.Contains(animal))
                .OrderByDescending(value => value.strength).ToList();
            if (known.Count == 0) return null;
            if (knowledge <= 0) return "This animal's behavior suggests a learned social tradition.";
            WildlifeMemoryMapComponent colonyMemory = map.GetComponent<WildlifeMemoryMapComponent>();
            List<WildlifeFolkloreRecord> colonyStories = colonyMemory?.Folklore
                .Where(value => value.species == animal.def).ToList() ?? new List<WildlifeFolkloreRecord>();
            return string.Join("\n", known.Take(4).Select(value =>
            {
                string text = value.title + ": " + (knowledge >= 2 ? value.belief :
                    "Behavior suggests " + KindClue(value.kind).ToLowerInvariant() + ".");
                if (knowledge >= 3)
                {
                    text += " Confidence: " + value.accuracy.ToStringPercent() + ".";
                    bool animalPositive = value.kind == AnimalTraditionKind.KindHands ||
                        value.kind == AnimalTraditionKind.SafeValley;
                    if (colonyStories.Any(story => story.positive != animalPositive))
                        text += " Colony folklore preserves a conflicting version of these events.";
                }
                return text;
            }));
        }

        public string RegionalSummary(ThingDef species, int knowledge)
        {
            List<AnimalTraditionRecord> known = traditions.Where(value => value.species == species).ToList();
            if (known.Count == 0 || knowledge <= 0) return null;
            int holders = known.SelectMany(value => value.holders).Where(pawn => pawn != null && !pawn.Dead).Distinct().Count();
            return holders + " known animal" + (holders == 1 ? "" : "s") + " carry " +
                known.Count + " tradition" + (known.Count == 1 ? "" : "s") + ".";
        }

        public void CorrectSpeciesTraditions(ThingDef species)
        {
            if (species == null) return;
            foreach (AnimalTraditionRecord tradition in traditions.Where(value => value.species == species))
            {
                if (tradition.accuracy < 0.55f)
                {
                    tradition.strength *= 0.45f;
                    tradition.belief = "Recent experience has weakened this inherited belief.";
                }
                tradition.accuracy = Mathf.Clamp01(tradition.accuracy + 0.35f);
            }
        }

        private static void SetWords(AnimalTraditionRecord tradition)
        {
            string subject = tradition.subject?.LabelShortCap ?? "the colony";
            tradition.title = tradition.kind == AnimalTraditionKind.FearedHunter ? "The " + subject + " Warning" :
                tradition.kind == AnimalTraditionKind.ThunderSticks ? "Thunder-Sticks" :
                tradition.kind == AnimalTraditionKind.TrapWise ? "The Still Teeth" :
                tradition.kind == AnimalTraditionKind.KindHands ? "Kind Hands" :
                tradition.kind == AnimalTraditionKind.SafeValley ? "The Safe Valley" : "The Easy Ranch";
            tradition.belief = tradition.kind == AnimalTraditionKind.FearedHunter ? subject + " brings pursuit and death." :
                tradition.kind == AnimalTraditionKind.ThunderSticks ? "Long human tools strike from beyond scent and claw." :
                tradition.kind == AnimalTraditionKind.TrapWise ? "Still food and narrow paths may conceal biting ground." :
                tradition.kind == AnimalTraditionKind.KindHands ? subject + " can bring healing and safety." :
                tradition.kind == AnimalTraditionKind.SafeValley ? "The colony's managed land can offer food, water, and refuge." :
                "Animals near the colony are vulnerable prey.";
        }

        private static string MutatedBelief(AnimalTraditionRecord tradition) =>
            tradition.kind == AnimalTraditionKind.FearedHunter ? "The warning has shifted to the wrong human." :
            tradition.kind == AnimalTraditionKind.ThunderSticks ? "Any sharp human noise is believed to kill at a distance." :
            tradition.kind == AnimalTraditionKind.TrapWise ? "Harmless paths and food are also treated as traps." :
            tradition.kind == AnimalTraditionKind.KindHands ? "A stranger is mistaken for the remembered helper." :
            tradition.kind == AnimalTraditionKind.SafeValley ? "Dangerous colony ground is remembered as sanctuary." :
            "The colony is believed easier prey than experience supports.";

        private static string KindClue(AnimalTraditionKind kind) =>
            kind == AnimalTraditionKind.FearedHunter ? "avoidance of a particular person" :
            kind == AnimalTraditionKind.ThunderSticks ? "learned fear of ranged weapons" :
            kind == AnimalTraditionKind.TrapWise ? "unusual caution around bait and paths" :
            kind == AnimalTraditionKind.KindHands ? "learned trust of a person" :
            kind == AnimalTraditionKind.SafeValley ? "attraction to managed habitat" :
            "predator interest in colony animals";

        [DebugAction("Wildlife", "Give selected animal a distorted tradition",
            actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void DebugTradition()
        {
            Find.Targeter.BeginTargeting(TargetingParameters.ForPawns(), target =>
            {
                Pawn animal = target.Pawn;
                if (animal?.RaceProps?.Animal != true) return;
                Pawn subject = animal.Map.mapPawns.FreeColonistsSpawned.FirstOrDefault();
                animal.Map.GetComponent<AnimalTraditionMapComponent>()?.Learn(animal,
                    AnimalTraditionKind.FearedHunter, subject, 0.85f, 0.25f);
            });
        }
    }

    public static class AnimalTraditionUtility
    {
        public static float AvoidanceFactor(Pawn animal, Pawn colonist) =>
            animal?.Map?.GetComponent<AnimalTraditionMapComponent>()?.AvoidanceFactor(animal, colonist) ?? 1f;

        public static float PredatorHumanPreyScore(Pawn predator, Pawn human) =>
            (predator?.Map?.GetComponent<AnimalTraditionMapComponent>()?.PredatorHumanPreyScore(predator, human) ?? 0f) +
            (predator?.Map?.GetComponent<WildlifeLandmarkMapComponent>()?.PredatorHumanPreyScore(predator) ?? 0f);

        public static void NotifyPredatorTargetsColonyAnimal(Pawn predator, Pawn prey) =>
            predator?.Map?.GetComponent<AnimalTraditionMapComponent>()?.NotifyPredatorTargetsColonyAnimal(predator, prey);
    }
}
