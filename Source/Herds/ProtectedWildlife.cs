using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Herds
{
    public static class ProtectedWildlifeUtility
    {
        public static void RespondToAttack(Pawn animal, Thing attacker)
        {
            if (animal?.Spawned != true || attacker == null) return;
            NotableWildlifeMapComponent notable = animal.Map.GetComponent<NotableWildlifeMapComponent>();
            NotableAnimalRecord record = notable?.For(animal);
            if (record?.intent != NotableAnimalIntent.Protect) return;
            int now = Find.TickManager.TicksGame;
            if (now - record.lastProtectionResponseTick < 600) return;
            record.lastProtectionResponseTick = now;

            if (attacker is Pawn playerAttacker && playerAttacker.Faction == Faction.OfPlayer)
            {
                if (playerAttacker.CurJobDef == JobDefOf.AttackMelee ||
                    playerAttacker.CurJobDef == JobDefOf.AttackStatic ||
                    playerAttacker.CurJobDef == JobDefOf.Hunt)
                    playerAttacker.jobs.EndCurrentJob(JobCondition.Incompletable);
                Messages.Message(playerAttacker.LabelShortCap + " stopped attacking protected " +
                    animal.LabelShortCap + ".", animal, MessageTypeDefOf.CautionInput, false);
                notable.NotifyProtectedAttack(record, attacker, 0);
                return;
            }

            Pawn hostile = attacker as Pawn;
            if (hostile?.Spawned != true) return;
            bool genuineThreat = hostile.HostileTo(Faction.OfPlayer) ||
                WildlifeSpeciesClassification.IsPredator(hostile.def) ||
                hostile.InMentalState;
            if (!genuineThreat) return;
            List<Pawn> responders = animal.Map.mapPawns.FreeColonistsSpawned.Where(pawn =>
                pawn?.Downed == false && !pawn.Drafted && !pawn.InMentalState &&
                !pawn.WorkTagIsDisabled(WorkTags.Violent) &&
                pawn.Position.InHorDistOf(animal.Position, 55f) &&
                pawn.CanReach(hostile, PathEndMode.Touch, Danger.Deadly))
                .OrderBy(pawn => pawn.Position.DistanceToSquared(animal.Position)).Take(3).ToList();
            for (int i = 0; i < responders.Count; i++)
            {
                Pawn responder = responders[i];
                bool ranged = responder.equipment?.Primary?.def.IsRangedWeapon == true;
                Job job = JobMaker.MakeJob(ranged ? JobDefOf.AttackStatic : JobDefOf.AttackMelee, hostile);
                job.playerForced = true;
                job.expiryInterval = 1800;
                responder.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            }
            if (responders.Count > 0)
                Messages.Message(responders.Count + (responders.Count == 1 ? " colonist is" : " colonists are") +
                    " moving to defend protected " + animal.LabelShortCap + ".", animal,
                    MessageTypeDefOf.ThreatSmall, false);
            notable.NotifyProtectedAttack(record, attacker, responders.Count);
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    public static class ProtectedWildlifeDamagePatch
    {
        public static void Prefix(Thing __instance, DamageInfo dinfo)
        {
            if (__instance is Pawn animal && animal.RaceProps?.Animal == true)
                ProtectedWildlifeUtility.RespondToAttack(animal, dinfo.Instigator);
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class ProtectedWildlifeDeathPatch
    {
        public static void Prefix(Pawn __instance, DamageInfo? dinfo)
        {
            if (__instance?.Map == null || __instance.RaceProps?.Animal != true) return;
            NotableWildlifeMapComponent component = __instance.Map.GetComponent<NotableWildlifeMapComponent>();
            NotableAnimalRecord record = component?.For(__instance);
            if (record?.intent != NotableAnimalIntent.Protect) return;
            component.NotifyProtectedDeath(record, dinfo?.Instigator);
        }
    }
}
