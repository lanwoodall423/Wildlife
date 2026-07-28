using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Packs;

public sealed class ITab_Pack : ITab
{
    private Vector2 scroll;

    public ITab_Pack()
    {
        size = new Vector2(560f, 480f);
        labelKey = "Packs_WildlifeKnowledge";
    }

    public override bool IsVisible
    {
        get
        {
            if (!PacksMod.Settings.enablePredators || !PacksMod.Settings.enableWildlifeKnowledge) return false;
            Thing selected = SelThing;
            if (selected?.Spawned != true) return false;
            PackMapComponent component = selected.Map.GetComponent<PackMapComponent>();
            if (selected is Pawn pawn) return pawn.Faction == Faction.OfPlayer
                ? pawn.RaceProps?.predator == true
                : component?.PackFor(pawn) != null;
            return selected is Building_PredatorDen den && component?.PackForDen(den) != null;
        }
    }

    protected override void FillTab()
    {
        Thing selected = SelThing;
        PackMapComponent component = selected?.Map?.GetComponent<PackMapComponent>();
        Pawn selectedPawn = selected as Pawn;
        PackSnapshot pack = selectedPawn != null ? component?.PackFor(selectedPawn) : component?.PackForDen(selected as Building_PredatorDen);
        if (pack == null && selectedPawn?.Faction == Faction.OfPlayer &&
            selectedPawn.RaceProps?.predator == true)
        {
            DrawTamedPredator(selectedPawn);
            return;
        }
        if (pack == null) return;

        Pawn focus = selectedPawn ?? pack.leader;
        AnimalPackSettings settings = PacksMod.Settings.For(pack.species);
        bool solitary = settings.socialStrategy == PredatorSocialStrategy.Solitary;
        bool observed = component.IsObserved(focus);
        Rect rect = new Rect(0f, 0f, size.x, size.y).ContractedBy(12f);

        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(rect.x, rect.y, rect.width, 32f), focus?.LabelShortCap ?? pack.species.LabelCap);
        Text.Font = GameFont.Small;
        Widgets.FillableBar(new Rect(rect.x, rect.y + 32f, rect.width, 7f), observed ? 1f : 0.12f);
        string identity = solitary ? "Solitary predator" : settings.socialStrategy + " • " + pack.members.Count + " visible";
        Widgets.Label(new Rect(rect.x, rect.y + 43f, rect.width, 22f), identity + " • " + (observed ? "Observed" : "Not observed"));

        if (!observed)
        {
            Rect notice = new Rect(rect.x, rect.y + 70f, rect.width, 68f);
            Widgets.DrawMenuSection(notice);
            Widgets.Label(notice.ContractedBy(9f), solitary
                ? "Observe this predator from a safe distance or an active observation post to reveal its hunting style, territory, home, and current behavior."
                : "Observe this group from a safe distance or an active observation post to reveal its leadership, roles, hunting style, territory, den, and current prey.");
            return;
        }

        float top = rect.y + 70f;
        Rect social = new Rect(rect.x, top, rect.width * 0.49f, 106f);
        Rect behavior = new Rect(rect.x + rect.width * 0.51f, top, rect.width * 0.49f, 106f);
        Widgets.DrawMenuSection(social);
        Widgets.DrawMenuSection(behavior);

        if (solitary)
        {
            DrawSectionTitle(social, "Individual Hunter");
            DrawValueRow(new Rect(social.x + 8f, social.y + 29f, social.width - 16f, 22f), "Lifestyle", "Solitary",
                "This predator normally hunts, rests, and controls territory alone.");
            DrawValueRow(new Rect(social.x + 8f, social.y + 51f, social.width - 16f, 22f), "Method", HuntingStyleLabel(settings.huntingStyle),
                "The positioning and attack method this predator prefers while hunting.");
            DrawValueRow(new Rect(social.x + 8f, social.y + 73f, social.width - 16f, 22f), "Humans", BoldnessLabel(pack),
                "How willing this predator is to remain near or threaten colonists.");
        }
        else
        {
            DrawSectionTitle(social, "Social Group");
            DrawThingLink(new Rect(social.x + 8f, social.y + 31f, social.width - 16f, 27f), "Leader", pack.leader,
                "The predator that anchors group movement, territory, and coordinated hunts.");
            DrawValueRow(new Rect(social.x + 8f, social.y + 61f, social.width - 16f, 24f), "Structure", settings.socialStrategy.ToString(),
                "The species' normal social organization. Pair, family, and pack predators coordinate differently.");
        }

        DrawSectionTitle(behavior, "Current Behavior");
        HuntPhase phase = component.HuntPhaseFor(focus);
        string state = pack.prey != null ? phase.ToString() : pack.record?.claimedCorpse?.Spawned == true ? "Guarding Kill" : "Roaming";
        DrawValueRow(new Rect(behavior.x + 8f, behavior.y + 29f, behavior.width - 16f, 22f), "State", state,
            "The predator's current activity or stage of a coordinated hunt.");
        DrawThingLink(new Rect(behavior.x + 8f, behavior.y + 51f, behavior.width - 16f, 22f), "Prey", pack.prey,
            "The animal currently being stalked or attacked.");
        DrawValueRow(new Rect(behavior.x + 8f, behavior.y + 73f, behavior.width - 16f, 22f), "Hunt Style", HuntingStyleLabel(settings.huntingStyle),
            "The species' preferred balance of stealth, pursuit, ambush, and positioning.");

        Rect territory = new Rect(rect.x, top + 114f, rect.width, 132f);
        Widgets.DrawMenuSection(territory);
        DrawSectionTitle(territory, "Home and Territory");
        Thing home = selectedPawn?.Faction == Faction.OfPlayer
            ? selectedPawn.ownership?.OwnedBed
            : pack.record?.denMarker;
        string homeLabel = selectedPawn?.Faction == Faction.OfPlayer ? "Animal Bed" : "Den";
        DrawThingLink(new Rect(territory.x + 9f, territory.y + 31f, territory.width * 0.48f, 28f), homeLabel, home,
            selectedPawn?.Faction == Faction.OfPlayer
                ? "Tamed predators use assigned animal beds or sleeping spots instead of wild dens."
                : "The concealed place where this predator or group rests and raises young.");
        DrawValueRow(new Rect(territory.x + territory.width * 0.51f, territory.y + 31f, territory.width * 0.47f, 28f),
            "Territory", settings.territoryRadius.ToString("0") + " tiles",
            "The approximate radius this predator patrols, hunts within, and may defend against rival predators.");
        int age = Mathf.Max(0, Find.TickManager.TicksGame - (pack.record?.formedTick ?? Find.TickManager.TicksGame));
        DrawValueRow(new Rect(territory.x + 9f, territory.y + 62f, territory.width - 18f, 27f), "Established",
            age.ToStringTicksToPeriod() + " ago", "How long this individual territory or social group has been established.");
        DrawValueRow(new Rect(territory.x + 9f, territory.y + 92f,
            territory.width - 18f, 27f), "Landscape",
            HerdsCompatibility.EcologicalRoleSummary(focus),
            HerdsCompatibility.EcologicalRoleTooltip(focus));

        float membersTop = top + 254f;
        string signalSummary = HerdsCompatibility.PredatorSignalSummary(focus);
        if (!signalSummary.NullOrEmpty())
        {
            Rect signals = new Rect(rect.x, membersTop, rect.width, 72f);
            Widgets.DrawMenuSection(signals);
            DrawSectionTitle(signals, "Signal Reading");
            Rect signalRow = new Rect(signals.x + 9f, signals.y + 30f,
                signals.width - 18f, 30f);
            Widgets.DrawHighlightIfMouseover(signalRow);
            Widgets.Label(signalRow, signalSummary);
            TooltipHandler.TipRegion(signalRow,
                HerdsCompatibility.PredatorSignalTooltip(focus));
            membersTop += 80f;
        }
        if (Prefs.DevMode)
        {
            Rect dev = new Rect(rect.x, membersTop, rect.width, 38f);
            Widgets.DrawBoxSolid(dev, new Color(0.12f, 0.22f, 0.28f, 0.28f));
            if (Widgets.ButtonText(new Rect(dev.x + 6f, dev.y + 5f, dev.width * 0.48f - 9f, 28f), "DEV: Jump to Center"))
                CameraJumper.TryJump(pack.center, selected.Map);
            if (Widgets.ButtonText(new Rect(dev.x + dev.width * 0.5f + 3f, dev.y + 5f, dev.width * 0.48f - 9f, 28f), "DEV: Jump to Movement Target"))
                CameraJumper.TryJump(pack.movementTarget, selected.Map);
            membersTop += 44f;
        }
        Widgets.Label(new Rect(rect.x + 4f, membersTop, rect.width, 24f), solitary ? "Current Activity" : "Group Members");
        DrawMembers(new Rect(rect.x, membersTop + 26f, rect.width, rect.yMax - membersTop - 26f), pack, component, selectedPawn);
    }

    private void DrawTamedPredator(Pawn pawn)
    {
        AnimalPackSettings settings = PacksMod.Settings.For(pawn.def);
        Rect rect = new Rect(0f, 0f, size.x, size.y).ContractedBy(12f);
        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(rect.x, rect.y, rect.width, 32f), pawn.LabelShortCap);
        Text.Font = GameFont.Small;
        Widgets.FillableBar(new Rect(rect.x, rect.y + 32f, rect.width, 7f), 1f);
        Widgets.Label(new Rect(rect.x, rect.y + 43f, rect.width, 22f),
            "Tamed predator • Colony animal");

        Rect life = new Rect(rect.x, rect.y + 72f, rect.width * 0.49f, 112f);
        Rect instincts = new Rect(rect.x + rect.width * 0.51f, rect.y + 72f,
            rect.width * 0.49f, 112f);
        Widgets.DrawMenuSection(life);
        Widgets.DrawMenuSection(instincts);
        DrawSectionTitle(life, "Colony Life");
        DrawValueRow(new Rect(life.x + 8f, life.y + 31f, life.width - 16f, 24f),
            "State", pawn.Downed ? "Downed" : pawn.CurJobDef?.LabelCap ?? "Idle",
            "This animal's current activity.");
        DrawThingLink(new Rect(life.x + 8f, life.y + 59f, life.width - 16f, 27f),
            "Animal Bed", pawn.ownership?.OwnedBed,
            "Tamed predators use animal beds or sleeping spots rather than wild dens.");

        DrawSectionTitle(instincts, "Wild Instincts");
        DrawValueRow(new Rect(instincts.x + 8f, instincts.y + 31f, instincts.width - 16f, 24f),
            "Lifestyle", settings.socialStrategy.ToString(),
            "The social structure this species normally uses in the wild.");
        DrawValueRow(new Rect(instincts.x + 8f, instincts.y + 59f, instincts.width - 16f, 24f),
            "Hunt Style", HuntingStyleLabel(settings.huntingStyle),
            "The hunting method inherited from its wild ancestry.");

        Rect status = new Rect(rect.x, rect.y + 194f, rect.width, 96f);
        Widgets.DrawMenuSection(status);
        DrawSectionTitle(status, "Domesticated Status");
        Widgets.Label(new Rect(status.x + 10f, status.y + 32f, status.width - 20f, 54f),
            "This predator belongs to the colony. It does not claim territory or maintain a den, " +
            "and it follows normal animal training, scheduling, and sleeping behavior.");
    }

    private void DrawMembers(Rect outer, PackSnapshot pack, PackMapComponent component, Pawn selected)
    {
        Rect view = new Rect(0f, 0f, outer.width - 16f, Mathf.Max(outer.height, pack.members.Count * 32f));
        Widgets.BeginScrollView(outer, ref scroll, view);
        for (int i = 0; i < pack.members.Count; i++)
        {
            Pawn pawn = pack.members[i];
            Rect row = new Rect(0f, i * 32f, view.width, 28f);
            if (pawn == selected) Widgets.DrawHighlightSelected(row); else Widgets.DrawHighlightIfMouseover(row);
            Widgets.Label(new Rect(8f, row.y + 3f, view.width * 0.58f, 24f), pawn.LabelShortCap + " • " + component.RoleFor(pawn));
            string status = pawn.Downed ? "Downed" : pawn.InMentalState ? pawn.MentalStateDef.LabelCap : pawn.CurJobDef?.LabelCap ?? "Idle";
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(new Rect(view.width * 0.60f, row.y, view.width * 0.37f, 28f), status);
            Text.Anchor = TextAnchor.UpperLeft;
            if (Widgets.ButtonInvisible(row) && pawn.Spawned)
            {
                Find.Selector.ClearSelection();
                Find.Selector.Select(pawn);
                CameraJumper.TryJump(pawn);
            }
        }
        Widgets.EndScrollView();
    }

    private static void DrawSectionTitle(Rect section, string title)
    {
        Text.Font = GameFont.Tiny;
        GUI.color = new Color(0.72f, 0.82f, 0.9f);
        Widgets.Label(new Rect(section.x + 9f, section.y + 7f, section.width - 18f, 20f), title.ToUpperInvariant());
        GUI.color = Color.white;
        Text.Font = GameFont.Small;
    }

    private static void DrawThingLink(Rect row, string label, Thing thing, string tooltip)
    {
        Widgets.DrawHighlightIfMouseover(row);
        Widgets.Label(new Rect(row.x + 3f, row.y, row.width * 0.38f, row.height), label);
        Text.Anchor = TextAnchor.MiddleRight;
        GUI.color = thing?.Spawned == true ? new Color(0.55f, 0.82f, 1f) : Color.gray;
        Widgets.Label(new Rect(row.x + row.width * 0.38f, row.y, row.width * 0.59f, row.height), thing?.Spawned == true ? thing.LabelShortCap : "None");
        GUI.color = Color.white;
        Text.Anchor = TextAnchor.UpperLeft;
        TooltipHandler.TipRegion(row, tooltip + (thing?.Spawned == true ? "\n\nClick to select " + thing.LabelShortCap + "." : ""));
        if (thing?.Spawned == true && Widgets.ButtonInvisible(row))
        {
            Find.Selector.ClearSelection();
            Find.Selector.Select(thing);
            CameraJumper.TryJump(thing);
        }
    }

    private static void DrawValueRow(Rect row, string label, string value, string tooltip)
    {
        Widgets.DrawHighlightIfMouseover(row);
        Widgets.Label(new Rect(row.x + 3f, row.y, row.width * 0.42f, row.height), label);
        Text.Anchor = TextAnchor.MiddleRight;
        GUI.color = new Color(0.82f, 0.88f, 0.92f);
        Widgets.Label(new Rect(row.x + row.width * 0.42f, row.y, row.width * 0.55f, row.height), value);
        GUI.color = Color.white;
        Text.Anchor = TextAnchor.UpperLeft;
        TooltipHandler.TipRegion(row, tooltip);
    }

    private static string BoldnessLabel(PackSnapshot pack)
    {
        if (!PacksMod.Settings.enablePredatorBoldness || pack.record == null) return "Unknown";
        return pack.record.humanBoldness < 0.3f ? "Wary" : pack.record.humanBoldness < 0.65f ? "Cautious" : "Bold";
    }

    private static string HuntingStyleLabel(PredatorHuntingStyle style)
    {
        return style.ToString();
    }
}
