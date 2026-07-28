using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace Packs;

public enum TestRollMode
{
	Natural,
	ForceSuccess,
	ForceFailure
}

public static class WildlifeTestLog
{
	private const string Prefix = "[WildlifeTest][Packs]";
	private static readonly Dictionary<string, string> transitions = new Dictionary<string, string>();
	private static readonly Dictionary<string, int> counters = new Dictionary<string, int>();
	public static bool Enabled { get; private set; }
	public static TestRollMode DetectionOutcome { get; set; }

	public static void Toggle()
	{
		SetEnabled(!Enabled, true);
	}

	public static void SetEnabledFromPartner(bool enabled)
	{
		SetEnabled(enabled, false);
	}

	public static void Write(string category, string message, Pawn pawn = null, Thing other = null)
	{
		if (!Enabled) return;
		int tick = Find.TickManager?.TicksGame ?? -1;
		Map map = pawn?.Map ?? other?.Map ?? Find.CurrentMap;
		Log.Message(Prefix + "[tick=" + tick + "][map=" + (map?.uniqueID.ToString() ?? "none") + "][" + category + "] " + message +
			ThingContext(" pawn", pawn) + ThingContext(" other", other));
	}

	public static void WriteTransition(string key, string category, string state, Pawn pawn = null, Thing other = null)
	{
		if (!Enabled) return;
		Map map = pawn?.Map ?? other?.Map ?? Find.CurrentMap;
		string scopedKey = (map?.uniqueID.ToString() ?? "none") + ":" + key;
		if (transitions.TryGetValue(scopedKey, out string previous) && previous == state) return;
		transitions[scopedKey] = state;
		Write(category, "state=" + state + (previous == null ? string.Empty : " previous=" + previous), pawn, other);
	}

	public static void Count(string outcome)
	{
		if (!Enabled) return;
		counters[outcome] = counters.TryGetValue(outcome, out int count) ? count + 1 : 1;
	}

	private static void SetEnabled(bool enabled, bool propagate)
	{
		if (Enabled != enabled)
		{
			if (enabled)
			{
				transitions.Clear();
				counters.Clear();
				Enabled = true;
				Log.Message(Prefix + " ===== DIAGNOSTIC SESSION START =====");
			}
			else
			{
				WriteSummary();
				Log.Message(Prefix + " ===== DIAGNOSTIC SESSION END =====");
				Enabled = false;
				transitions.Clear();
				counters.Clear();
			}
		}
		if (propagate) SetPartner(enabled);
	}

	private static void WriteSummary()
	{
		var keys = new List<string>(counters.Keys);
		keys.Sort(StringComparer.Ordinal);
		var parts = new List<string>(keys.Count);
		for (int i = 0; i < keys.Count; i++) parts.Add(keys[i] + "=" + counters[keys[i]]);
		Log.Message(Prefix + "[SUMMARY] " + (parts.Count == 0 ? "no recorded outcomes" : string.Join(", ", parts)));
	}

	private static void SetPartner(bool enabled)
	{
		try
		{
			Type type = AccessTools.TypeByName("Herds.WildlifeTestLog");
			if (type == null) return;
			MethodInfo method = AccessTools.Method(type, "SetEnabledFromPartner", new[] { typeof(bool) });
			method?.Invoke(null, new object[] { enabled });
		}
		catch (Exception exception)
		{
			Log.Warning(Prefix + " Could not synchronize Herds and Hiders diagnostic logging: " + exception.GetBaseException().Message);
		}
	}

	private static string ThingContext(string name, Thing thing)
	{
		if (thing == null) return string.Empty;
		return " |" + name + "=" + thing.LabelShortCap + "#" + thing.thingIDNumber + "@" + (thing.Spawned ? thing.Position.ToString() : "unspawned");
	}
}
