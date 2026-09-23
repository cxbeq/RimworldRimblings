using HarmonyLib;
using UnityEngine;
using Verse;

namespace Rimblings;

public sealed class RimblingsMod : Mod
{
    public static RimblingsSettings Settings = new RimblingsSettings();
    public static string Root = string.Empty;
    private Vector2 scroll;
    private float contentHeight = 800;
    public RimblingsMod(ModContentPack content) : base(content)
    {
        Root = content.RootDir;
        Settings = GetSettings<RimblingsSettings>();
        new Harmony("paddy.rimblings").PatchAll(typeof(RimblingsMod).Assembly);
    }
    public override string SettingsCategory() => "Rimblings";
    public override void DoSettingsWindowContents(Rect rect)
    {
        // Keep the reset action visible while the options scroll. The listing
        // gets a tall layout rect so later rows never wrap into another column.
        var scrollRect = new Rect(rect.x, rect.y, rect.width, Mathf.Max(0f, rect.height - 42f));
        var view = new Rect(0f, 0f, scrollRect.width - 20f, Mathf.Max(scrollRect.height, contentHeight));
        Widgets.BeginScrollView(scrollRect, ref scroll, view);
        var listing = new Listing_Standard();
        listing.Begin(new Rect(0f, 0f, view.width, 100000f));
        listing.Label("Rimblings.Description".Translate());
        listing.Gap(8);
        Checkbox(listing, "Rimblings.Enabled", "Rimblings.EnabledDesc", ref Settings.Enabled);
        listing.Gap(12);

        Section(listing, "Rimblings.SectionSpeech", "Rimblings.SectionSpeechDesc");
        Slider(listing, "Rimblings.Volume", "Rimblings.VolumeDesc",
            ref Settings.Volume, 0f, 1f, Settings.Volume.ToString("P0"));
        Checkbox(listing, "Rimblings.ShortenWords", "Rimblings.ShortenWordsDesc", ref Settings.ShortenWords);
        Checkbox(listing, "Rimblings.NonPlayer", "Rimblings.NonPlayerDesc", ref Settings.NonPlayer);

        Section(listing, "Rimblings.SectionHearing", "Rimblings.SectionHearingDesc");
        Slider(listing, "Rimblings.FullZoom", "Rimblings.FullZoomDesc",
            ref Settings.FullZoom, 4f, 30f, Settings.FullZoom.ToString("F1"));
        Settings.SilentZoom = Mathf.Max(Settings.SilentZoom, Settings.FullZoom + 0.5f);
        Slider(listing, "Rimblings.SilentZoom", "Rimblings.SilentZoomDesc",
            ref Settings.SilentZoom, Settings.FullZoom + 0.5f, 60f, Settings.SilentZoom.ToString("F1"));
        Slider(listing, "Rimblings.HearingRadius", "Rimblings.HearingRadiusDesc",
            ref Settings.HearingRadius, 20f, 120f, Settings.HearingRadius.ToString("F0"));

        Section(listing, "Rimblings.SectionMixing", "Rimblings.SectionMixingDesc");
        Slider(listing, "Rimblings.Ducking", "Rimblings.DuckingDesc",
            ref Settings.BackgroundGain, 0.05f, 1f, Settings.BackgroundGain.ToString("P0"));
        Checkbox(listing, "Rimblings.LimitVoices", "Rimblings.LimitVoicesDesc", ref Settings.LimitVoices);
        if (Settings.LimitVoices)
        {
            float maxVoices = Settings.MaxVoices;
            Slider(listing, "Rimblings.MaxVoices", "Rimblings.MaxVoicesDesc",
                ref maxVoices, 1f, 64f, Settings.MaxVoices.ToString());
            Settings.MaxVoices = Mathf.RoundToInt(maxVoices);
        }

        listing.Gap(8);
        contentHeight = listing.CurHeight + 12f;
        listing.End();
        Widgets.EndScrollView();

        var resetRect = new Rect(rect.xMax - 180f, rect.yMax - 32f, 180f, 30f);
        if (Widgets.ButtonText(resetRect, "Rimblings.Reset".Translate()))
        {
            Settings.Reset();
            WriteSettings();
        }
        Settings.Clamp();
    }

    private static void Section(Listing_Standard listing, string title, string description)
    {
        listing.Gap(14);
        GameFont previousFont = Text.Font;
        Text.Font = GameFont.Medium;
        listing.Label(title.Translate());
        Text.Font = GameFont.Small;
        listing.Label(description.Translate());
        Text.Font = previousFont;
        listing.Gap(4);
    }

    private static void Checkbox(Listing_Standard listing, string label, string description, ref bool value)
    {
        listing.CheckboxLabeled(label.Translate(), ref value, description.Translate());
    }

    private static void Slider(Listing_Standard listing, string label, string description,
        ref float value, float minimum, float maximum, string displayValue)
    {
        float top = listing.CurHeight;
        listing.Label(label.Translate() + ": " + displayValue);
        value = listing.Slider(value, minimum, maximum);
        TooltipHandler.TipRegion(new Rect(0, top, listing.ColumnWidth, listing.CurHeight - top), description.Translate());
        listing.Gap(4);
    }
}
