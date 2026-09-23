using HarmonyLib;
using UnityEngine;
using Verse;

namespace Rimblings;

public sealed class RimblingsMod : Mod
{
    public static RimblingsSettings Settings = new RimblingsSettings();
    public static string Root = string.Empty;
    private Vector2 scroll;
    private float contentHeight = 580;
    public RimblingsMod(ModContentPack content) : base(content)
    {
        Root = content.RootDir;
        Settings = GetSettings<RimblingsSettings>();
        new Harmony("paddy.rimblings").PatchAll(typeof(RimblingsMod).Assembly);
    }
    public override string SettingsCategory() => "Rimblings";
    public override void DoSettingsWindowContents(Rect rect)
    {
        var view = new Rect(0, 0, rect.width - 20, Mathf.Max(rect.height - 1, contentHeight));
        Widgets.BeginScrollView(rect, ref scroll, view);
        var listing = new Listing_Standard();
        listing.Begin(view);
        listing.Label("Rimblings.Description".Translate());
        listing.Gap(8);
        Checkbox(listing, view.width, "Rimblings.Enabled", "Rimblings.EnabledDesc", ref Settings.Enabled);
        listing.Gap(12);

        Section(listing, "Rimblings.SectionSpeech", "Rimblings.SectionSpeechDesc");
        Slider(listing, view.width, "Rimblings.Volume", "Rimblings.VolumeDesc",
            ref Settings.Volume, 0f, 1f, Settings.Volume.ToString("P0"));
        Checkbox(listing, view.width, "Rimblings.ShortenWords", "Rimblings.ShortenWordsDesc", ref Settings.ShortenWords);
        Checkbox(listing, view.width, "Rimblings.NonPlayer", "Rimblings.NonPlayerDesc", ref Settings.NonPlayer);

        Section(listing, "Rimblings.SectionHearing", "Rimblings.SectionHearingDesc");
        Slider(listing, view.width, "Rimblings.FullZoom", "Rimblings.FullZoomDesc",
            ref Settings.FullZoom, 4f, 30f, Settings.FullZoom.ToString("F1"));
        Settings.SilentZoom = Mathf.Max(Settings.SilentZoom, Settings.FullZoom + 0.5f);
        Slider(listing, view.width, "Rimblings.SilentZoom", "Rimblings.SilentZoomDesc",
            ref Settings.SilentZoom, Settings.FullZoom + 0.5f, 60f, Settings.SilentZoom.ToString("F1"));
        Slider(listing, view.width, "Rimblings.HearingRadius", "Rimblings.HearingRadiusDesc",
            ref Settings.HearingRadius, 20f, 120f, Settings.HearingRadius.ToString("F0"));

        Section(listing, "Rimblings.SectionMixing", "Rimblings.SectionMixingDesc");
        Slider(listing, view.width, "Rimblings.Ducking", "Rimblings.DuckingDesc",
            ref Settings.BackgroundGain, 0.05f, 1f, Settings.BackgroundGain.ToString("P0"));
        Checkbox(listing, view.width, "Rimblings.LimitVoices", "Rimblings.LimitVoicesDesc", ref Settings.LimitVoices);
        if (Settings.LimitVoices)
        {
            float maxVoices = Settings.MaxVoices;
            Slider(listing, view.width, "Rimblings.MaxVoices", "Rimblings.MaxVoicesDesc",
                ref maxVoices, 1f, 64f, Settings.MaxVoices.ToString());
            Settings.MaxVoices = Mathf.RoundToInt(maxVoices);
        }

        listing.Gap(16);
        if (listing.ButtonText("Rimblings.Reset".Translate()))
        {
            Settings.Reset();
            WriteSettings();
        }
        contentHeight = listing.CurHeight + 20;
        listing.End();
        Widgets.EndScrollView();
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

    private static void Checkbox(Listing_Standard listing, float width, string label, string description, ref bool value)
    {
        float top = listing.CurHeight;
        listing.CheckboxLabeled(label.Translate(), ref value);
        TooltipHandler.TipRegion(new Rect(0, top, width, listing.CurHeight - top), description.Translate());
    }

    private static void Slider(Listing_Standard listing, float width, string label, string description,
        ref float value, float minimum, float maximum, string displayValue)
    {
        float top = listing.CurHeight;
        listing.Label(label.Translate() + ": " + displayValue);
        value = listing.Slider(value, minimum, maximum);
        TooltipHandler.TipRegion(new Rect(0, top, width, listing.CurHeight - top), description.Translate());
        listing.Gap(4);
    }
}
