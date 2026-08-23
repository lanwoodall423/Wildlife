using Verse;

namespace Herds
{
    /// <summary>
    /// Optional, presentation-only boundary for Packs and Predators. Packs keeps its
    /// own gameplay and UI assembly; these methods only answer whether a player-facing
    /// disclosure is authorized by Wildlife's Knowledge Framework state.
    /// </summary>
    public static class WildlifePackPresentationAPI
    {
        public static bool CanRevealPackIdentity(ThingDef species, bool observed) =>
            WildlifeInsightDisclosure.CanRevealPackIdentity(species, observed);

        public static bool CanRevealPackSocial(ThingDef species, bool observed) =>
            WildlifeInsightDisclosure.CanRevealPackSocial(species, observed);

        public static bool CanRevealPackBehavior(ThingDef species, bool observed) =>
            WildlifeInsightDisclosure.CanRevealPackBehavior(species, observed);

        public static bool CanRevealPackTerritory(ThingDef species, bool observed) =>
            WildlifeInsightDisclosure.CanRevealPackTerritory(species, observed);

        public static bool CanRevealPackDen(ThingDef species, bool observed) =>
            WildlifeInsightDisclosure.CanRevealPackDen(species, observed);

        public static bool CanRevealCurrentPrey(ThingDef species, bool preyObserved) =>
            WildlifeInsightDisclosure.CanRevealCurrentPrey(species, preyObserved);
    }
}
