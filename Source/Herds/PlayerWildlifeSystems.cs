using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Herds
{
    public static class ColonistHuntingUtility
    {
        public static float HuntingSkill(Pawn pawn)
        {
            float animals = pawn?.skills?.GetSkill(SkillDefOf.Animals)?.Level ?? 0f;
            bool ranged = pawn?.equipment?.Primary?.def.IsRangedWeapon == true;
            float combat = pawn?.skills?.GetSkill(ranged ? SkillDefOf.Shooting : SkillDefOf.Melee)?.Level ?? 0f;
            float proficiency = pawn?.Map?.GetComponent<HuntingKnowledgeMapComponent>()?.WildlifeProficiencyLevel(pawn) * 0.5f ?? 0f;
            float journal = pawn?.Map?.GetComponent<WildlifeFieldJournalMapComponent>()?.HuntingSkillBonus ?? 0f;
            float role = WildlifeRoleUtility.IsMasterHunter(pawn) ? 2f :
                WildlifeRoleUtility.IsMasterConservationist(pawn) ? 0.5f : 0f;
            return Mathf.Min(animals, combat) * 0.65f + (animals + combat) * 0.175f + proficiency + journal + role;
        }

        public static float HuntingSkill(Pawn pawn, ThingDef species)
        {
            float skill = HuntingSkill(pawn);
            if (HerdsMod.Settings.enableSpeciesKnowledgeProgression && pawn?.Map != null && species != null) skill += pawn.Map.GetComponent<HuntingKnowledgeMapComponent>()?.TacticalBonus(pawn, species) ?? 0f;
            if (pawn?.Map != null && species != null &&
                HerdsMod.Settings.enableWildlifeLandscaping &&
                HerdsMod.Settings.enableLandscapeEffects)
                skill += pawn.Map.GetComponent<WildlifeLandscapeMapComponent>()?
                    .HuntingBonus(pawn, species) ?? 0f;
            return skill;
        }

        public static bool IsSneaking(Pawn pawn) => pawn?.Spawned == true &&
            (pawn.CurJobDef == HerdsDefOf.Herds_FieldcraftHunt ||
             pawn.CurJobDef == HerdsDefOf.Herds_ObserveWildlifeMoment ||
             pawn.CurJobDef == HerdsDefOf.Herds_FollowWildlifeTrail ||
             pawn.CurJobDef == HerdsDefOf.Herds_StudyWildlifeSign);
    }

    public sealed class StewardSpeciesRecord : IExposable
    {
        public string species;
        public bool protectedSpecies;
        public int quota = -1;
        public int killsThisSeason;
        public int season = -1;
        public int desiredMinimum;
        public int desiredMaximum = 999;
        public int closedSeason = -1;
        public void ExposeData()
        {
            Scribe_Values.Look(ref species, "species"); Scribe_Values.Look(ref protectedSpecies, "protectedSpecies");
            Scribe_Values.Look(ref quota, "quota", -1); Scribe_Values.Look(ref killsThisSeason, "killsThisSeason"); Scribe_Values.Look(ref season, "season", -1);
            Scribe_Values.Look(ref desiredMinimum, "desiredMinimum"); Scribe_Values.Look(ref desiredMaximum, "desiredMaximum", 999);
            Scribe_Values.Look(ref closedSeason, "closedSeason", -1);
        }
    }

    public sealed class WildlifeStewardMapComponent : MapComponent
    {
        private List<StewardSpeciesRecord> records = new List<StewardSpeciesRecord>();
        private Dictionary<string, int> warningCooldowns = new Dictionary<string, int>();
        private int nextEcologyTick;
        private int nextWarningTick;

        public WildlifeStewardMapComponent(Map map) : base(map) { }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref records, "stewardSpecies", LookMode.Deep);
            Scribe_Collections.Look(ref warningCooldowns, "predatorWarningCooldowns", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) { records = records ?? new List<StewardSpeciesRecord>(); warningCooldowns = warningCooldowns ?? new Dictionary<string, int>(); }
        }

        public override void MapComponentTick()
        {
            bool stewardship = HerdsMod.Settings.enableWildlifeSteward && WildlifeProgression.Unlocked(WildlifeCapability.Stewardship);
            bool warnings = HerdsMod.Settings.enableUncertainPredatorWarnings && WildlifeProgression.Unlocked(WildlifeCapability.WarningSystems);
            if (!stewardship && !warnings) return;
            int now = Find.TickManager.TicksGame;
            if (stewardship && now >= nextEcologyTick) UpdateSteward(now);
            if (warnings && now >= nextWarningTick) UpdateWarnings(now);
        }

        public StewardSpeciesRecord For(ThingDef def)
        {
            StewardSpeciesRecord record = records.FirstOrDefault(item => item.species == def.defName);
            if (record == null) { record = new StewardSpeciesRecord { species = def.defName }; records.Add(record); }
            int season = GenLocalDate.Season(map).GetHashCode() + GenLocalDate.Year(map) * 10;
            if (record.season != season) { record.season = season; record.killsThisSeason = 0; }
            return record;
        }

        public bool CanHunt(ThingDef def, out string reason)
        {
            reason = null;
            if (def == null || !WildlifeProgression.Unlocked(WildlifeCapability.Stewardship) || (!HerdsMod.Settings.enableWildlifeSteward && !HerdsMod.Settings.enableHuntingRegulations)) return true;
            StewardSpeciesRecord record = For(def);
            if (HerdsMod.Settings.enableHuntingRegulations && record.protectedSpecies) { reason = def.LabelCap + " is protected by the colony's Steward policy."; return false; }
            if (HerdsMod.Settings.enableHuntingRegulations && record.quota >= 0 && record.killsThisSeason >= record.quota) { reason = "Seasonal hunting quota reached for " + def.label + "."; return false; }
            if (HerdsMod.Settings.enableHuntingRegulations && record.closedSeason == (int)GenLocalDate.Season(map)) { reason = "Hunting " + def.label + " is closed during " + GenLocalDate.Season(map) + "."; return false; }
            int count = CountSpecies(def);
            if (HerdsMod.Settings.enableWildlifeSteward && record.desiredMinimum > 0 && count <= record.desiredMinimum) { reason = def.LabelCap + " is at its desired minimum population."; return false; }
            return true;
        }

        public void RecordKill(Pawn victim)
        {
            if (!WildlifeProgression.Unlocked(WildlifeCapability.Stewardship) || victim?.def == null || !PreyProfileDatabase.IsEligible(victim.def)) return;
            if (HerdsMod.Settings.enableHuntingRegulations) For(victim.def).killsThisSeason++;
        }

        public int CountSpecies(ThingDef def)
        {
            int count = 0; IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++) if (pawns[i]?.Dead == false && pawns[i].def == def && pawns[i].Faction != Faction.OfPlayer) count++;
            return count;
        }

        private void UpdateSteward(int now)
        {
            nextEcologyTick = now + 60000;
            for (int i = 0; i < records.Count; i++)
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(records[i].species);
                if (def == null || records[i].desiredMinimum <= 0) continue;
                int count = CountSpecies(def);
                if (count < records[i].desiredMinimum) Messages.Message("Steward report: " + def.LabelCap + " population is below the desired range (" + count + "/" + records[i].desiredMinimum + ").", MessageTypeDefOf.CautionInput, false);
                else if (records[i].desiredMaximum < 999 && count > records[i].desiredMaximum) Messages.Message("Steward report: " + def.LabelCap + " population is above the desired range (" + count + "/" + records[i].desiredMaximum + ").", MessageTypeDefOf.NeutralEvent, false);
            }
        }

        private void UpdateWarnings(int now)
        {
            nextWarningTick = now + 1200;
            if (map.listerBuildings.allBuildingsColonist.Count == 0) return;
            IntVec3 colonyCenter = IntVec3.Zero;
            for (int i = 0; i < map.listerBuildings.allBuildingsColonist.Count; i++) colonyCenter += map.listerBuildings.allBuildingsColonist[i].Position;
            colonyCenter.x /= map.listerBuildings.allBuildingsColonist.Count;
            colonyCenter.z /= map.listerBuildings.allBuildingsColonist.Count;
            int skill = map.GetComponent<WildlifeFieldcraftMapComponent>()?.BestTrackerSkill ?? 0;
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = PositiveMod(now / 1200, 4); i < pawns.Count; i += 4)
            {
                Pawn predator = pawns[i];
                if (predator?.Spawned != true || predator.Dead ||
                    !WildlifeSpeciesClassification.IsPredator(predator.def) ||
                    predator.Faction == Faction.OfPlayer) continue;
                if (predator.Position.DistanceToSquared(colonyCenter) > 3600 || warningCooldowns.TryGetValue(predator.def.defName, out int until) && until > now) continue;
                warningCooldowns[predator.def.defName] = now + 30000;
                IntVec3 estimate = predator.Position + new IntVec3(skill >= 10 ? 0 : PositiveMod(predator.thingIDNumber, 21) - 10, 0, skill >= 10 ? 0 : PositiveMod(predator.thingIDNumber * 7, 21) - 10);
                string identity = skill >= 6 ? predator.def.LabelCap.ToString() : "Predator";
                Messages.Message(identity + " signs reported near the " + DirectionFromCenter(estimate) + " side of the colony. Location is approximate.", MessageTypeDefOf.ThreatSmall, false);
                if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("PredatorWarning", "identity=" + identity + " actual=" + predator.Position + " estimate=" + estimate + " skill=" + skill, predator);
            }
        }

        public void DebugPredatorWarning(Pawn predator)
        {
            if (predator?.Spawned != true ||
                !WildlifeSpeciesClassification.IsPredator(predator.def)) { Messages.Message("Choose a spawned predator.", MessageTypeDefOf.RejectInput, false); return; }
            int skill = map.GetComponent<WildlifeFieldcraftMapComponent>()?.BestTrackerSkill ?? 0;
            IntVec3 estimate = predator.Position + new IntVec3(skill >= 10 ? 0 : 10, 0, skill >= 10 ? 0 : -10);
            Messages.Message("DEV warning estimate: " + predator.def.LabelCap + " near the " + DirectionFromCenter(estimate) + ".", MessageTypeDefOf.ThreatSmall, false);
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("DevPredatorWarning", "actual=" + predator.Position + " estimate=" + estimate + " skill=" + skill, predator);
        }

        public override void MapComponentDraw()
        {
            base.MapComponentDraw();
            if (!Prefs.DevMode || !FieldcraftDebug.WarningOverlay || Find.CurrentMap != map) return;
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++) if (pawns[i]?.Spawned == true &&
                WildlifeSpeciesClassification.IsPredator(pawns[i].def) &&
                pawns[i].Faction != Faction.OfPlayer) GenDraw.DrawRadiusRing(pawns[i].Position, 10f, Color.red);
        }

        private string DirectionFromCenter(IntVec3 cell)
        {
            IntVec3 delta = cell - map.Center;
            return Mathf.Abs(delta.x) > Mathf.Abs(delta.z) ? (delta.x > 0 ? "east" : "west") : (delta.z > 0 ? "north" : "south");
        }

        private static int PositiveMod(int value, int modulus) { int result = value % modulus; return result < 0 ? result + modulus : result; }
    }

    public sealed class JobDriver_FieldcraftHunt : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            if (job.targetC.IsValid && job.targetC.Cell != job.targetB.Cell)
                yield return Toils_Goto.GotoCell(TargetIndex.C, PathEndMode.OnCell);
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);
            Toil coordinate = Toils_General.Wait(999999, TargetIndex.A);
            coordinate.defaultCompleteMode = ToilCompleteMode.Never;
            coordinate.AddPreTickAction(delegate
            {
                WildlifeHuntCoordinator component = pawn.Map?.GetComponent<WildlifeHuntCoordinator>();
                if (component?.NotifyReady(job.targetA.Pawn, pawn) == true) pawn.jobs.curDriver.ReadyForNextToil();
            });
            yield return coordinate;
            Toil begin = ToilMaker.MakeToil("BeginFieldcraftHunt");
            begin.initAction = delegate
            {
                Pawn prey = job.targetA.Pawn;
                if (prey?.Spawned != true) return;
                Job hunt = WildlifeHuntCoordinator.CreatePursuitJob(pawn, prey);
                pawn.jobs.StartJob(hunt, JobCondition.Succeeded);
            };
            begin.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return begin;
        }
    }

    internal sealed class CoordinatedHuntRecord
    {
        public Pawn prey;
        public Thing huntingSpot;
        public readonly List<Pawn> hunters = new List<Pawn>();
        public readonly Dictionary<Pawn, IntVec3> staging = new Dictionary<Pawn, IntVec3>();
        public readonly Dictionary<Pawn, IntVec3> approachWaypoints = new Dictionary<Pawn, IntVec3>();
        public readonly HashSet<Pawn> ready = new HashSet<Pawn>();
        public int launchDeadline;
        public int earliestLaunchTick;
        public bool launched;
        public HuntPlanOptions options;
        public bool resolved;
        public int endedTick;
        public readonly HashSet<Pawn> retreated = new HashSet<Pawn>();
        public readonly Dictionary<Pawn, float> gearBonuses = new Dictionary<Pawn, float>();
        public readonly Dictionary<Pawn, string> roles = new Dictionary<Pawn, string>();
        public Thing treeRefuge;
        public string phase = "Staging";
        public int pursuitDeadline;
        public int lastTrailTick;
        public int lastSeenTick;
        public float healthAtLaunch = 1f;
        public bool woundedTracking;
        public bool trackingMode;
        public IntVec3 lastPreyPosition = IntVec3.Invalid;
        public readonly List<IntVec3> bloodTrail = new List<IntVec3>();
        public readonly Dictionary<Pawn, int> enduranceDeadlines = new Dictionary<Pawn, int>();
    }

    public sealed class WildlifeHuntCoordinator : MapComponent
    {
        private readonly Dictionary<Pawn, CoordinatedHuntRecord> hunts = new Dictionary<Pawn, CoordinatedHuntRecord>();
        public int ActiveHuntCount => hunts.Count(pair => pair.Value != null && !pair.Value.resolved);

        public WildlifeHuntCoordinator(Map map) : base(map) { }

        public override void MapComponentTick()
        {
            if (!HerdsMod.Settings.enableHuntingChanges || !HerdsMod.Settings.enableHuntingExpeditions)
            {
                if (hunts.Count > 0)
                {
                    foreach (CoordinatedHuntRecord record in hunts.Values.ToList())
                    {
                        StopHunters(record);
                        EndHunt(record, "coordinated hunts were disabled in mod settings.", "feature-disabled", MessageTypeDefOf.NeutralEvent);
                    }
                    hunts.Clear();
                }
                return;
            }
            int now = Find.TickManager.TicksGame;
            if (now % 60 != 0 || hunts.Count == 0) return;
            foreach (CoordinatedHuntRecord record in hunts.Values.ToList())
            {
                if (record.resolved) continue;
                if (record.huntingSpot != null && record.huntingSpot.DestroyedOrNull())
                {
                    StopHunters(record);
                    EndHunt(record, "the Hunting Spot was removed.", "spot-removed", MessageTypeDefOf.CautionInput);
                    continue;
                }
                if (record.prey == null)
                {
                    StopHunters(record);
                    EndHunt(record, "the target became unavailable.", "target-missing", MessageTypeDefOf.CautionInput);
                    continue;
                }
                if (record.prey.Dead)
                {
                    for (int i = 0; i < record.hunters.Count; i++) record.hunters[i]?.Map?.GetComponent<HuntingKnowledgeMapComponent>()?.Learn(record.hunters[i], record.prey.def, 70f, true);
                    EndHunt(record, record.prey.LabelShortCap + " was killed.", "kill", MessageTypeDefOf.PositiveEvent);
                    continue;
                }
                if (!record.prey.Spawned)
                {
                    HerdMapComponent herds = map.GetComponent<HerdMapComponent>();
                    bool hidden = herds?.IsHidden(record.prey) == true;
                    Thing refuge = hidden ? herds.HiddenRefugeFor(record.prey) : null;
                    if (refuge is Plant plant && plant.def.plant?.IsTree == true)
                    {
                        if (record.launched && !UpdateHunterEndurance(record, now)) continue;
                        if (MaintainTreeAttack(record, refuge)) continue;
                    }
                    StopHunters(record);
                    EndHunt(record, hidden ? record.prey.LabelShortCap + " escaped into a hiding place." : record.prey.LabelShortCap + " left the map or became unreachable.", hidden ? "hidden" : "escaped", MessageTypeDefOf.NeutralEvent);
                    continue;
                }
                if (record.treeRefuge != null) ResumePursuitFromTree(record);
                if (record.launched && !UpdateTrackingAndEndurance(record, now)) continue;
                if (HerdsMod.Settings.enableWoundedTrackingAndRetreat)
                {
                    float retreatAt = Mathf.Lerp(0.72f, 0.30f, record.options?.riskTolerance ?? 0.5f);
                    for (int i = 0; i < record.hunters.Count; i++)
                    {
                        Pawn hunter = record.hunters[i];
                        if (hunter?.Spawned != true || record.retreated.Contains(hunter)) continue;
                        if (hunter.Downed)
                        {
                            record.retreated.Add(hunter);
                            Pawn rescuer = map.mapPawns.FreeColonistsSpawned.FirstOrDefault(pawn => pawn != hunter && !pawn.Downed && !pawn.Drafted && !record.hunters.Contains(pawn) && pawn.CanReserveAndReach(hunter, PathEndMode.Touch, Danger.Deadly));
                            if (rescuer != null) { Job rescue = JobMaker.MakeJob(JobDefOf.Rescue, hunter); rescue.playerForced = true; rescuer.jobs.TryTakeOrderedJob(rescue, JobTag.Misc); }
                            Messages.Message(hunter.LabelShortCap + " is downed during the hunt" + (rescuer == null ? "." : "; " + rescuer.LabelShortCap + " is responding."), hunter, MessageTypeDefOf.ThreatBig, false);
                            continue;
                        }
                        if (hunter.health.summaryHealth.SummaryHealthPercent >= retreatAt) continue;
                        record.retreated.Add(hunter);
                        IntVec3 safe = map.listerBuildings.allBuildingsColonist.Count > 0 ? map.listerBuildings.allBuildingsColonist[0].Position : map.Center;
                        Job retreat = JobMaker.MakeJob(JobDefOf.Goto, CellFinder.RandomClosewalkCellNear(safe, map, 5)); retreat.playerForced = true;
                        hunter.jobs.TryTakeOrderedJob(retreat, JobTag.Misc);
                        map.GetComponent<HuntingKnowledgeMapComponent>()?.Learn(hunter, record.prey.def, 12f, false, true);
                        Messages.Message(hunter.LabelShortCap + " is wounded and withdrawing from the hunt.", hunter, MessageTypeDefOf.CautionInput, false);
                        if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("HunterRetreat", "health=" + hunter.health.summaryHealth.SummaryHealthPercent.ToString("0.00") + " threshold=" + retreatAt.ToString("0.00") + " safe=" + safe, hunter, record.prey);
                    }
                }
                int available = record.hunters.Count(hunter => hunter?.Spawned == true && !hunter.Downed && !record.retreated.Contains(hunter));
                if (available == 0)
                {
                    EndHunt(record, "all assigned hunters withdrew, were downed, or became unavailable.", "no-hunters", MessageTypeDefOf.CautionInput);
                    continue;
                }
                if (!record.launched && now > record.launchDeadline)
                {
                    StopHunters(record);
                    EndHunt(record, "not every available hunter reached their staging position before the assembly deadline.", "assembly-timeout", MessageTypeDefOf.NeutralEvent);
                    continue;
                }
                if (now > record.earliestLaunchTick + 180)
                {
                    bool stillParticipating = record.launched
                        ? MaintainPursuit(record)
                        : record.hunters.Any(hunter => hunter?.Spawned == true && !record.retreated.Contains(hunter) && hunter.CurJobDef == HerdsDefOf.Herds_FieldcraftHunt);
                    if (!stillParticipating)
                    {
                        EndHunt(record, record.launched ? "all remaining hunters were manually redirected from the target." : "the assigned hunters stopped staging before launch.", record.launched ? "manually-redirected" : "staging-abandoned", MessageTypeDefOf.NeutralEvent);
                        continue;
                    }
                }
            }
            foreach (Pawn prey in hunts.Where(pair => pair.Key == null || pair.Value.resolved && now > pair.Value.endedTick + 1200).Select(pair => pair.Key).ToList()) hunts.Remove(prey);
        }

        private void EndHunt(CoordinatedHuntRecord record, string reason, string result, MessageTypeDef messageType)
        {
            if (record == null || record.resolved) return;
            record.resolved = true;
            record.endedTick = Find.TickManager.TicksGame;
            for (int i = 0; i < record.hunters.Count; i++)
            {
                Pawn hunter = record.hunters[i];
                Hediff onTrack = hunter?.health?.hediffSet?.GetFirstHediffOfDef(HerdsDefOf.Herds_HuntingOnTrack);
                if (onTrack != null) hunter.health.RemoveHediff(onTrack);
            }
            Thing lookTarget = record.prey?.Spawned == true ? record.prey : record.huntingSpot?.Spawned == true ? record.huntingSpot : record.hunters.FirstOrDefault(hunter => hunter?.Spawned == true);
            string text = "Fieldcraft hunt ended: " + reason;
            if (lookTarget != null) Messages.Message(text, lookTarget, messageType, false); else Messages.Message(text, messageType, false);
            WildlifeExperience.Record("Hunt", text, lookTarget, messageType != MessageTypeDefOf.PositiveEvent);
            record.prey?.MapHeld?.GetComponent<NotableWildlifeMapComponent>()?.NotifyHuntOutcome(record.prey, result);
            if (result == "kill" || result == "killed-during-staging")
            {
                map.GetComponent<WildlifeFieldJournalMapComponent>()?.NotifyHuntKill(record.prey?.def);
                map.GetComponent<WildlifeFieldJournalMapComponent>()?.ResolveLocalHuntReward(record.hunters, record.prey);
            }
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("GroupHuntOutcome", "result=" + result + " reason=" + reason + " hunters=" + record.hunters.Count + " retreats=" + record.retreated.Count, record.hunters.FirstOrDefault(), record.prey ?? lookTarget);
        }

        private static void StopHunters(CoordinatedHuntRecord record)
        {
            for (int i = 0; i < record.hunters.Count; i++)
            {
                Pawn hunter = record.hunters[i];
                if (hunter?.CurJobDef == HerdsDefOf.Herds_FieldcraftHunt || hunter?.CurJob?.targetA.Thing == record.prey) hunter.jobs.EndCurrentJob(JobCondition.InterruptForced);
            }
        }

        public static Job CreatePursuitJob(Pawn hunter, Pawn prey)
        {
            if (hunter == null || prey == null) return null;
            bool ranged = hunter.equipment?.Primary?.def.IsRangedWeapon == true;
            Job pursuit = JobMaker.MakeJob(ranged ? JobDefOf.AttackStatic : JobDefOf.AttackMelee, prey);
            pursuit.playerForced = true;
            pursuit.killIncappedTarget = true;
            return pursuit;
        }

        private bool MaintainPursuit(CoordinatedHuntRecord record)
        {
            bool participating = false;
            for (int i = 0; i < record.hunters.Count; i++)
            {
                Pawn hunter = record.hunters[i];
                if (hunter?.Spawned != true || hunter.Downed || record.retreated.Contains(hunter)) continue;
                if (record.trackingMode && hunter.CurJobDef == JobDefOf.Goto) { participating = true; continue; }
                if (hunter.CurJobDef == HerdsDefOf.Herds_FieldcraftHunt || hunter.CurJob?.targetA.Thing == record.prey)
                {
                    participating = true;
                    continue;
                }
                if (hunter.Drafted || hunter.CurJob?.playerForced == true) continue;
                Job pursuit = CreatePursuitJob(hunter, record.prey);
                if (pursuit == null) continue;
                hunter.jobs.TryTakeOrderedJob(pursuit, JobTag.Misc);
                participating = true;
                if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("GroupHuntReengage", "job=" + pursuit.def.defName + " reason=previous-job-ended", hunter, record.prey);
            }
            return participating;
        }

        private bool UpdateTrackingAndEndurance(CoordinatedHuntRecord record, int now)
        {
            if (!UpdateHunterEndurance(record, now)) return false;
            if (record.phase == "Assault" && now > record.lastSeenTick + 300) record.phase = "Pursuit";
            if (!HerdsMod.Settings.enableHuntTracking) return true;
            float currentHealth = record.prey.health.summaryHealth.SummaryHealthPercent;
            if (!record.woundedTracking && currentHealth < record.healthAtLaunch - 0.001f)
            {
                record.woundedTracking = true;
                record.phase = "Pursuit";
                record.lastTrailTick = now;
                record.lastPreyPosition = record.prey.Position;
                record.bloodTrail.Add(record.prey.Position);
                if (HerdsMod.Settings.enableHuntedAdrenaline) AddHuntHediff(record.prey, HerdsDefOf.Herds_HuntedAdrenaline);
                if (WildlifeProgression.Unlocked(WildlifeCapability.Fieldcraft))
                    for (int i = 0; i < record.hunters.Count; i++) AddHuntHediff(record.hunters[i], HerdsDefOf.Herds_HuntingOnTrack);
                if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("BloodTrailStarted", "health=" + currentHealth.ToString("0.00") + " pursuitDeadline=" + record.pursuitDeadline, record.hunters.FirstOrDefault(), record.prey);
            }
            if (!record.woundedTracking) return true;

            bool bleeding = record.prey.health.hediffSet.BleedRateTotal > 0.01f;
            if (bleeding && now - record.lastTrailTick >= 120 && record.prey.Position.DistanceToSquared(record.lastPreyPosition) >= 4)
            {
                record.lastTrailTick = now;
                record.lastPreyPosition = record.prey.Position;
                record.bloodTrail.Add(record.prey.Position);
                if (record.bloodTrail.Count > 40) record.bloodTrail.RemoveAt(0);
            }
            bool visible = record.hunters.Any(hunter => hunter?.Spawned == true && !hunter.Downed && GenSight.LineOfSight(hunter.Position, record.prey.Position, map));
            if (visible)
            {
                record.lastSeenTick = now;
                record.phase = "Pursuit";
                if (WildlifeProgression.Unlocked(WildlifeCapability.Fieldcraft))
                    for (int i = 0; i < record.hunters.Count; i++) AddHuntHediff(record.hunters[i], HerdsDefOf.Herds_HuntingOnTrack);
                if (record.trackingMode)
                {
                    record.trackingMode = false;
                    for (int i = 0; i < record.hunters.Count; i++)
                    {
                        Pawn hunter = record.hunters[i];
                        if (hunter?.Spawned != true || hunter.Downed || record.retreated.Contains(hunter)) continue;
                        Job pursuit = CreatePursuitJob(hunter, record.prey);
                        if (pursuit != null) hunter.jobs.TryTakeOrderedJob(pursuit, JobTag.Misc);
                    }
                    if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("TrailReacquired", "trailPoints=" + record.bloodTrail.Count, record.hunters.FirstOrDefault(), record.prey);
                }
                return true;
            }
            if (now - record.lastSeenTick < 180) return true;
            record.phase = "Tracking";
            record.trackingMode = true;
            float averageSkill = record.hunters.Where(hunter => hunter != null).DefaultIfEmpty().Average(hunter => hunter == null ? 0f : ColonistHuntingUtility.HuntingSkill(hunter, record.prey.def));
            int retention = Mathf.RoundToInt(900f + averageSkill * 90f - map.weatherManager.RainRate * 600f);
            if (record.bloodTrail.Count == 0 || now - record.lastTrailTick > Mathf.Max(300, retention))
            {
                StopHunters(record);
                for (int i = 0; i < record.hunters.Count; i++) SendHunterHome(record.hunters[i], record);
                EndHunt(record, "the blood trail went cold and the prey could not be reacquired.", "trail-lost", MessageTypeDefOf.NeutralEvent);
                return false;
            }
            IntVec3 trail = record.bloodTrail[record.bloodTrail.Count - 1];
            IntVec3 previous = record.bloodTrail.Count > 1 ? record.bloodTrail[record.bloodTrail.Count - 2] : trail;
            Vector2 direction = new Vector2(trail.x - previous.x, trail.z - previous.z).normalized;
            Vector2 cross = new Vector2(-direction.y, direction.x);
            Pawn tracker = record.hunters.Where(hunter => hunter?.Spawned == true && !hunter.Downed && !record.retreated.Contains(hunter)).OrderByDescending(hunter => ColonistHuntingUtility.HuntingSkill(hunter, record.prey.def)).FirstOrDefault();
            for (int i = 0; i < record.hunters.Count; i++)
            {
                Pawn hunter = record.hunters[i];
                if (hunter?.Spawned != true || hunter.Downed || record.retreated.Contains(hunter)) continue;
                IntVec3 destination = trail;
                if (hunter != tracker && direction != Vector2.zero)
                {
                    float side = i % 2 == 0 ? 1f : -1f;
                    IntVec3 predicted = trail + new IntVec3(Mathf.RoundToInt(direction.x * 6f + cross.x * side * 4f), 0, Mathf.RoundToInt(direction.y * 6f + cross.y * side * 4f));
                    if (predicted.InBounds(map) && predicted.Standable(map)) destination = predicted;
                }
                if (hunter.Position.DistanceToSquared(destination) <= 4 || hunter.CurJobDef == JobDefOf.Goto && hunter.CurJob.targetA.Cell == destination) continue;
                Job follow = JobMaker.MakeJob(JobDefOf.Goto, destination); follow.playerForced = true; follow.locomotionUrgency = LocomotionUrgency.Jog;
                hunter.jobs.TryTakeOrderedJob(follow, JobTag.Misc);
            }
            return true;
        }

        private bool UpdateHunterEndurance(CoordinatedHuntRecord record, int now)
        {
            if (HerdsMod.Settings.enableHuntEndurance)
            {
                for (int i = 0; i < record.hunters.Count; i++)
                {
                    Pawn hunter = record.hunters[i];
                    if (hunter?.Spawned != true || hunter.Downed || record.retreated.Contains(hunter) || !record.enduranceDeadlines.TryGetValue(hunter, out int deadline) || now < deadline) continue;
                    record.retreated.Add(hunter);
                    if (hunter.CurJob?.playerForced == true) hunter.jobs.EndCurrentJob(JobCondition.InterruptForced);
                    AddHuntHediff(hunter, HerdsDefOf.Herds_HuntFatigue);
                    SendHunterHome(hunter, record);
                    Messages.Message(hunter.LabelShortCap + " exhausted their pursuit time and is returning home.", hunter, MessageTypeDefOf.NeutralEvent, false);
                    if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("HunterEnduranceExpired", "deadline=" + deadline + " phase=" + record.phase, hunter, record.prey);
                }
                if (!record.hunters.Any(hunter => hunter?.Spawned == true && !hunter.Downed && !record.retreated.Contains(hunter)))
                {
                    EndHunt(record, "all hunters exhausted their pursuit time and returned home.", "pursuit-exhausted", MessageTypeDefOf.NeutralEvent);
                    return false;
                }
            }
            return true;
        }

        private void SendHunterHome(Pawn hunter, CoordinatedHuntRecord record)
        {
            if (hunter?.Spawned != true || hunter.Downed) return;
            IntVec3 destination = record.huntingSpot?.Spawned == true ? record.huntingSpot.Position : hunter.ownership?.OwnedBed?.Position ?? map.Center;
            Job home = JobMaker.MakeJob(JobDefOf.Goto, CellFinder.RandomClosewalkCellNear(destination, map, 4));
            home.playerForced = true;
            hunter.jobs.TryTakeOrderedJob(home, JobTag.Misc);
        }

        private static void AddHuntHediff(Pawn pawn, HediffDef def)
        {
            if (pawn?.health == null || def == null || pawn.health.hediffSet.HasHediff(def)) return;
            pawn.health.AddHediff(HediffMaker.MakeHediff(def, pawn));
        }

        private bool MaintainTreeAttack(CoordinatedHuntRecord record, Thing refuge)
        {
            bool hasRangedHunter = false;
            for (int i = 0; i < record.hunters.Count; i++)
            {
                Pawn hunter = record.hunters[i];
                if (hunter?.Spawned != true || hunter.Downed || record.retreated.Contains(hunter)) continue;
                bool ranged = hunter.equipment?.Primary?.def.IsRangedWeapon == true;
                if (!ranged)
                {
                    if (hunter.CurJob?.targetA.Thing == record.prey) hunter.jobs.EndCurrentJob(JobCondition.InterruptForced);
                    continue;
                }
                hasRangedHunter = true;
                if (hunter.CurJob?.targetA.Thing == refuge) continue;
                Job attackTree = JobMaker.MakeJob(JobDefOf.AttackStatic, refuge);
                attackTree.playerForced = true;
                hunter.jobs.TryTakeOrderedJob(attackTree, JobTag.Misc);
            }
            if (!hasRangedHunter) return false;
            if (record.treeRefuge != refuge)
            {
                record.treeRefuge = refuge;
                Messages.Message(record.prey.LabelShortCap + " is hiding in a tree. Ranged hunters are firing at reduced accuracy; melee hunters cannot reach it.", refuge, MessageTypeDefOf.CautionInput, false);
                if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("TreeRefugeEngaged", "rangedAccuracy=25%-65% damageScale=0.65", record.hunters.FirstOrDefault(), refuge);
            }
            return true;
        }

        private void ResumePursuitFromTree(CoordinatedHuntRecord record)
        {
            Thing refuge = record.treeRefuge;
            record.treeRefuge = null;
            for (int i = 0; i < record.hunters.Count; i++)
            {
                Pawn hunter = record.hunters[i];
                if (hunter?.Spawned != true || hunter.Downed || record.retreated.Contains(hunter)) continue;
                if (hunter.CurJob?.targetA.Thing == refuge) hunter.jobs.EndCurrentJob(JobCondition.InterruptForced);
                Job pursuit = CreatePursuitJob(hunter, record.prey);
                if (pursuit != null) hunter.jobs.TryTakeOrderedJob(pursuit, JobTag.Misc);
            }
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("TreeRefugeExited", "pursuit resumed", record.hunters.FirstOrDefault(), record.prey);
        }

        public void Begin(Pawn prey, List<Pawn> hunters, HuntPlanOptions options, Thing huntingSpot = null)
        {
            if (!WildlifeProgression.Unlocked(WildlifeCapability.BasicHunting))
            {
                Messages.Message(WildlifeProgression.LockReason(WildlifeCapability.BasicHunting), MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (!WildlifeProgression.Unlocked(WildlifeCapability.Fieldcraft) && hunters?.Count > 2)
            {
                Messages.Message("Fieldcraft is required to coordinate more than two hunters.", MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (prey?.Spawned != true || hunters == null || hunters.Count == 0) return;
            if (hunts.TryGetValue(prey, out CoordinatedHuntRecord existing) && !existing.resolved)
            {
                Messages.Message(prey.LabelShortCap + " is already the target of a coordinated hunt.", prey, MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (huntingSpot != null && hunts.Values.Any(record => !record.resolved && record.huntingSpot == huntingSpot))
            {
                Messages.Message("This hunting spot is already coordinating an expedition.", huntingSpot, MessageTypeDefOf.RejectInput, false);
                return;
            }
            Vector2 wind = WildlifeFieldcraftMapComponent.WindVector(map);
            Vector2 crosswind = new Vector2(-wind.y, wind.x);
            hunters = hunters.OrderByDescending(pawn => pawn.skills?.GetSkill(SkillDefOf.Animals)?.Level ?? 0).ThenByDescending(pawn => pawn.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0).ToList();
            float averageSkill = hunters.Average(ColonistHuntingUtility.HuntingSkill);
            options = options ?? new HuntPlanOptions();
            CoordinatedHuntRecord record = new CoordinatedHuntRecord { prey = prey, huntingSpot = huntingSpot, options = options, earliestLaunchTick = Find.TickManager.TicksGame + Mathf.RoundToInt(Mathf.Lerp(480f, 120f, Mathf.InverseLerp(0f, 20f, averageSkill))) + Mathf.RoundToInt((0.5f - options.riskTolerance) * 240f), launchDeadline = Find.TickManager.TicksGame + 2400, healthAtLaunch = prey.health.summaryHealth.SummaryHealthPercent, lastPreyPosition = prey.Position };
            if (HerdsMod.Settings.enableFieldcraftEquipment && options.useFieldcraftGear) ApplySelectedResources(record, hunters, options, huntingSpot);
            for (int i = 0; i < hunters.Count; i++)
            {
                Pawn hunter = hunters[i];
                float side = i % 2 == 0 ? 1f : -1f;
                int rank = (i + 1) / 2;
                Vector2 offset;
                string role;
                if (i == 0) { offset = wind * 16f; role = "spotter"; }
                else if (i <= 2) { offset = wind * 5f + crosswind * side * (11f + rank * 2f); role = "flanker"; }
                else if (i == 3) { offset = wind * 8f; role = "closer"; }
                else { float angle = (i - 3) * 55f * Mathf.Deg2Rad; offset = wind * 14f + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 7f; role = "support"; }
                bool rangedWeapon = hunter.equipment?.Primary?.def.IsRangedWeapon == true;
                float huntingSkill = ColonistHuntingUtility.HuntingSkill(hunter, prey.def);
                if (HerdsMod.Settings.enableWeaponAwareTactics)
                {
                    if (!rangedWeapon) { offset = -wind * (18f + options.riskTolerance * 6f); role = "driver"; }
                    else
                    {
                        float range = hunter.equipment.PrimaryEq?.PrimaryVerb?.verbProps?.range ?? 20f;
                        float preferred = Mathf.Clamp(range * 0.62f, 9f, 28f);
                        offset = offset.normalized * Mathf.Lerp(preferred + 4f, preferred - 2f, options.riskTolerance);
                        role = range < 24f ? role + " bow" : role + " rifle";
                    }
                }
                int positioningError = Mathf.RoundToInt(Mathf.Lerp(7f, 0f, Mathf.InverseLerp(0f, 16f, huntingSkill)));
                if (positioningError > 0)
                {
                    float errorAngle = ((hunter.thingIDNumber * 53) % 360) * Mathf.Deg2Rad;
                    offset += new Vector2(Mathf.Cos(errorAngle), Mathf.Sin(errorAngle)) * positioningError;
                }
                IntVec3 desired = prey.Position + new IntVec3(Mathf.RoundToInt(offset.x), 0, Mathf.RoundToInt(offset.y));
                float safeRadius = rangedWeapon ? 9f : 12f;
                IntVec3 stage = FindSafeStagingCell(hunter, prey, desired.ClampInsideMap(map), safeRadius);
                IntVec3 waypoint = FindSafeApproachWaypoint(hunter, prey, stage, safeRadius);
                record.hunters.Add(hunter); record.staging[hunter] = stage; record.roles[hunter] = role;
                Job job = JobMaker.MakeJob(HerdsDefOf.Herds_FieldcraftHunt, prey, stage);
                if (waypoint.IsValid && waypoint != stage) { job.targetC = waypoint; record.approachWaypoints[hunter] = waypoint; }
                job.playerForced = true; job.locomotionUrgency = LocomotionUrgency.Amble;
                hunter.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("GroupHuntRole", "role=" + role + " staging=" + stage + " waypoint=" + (waypoint.IsValid ? waypoint.ToString() : "direct") + " safeRadius=" + safeRadius.ToString("0") + " group=" + hunters.Count + " skill=" + huntingSkill.ToString("0.0") + " gear=" + (record.gearBonuses.TryGetValue(hunter, out float usedBonus) ? usedBonus.ToString("0.0") : "0") + " weapon=" + (hunter.equipment?.Primary?.LabelShortCap.ToString() ?? "unarmed"), hunter, prey);
            }
            hunts[prey] = record;
            if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("GroupHuntPlan", "hunters=" + hunters.Count + " risk=" + options.riskTolerance.ToString("0.00") + " useGear=" + options.useFieldcraftGear + " spot=" + (huntingSpot?.Position.ToString() ?? "none") + " earliest=" + record.earliestLaunchTick + " deadline=" + record.launchDeadline, hunters[0], prey);
            Messages.Message("Coordinated hunt forming: " + hunters.Count + " hunters will approach and launch together.", prey, MessageTypeDefOf.PositiveEvent, false);
        }

        private void ApplySelectedResources(CoordinatedHuntRecord record, List<Pawn> hunters, HuntPlanOptions options, Thing huntingSpot)
        {
            foreach (string defName in options.selectedResources ?? new HashSet<string>())
            {
                HuntResourceDef resource = DefDatabase<HuntResourceDef>.GetNamedSilentFail(defName);
                if (resource == null) continue;
                if (resource.use == HuntResourceUse.ReusableSingle)
                {
                    if (hunters.Any(hunter => FindCarriedOrNearby(hunter, resource.thingDef, huntingSpot, 12f) != null))
                        for (int i = 0; i < hunters.Count; i++) AddGearBonus(record, hunters[i], resource.fieldcraftBonus);
                    continue;
                }
                for (int i = 0; i < hunters.Count; i++)
                {
                    Pawn hunter = hunters[i];
                    if (resource.use == HuntResourceUse.ScentChargePerHunter)
                    {
                        if (TryConsumeScentMask(hunter, huntingSpot, resource.sourceBuildingDef)) AddGearBonus(record, hunter, resource.fieldcraftBonus);
                    }
                    else
                    {
                        Thing item = FindCarriedOrNearby(hunter, resource.thingDef, huntingSpot, 12f);
                        if (item != null) { item.SplitOff(1).Destroy(DestroyMode.Vanish); AddGearBonus(record, hunter, resource.fieldcraftBonus); }
                    }
                }
            }
        }

        private static void AddGearBonus(CoordinatedHuntRecord record, Pawn hunter, float bonus)
        {
            record.gearBonuses[hunter] = (record.gearBonuses.TryGetValue(hunter, out float current) ? current : 0f) + bonus;
        }

        private IntVec3 FindSafeStagingCell(Pawn hunter, Pawn prey, IntVec3 desired, float minimumDistance)
        {
            IntVec3 best = IntVec3.Invalid;
            float bestScore = float.MaxValue;
            int count = GenRadial.NumCellsInRadius(7f);
            float minimumSquared = minimumDistance * minimumDistance;
            for (int i = 0; i < count; i++)
            {
                IntVec3 cell = desired + GenRadial.RadialPattern[i];
                if (!cell.InBounds(map) || !cell.Standable(map) || cell.IsForbidden(hunter) || cell.DistanceToSquared(prey.Position) < minimumSquared) continue;
                if (!hunter.CanReach(cell, PathEndMode.OnCell, Danger.Deadly)) continue;
                float score = cell.DistanceToSquared(desired);
                if (score < bestScore) { best = cell; bestScore = score; }
            }
            if (best.IsValid) return best;
            int fallbackCount = GenRadial.NumCellsInRadius(minimumDistance + 8f);
            for (int i = 0; i < fallbackCount; i++)
            {
                IntVec3 cell = prey.Position + GenRadial.RadialPattern[i];
                float distanceSquared = cell.DistanceToSquared(prey.Position);
                if (distanceSquared < minimumSquared || distanceSquared > (minimumDistance + 7f) * (minimumDistance + 7f)) continue;
                if (!cell.InBounds(map) || !cell.Standable(map) || cell.IsForbidden(hunter) || !hunter.CanReach(cell, PathEndMode.OnCell, Danger.Deadly)) continue;
                float score = cell.DistanceToSquared(desired);
                if (score < bestScore) { best = cell; bestScore = score; }
            }
            return best.IsValid ? best : hunter.Position;
        }

        private IntVec3 FindSafeApproachWaypoint(Pawn hunter, Pawn prey, IntVec3 stage, float safeRadius)
        {
            if (DistanceToSegmentSquared(prey.Position, hunter.Position, stage) >= safeRadius * safeRadius) return IntVec3.Invalid;
            IntVec3 best = IntVec3.Invalid;
            float bestScore = float.MinValue;
            int count = GenRadial.NumCellsInRadius(safeRadius + 5f);
            for (int i = 0; i < count; i++)
            {
                IntVec3 cell = prey.Position + GenRadial.RadialPattern[i];
                float radiusSquared = cell.DistanceToSquared(prey.Position);
                if (radiusSquared < safeRadius * safeRadius || radiusSquared > (safeRadius + 4f) * (safeRadius + 4f)) continue;
                if (!cell.InBounds(map) || !cell.Standable(map) || cell.IsForbidden(hunter) || !hunter.CanReach(cell, PathEndMode.OnCell, Danger.Deadly)) continue;
                float clearance = Mathf.Min(DistanceToSegmentSquared(prey.Position, hunter.Position, cell), DistanceToSegmentSquared(prey.Position, cell, stage));
                float score = clearance - cell.DistanceToSquared(stage) * 0.02f;
                if (score > bestScore) { best = cell; bestScore = score; }
            }
            return best;
        }

        private static float DistanceToSegmentSquared(IntVec3 point, IntVec3 start, IntVec3 end)
        {
            Vector2 p = new Vector2(point.x, point.z), a = new Vector2(start.x, start.z), b = new Vector2(end.x, end.z);
            Vector2 segment = b - a;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared <= 0.001f) return (p - a).sqrMagnitude;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, segment) / lengthSquared);
            return (p - (a + segment * t)).sqrMagnitude;
        }

        public bool NotifyReady(Pawn prey, Pawn hunter)
        {
            if (prey == null || hunter == null || !hunts.TryGetValue(prey, out CoordinatedHuntRecord record) || record.resolved) return false;
            if (prey.Dead)
            {
                EndHunt(record, prey.LabelShortCap + " was killed before the hunters finished staging.", "killed-during-staging", MessageTypeDefOf.PositiveEvent);
                return true;
            }
            if (!prey.Spawned)
            {
                bool hidden = map.GetComponent<HerdMapComponent>()?.IsHidden(prey) == true;
                EndHunt(record, hidden ? prey.LabelShortCap + " escaped into a hiding place before the hunters finished staging." : prey.LabelShortCap + " left the map before the hunters finished staging.", hidden ? "hidden-during-staging" : "escaped-during-staging", MessageTypeDefOf.NeutralEvent);
                return true;
            }
            if (record.staging.TryGetValue(hunter, out IntVec3 stage) && hunter.Position.DistanceToSquared(stage) <= 4) record.ready.Add(hunter);
            int active = record.hunters.Count(participant => participant?.Spawned == true && !participant.Downed && !record.retreated.Contains(participant));
            int ready = record.ready.Count(participant => participant?.Spawned == true && !participant.Downed && !record.retreated.Contains(participant));
            if (!record.launched && active > 0 && ready >= active && Find.TickManager.TicksGame >= record.earliestLaunchTick)
            {
                record.launched = true;
                record.phase = "Assault";
                record.lastSeenTick = Find.TickManager.TicksGame;
                int longestPursuit = Find.TickManager.TicksGame;
                for (int i = 0; i < record.hunters.Count; i++)
                {
                    Pawn participant = record.hunters[i];
                    float skill = ColonistHuntingUtility.HuntingSkill(participant, prey.def);
                    float rest = participant.needs?.rest?.CurLevelPercentage ?? 0.75f;
                    float health = participant.health.summaryHealth.SummaryHealthPercent;
                    float equipment = record.gearBonuses.TryGetValue(participant, out float bonus) ? bonus : 0f;
                    int duration = Mathf.RoundToInt(Mathf.Lerp(5400f, 10500f, record.options?.riskTolerance ?? 0.5f) + skill * 150f + rest * 1200f + health * 900f + equipment * 90f);
                    int deadline = Find.TickManager.TicksGame + duration;
                    record.enduranceDeadlines[participant] = deadline;
                    longestPursuit = Mathf.Max(longestPursuit, deadline);
                }
                record.pursuitDeadline = longestPursuit;
                for (int i = 0; i < record.hunters.Count; i++)
                {
                    Pawn participant = record.hunters[i];
                    participant?.skills?.Learn(SkillDefOf.Animals, 90f, true);
                    participant?.skills?.Learn(participant.equipment?.Primary?.def.IsRangedWeapon == true ? SkillDefOf.Shooting : SkillDefOf.Melee, 45f, true);
                    participant?.Map?.GetComponent<HuntingKnowledgeMapComponent>()?.Learn(participant, prey.def, 18f);
                }
                if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("GroupHuntLaunch", "ready=" + ready + " active=" + active + " barrier=all-available", hunter, prey);
                Messages.Message("The fieldcraft hunt has launched against " + prey.LabelShortCap + ".", prey, MessageTypeDefOf.NeutralEvent, false);
                if (HerdsMod.Settings.enableAdaptivePreyResponses)
                {
                    Pawn driver = record.hunters.FirstOrDefault(participant => participant.equipment?.Primary?.def.IsRangedWeapon != true) ?? record.hunters.OrderBy(participant => participant.Position.DistanceToSquared(prey.Position)).FirstOrDefault();
                    prey.Map?.GetComponent<HerdMapComponent>()?.NotifyThreat(prey, driver, 1200);
                }
            }
            return record.launched;
        }

        public float StealthBonus(Pawn hunter)
        {
            foreach (CoordinatedHuntRecord record in hunts.Values) if (record.gearBonuses.TryGetValue(hunter, out float bonus)) return bonus;
            return 0f;
        }

        public bool HasActiveHunt(Pawn prey) => prey != null && hunts.TryGetValue(prey, out CoordinatedHuntRecord record) && !record.resolved;

        public bool TryGetSpotStatus(Thing spot, out string status, out string details, out float progress, out Pawn prey)
        {
            CoordinatedHuntRecord record = hunts.Values.FirstOrDefault(item => item.huntingSpot == spot && !item.resolved);
            if (record == null)
            {
                status = details = null; progress = 0f; prey = null; return false;
            }
            prey = record.prey;
            int active = record.hunters.Count(hunter => hunter?.Spawned == true && !hunter.Downed && !record.retreated.Contains(hunter));
            if (!record.launched)
            {
                status = "Hunt: staging " + record.ready.Count + "/" + Mathf.Max(1, active);
                progress = Mathf.Clamp01(0.08f + 0.47f * record.ready.Count / Mathf.Max(1f, active));
            }
            else
            {
                float targetHealth = prey?.health?.summaryHealth?.SummaryHealthPercent ?? 0f;
                status = "Hunt: " + record.phase.ToLowerInvariant() + (record.retreated.Count > 0 ? " (" + record.retreated.Count + " withdrew)" : "");
                progress = Mathf.Clamp01(0.55f + (1f - targetHealth) * 0.4f);
            }
            float retreat = Mathf.Lerp(0.72f, 0.30f, record.options?.riskTolerance ?? 0.5f);
            float riskValue = record.options?.riskTolerance ?? 0.5f;
            string riskLabel = riskValue < 0.34f ? "Cautious" : riskValue < 0.67f ? "Balanced" : "Bold";
            int pursuitRemaining = record.launched ? Mathf.Max(0, record.pursuitDeadline - Find.TickManager.TicksGame) : 0;
            details = "Target: " + (prey?.LabelShortCap.ToString() ?? "missing") + "\nHunters: " + active + "/" + record.hunters.Count + " active | Ready: " + record.ready.Count + "\nRisk: " + riskLabel + " | Retreat below: " + retreat.ToStringPercent() + (record.launched ? "\nPhase: " + record.phase + " | Pursuit: " + (HerdsMod.Settings.enableHuntEndurance ? pursuitRemaining.ToStringTicksToPeriod() : "unlimited") + " | Trail certainty: " + TrailCertainty(record).ToStringPercent() : "") + "\nProgress: " + progress.ToStringPercent();
            return true;
        }

        private float TrailCertainty(CoordinatedHuntRecord record)
        {
            if (!record.woundedTracking || record.bloodTrail.Count == 0) return 0f;
            if (Find.TickManager.TicksGame - record.lastSeenTick < 180) return 1f;
            float averageSkill = record.hunters.Where(hunter => hunter != null).DefaultIfEmpty().Average(hunter => hunter == null ? 0f : ColonistHuntingUtility.HuntingSkill(hunter, record.prey?.def));
            float retention = Mathf.Max(300f, 900f + averageSkill * 90f - map.weatherManager.RainRate * 600f);
            return Mathf.Clamp01(1f - (Find.TickManager.TicksGame - record.lastTrailTick) / retention);
        }

        public void CancelHuntsFromSpot(Thing spot)
        {
            List<CoordinatedHuntRecord> cancelled = hunts.Values.Where(record => record.huntingSpot == spot).ToList();
            for (int i = 0; i < cancelled.Count; i++)
            {
                CoordinatedHuntRecord record = cancelled[i];
                for (int j = 0; j < record.hunters.Count; j++)
                {
                    Pawn hunter = record.hunters[j];
                    if (hunter?.CurJobDef == HerdsDefOf.Herds_FieldcraftHunt || hunter?.CurJob?.targetA.Thing == record.prey) hunter.jobs.EndCurrentJob(JobCondition.InterruptForced);
                }
                hunts.Remove(record.prey);
                if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("GroupHuntCancelled", "spot=" + spot.Position + " hunters=" + record.hunters.Count, record.hunters.FirstOrDefault(), record.prey);
            }
            if (cancelled.Count > 0) Messages.Message("Fieldcraft hunt ended: cancelled by the player; hunters recalled.", spot, MessageTypeDefOf.NeutralEvent, false);
        }

        public void DrawForSpot(Thing spot)
        {
            CoordinatedHuntRecord record = hunts.Values.FirstOrDefault(item => item.huntingSpot == spot && !item.resolved);
            if (record == null) return;
            Color state = record.launched ? Color.red : Color.yellow;
            GenDraw.DrawRadiusRing(spot.Position, 1.1f, state);
            if (record.prey?.Spawned == true) GenDraw.DrawLineBetween(spot.Position.ToVector3Shifted(), record.prey.Position.ToVector3Shifted(), record.launched ? SimpleColor.Red : SimpleColor.Yellow);
            for (int i = 0; i < record.hunters.Count; i++)
            {
                Pawn hunter = record.hunters[i];
                if (hunter?.Spawned != true) continue;
                GenDraw.DrawLineBetween(spot.Position.ToVector3Shifted(), hunter.Position.ToVector3Shifted(), record.ready.Contains(hunter) ? SimpleColor.Green : SimpleColor.Yellow);
            }
        }

        public List<string> DebugOverviewLines()
        {
            List<string> lines = new List<string>();
            foreach (CoordinatedHuntRecord record in hunts.Values)
            {
                lines.Add("COLONIST HUNT | prey=" + record.prey?.LabelShortCap + " | phase=" + record.phase + " | trail=" + record.bloodTrail.Count + " | pursuitRemaining=" + Mathf.Max(0, record.pursuitDeadline - Find.TickManager.TicksGame).ToStringTicksToPeriod() + " | spot=" + (record.huntingSpot?.Position.ToString() ?? "none") + " | launched=" + record.launched + " resolved=" + record.resolved + " ready=" + record.ready.Count + "/" + record.hunters.Count + " risk=" + (record.options?.riskTolerance ?? 0.5f).ToString("0.00"));
                for (int i = 0; i < record.hunters.Count; i++)
                {
                    Pawn hunter = record.hunters[i];
                    lines.Add("  " + hunter?.LabelShortCap + " | role=" + (record.roles.TryGetValue(hunter, out string role) ? role : "hunter") + " | stage=" + (record.staging.TryGetValue(hunter, out IntVec3 stage) ? stage.ToString() : "-") + " | ready=" + record.ready.Contains(hunter) + " | endurance=" + (record.enduranceDeadlines.TryGetValue(hunter, out int deadline) ? Mathf.Max(0, deadline - Find.TickManager.TicksGame).ToStringTicksToPeriod() : "-") + " | skill=" + ColonistHuntingUtility.HuntingSkill(hunter, record.prey?.def).ToString("0.0") + " | gear=" + (record.gearBonuses.TryGetValue(hunter, out float bonus) ? bonus.ToString("0.0") : "0"));
                }
            }
            return lines.Count > 0 ? lines : new List<string> { "No active colonist hunts." };
        }

        public override void MapComponentDraw()
        {
            base.MapComponentDraw();
            if (!Prefs.DevMode || !FieldcraftDebug.HuntOverlay || Find.CurrentMap != map) return;
            foreach (CoordinatedHuntRecord record in hunts.Values)
            {
                if (record.prey?.Spawned == true) GenDraw.DrawRadiusRing(record.prey.Position, 1.2f, Color.red);
                for (int trailIndex = 0; trailIndex < record.bloodTrail.Count; trailIndex++)
                {
                    IntVec3 point = record.bloodTrail[trailIndex];
                    GenDraw.DrawRadiusRing(point, 0.32f, Color.red);
                    if (trailIndex > 0) GenDraw.DrawLineBetween(record.bloodTrail[trailIndex - 1].ToVector3Shifted(), point.ToVector3Shifted(), SimpleColor.Red);
                }
                if (record.huntingSpot?.Spawned == true)
                {
                    GenDraw.DrawRadiusRing(record.huntingSpot.Position, 0.9f, record.launched ? Color.red : Color.yellow);
                    if (record.prey?.Spawned == true) GenDraw.DrawLineBetween(record.huntingSpot.Position.ToVector3Shifted(), record.prey.Position.ToVector3Shifted(), record.launched ? SimpleColor.Red : SimpleColor.Yellow);
                }
                for (int i = 0; i < record.hunters.Count; i++)
                {
                    Pawn hunter = record.hunters[i]; if (hunter?.Spawned != true || !record.staging.TryGetValue(hunter, out IntVec3 stage)) continue;
                    GenDraw.DrawRadiusRing(stage, 0.8f, record.ready.Contains(hunter) ? Color.green : Color.yellow);
                    if (record.approachWaypoints.TryGetValue(hunter, out IntVec3 waypoint))
                    {
                        GenDraw.DrawRadiusRing(waypoint, 0.55f, Color.cyan);
                        GenDraw.DrawLineBetween(hunter.Position.ToVector3Shifted(), waypoint.ToVector3Shifted(), SimpleColor.Cyan);
                        GenDraw.DrawLineBetween(waypoint.ToVector3Shifted(), stage.ToVector3Shifted(), SimpleColor.Cyan);
                    }
                    else GenDraw.DrawLineBetween(hunter.Position.ToVector3Shifted(), stage.ToVector3Shifted(), record.ready.Contains(hunter) ? SimpleColor.Green : SimpleColor.Yellow);
                    if (record.prey?.Spawned == true) GenDraw.DrawLineBetween(stage.ToVector3Shifted(), record.prey.Position.ToVector3Shifted(), SimpleColor.Red);
                }
            }
        }

        public override void MapComponentOnGUI()
        {
            base.MapComponentOnGUI();
            if (!Prefs.DevMode || !FieldcraftDebug.HuntOverlay || Find.CurrentMap != map) return;
            foreach (CoordinatedHuntRecord record in hunts.Values)
                for (int i = 0; i < record.hunters.Count; i++)
                {
                    Pawn hunter = record.hunters[i]; if (hunter?.Spawned != true) continue;
                    string role = record.roles.TryGetValue(hunter, out string assigned) ? assigned : "hunter";
                    GenMapUI.DrawThingLabel(hunter, role + " | skill " + ColonistHuntingUtility.HuntingSkill(hunter, record.prey?.def).ToString("0.0") + " | " + (record.launched ? record.phase.ToLowerInvariant() : record.ready.Contains(hunter) ? "ready" : "moving"));
                }
        }

        private Thing FindCarriedOrNearby(Pawn pawn, ThingDef def, Thing huntingSpot, float radius)
        {
            if (def == null) return null;
            Thing carried = pawn.inventory?.innerContainer?.FirstOrDefault(thing => thing.def == def);
            if (carried != null) return carried;
            List<Thing> things = map.listerThings.ThingsOfDef(def); float best = radius * radius; Thing result = null;
            for (int i = 0; i < things.Count; i++)
            {
                float hunterDistance = things[i].Position.DistanceToSquared(pawn.Position);
                float spotDistance = huntingSpot?.Spawned == true ? things[i].Position.DistanceToSquared(huntingSpot.Position) : float.MaxValue;
                float distance = Mathf.Min(hunterDistance, spotDistance);
                if (distance <= best) { best = distance; result = things[i]; }
            }
            return result;
        }

        private bool TryConsumeScentMask(Pawn hunter, Thing huntingSpot, ThingDef stationDef = null)
        {
            stationDef ??= HerdsDefOf.Herds_ScentMaskStation;
            if (!HerdsMod.Settings.enableScentMasking || stationDef == null) return false;
            List<Thing> stations = map.listerThings.ThingsOfDef(stationDef);
            Building_WildlifeTool best = null; float bestDistance = 144f;
            for (int i = 0; i < stations.Count; i++)
            {
                if (stations[i] is not Building_WildlifeTool station || !station.active || station.scentCharges <= 0) continue;
                float distance = huntingSpot?.Spawned == true ? station.Position.DistanceToSquared(huntingSpot.Position) : station.Position.DistanceToSquared(hunter.Position);
                if (distance <= bestDistance) { best = station; bestDistance = distance; }
            }
            if (best == null) return false;
            best.scentCharges--;
            map.GetComponent<WildlifeFieldcraftMapComponent>()?.ApplyScentMask(hunter);
            return true;
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class PlayerWildlifeCommandPatch
    {
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> values, Pawn __instance)
        {
            foreach (Gizmo gizmo in values) yield return gizmo;
            if (__instance?.Spawned != true) yield break;
            if (HerdsMod.Settings.enableWildlifeSteward && WildlifeProgression.Unlocked(WildlifeCapability.Stewardship) && __instance.Faction != Faction.OfPlayer && PreyProfileDatabase.IsEligible(__instance.def))
            {
                WildlifeStewardMapComponent component = __instance.Map.GetComponent<WildlifeStewardMapComponent>();
                StewardSpeciesRecord record = component.For(__instance.def);
                if (HerdsMod.Settings.enableHuntingRegulations)
                {
                    yield return new Command_Action { defaultLabel = record.protectedSpecies ? "Unprotect species" : "Protect species", defaultDesc = "Toggle colony hunting protection for this entire species.", icon = TexCommand.ForbidOn, action = () => record.protectedSpecies = !record.protectedSpecies };
                    yield return new Command_Action { defaultLabel = "Quota: " + (record.quota < 0 ? "unlimited" : record.killsThisSeason + "/" + record.quota), defaultDesc = "Set the colony's seasonal hunting quota for this species.", icon = TexCommand.Attack, action = () => ShowQuotaMenu(__instance, record) };
                    yield return new Command_Action { defaultLabel = "Closed season: " + (record.closedSeason < 0 ? "none" : ((Season)record.closedSeason).ToString()), defaultDesc = "Protect this species from hunting during one season each year.", icon = TexCommand.ForbidOn, action = () => ShowSeasonMenu(record) };
                }
                yield return new Command_Action { defaultLabel = "Population: " + component.CountSpecies(__instance.def) + (record.desiredMinimum > 0 ? " (goal " + record.desiredMinimum + "–" + record.desiredMaximum + ")" : ""), defaultDesc = "Set the desired wild population range used by Steward reports and hunting restrictions.", icon = TexCommand.GatherSpotActive, action = () => ShowPopulationMenu(__instance, record) };
            }
        }

        private static void ShowQuotaMenu(Pawn pawn, StewardSpeciesRecord record)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            foreach (int value in new[] { -1, 0, 2, 5, 10 }) { int captured = value; options.Add(new FloatMenuOption(value < 0 ? "Unlimited" : value + " Per Season", () => record.quota = captured)); }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static void ShowPopulationMenu(Pawn pawn, StewardSpeciesRecord record)
        {
            Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
            {
                new FloatMenuOption("No Goal", () => { record.desiredMinimum = 0; record.desiredMaximum = 999; }),
                new FloatMenuOption("Small: 2–6", () => { record.desiredMinimum = 2; record.desiredMaximum = 6; }),
                new FloatMenuOption("Stable: 4–12", () => { record.desiredMinimum = 4; record.desiredMaximum = 12; }),
                new FloatMenuOption("Abundant: 8–20", () => { record.desiredMinimum = 8; record.desiredMaximum = 20; })
            }));
        }

        public static void ShowRegulationMenu(Map map, ThingDef species)
        {
            if (!WildlifeProgression.Unlocked(WildlifeCapability.Stewardship))
            {
                Messages.Message(WildlifeProgression.LockReason(WildlifeCapability.Stewardship), MessageTypeDefOf.RejectInput, false);
                return;
            }
            WildlifeStewardMapComponent component = map?.GetComponent<WildlifeStewardMapComponent>();
            StewardSpeciesRecord record = component?.For(species);
            if (record == null) return;
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            if (HerdsMod.Settings.enableHuntingRegulations)
            {
                options.Add(new FloatMenuOption(record.protectedSpecies ? "Remove Protected Status" : "Protect Species", () => record.protectedSpecies = !record.protectedSpecies));
                options.Add(new FloatMenuOption("Set Seasonal Quota…", () => ShowQuotaMenu(null, record)));
                options.Add(new FloatMenuOption("Set Closed Season…", () => ShowSeasonMenu(record)));
            }
            if (HerdsMod.Settings.enableWildlifeSteward)
            {
                options.Add(new FloatMenuOption("Population Goal: None", () => { record.desiredMinimum = 0; record.desiredMaximum = 999; }));
                options.Add(new FloatMenuOption("Population Goal: Stable (4–12)", () => { record.desiredMinimum = 4; record.desiredMaximum = 12; }));
                options.Add(new FloatMenuOption("Population Goal: Abundant (8–20)", () => { record.desiredMinimum = 8; record.desiredMaximum = 20; }));
            }
            if (options.Count > 0) Find.WindowStack.Add(new FloatMenu(options));
        }

        private static void ShowSeasonMenu(StewardSpeciesRecord record)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption> { new FloatMenuOption("No Closed Season", () => record.closedSeason = -1) };
            foreach (Season season in Enum.GetValues(typeof(Season))) { Season captured = season; options.Add(new FloatMenuOption("Close During " + season, () => record.closedSeason = (int)captured)); }
            Find.WindowStack.Add(new FloatMenu(options));
        }
    }

    [HarmonyPatch(typeof(Designator_Hunt), "CanDesignateThing")]
    public static class StewardHuntDesignationPatch
    {
        public static void Postfix(Thing t, ref AcceptanceReport __result)
        {
            if (!__result.Accepted || t is not Pawn pawn || pawn.Map == null) return;
            if (HerdsMod.Settings.enableCulturalAnimals &&
                pawn.Map.GetComponent<NotableWildlifeMapComponent>()?.For(pawn)?.culturalStatus == WildlifeCulturalStatus.Sacred)
            {
                __result = new AcceptanceReport("This animal is sacred to the colony. Change its cultural status before hunting it.");
                return;
            }
            if (!WildlifeProgression.Unlocked(WildlifeCapability.Stewardship) ||
                (!HerdsMod.Settings.enableWildlifeSteward && !HerdsMod.Settings.enableHuntingRegulations)) return;
            if (!pawn.Map.GetComponent<WildlifeStewardMapComponent>().CanHunt(pawn.def, out string reason)) __result = new AcceptanceReport(reason);
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class StewardKillPatch
    {
        public static void Prefix(Pawn __instance, DamageInfo? dinfo)
        {
            if (!HerdsMod.Settings.enableWildlifeSteward && !HerdsMod.Settings.enableHuntingRegulations &&
                !HerdsMod.Settings.enableSpeciesKnowledgeProgression && !HerdsMod.Settings.enableAnimalMemory &&
                !HerdsMod.Settings.enableWildlifeIdeology) return;
            if (__instance?.Map == null) return;
            Pawn killer = dinfo?.Instigator as Pawn;
            if (__instance.RaceProps?.Animal == true && HerdsMod.Settings.enableAnimalMemory)
                __instance.Map.GetComponent<WildlifeMemoryMapComponent>()?.RememberPackMemberKilled(__instance, killer);
            if (killer?.Faction != Faction.OfPlayer) return;
            if (__instance.RaceProps?.Animal == true)
            {
                Map map = __instance.Map;
                NotableAnimalRecord notable = map.GetComponent<NotableWildlifeMapComponent>()?.For(__instance);
                WildlifeIdeologyUtility.Notify(map,
                    notable != null ? WildlifeIdeologyEvent.NotableKill : WildlifeIdeologyEvent.HuntKill,
                    __instance, killer);
                if (HerdsMod.Settings.enableAnimalMemory)
                {
                    foreach (Pawn witness in map.mapPawns.AllPawnsSpawned)
                        if (witness != __instance && witness.RaceProps?.Animal == true && witness.def == __instance.def &&
                            witness.Faction != Faction.OfPlayer && witness.Position.InHorDistOf(__instance.Position, 28f))
                            WildlifeMemoryUtility.Remember(witness, killer, AnimalMemoryKind.KinKilled, 0.8f);
                }
            }
            if (WildlifeProgression.Unlocked(WildlifeCapability.Stewardship) && (HerdsMod.Settings.enableWildlifeSteward || HerdsMod.Settings.enableHuntingRegulations)) __instance.Map.GetComponent<WildlifeStewardMapComponent>()?.RecordKill(__instance);
            if (HerdsMod.Settings.enableSpeciesKnowledgeProgression && __instance.RaceProps?.Animal == true) __instance.Map.GetComponent<HuntingKnowledgeMapComponent>()?.Learn(killer, __instance.def, 120f, true);
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    public static class SpeciesKnowledgeDamagePatch
    {
        public static void Prefix(Thing __instance, DamageInfo dinfo)
        {
            if (!HerdsMod.Settings.enableSpeciesKnowledgeProgression || __instance is not Pawn victim || victim.Map == null || victim.RaceProps?.Animal != true || dinfo.Instigator is not Pawn attacker || attacker.Faction != Faction.OfPlayer) return;
            victim.Map.GetComponent<HuntingKnowledgeMapComponent>()?.Learn(attacker, victim.def, 4f);
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    public static class TreeHiddenPreyDamagePatch
    {
        public static void Prefix(Thing __instance, DamageInfo dinfo)
        {
            if (HerdsMod.Settings?.enablePreyAndHerds != true || __instance?.Spawned != true || !(__instance is Plant plant) || plant.def.plant?.IsTree != true) return;
            __instance.Map?.GetComponent<HerdMapComponent>()?.TryHitTreeHiddenPrey(__instance, dinfo);
        }
    }

}
