using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Herds
{
    /// <summary>Resolves and plays signal vocalizations without changing signal simulation state.</summary>
    public static class WildlifeSignalAudio
    {
        public static void CaptureAndPlay(Pawn speaker, WildlifeSignalKind kind, WildlifeSignalTrace trace)
        {
            if (trace == null)
                return;
            SoundDef sound = Resolve(speaker, kind);
            trace.soundDef = sound;
            trace.soundPitch = PitchFor(speaker, trace.traceId, kind);
            trace.soundPlayed = false;
            trace.soundStatus = sound == null ? "no usable vocalization" : sound.defName;
            if (sound != null && speaker?.Spawned == true && speaker.Map != null)
                trace.soundPlayed = Play(sound, speaker.Position, speaker.Map, trace.soundPitch);
        }

        public static bool Replay(Map map, WildlifeSignalTrace trace)
        {
            if (trace?.soundDef == null || map == null || !trace.cell.IsValid)
                return false;
            return Play(trace.soundDef, trace.cell, map, trace.soundPitch);
        }

        public static SoundDef Resolve(Pawn speaker, WildlifeSignalKind kind)
        {
            if (speaker?.RaceProps?.Animal != true)
                return null;
            LifeStageAge current = CurrentLifeStage(speaker);
            SoundDef preferred = IsAlert(kind) ? current?.soundAngry : current?.soundCall;
            if (preferred != null)
                return preferred;
            SoundDef fallback = IsAlert(kind) ? current?.soundCall : current?.soundAngry;
            if (fallback != null)
                return fallback;
            for (int i = 0; i < (speaker.RaceProps.lifeStageAges?.Count ?? 0); i++)
            {
                LifeStageAge stage = speaker.RaceProps.lifeStageAges[i];
                SoundDef sound = IsAlert(kind) ? stage?.soundAngry : stage?.soundCall;
                if (sound == null)
                    sound = IsAlert(kind) ? stage?.soundCall : stage?.soundAngry;
                if (sound != null)
                    return sound;
            }
            return null;
        }

        private static LifeStageAge CurrentLifeStage(Pawn speaker)
        {
            LifeStageDef currentDef = speaker?.ageTracker?.CurLifeStage;
            List<LifeStageAge> stages = speaker?.RaceProps?.lifeStageAges;
            if (currentDef == null || stages == null)
                return null;
            for (int i = 0; i < stages.Count; i++)
                if (stages[i]?.def == currentDef)
                    return stages[i];
            return null;
        }

        public static float PitchFor(Pawn speaker, int traceId, WildlifeSignalKind kind)
        {
            if (speaker == null)
                return 1f;
            return PitchForId(speaker.thingIDNumber, traceId, kind);
        }

        public static bool SelfTest()
        {
            float first = PitchForId(17, 42, WildlifeSignalKind.Contact);
            float repeat = PitchForId(17, 42, WildlifeSignalKind.Contact);
            bool variesByIndividual = false;
            for (int id = 18; id < 32; id++)
                variesByIndividual |= !Mathf.Approximately(first,
                    PitchForId(id, 42, WildlifeSignalKind.Contact));
            return first >= 0.96f && first <= 1.04f &&
                Mathf.Approximately(first, repeat) && variesByIndividual;
        }

        private static float PitchForId(int speakerId, int traceId, WildlifeSignalKind kind)
        {
            int hash = Gen.HashCombineInt(speakerId, traceId);
            hash = Gen.HashCombineInt(hash, (int)kind);
            float individual = ((Mathf.Abs(hash % 9) - 4) * 0.004f);
            float instance = ((Mathf.Abs(Gen.HashCombineInt(hash, 17) % 5) - 2) * 0.003f);
            return Mathf.Clamp(1f + individual + instance, 0.96f, 1.04f);
        }

        private static bool Play(SoundDef sound, IntVec3 cell, Map map, float pitch)
        {
            if (sound == null || map == null || !cell.IsValid)
                return false;
            try
            {
                SoundInfo info = new TargetInfo(cell, map);
                info.volumeFactor = 0.72f;
                info.pitchFactor = Mathf.Clamp(pitch <= 0f ? 1f : pitch, 0.96f, 1.04f);
                SoundStarter.PlayOneShot(sound, info);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsAlert(WildlifeSignalKind kind) =>
            kind == WildlifeSignalKind.Alarm || kind == WildlifeSignalKind.HumanDanger;
    }
}
