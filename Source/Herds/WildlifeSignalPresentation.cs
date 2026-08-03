using System;
using RimWorld;
using Verse;

namespace Herds
{
    public enum WildlifeSignalDisplayTier
    {
        Unknown,
        Family,
        Exact,
        Reliability,
        Truthfulness
    }

    public sealed class WildlifeSignalObservationPresentation : IExposable
    {
        public Pawn observer;
        public float understanding;
        public int tier;
        public string description;

        public void ExposeData()
        {
            Scribe_References.Look(ref observer, "observer");
            Scribe_Values.Look(ref understanding, "understanding", 0f);
            Scribe_Values.Look(ref tier, "tier", 0);
            Scribe_Values.Look(ref description, "description");
        }
    }

    public static class WildlifeSignalPresentation
    {
        public static WildlifeSignalDisplayTier TierFor(float understanding)
        {
            return understanding < 0.15f ? WildlifeSignalDisplayTier.Unknown :
                understanding < 0.4f ? WildlifeSignalDisplayTier.Family :
                understanding < 0.7f ? WildlifeSignalDisplayTier.Exact :
                understanding < 0.92f ? WildlifeSignalDisplayTier.Reliability :
                WildlifeSignalDisplayTier.Truthfulness;
        }

        public static string TierLabel(float understanding) => TierFor(understanding) switch
        {
            WildlifeSignalDisplayTier.Family => "Family recognition",
            WildlifeSignalDisplayTier.Exact => "Exact intent",
            WildlifeSignalDisplayTier.Reliability => "Reliability and response",
            WildlifeSignalDisplayTier.Truthfulness => "Misleading calls",
            _ => "Unfamiliar"
        };

        public static string Description(WildlifeSignalKind kind, float understanding, bool truthful,
            bool verified, bool behaviorConsistent, Pawn speaker, ThingDef species, float radius,
            string expectedBehavior, string observedBehavior, Map map)
        {
            WildlifeSignalDisplayTier tier = TierFor(understanding);
            string location = tier >= WildlifeSignalDisplayTier.Reliability && speaker != null
                ? "from " + AnimalReference(speaker, species, map)
                : "near " + SpeciesReference(species);
            if (tier == WildlifeSignalDisplayTier.Unknown)
                return "A strange wildlife call was heard " + location + ".";

            string family = WildlifeSignalCultureMapComponent.SignalFamily(kind).ToLowerInvariant();
            if (tier == WildlifeSignalDisplayTier.Family)
                return "A " + family + " was heard " + location + ".";

            string result = "A " + WildlifeSignalCultureMapComponent.SignalLabel(kind).ToLowerInvariant() +
                " was heard " + location + ". It appears to mean " +
                WildlifeSignalCultureMapComponent.SignalMeaning(kind).ToLowerInvariant() + ".";
            if (tier >= WildlifeSignalDisplayTier.Reliability)
            {
                result += " It carries about " + radius.ToString("0") +
                    " cells; " + (expectedBehavior.NullOrEmpty() ?
                        "nearby animals are expected to respond." : expectedBehavior);
                if (verified && !observedBehavior.NullOrEmpty())
                    result += " Observed response: " + observedBehavior + ".";
            }
            if (tier >= WildlifeSignalDisplayTier.Truthfulness && verified)
                result += !truthful || !behaviorConsistent
                    ? " The call proved misleading in this instance."
                    : " The observed response matched the interpreted signal.";
            return result;
        }

        public static string SpeciesReference(ThingDef species)
        {
            string label = species?.label ?? "wildlife";
            if (label.NullOrEmpty()) return "wildlife";
            string trimmed = label.Trim();
            if (trimmed.NullOrEmpty()) return "wildlife";
            trimmed = char.ToLowerInvariant(trimmed[0]) + trimmed.Substring(1);
            char first = trimmed[0];
            return ("aeiou".IndexOf(first) >= 0 ? "an " : "a ") + trimmed;
        }

        public static string AnimalReference(Pawn animal, ThingDef species, Map map)
        {
            if (animal != null)
            {
                NotableAnimalRecord notable = (map ?? animal.MapHeld)?.GetComponent<NotableWildlifeMapComponent>()?.For(animal);
                if (notable != null && !notable.title.NullOrEmpty())
                    return notable.title;
            }
            return SpeciesReference(species ?? animal?.def);
        }

        public static string HistoricalDescription(WildlifeSignalTrace trace, Pawn observer,
            ThingDef species, Map map)
        {
            if (trace == null) return "No signal evidence has been recorded.";
            WildlifeSignalObservationPresentation presentation = trace.presentations?.Find(value =>
                value?.observer == observer);
            if (presentation?.description.NullOrEmpty() == false) return presentation.description;
            if (!trace.playerFacingDescription.NullOrEmpty()) return trace.playerFacingDescription;
            return Description(trace.kind, observer == null ? 0f : 0f, trace.truthful, false,
                false, null, species, trace.radius, null, null, map);
        }

        public static bool SelfTest()
        {
            return TierFor(0.149f) == WildlifeSignalDisplayTier.Unknown &&
                TierFor(0.15f) == WildlifeSignalDisplayTier.Family &&
                TierFor(0.4f) == WildlifeSignalDisplayTier.Exact &&
                TierFor(0.7f) == WildlifeSignalDisplayTier.Reliability &&
                TierFor(0.92f) == WildlifeSignalDisplayTier.Truthfulness &&
                !Description(WildlifeSignalKind.HumanDanger, 0.1f, false, true, false,
                     null, ThingDefOf.Muffalo, 30f, "Listeners should flee.", "", null).Contains("human-danger") &&
                Description(WildlifeSignalKind.HumanDanger, 0.4f, false, false, false,
                    null, ThingDefOf.Muffalo, 30f, "Listeners should flee.", "", null).Contains("human-danger") &&
                SpeciesReference(ThingDefOf.Muffalo).StartsWith("a ", StringComparison.Ordinal) &&
                !SpeciesReference(ThingDefOf.Muffalo).Contains("Muffalo");
        }
    }
}
