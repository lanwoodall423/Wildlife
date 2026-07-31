using System;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Herds
{
    // Retained as a compatibility probe for existing callers. The shared framework now owns
    // the only Bio panel, including when Progression: Education is active.
    public static class ProgressionEducationKnowledgeCompatibility
    {
        private const string PackageId = "ferny.ProgressionEducation";
        public static bool Active => ModsConfig.IsActive(PackageId);
        public static void Initialize() { }
    }

    [HarmonyPatch(typeof(Pawn_FlightTracker), nameof(Pawn_FlightTracker.Notify_JobStarted))]
    public static class FlightTrackerJobSafetyPatch
    {
        private static bool warned;
        public static bool Prepare() => ModsConfig.IsActive("lan.codex.flockmasterpsycasts");

        public static Exception Finalizer(Exception __exception, Job job)
        {
            if (!(__exception is NullReferenceException)) return __exception;
            if (!warned)
            {
                warned = true;
                Log.Warning("[Wildlife] Recovered an invalid bird flight-tracker state while starting " +
                    (job?.def?.defName ?? "an unknown job") + ".");
            }
            return null;
        }
    }
}
