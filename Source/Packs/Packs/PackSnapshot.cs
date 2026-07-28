using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Packs;

public sealed class PackSnapshot
{
	public int id;

	public PackRecord record;

	public ThingDef species;

	public Faction faction;

	public Pawn leader;

	public Pawn prey;

	public IntVec3 center;

	public IntVec3 movementTarget;

	public readonly List<Pawn> members = new List<Pawn>();

	public string Label => record?.Label ?? ((string)(species.LabelCap + " group"));
}
