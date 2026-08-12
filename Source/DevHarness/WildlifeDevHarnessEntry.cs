using System;
using HarmonyLib;
using Verse;

namespace Herds
{
    public static class WildlifeDevHarnessEntry
    {
        public static void OpenDashboard()
        {
            Type windowType = AccessTools.TypeByName("Herds.Window_WildlifeDevelopmentOverview");
            if (windowType == null)
            {
                Messages.Message("Wildlife development dashboard is unavailable.",
                    RimWorld.MessageTypeDefOf.RejectInput, false);
                return;
            }

            Window window = Activator.CreateInstance(windowType, Find.CurrentMap) as Window;
            if (window != null)
                Find.WindowStack.Add(window);
        }
    }
}
