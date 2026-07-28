using RimWorld;
using UnityEngine;
using Verse;
using System.Collections.Generic;

namespace Herds
{
    public sealed class Building_WildlifeSnare : Building
    {
        private int resetAt;
        public override void ExposeData() { base.ExposeData(); Scribe_Values.Look(ref resetAt, "resetAt"); }
        public override void TickRare()
        {
            base.TickRare();
            if (!HerdsMod.Settings.enableFieldcraftEquipment || !Spawned || Find.TickManager.TicksGame < resetAt) return;
            for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++)
            {
                IntVec3 cell = Position + new IntVec3(dx, 0, dz); if (!cell.InBounds(Map)) continue;
                Pawn prey = cell.GetFirstPawn(Map);
                if (prey?.Spawned != true || prey.Faction == Faction.OfPlayer || !PreyProfileDatabase.IsEligible(prey.def)) continue;
                float chance = Mathf.Clamp01(0.68f - prey.BodySize * 0.12f);
                resetAt = Find.TickManager.TicksGame + 15000;
                if (Rand.Chance(chance))
                {
                    prey.TakeDamage(new DamageInfo(DamageDefOf.Blunt, Mathf.Clamp(5f + prey.BodySize * 2f, 5f, 12f), 0f, -1f, this));
                    prey.stances?.stunner?.StunFor(300, this);
                    Map.GetComponent<HerdMapComponent>()?.NotifyThreat(prey, this, 1200);
                    Messages.Message("Wildlife snare caught " + prey.LabelShortCap + ".", prey, MessageTypeDefOf.PositiveEvent, false);
                    if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("WildlifeSnare", "result=caught chance=" + chance.ToString("0.00") + " resetAt=" + resetAt, prey, this);
                }
                else { Messages.Message(prey.LabelShortCap + " escaped a wildlife snare.", prey, MessageTypeDefOf.NeutralEvent, false); if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("WildlifeSnare", "result=escaped chance=" + chance.ToString("0.00") + " resetAt=" + resetAt, prey, this); }
                return;
            }
        }
        public override string GetInspectString() => Find.TickManager.TicksGame >= resetAt ? "Armed" : "Resetting: " + (resetAt - Find.TickManager.TicksGame).ToStringTicksToPeriod();

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos()) yield return gizmo;
            if (!Prefs.DevMode) yield break;
            yield return new Command_Action { defaultLabel = "DEV: Wildlife Overview", defaultDesc = "Open the organized wildlife development dashboard.", icon = TexCommand.OpenLinkedQuestTex, action = WildlifeDevMaster.OpenDashboard };
            yield return WildlifeDevMenus.CompleteOverlayToggle();
            yield return WildlifeDevMenus.DiagnosticToggle(null);
            yield return new Command_Action { defaultLabel = "DEV: Snare Tests...", defaultDesc = "Open organized snare test controls.", icon = TexCommand.Attack, action = ShowDevMenu };
        }

        private void ShowDevMenu()
        {
            Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
            {
                new FloatMenuOption("Reset / arm snare", () => { resetAt = 0; if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("DevSnare", "reset", null, this); }),
                new FloatMenuOption("Trigger against prey", BeginDebugTrigger)
            }));
        }

        private void BeginDebugTrigger()
        {
            TargetingParameters parameters = new TargetingParameters { canTargetPawns = true, canTargetAnimals = true, canTargetHumans = false, canTargetLocations = false, validator = target => target.Thing is Pawn pawn && pawn.Spawned && PreyProfileDatabase.IsEligible(pawn.def) };
            Find.Targeter.BeginTargeting(parameters, target => DebugTrigger((Pawn)target.Thing));
        }

        private void DebugTrigger(Pawn prey)
        {
            resetAt = Find.TickManager.TicksGame + 15000;
            float chance = Mathf.Clamp01(0.68f - prey.BodySize * 0.12f);
            bool caught = Rand.Chance(chance);
            if (caught) { prey.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 8f, 0f, -1f, this)); prey.stances?.stunner?.StunFor(300, this); }
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("DevSnare", "result=" + (caught ? "caught" : "escaped") + " chance=" + chance.ToString("0.00"), prey, this);
            Messages.Message("DEV snare result: " + (caught ? "caught" : "escaped") + ".", prey, MessageTypeDefOf.NeutralEvent, false);
        }
    }
}
