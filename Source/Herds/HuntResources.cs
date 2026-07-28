using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Herds
{
    public enum HuntResourceUse
    {
        ConsumablePerHunter,
        ReusableSingle,
        ScentChargePerHunter
    }

    public sealed class HuntResourceDef : Def
    {
        public ThingDef thingDef;
        public ThingDef sourceBuildingDef;
        public HuntResourceUse use = HuntResourceUse.ConsumablePerHunter;
        public float fieldcraftBonus;
        public bool enabledByScentMasking;

        public int RequiredFor(int hunters) => use == HuntResourceUse.ReusableSingle ? 1 : hunters;

        public string RequirementLabel(int hunters)
        {
            int required = RequiredFor(hunters);
            return required + " requested" + (use == HuntResourceUse.ReusableSingle ? " (reusable)" : use == HuntResourceUse.ConsumablePerHunter ? " (consumed)" : "");
        }
    }

    public sealed class HuntResourceDiscovery : GameComponent
    {
        private List<string> discovered = new List<string>();
        private HashSet<string> discoveredSet = new HashSet<string>();

        public HuntResourceDiscovery(Game game) { }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref discovered, "discoveredHuntResources", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                discovered ??= new List<string>();
                discoveredSet = new HashSet<string>(discovered);
            }
        }

        public override void GameComponentTick()
        {
            if (!HerdsMod.Settings.enableFieldcraftEquipment || Find.TickManager.TicksGame % 600 != 0) return;
            for (int i = 0; i < Find.Maps.Count; i++) Refresh(Find.Maps[i]);
        }

        public bool IsDiscovered(HuntResourceDef def) => def != null && discoveredSet.Contains(def.defName);

        public void Refresh(Map map)
        {
            if (map == null) return;
            foreach (HuntResourceDef def in DefDatabase<HuntResourceDef>.AllDefsListForReading)
            {
                if (IsDiscovered(def) || def.enabledByScentMasking && !HerdsMod.Settings.enableScentMasking) continue;
                bool found = false;
                if (def.thingDef != null)
                {
                    List<Thing> things = map.listerThings.ThingsOfDef(def.thingDef);
                    for (int i = 0; i < things.Count && !found; i++)
                        found = things[i].IsInAnyStorage() || map.areaManager.Home[things[i].Position];
                    if (!found)
                        found = map.mapPawns.FreeColonists.Any(pawn => pawn.inventory?.innerContainer?.Any(thing => thing.def == def.thingDef) == true);
                }
                if (!found && def.sourceBuildingDef != null)
                    found = map.listerThings.ThingsOfDef(def.sourceBuildingDef).Any(thing => thing.Faction == Faction.OfPlayer);
                if (found) Discover(def);
            }
        }

        private void Discover(HuntResourceDef def)
        {
            if (def == null || !discoveredSet.Add(def.defName)) return;
            discovered.Add(def.defName);
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("HuntResourceDiscovered", "resource=" + def.defName);
        }
    }

    public sealed class HediffCompProperties_HuntTimed : HediffCompProperties
    {
        public int durationTicks = 3000;
        public HediffCompProperties_HuntTimed() { compClass = typeof(HediffComp_HuntTimed); }
    }

    public sealed class HediffComp_HuntTimed : HediffComp
    {
        private int remaining;
        public HediffCompProperties_HuntTimed Props => (HediffCompProperties_HuntTimed)props;
        public override void CompPostMake() { base.CompPostMake(); remaining = Props.durationTicks; }
        public override void CompPostTick(ref float severityAdjustment)
        {
            if (--remaining <= 0) parent.pawn.health.RemoveHediff(parent);
        }
        public override void CompExposeData() { Scribe_Values.Look(ref remaining, "remaining", Props.durationTicks); }
    }
}
