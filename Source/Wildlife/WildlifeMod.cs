using Verse;

namespace Wildlife
{
    // Settings are owned by HerdsMod so the InsightCanvas presentation has one authoritative owner.
    public sealed class WildlifeMod : Mod
    {
        public WildlifeMod(ModContentPack content) : base(content) { }

        public override string SettingsCategory() => null;
    }
}
