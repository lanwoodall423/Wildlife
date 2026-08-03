using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Herds
{
    public enum WildlifeToolKind
    {
        None,
        ObservationPost,
        Bait,
        PredatorDeterrent,
        Reserve,
        ScentMaskStation,
        HabitatRestoration,
        WaterSource,
        MigrationCorridor,
        ManagedBurn,
        CameraTrap,
        TelemetryStation
    }

    public sealed class Building_WildlifeTool : Building
    {
        public bool active = true;
        public float baitRemaining = 12f;
        public int scentCharges = 12;
        private float configuredRadius = -1f;
        private int nextAutomationTick;

        public WildlifeToolKind Kind
        {
            get
            {
                if (def == HerdsDefOf.Herds_ObservationPost) return WildlifeToolKind.ObservationPost;
                if (def == HerdsDefOf.Herds_WildlifeBait) return WildlifeToolKind.Bait;
                if (def == HerdsDefOf.Herds_PredatorDeterrent) return WildlifeToolKind.PredatorDeterrent;
                if (def == HerdsDefOf.Herds_WildlifeReserve) return WildlifeToolKind.Reserve;
                if (def == HerdsDefOf.Herds_ScentMaskStation) return WildlifeToolKind.ScentMaskStation;
                if (def == HerdsDefOf.Herds_HabitatRestoration) return WildlifeToolKind.HabitatRestoration;
                if (def == HerdsDefOf.Herds_WildlifeWaterSource) return WildlifeToolKind.WaterSource;
                if (def == HerdsDefOf.Herds_MigrationCorridor) return WildlifeToolKind.MigrationCorridor;
                if (def == HerdsDefOf.Herds_ManagedBurnMarker) return WildlifeToolKind.ManagedBurn;
                if (def == HerdsDefOf.Herds_CameraTrap) return WildlifeToolKind.CameraTrap;
                if (def == HerdsDefOf.Herds_TelemetryStation) return WildlifeToolKind.TelemetryStation;
                return WildlifeToolKind.None;
            }
        }

        private float DefaultInfluenceRadius => Kind == WildlifeToolKind.ObservationPost ? (WildlifeProgression.Unlocked(WildlifeCapability.WarningSystems) ? 65f : 40f) : Kind == WildlifeToolKind.Bait ? 45f : Kind == WildlifeToolKind.PredatorDeterrent ? 38f : Kind == WildlifeToolKind.Reserve ? 55f : Kind == WildlifeToolKind.HabitatRestoration ? 35f : Kind == WildlifeToolKind.WaterSource ? 28f : Kind == WildlifeToolKind.MigrationCorridor ? 45f : Kind == WildlifeToolKind.ManagedBurn ? 12f : Kind == WildlifeToolKind.CameraTrap ? 22f : Kind == WildlifeToolKind.TelemetryStation ? 60f : 0f;
        public bool HasAdjustableRadius => Kind == WildlifeToolKind.Reserve || Kind == WildlifeToolKind.HabitatRestoration || Kind == WildlifeToolKind.MigrationCorridor || Kind == WildlifeToolKind.ManagedBurn;
        public float InfluenceRadius => HasAdjustableRadius && configuredRadius > 0f ? Mathf.Clamp(configuredRadius, MinimumRadius, MaximumRadius) : DefaultInfluenceRadius;
        private int MinimumRadius => Kind == WildlifeToolKind.ManagedBurn ? 4 : 10;
        private int MaximumRadius => Kind == WildlifeToolKind.ManagedBurn ? 30 : 80;
        public bool Operational => active && FeatureEnabled() && Powered;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref active, "active", true);
            Scribe_Values.Look(ref baitRemaining, "baitRemaining", 12f);
            Scribe_Values.Look(ref scentCharges, "scentCharges", 12);
            Scribe_Values.Look(ref configuredRadius, "configuredRadius", -1f);
            Scribe_Values.Look(ref nextAutomationTick, "nextAutomationTick", 0);
        }

        public override void TickRare()
        {
            base.TickRare();
            if (!active || Map == null || !FeatureEnabled() || !Powered) return;
            int now = Find.TickManager.TicksGame;
            if (Kind == WildlifeToolKind.CameraTrap && now >= nextAutomationTick)
            {
                nextAutomationTick = now + 2500;
                Map.GetComponent<RegionalWildlifeMapComponent>()?.AutomatedSurvey(Position, InfluenceRadius);
                return;
            }
            if (Kind == WildlifeToolKind.TelemetryStation && now >= nextAutomationTick)
            {
                nextAutomationTick = now + 2500;
                Map.GetComponent<RegionalWildlifeMapComponent>()?.TelemetrySurvey();
                return;
            }
            if (Kind == WildlifeToolKind.WaterSource && now >= nextAutomationTick)
            {
                nextAutomationTick = now + 2500;
                if (VisibleOnCurrentMap()) FleckMaker.WaterRipple(Position.ToVector3Shifted(), Map, 0.35f);
                return;
            }
            if (Kind == WildlifeToolKind.PredatorDeterrent && now >= nextAutomationTick)
            {
                nextAutomationTick = now + 2500;
                if (VisibleOnCurrentMap()) FleckMaker.ThrowDustPuff(Position.ToVector3Shifted(), Map, 0.35f);
                return;
            }
            if (Kind == WildlifeToolKind.ManagedBurn && now >= nextAutomationTick)
            {
                nextAutomationTick = now + 2500;
                TendManagedBurn();
                if (VisibleOnCurrentMap())
                {
                    FleckMaker.ThrowSmoke(Position.ToVector3Shifted(), Map, 0.28f);
                    FleckMaker.ThrowFireGlow(Position.ToVector3Shifted(), Map, 0.18f);
                }
                return;
            }
            if (Kind != WildlifeToolKind.Bait) return;
            IReadOnlyList<Pawn> pawns = Map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn?.Spawned != true || pawn.Dead || !PreyProfileDatabase.IsEligible(pawn.def) || pawn.Position.DistanceToSquared(Position) > 36) continue;
                baitRemaining -= Mathf.Max(0.08f, pawn.BodySize * 0.08f);
                if (VisibleOnCurrentMap()) FleckMaker.ThrowDustPuff(Position.ToVector3Shifted(), Map, 0.22f);
                if (baitRemaining <= 0f)
                {
                    Messages.Message("Wildlife consumed the bait.", this, MessageTypeDefOf.NeutralEvent, false);
                    Destroy(DestroyMode.Vanish);
                }
                break;
            }
        }

        public override string GetInspectString()
        {
            string state = active && FeatureEnabled() ? "Active" : "Disabled";
            string result = Kind + "\nState: " + state + "\nInfluence radius: " + InfluenceRadius.ToString("0") + " cells";
            WildlifeCapability? capability = RequiredCapability();
            if (capability.HasValue && !WildlifeProgression.Unlocked(capability.Value)) result += "\nResearch locked: " + WildlifeProgression.Label(capability.Value);
            if (Kind == WildlifeToolKind.Bait) result += "\nBait remaining: " + Mathf.Max(0f, baitRemaining).ToString("0.0");
            if (Kind == WildlifeToolKind.ScentMaskStation) result += "\nApplications remaining: " + Mathf.Max(0, scentCharges);
            if (Kind == WildlifeToolKind.TelemetryStation) result += "\nTracking collars nearby: " + AvailableTrackingCollars();
            if (Kind == WildlifeToolKind.WaterSource) result += "\nWild animals periodically visit this basin to drink.";
            if (Kind == WildlifeToolKind.ManagedBurn) result += "\nControlled embers gradually suppress nearby wild brush.";
            if (Kind == WildlifeToolKind.PredatorDeterrent) result += "\nMoving scraps and noise discourage ordinary predators.";
            return result;
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos()) yield return gizmo;
            Command_Toggle toggle = new Command_Toggle
            {
                defaultLabel = active ? "Deactivate wildlife tool" : "Activate wildlife tool",
                defaultDesc = "Temporarily enable or disable this structure's wildlife influence.",
                icon = TexCommand.GatherSpotActive,
                isActive = () => active,
                toggleAction = () =>
                {
                    active = !active;
                    Map?.GetComponent<HerdMapComponent>()?.ForceRefresh();
                }
            };
            WildlifeCapability? required = RequiredCapability();
            if (required.HasValue && !WildlifeProgression.Unlocked(required.Value)) toggle.Disable(WildlifeProgression.LockReason(required.Value));
            yield return toggle;
            if (HasAdjustableRadius)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Set influence radius",
                    defaultDesc = "Adjust the area affected by this marker. Current radius: " + InfluenceRadius.ToString("0") + " cells.",
                    icon = TexCommand.GatherSpotActive,
                    action = () => Find.WindowStack.Add(new Dialog_Slider(
                        value => "Influence radius: " + value + " cells",
                        MinimumRadius,
                        MaximumRadius,
                        value =>
                        {
                            configuredRadius = value;
                            Map?.GetComponent<HerdMapComponent>()?.ForceRefresh();
                        },
                        Mathf.RoundToInt(InfluenceRadius),
                        1f))
                };
            }
            if (Kind == WildlifeToolKind.ObservationPost && HerdsMod.Settings.enableMannedBlinds)
            {
                yield return new Command_Action { defaultLabel = "Man observation post", defaultDesc = "Send a colonist here to watch wildlife and hunt from concealment.", icon = TexCommand.GatherSpotActive, action = BeginManning };
            }
            if (Kind == WildlifeToolKind.ObservationPost && HerdsMod.Settings.enableAnimalCalls)
            {
                Pawn caller = ManningColonist();
                Command_Action call = new Command_Action { defaultLabel = "Use animal call", defaultDesc = caller == null ? "The observation post must be actively manned before an animal call can be used." : "Choose an animal to call. Success and attraction distance depend on " + caller.LabelShortCap + "'s personal Animal Knowledge of that species.", icon = TexCommand.ForbidOff, action = BeginAnimalCall };
                if (!WildlifeProgression.Unlocked(WildlifeCapability.AnimalHandling)) call.Disable(WildlifeProgression.LockReason(WildlifeCapability.AnimalHandling));
                else if (caller == null) call.Disable("The observation post must be actively manned.");
                yield return call;
            }
            if (Kind == WildlifeToolKind.ObservationPost &&
                HerdsMod.Settings.enableWildlifeSignalCulture)
            {
                Pawn listener = ManningColonist();
                yield return new Command_Action
                {
                    defaultLabel = "Local Wildlife Signals",
                    defaultDesc = listener == null
                        ? "Review local animal dialects and colony understanding. Man this post to decipher calls much faster."
                        : "Review the calls " + listener.LabelShortCap +
                          " can recognize while listening from this observation post.",
                    icon = TexCommand.OpenLinkedQuestTex,
                     action = () => Window_WildlifeJournal.OpenSignals(Map, ManningColonist())
                };
            }
            if (Kind == WildlifeToolKind.ObservationPost && HerdsMod.Settings.enableRegionalPopulations)
            {
                Pawn observer = ManningColonist();
                Command_Action survey = new Command_Action { defaultLabel = "Survey regional wildlife", defaultDesc = observer == null ? "The observation post must be manned." : "Improve regional population estimates using this colonist's Animals skill and Animal Knowledge.", icon = TexCommand.OpenLinkedQuestTex, action = () => Map?.GetComponent<RegionalWildlifeMapComponent>()?.Survey(ManningColonist()) };
                if (observer == null) survey.Disable("The observation post must be actively manned.");
                yield return survey;
            }
            if (Kind == WildlifeToolKind.ScentMaskStation && HerdsMod.Settings.enableScentMasking && scentCharges > 0)
            {
                yield return new Command_Action { defaultLabel = "Apply scent masking", defaultDesc = "Choose a colonist to mask their scent for six hours.", icon = TexCommand.DesirePower, action = BeginScentMasking };
            }
            if (Kind == WildlifeToolKind.TelemetryStation && HerdsMod.Settings.enableRegionalPopulations)
            {
                Command_Action tag = new Command_Action { defaultLabel = "Fit tracking collar", defaultDesc = "Fit a nearby or downed animal with a prepared tracking collar. Tagged animals improve surveys and migration forecasts.", icon = TexCommand.OpenLinkedQuestTex, action = BeginTagging };
                if (!WildlifeProgression.Unlocked(WildlifeCapability.Telemetry)) tag.Disable(WildlifeProgression.LockReason(WildlifeCapability.Telemetry));
                else if (!Powered) tag.Disable("The telemetry station requires power.");
                else if (AvailableTrackingCollars() <= 0) tag.Disable("Place a tracking collar within 12 cells of the station.");
                yield return tag;
            }
        }

        public override void DrawExtraSelectionOverlays()
        {
            base.DrawExtraSelectionOverlays();
            if (active && FeatureEnabled()) GenDraw.DrawRadiusRing(Position, InfluenceRadius, Kind == WildlifeToolKind.PredatorDeterrent ? Color.red : Kind == WildlifeToolKind.Reserve || Kind == WildlifeToolKind.HabitatRestoration ? Color.green : Kind == WildlifeToolKind.Bait ? Color.yellow : Kind == WildlifeToolKind.WaterSource || Kind == WildlifeToolKind.TelemetryStation ? Color.cyan : new Color(0.7f, 0.5f, 0.2f));
        }

        private bool FeatureEnabled()
        {
            bool enabled = Kind == WildlifeToolKind.ObservationPost ? HerdsMod.Settings.enableObservationPosts :
                Kind == WildlifeToolKind.Bait ? HerdsMod.Settings.enableWildlifeBait :
                Kind == WildlifeToolKind.PredatorDeterrent ? HerdsMod.Settings.enablePredatorDeterrents :
                Kind == WildlifeToolKind.Reserve ? HerdsMod.Settings.enableWildlifeReserves :
                Kind == WildlifeToolKind.ScentMaskStation ? HerdsMod.Settings.enableScentMasking :
                Kind == WildlifeToolKind.HabitatRestoration || Kind == WildlifeToolKind.WaterSource || Kind == WildlifeToolKind.MigrationCorridor || Kind == WildlifeToolKind.ManagedBurn ? HerdsMod.Settings.enableConservationActions :
                Kind == WildlifeToolKind.CameraTrap ? HerdsMod.Settings.enableRegionalPopulations && HerdsMod.Settings.enableCameraTraps :
                Kind == WildlifeToolKind.TelemetryStation ? HerdsMod.Settings.enableRegionalPopulations && HerdsMod.Settings.enableTelemetry : false;
            WildlifeCapability? required = RequiredCapability();
            return enabled && (!required.HasValue || WildlifeProgression.Unlocked(required.Value));
        }

        private bool Powered => GetComp<CompPowerTrader>()?.PowerOn ?? true;

        private bool VisibleOnCurrentMap()
        {
            return Find.CurrentMap == Map && Find.CameraDriver?.CurrentViewRect.ExpandedBy(2).Contains(Position) == true;
        }

        private void TendManagedBurn()
        {
            int count = GenRadial.NumCellsInRadius(InfluenceRadius);
            int start = (Find.TickManager.TicksGame / 2500 + thingIDNumber) % Mathf.Max(1, count);
            int samples = Mathf.Min(96, count);
            int step = Mathf.Max(1, count / samples);
            for (int offset = 0; offset < samples; offset++)
            {
                IntVec3 cell = Position + GenRadial.RadialPattern[(start + offset * step) % count];
                if (!cell.InBounds(Map)) continue;
                List<Thing> things = cell.GetThingList(Map);
                for (int i = 0; i < things.Count; i++)
                {
                    if (things[i] is not Plant plant || plant.def.plant?.IsTree == true || plant.sown) continue;
                    plant.Growth = Mathf.Max(0.05f, plant.Growth - 0.12f);
                    return;
                }
            }
        }

        private WildlifeCapability? RequiredCapability()
        {
            if (Kind == WildlifeToolKind.Bait) return WildlifeCapability.FeedingGrounds;
            if (Kind == WildlifeToolKind.Reserve || Kind == WildlifeToolKind.MigrationCorridor) return WildlifeCapability.Stewardship;
            if (Kind == WildlifeToolKind.ScentMaskStation) return WildlifeCapability.Fieldcraft;
            if (Kind == WildlifeToolKind.HabitatRestoration) return WildlifeCapability.TreeHabitat;
            if (Kind == WildlifeToolKind.WaterSource) return WildlifeCapability.HabitatSupport;
            if (Kind == WildlifeToolKind.ManagedBurn) return WildlifeCapability.ManagedBurns;
            if (Kind == WildlifeToolKind.CameraTrap) return WildlifeCapability.CameraMonitoring;
            if (Kind == WildlifeToolKind.TelemetryStation) return WildlifeCapability.Telemetry;
            return null;
        }

        private void BeginManning()
        {
            TargetingParameters parameters = new TargetingParameters { canTargetPawns = true, canTargetHumans = true, canTargetAnimals = false, canTargetLocations = false, validator = target => target.Thing is Pawn pawn && pawn.Faction == Faction.OfPlayer && pawn.Spawned && !pawn.Downed };
            Find.Targeter.BeginTargeting(parameters, target =>
            {
                OrderManning((Pawn)target.Thing);
            });
        }

        public void OrderManning(Pawn pawn)
        {
            if (pawn?.Spawned != true || pawn.Downed || pawn.Faction != Faction.OfPlayer || Map != pawn.Map) return;
            Job job = JobMaker.MakeJob(HerdsDefOf.Herds_ManObservationPost, this);
            job.playerForced = true;
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        private void BeginAnimalCall()
        {
            Pawn caller = ManningColonist();
            if (caller == null) { Messages.Message("The observation post is no longer manned.", this, MessageTypeDefOf.RejectInput, false); return; }
            WildlifeFieldcraftMapComponent fieldcraft = Map?.GetComponent<WildlifeFieldcraftMapComponent>();
            WildlifeSignalCultureMapComponent signals =
                Map?.GetComponent<WildlifeSignalCultureMapComponent>();
            List<IGrouping<ThingDef, Pawn>> species = Map.mapPawns.AllPawnsSpawned
                .Where(pawn => pawn?.Spawned == true && !pawn.Dead && pawn.Faction != Faction.OfPlayer && PreyProfileDatabase.IsEligible(pawn.def))
                .GroupBy(pawn => pawn.def)
                .OrderBy(group => group.Key.label)
                .ToList();
            if (species.Count == 0) { Messages.Message("No callable wildlife species are currently on the map.", this, MessageTypeDefOf.RejectInput, false); return; }
            List<FloatMenuOption> options = new List<FloatMenuOption>(species.Count);
            for (int i = 0; i < species.Count; i++)
            {
                IGrouping<ThingDef, Pawn> group = species[i];
                ThingDef chosenSpecies = group.Key;
                int count = group.Count();
                int level = fieldcraft.AnimalCallKnowledge(caller, chosenSpecies);
                float chance = Mathf.Clamp01(fieldcraft.AnimalCallChance(level, caller) *
                    (signals?.PlayerImitationFactor(caller, chosenSpecies) ?? 1f));
                string dialect = signals == null ? "" : "  —  " +
                    signals.UnderstandingLabel(signals.Understanding(caller, chosenSpecies)) +
                    " dialect";
                string label = chosenSpecies.LabelCap + "  —  " + count + " on map  —  " +
                    HuntingKnowledgeMapComponent.LevelLabel(level) + dialect + "  —  " +
                    chance.ToStringPercent() + " chance  —  " +
                    fieldcraft.AnimalCallDistance(level, caller).ToString("0") + " cells";
                options.Add(new FloatMenuOption(label, () => BeginSignalCall(chosenSpecies)));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void BeginSignalCall(ThingDef species)
        {
            Pawn caller = ManningColonist();
            if (caller == null)
            {
                Messages.Message("The observation post is no longer manned.", this,
                    MessageTypeDefOf.RejectInput, false);
                return;
            }
            WildlifeFieldcraftMapComponent fieldcraft =
                Map?.GetComponent<WildlifeFieldcraftMapComponent>();
            WildlifeSignalCultureMapComponent signals =
                Map?.GetComponent<WildlifeSignalCultureMapComponent>();
            if (signals == null || !HerdsMod.Settings.enablePlayerSignalImitation)
            {
                fieldcraft?.TryAnimalCall(species, this, caller);
                return;
            }
            float understanding = signals.Understanding(caller, species);
            string contactLabel = WildlifeSignalCultureMapComponent.PlayerFacingSignal(
                WildlifeSignalKind.Contact, understanding, true, false);
            string alarmLabel = WildlifeSignalCultureMapComponent.PlayerFacingSignal(
                WildlifeSignalKind.Alarm, understanding, true, false);
            string allClearLabel = WildlifeSignalCultureMapComponent.PlayerFacingSignal(
                WildlifeSignalKind.AllClear, understanding, true, false);
            List<FloatMenuOption> options = new List<FloatMenuOption>
            {
                new FloatMenuOption(contactLabel + "  —  attract one or more animals",
                    () => fieldcraft?.TryAnimalCall(species, this, ManningColonist())),
                new FloatMenuOption(alarmLabel + "  —  warn or drive animals away",
                    () => signals.TryPlayerSignal(species, this, ManningColonist(),
                        WildlifeSignalKind.Alarm)),
                new FloatMenuOption(allClearLabel + "  —  calm animals when danger has passed",
                    () => signals.TryPlayerSignal(species, this, ManningColonist(),
                        WildlifeSignalKind.AllClear))
            };
            if (understanding < 0.15f)
            {
                options[1] = new FloatMenuOption(
                    alarmLabel + "  —  requires recognizing this dialect", null);
                options[2] = new FloatMenuOption(
                    allClearLabel + "  —  requires recognizing this dialect", null);
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        public Pawn ManningColonist()
        {
            if (!active || Map == null) return null;
            IReadOnlyList<Pawn> colonists = Map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn pawn = colonists[i];
                if (pawn?.Downed == false && pawn.CurJobDef == HerdsDefOf.Herds_ManObservationPost && pawn.CurJob?.targetA.Thing == this && pawn.Position.InHorDistOf(Position, 4f)) return pawn;
            }
            return null;
        }

        private void BeginScentMasking()
        {
            TargetingParameters parameters = new TargetingParameters { canTargetPawns = true, canTargetHumans = true, canTargetAnimals = false, canTargetLocations = false, validator = target => target.Thing is Pawn pawn && pawn.Faction == Faction.OfPlayer && pawn.Spawned };
            Find.Targeter.BeginTargeting(parameters, target =>
            {
                Map?.GetComponent<WildlifeFieldcraftMapComponent>()?.ApplyScentMask((Pawn)target.Thing);
                scentCharges--;
            });
        }

        private int AvailableTrackingCollars()
        {
            if (Map == null || HerdsDefOf.Herds_TrackingCollarItem == null) return 0;
            List<Thing> collars = Map.listerThings.ThingsOfDef(HerdsDefOf.Herds_TrackingCollarItem);
            int count = 0;
            for (int i = 0; i < collars.Count; i++) if (collars[i].Position.DistanceToSquared(Position) <= 144) count += collars[i].stackCount;
            return count;
        }

        private void BeginTagging()
        {
            TargetingParameters parameters = new TargetingParameters
            {
                canTargetPawns = true,
                canTargetAnimals = true,
                canTargetHumans = false,
                canTargetLocations = false,
                validator = target => target.Thing is Pawn pawn && pawn.Spawned && pawn.RaceProps?.Animal == true &&
                    (pawn.Downed || pawn.Position.DistanceToSquared(Position) <= 144) &&
                    pawn.health?.hediffSet?.GetFirstHediffOfDef(HerdsDefOf.Herds_TrackingCollar) == null
            };
            Find.Targeter.BeginTargeting(parameters, target =>
            {
                Pawn pawn = target.Thing as Pawn;
                Thing collar = Map.listerThings.ThingsOfDef(HerdsDefOf.Herds_TrackingCollarItem)
                    .Where(thing => thing.Position.DistanceToSquared(Position) <= 144).FirstOrDefault();
                if (pawn == null || collar == null) return;
                collar.SplitOff(1).Destroy(DestroyMode.Vanish);
                pawn.health.AddHediff(HerdsDefOf.Herds_TrackingCollar);
                pawn.Map?.GetComponent<NotableWildlifeMapComponent>()?.NotifyTracked(pawn, LabelCap);
                Messages.Message(pawn.LabelShortCap + " was fitted with a wildlife tracking collar.", pawn, MessageTypeDefOf.PositiveEvent, false);
                if (WildlifeTestLog.Enabled) WildlifeTestLog.Write("TelemetryTag", "station=" + Position, pawn);
            });
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.GetOptions))]
    public static class ObservationPostFloatMenuPatch
    {
        public static void Postfix(List<Pawn> selectedPawns, Vector3 clickPos, ref FloatMenuContext context, ref List<FloatMenuOption> __result)
        {
            if (HerdsMod.Settings?.enableObservationPosts != true || selectedPawns == null || selectedPawns.Count == 0 || context?.map == null || __result == null) return;
            IntVec3 cell = IntVec3.FromVector3(clickPos);
            if (!cell.InBounds(context.map)) return;
            Building_WildlifeTool post = cell.GetThingList(context.map).OfType<Building_WildlifeTool>().FirstOrDefault(tool => tool.Kind == WildlifeToolKind.ObservationPost);
            if (post == null) return;
            Pawn current = post.ManningColonist();
            for (int i = 0; i < selectedPawns.Count; i++)
            {
                Pawn pawn = selectedPawns[i];
                if (pawn?.Spawned != true || pawn.Downed || pawn.Faction != Faction.OfPlayer || pawn.Map != post.Map) continue;
                string label = selectedPawns.Count == 1 ? "Man observation post" : "Man observation post: " + pawn.LabelShortCap;
                if (current != null && current != pawn)
                    __result.Add(new FloatMenuOption(label + " (Already manned)", null));
                else if (!pawn.CanReach(post, PathEndMode.Touch, Danger.Deadly))
                    __result.Add(new FloatMenuOption(label + " (No path)", null));
                else
                    __result.Add(new FloatMenuOption(label, () => post.OrderManning(pawn)));
            }
        }
    }
}
