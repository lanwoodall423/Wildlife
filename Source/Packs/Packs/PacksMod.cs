using HarmonyLib;
using Verse;

namespace Packs;

public sealed class PacksMod : Mod
{
    public static PacksMod Instance;
    public static PacksSettings Settings;

    public static readonly Harmony Harmony = new Harmony("lan.packsandpredators");

    public PacksMod(ModContentPack content)
        : base(content)
    {
        Instance = this;
        Settings = GetSettings<PacksSettings>();
    }

    public override string SettingsCategory() => null;

    public override void WriteSettings()
    {
        PacksSettings.ClearSpeciesCaches();
        PacksStartup.ApplyWildGroupSizes();
        PacksStartup.RefreshTabs();
        if (Current.Game?.Maps != null)
        {
            for (int i = 0; i < Current.Game.Maps.Count; i++)
                Current.Game.Maps[i].GetComponent<PackMapComponent>()?.ForceRefresh();
        }
        base.WriteSettings();
    }
}
