using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Herds
{
    public sealed class ExpeditionEventChoiceDef
    {
        public string label;
        public string description;
        public string result;
        public bool turnBack;
        public int delayTicks;
        public float encounterModifier;
        public float successModifier;
        public float dangerModifier;
        public float knowledgeGain;
        public bool injureParty;
    }

    public sealed class ExpeditionEventDef : Def
    {
        public float chance = 0.12f;
        public List<ExpeditionStage> stages;
        public List<ExpeditionObjective> objectives;
        public List<string> biomeTags;
        public string narrative;
        public List<ExpeditionEventChoiceDef> choices = new List<ExpeditionEventChoiceDef>();

        public bool Applies(HuntingExpeditionRecord record, BiomeDef biome)
        {
            if (record == null || choices.NullOrEmpty() ||
                (stages != null && stages.Count > 0 && !stages.Contains(record.stage)) ||
                (objectives != null && objectives.Count > 0 && !objectives.Contains(record.objective)))
                return false;
            if (biomeTags.NullOrEmpty()) return true;
            string identity = ((biome?.defName ?? string.Empty) + " " +
                (biome?.label ?? string.Empty)).ToLowerInvariant();
            return biomeTags.Exists(tag => !tag.NullOrEmpty() &&
                identity.Contains(tag.ToLowerInvariant()));
        }
    }

    public sealed class ExpeditionTrailPath : IExposable
    {
        public int fromTile = -1;
        public int toTile = -1;
        public int createdTick;
        public ThingDef targetSpecies;

        public void ExposeData()
        {
            Scribe_Values.Look(ref fromTile, "fromTile", -1);
            Scribe_Values.Look(ref toTile, "toTile", -1);
            Scribe_Values.Look(ref createdTick, "createdTick");
            Scribe_Defs.Look(ref targetSpecies, "targetSpecies");
        }

        public bool Connects(int a, int b) =>
            (fromTile == a && toTile == b) || (fromTile == b && toTile == a);
    }
}
