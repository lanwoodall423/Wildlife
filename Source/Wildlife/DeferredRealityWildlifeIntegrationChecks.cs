using System;
using System.Collections.Generic;
using System.Linq;
using DeferredReality.API;
using DeferredReality.Materialization;
using LudeonTK;
using RimWorld;
using Verse;

namespace DeferredReality.Wildlife
{
    /// <summary>Live checks for the optional provider integration; no gameplay state is fabricated.</summary>
    public static class DeferredRealityWildlifeIntegrationChecks
    {
        private const string Category = "Deferred Reality";

        [DebugAction(Category, "Verify Wildlife adjacent integration", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void Verify()
        {
            DeferredRealityWorldComponent world = DeferredRealityWorldComponent.Current;
            var failures = new List<string>();
            if (world == null) failures.Add("framework world component is unavailable");
            if (WildlifeRealityIntegration.Provider == null ||
                !RealityProviderRegistry.TryGet(WildlifeRealityProvider.ProviderId, out IRealityProvider registered) ||
                registered?.GetType() != WildlifeRealityIntegration.Provider.GetType())
                failures.Add("the Wildlife provider is missing or a duplicate provider owns the ID");

            if (world != null && WildlifeRealityIntegration.Provider != null)
            {
                foreach (RealityAdjacentMapRecord marker in world.AdjacentMapSnapshots()
                    .Where(item => item != null && item.providerId == WildlifeRealityProvider.ProviderId)
                    .OrderBy(item => item.mapUniqueId))
                {
                    Map map = Find.Maps?.FirstOrDefault(item => item != null && item.uniqueID == marker.mapUniqueId);
                    if (map == null)
                    {
                        failures.Add("marked map " + marker.mapUniqueId + " is not loaded");
                        continue;
                    }
                    if (!RealityRegionId.TryParse(marker.regionId, out RealityRegionId regionId))
                    {
                        failures.Add("marker " + marker.mapUniqueId + " has an invalid represented region");
                        continue;
                    }
                    if (!(map.Parent is WildlifeDeferredMapParent parent) || parent.regionId != marker.regionId)
                        failures.Add("map " + marker.mapUniqueId + " has the wrong Wildlife map-parent identity");
                    if (!world.TryGetRegion(regionId, out RealityRegionSnapshot region) || region == null ||
                        region.activeMapUniqueId != map.uniqueID)
                        failures.Add("map " + marker.mapUniqueId + " is not the active projection for its region");
                    if (!RealityAdjacentConstructionGuards.IsBlocked(map))
                        failures.Add("map " + marker.mapUniqueId + " is not construction-blocked");

                    var vetoes = new List<RealityVeto>();
                    WildlifeRealityIntegration.Provider.CanCompress(new RealityCompressionRequest
                    {
                        Map = map,
                        regionId = regionId,
                        providerId = WildlifeRealityProvider.ProviderId,
                        now = world.Now,
                        reason = "integration-check",
                        dryRun = true
                    }, vetoes);
                    if (vetoes.Count != 0)
                        failures.Add("valid Wildlife map " + marker.mapUniqueId + " failed compression ownership: " +
                            string.Join(";", vetoes.Select(item => item.message)));
                }

                var ownedTickets = world.ExcursionSnapshots()
                    .Where(item => item != null && item.providerId == WildlifeRealityProvider.ProviderId &&
                        !RealityRetentionPolicy.IsTerminalExcursion(item))
                    .ToList();
                if (ownedTickets.GroupBy(item => item.pawnLoadId, StringComparer.Ordinal)
                    .Any(group => group.Count() != 1))
                    failures.Add("a nonterminal Pawn has more than one Wildlife excursion ticket");
                if (WildlifeRealityIntegration.Provider.Registration.operationRetentionTicks >= 0 ||
                    WildlifeRealityIntegration.Provider.Registration.compactableOperationKinds.Count != 0)
                    failures.Add("Wildlife declared operation compaction without a replay-proof cursor");
            }

            if (failures.Count == 0)
                Log.Message("[DeferredReality] Wildlife adjacent integration PASS: markers, map identity, construction policy, compression ownership, ticket uniqueness, and durable operation policy.");
            else
                Log.Error("[DeferredReality] Wildlife adjacent integration FAIL: " + string.Join(" | ", failures));
        }
    }
}
