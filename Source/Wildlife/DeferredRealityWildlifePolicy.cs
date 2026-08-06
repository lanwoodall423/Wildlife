using System;

namespace DeferredReality.Wildlife
{
    internal static class DeferredRealityWildlifePolicy
    {
        public static bool ShouldReportProgress(bool onDestination, string previousFingerprint, string currentFingerprint)
        {
            return onDestination && !string.Equals(previousFingerprint, currentFingerprint, StringComparison.Ordinal);
        }

        public static bool CanCommitAnimalDeparture(bool exitMapThrew, bool pawnSpawned, bool pawnHasMap,
            bool worldPawnsContains)
        {
            return !exitMapThrew && !pawnSpawned && !pawnHasMap && worldPawnsContains;
        }

        public static bool CanReconstructTask(string ticketProviderId, string providerId, string taskId,
            string ticketPawnLoadId, string leadPawnLoadId, int ticketDestinationMapId, int leadMapId,
            bool beyondMap, int matchingLeadCount)
        {
            return matchingLeadCount == 1 && beyondMap &&
                string.Equals(ticketProviderId, providerId, StringComparison.Ordinal) &&
                !string.IsNullOrEmpty(taskId) &&
                !string.IsNullOrEmpty(ticketPawnLoadId) &&
                string.Equals(ticketPawnLoadId, leadPawnLoadId, StringComparison.Ordinal) &&
                ticketDestinationMapId == leadMapId;
        }

        public static bool SelfTest(out string failure)
        {
            failure = null;
            if (ShouldReportProgress(true, "same", "same"))
                failure = "same provider fingerprint was reported as progress";
            else if (!ShouldReportProgress(true, "before", "after"))
                failure = "changed provider fingerprint was not reported";
            else if (ShouldReportProgress(false, "before", "after"))
                failure = "off-destination movement could renew the task";
            else if (!CanCommitAnimalDeparture(false, false, false, true))
                failure = "valid world-pawn disposition was rejected";
            else if (CanCommitAnimalDeparture(true, false, false, true) ||
                CanCommitAnimalDeparture(false, true, false, true) ||
                CanCommitAnimalDeparture(false, false, true, true) ||
                CanCommitAnimalDeparture(false, false, false, false))
                failure = "an invalid ExitMap disposition was accepted";
            else if (!CanReconstructTask("provider", "provider", "task", "pawn", "pawn", 7, 7, true, 1) ||
                CanReconstructTask("provider", "provider", "task", "pawn", "other", 7, 7, true, 1) ||
                CanReconstructTask("provider", "provider", "task", "pawn", "pawn", 7, 7, true, 2) ||
                CanReconstructTask("provider", "provider", "task", "pawn", "pawn", 7, 7, false, 1))
                failure = "ambiguous, mismatched, or non-adjacent task reconstruction was accepted";
            return failure == null;
        }
    }
}
