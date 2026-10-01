using System;
using System.Collections.Generic;
using System.Linq;
using Rimblings.Core;
using RimWorld;
using UnityEngine;
using Verse;

namespace Rimblings;

public sealed class MainButtonWorker_VoiceEditor : MainButtonWorker_ToggleTab
{
    public override bool Visible => !RimblingsMod.Settings.HideVoiceEditor && base.Visible;
}

// Character Editor uses the same small main tab to launch its editor window.
// Our definition is loaded normally from XML, so no private toolbar fields or
// runtime def injection are needed.
public sealed class MainTabWindow_VoiceEditor : MainTabWindow
{
    public override Vector2 InitialSize => Vector2.one;
    public MainTabWindow_VoiceEditor() { closeOnAccept = closeOnCancel = false; }
    public override void DoWindowContents(Rect inRect)
    {
        if (!Find.WindowStack.IsOpen<Dialog_VoiceEditor>()) Find.WindowStack.Add(new Dialog_VoiceEditor());
        Close();
    }
}

public sealed class Dialog_VoiceEditor : Window
{
    private readonly List<Pawn> pawns = new List<Pawn>();
    private SpeechController? controller;
    private Pawn? pawn;
    private VoiceProfile draft;
    private bool restoreGenerated;
    private bool discardPrompt;
    private bool allowClose;
    private string search = string.Empty;
    private string status = string.Empty;
    private int phrase;
    private Vector2 pawnScroll;
    private Vector2 controlsScroll;
    private float controlsHeight = 560;
    public override Vector2 InitialSize => new Vector2(Mathf.Min(940, UI.screenWidth), Mathf.Min(730, UI.screenHeight));

    public Dialog_VoiceEditor()
    {
        doCloseX = true;
        forcePause = true;
        absorbInputAroundWindow = true;
        closeOnAccept = false;
    }

    public override void PostOpen()
    {
        base.PostOpen();
        controller = Current.Game?.GetComponent<SpeechController>();
        RefreshPawns();
        Pawn? selected = Find.Selector.SingleSelectedObject as Pawn;
        LoadPawn(selected != null && pawns.Contains(selected) ? selected : pawns.FirstOrDefault());
    }

    private void RefreshPawns()
    {
        pawns.Clear();
        pawns.AddRange(PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists_NoLodgers
            .Where(p => EligibleColonist(p)
                && (!p.Spawned || !p.Map.fogGrid.IsFogged(p.Position)))
            .Distinct().OrderBy(p => p.LabelShortCap.ToString()));
    }

    private static bool EligibleColonist(Pawn candidate) => !candidate.Destroyed && !candidate.Dead
        && candidate.RaceProps?.Humanlike == true && candidate.IsFreeColonist && !candidate.IsQuestLodger();

    private bool Dirty => pawn != null && EligibleColonist(pawn) && controller != null
        && ((restoreGenerated && controller.HasVoiceOverride(pawn)) || !Same(draft, controller.GetVoice(pawn)));

    private static bool Same(VoiceProfile a, VoiceProfile b) => a.Pitch == b.Pitch && a.Cadence == b.Cadence
        && a.Depth == b.Depth && a.Tone == b.Tone && a.BankIndex == b.BankIndex;

    private void LoadPawn(Pawn? selected)
    {
        controller?.StopPreview();
        pawn = selected != null && EligibleColonist(selected) && pawns.Contains(selected) ? selected : null;
        restoreGenerated = false;
        status = string.Empty;
        controlsScroll = Vector2.zero;
        if (pawn != null && controller != null) draft = controller.GetVoice(pawn);
    }

    private void WithDiscardConfirmation(Action action)
    {
        if (!Dirty) { action(); return; }
        if (discardPrompt) return;
        discardPrompt = true;
        Find.WindowStack.Add(new Dialog_MessageBox("Rimblings.EditorDiscardDesc".Translate(),
            "Rimblings.EditorDiscard".Translate(), () => { discardPrompt = false; action(); },
            "Cancel".Translate(), () => discardPrompt = false,
            acceptAction: () => { discardPrompt = false; action(); }, cancelAction: () => discardPrompt = false));
    }

    public override void Close(bool doCloseSound = true)
    {
        if (allowClose || !Dirty) { base.Close(doCloseSound); return; }
        WithDiscardConfirmation(() => { allowClose = true; base.Close(doCloseSound); });
    }

    public override void PreClose()
    {
        controller?.StopPreview();
        pawns.Clear();
        pawn = null;
        controller = null;
        base.PreClose();
    }

    public override void DoWindowContents(Rect inRect)
    {
        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(0, 0, inRect.width - 36, 38), "Rimblings.VoiceEditor".Translate());
        Text.Font = GameFont.Small;
        Widgets.Label(new Rect(0, 42, inRect.width, 52), "Rimblings.EditorSaveHelp".Translate());
        if (controller == null)
        {
            Widgets.Label(new Rect(0, 104, inRect.width, 50), "Rimblings.EditorUnavailable".Translate());
            return;
        }
        if (pawn != null && !EligibleColonist(pawn)) LoadPawn(pawns.FirstOrDefault(EligibleColonist));
        float leftWidth = Mathf.Min(270, inRect.width * 0.34f);
        float paneHeight = Mathf.Max(100, inRect.height - 148);
        DrawPawnList(new Rect(0, 102, leftWidth, paneHeight));
        DrawControls(new Rect(leftWidth + 18, 102, inRect.width - leftWidth - 18, paneHeight));
        if (Widgets.ButtonText(new Rect(inRect.width - 120, inRect.height - 32, 120, 30), "Close".Translate())) Close();
    }

    private void DrawPawnList(Rect rect)
    {
        search = Widgets.TextField(new Rect(rect.x, rect.y, rect.width, 30), search);
        TooltipHandler.TipRegion(new Rect(rect.x, rect.y, rect.width, 30), "Rimblings.EditorSearch".Translate());
        List<Pawn> filtered = pawns.Where(p => EligibleColonist(p) && (search.Length == 0
            || p.LabelShortCap.ToString().IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0)).ToList();
        var outer = new Rect(rect.x, rect.y + 38, rect.width, rect.height - 74);
        var view = new Rect(0, 0, outer.width - 18, Mathf.Max(outer.height, filtered.Count * 54));
        Widgets.BeginScrollView(outer, ref pawnScroll, view);
        for (int i = 0; i < filtered.Count; i++)
        {
            Pawn candidate = filtered[i];
            var row = new Rect(0, i * 54, view.width, 50);
            if (row.yMax < pawnScroll.y || row.y > pawnScroll.y + outer.height) continue;
            var portraitRect = new Rect(row.xMax - 50, row.y + 3, 44, 44);
            float labelWidth = Mathf.Max(0, portraitRect.x - 12);
            if (candidate == pawn) Widgets.DrawHighlightSelected(row);
            Widgets.DrawHighlightIfMouseover(row);
            Widgets.Label(new Rect(6, row.y + 3, labelWidth, 24), candidate.LabelShortCap);
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(6, row.y + 27, labelWidth, 20), candidate.Faction?.Name ?? "Rimblings.EditorNoFaction".Translate().ToString());
            Text.Font = GameFont.Small;
            if (Event.current.type == EventType.Repaint)
                GUI.DrawTexture(portraitRect, PortraitsCache.Get(candidate, portraitRect.size, Rot4.South));
            TooltipHandler.TipRegion(row, candidate.LabelCap.ToString());
            if (Widgets.ButtonInvisible(row) && candidate != pawn) WithDiscardConfirmation(() => LoadPawn(candidate));
        }
        if (filtered.Count == 0) Widgets.Label(new Rect(0, 0, view.width, 60), "Rimblings.EditorNoPawns".Translate());
        Widgets.EndScrollView();
        var shortVoiceRect = new Rect(rect.x, rect.yMax - 30, rect.width, 30);
        bool wasShort = RimblingsMod.Settings.ShortenWords;
        Widgets.CheckboxLabeled(shortVoiceRect, "Rimblings.EditorShortVoice".Translate(), ref RimblingsMod.Settings.ShortenWords);
        TooltipHandler.TipRegion(shortVoiceRect, "Rimblings.EditorShortVoiceDesc".Translate());
        if (wasShort != RimblingsMod.Settings.ShortenWords)
        {
            controller?.StopPreview();
            LoadedModManager.GetMod<RimblingsMod>().WriteSettings();
        }
    }

    private void DrawControls(Rect rect)
    {
        if (pawn == null || !EligibleColonist(pawn))
        {
            Widgets.Label(rect, "Rimblings.EditorSelectPawn".Translate());
            return;
        }
        var view = new Rect(0, 0, rect.width - 18, Mathf.Max(rect.height, controlsHeight));
        Widgets.BeginScrollView(rect, ref controlsScroll, view);
        var listing = new Listing_Standard();
        listing.Begin(new Rect(0, 0, view.width, 100000));
        Text.Font = GameFont.Medium;
        listing.Label(pawn.LabelShortCap);
        Text.Font = GameFont.Small;
        listing.Label((Dirty ? "Rimblings.EditorUnsaved" : controller!.HasVoiceOverride(pawn)
            ? "Rimblings.EditorCustom" : "Rimblings.EditorGenerated").Translate());
        listing.Gap(8);

        float pitch = draft.Pitch, speed = draft.Cadence, depth = draft.Depth;
        Control(listing, "Rimblings.EditorPitch", ref pitch, 0.7f, 1.8f, pitch.ToString("F2") + "x");
        Control(listing, "Rimblings.EditorDepth", ref depth, 0, 1, depth.ToString("P0"));
        Control(listing, "Rimblings.EditorSpeed", ref speed, 0.75f, 1.35f, speed.ToString("F2") + "x");
        VoiceProfile adjusted = new VoiceProfile(pitch, speed, draft.Seed, draft.Tone, draft.BankIndex, depth);
        if (!Same(adjusted, draft)) { draft = adjusted; restoreGenerated = false; status = string.Empty; }

        if (Widgets.ButtonText(listing.GetRect(32), "Rimblings.EditorTone".Translate() + ": " + ("Rimblings.Tone" + draft.Tone).Translate()))
        {
            var options = new List<FloatMenuOption>();
            foreach (VoiceTone tone in Enum.GetValues(typeof(VoiceTone)))
            {
                VoiceTone chosen = tone;
                options.Add(new FloatMenuOption(("Rimblings.Tone" + chosen).Translate(), () =>
                { draft = new VoiceProfile(draft.Pitch, draft.Cadence, draft.Seed, chosen, draft.BankIndex, draft.Depth); restoreGenerated = false; status = string.Empty; }));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }
        listing.Gap(6);
        if (Widgets.ButtonText(listing.GetRect(32), "Rimblings.EditorBank".Translate() + ": " + BankLabel(draft.BankIndex)))
        {
            var options = new List<FloatMenuOption>();
            for (int i = 0; i < 8; i++)
            {
                int chosen = i;
                options.Add(new FloatMenuOption(BankLabel(chosen), () =>
                { draft = new VoiceProfile(draft.Pitch, draft.Cadence, draft.Seed, draft.Tone, chosen, draft.Depth); restoreGenerated = false; status = string.Empty; }));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }
        listing.Gap(14);
        if (Widgets.ButtonText(listing.GetRect(32), "Rimblings.EditorPhrase".Translate() + ": " + Phrase()))
        {
            var options = new List<FloatMenuOption>();
            for (int i = 0; i < 3; i++)
            {
                int chosen = i;
                options.Add(new FloatMenuOption(("Rimblings.EditorPhrase" + chosen).Translate(), () => phrase = chosen));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }
        listing.Gap(6);
        Rect previewRow = listing.GetRect(32);
        if (Widgets.ButtonText(new Rect(previewRow.x, previewRow.y, (previewRow.width - 8) / 2, 32), "Rimblings.EditorPreview".Translate()))
            status = controller!.Preview(pawn, draft, Phrase()) ? string.Empty : "Rimblings.EditorPreviewFailed".Translate().ToString();
        if (Widgets.ButtonText(new Rect(previewRow.center.x + 4, previewRow.y, (previewRow.width - 8) / 2, 32), "Rimblings.EditorStop".Translate())) controller!.StopPreview();
        listing.Gap(14);
        if (Widgets.ButtonText(listing.GetRect(34), "Rimblings.EditorSave".Translate()))
        {
            controller!.StopPreview();
            if (restoreGenerated) controller.ResetVoice(pawn); else controller.SaveVoice(pawn, draft);
            restoreGenerated = false;
            status = "Rimblings.EditorApplied".Translate().ToString();
        }
        listing.Gap(6);
        if (Widgets.ButtonText(listing.GetRect(32), "Rimblings.EditorRestore".Translate()))
        { controller!.StopPreview(); draft = SpeechController.GeneratedVoice(pawn); restoreGenerated = true; status = string.Empty; }
        listing.Gap(6);
        if (Widgets.ButtonText(listing.GetRect(32), "Rimblings.EditorRevert".Translate())) LoadPawn(pawn);
        if (!string.IsNullOrEmpty(status)) { listing.Gap(8); listing.Label(status); }
        controlsHeight = listing.CurHeight + 12;
        listing.End();
        Widgets.EndScrollView();
    }

    private string Phrase() => ("Rimblings.EditorPhrase" + phrase).Translate().ToString();
    private static string BankLabel(int bank) => (bank < 4 ? "Rimblings.EditorFemaleBank" : "Rimblings.EditorMaleBank").Translate(bank % 4 + 1).ToString();
    private static void Control(Listing_Standard listing, string key, ref float value, float min, float max, string display)
    {
        float top = listing.CurHeight;
        listing.Label(key.Translate() + ": " + display);
        value = listing.Slider(value, min, max);
        TooltipHandler.TipRegion(new Rect(0, top, listing.ColumnWidth, listing.CurHeight - top), (key + "Desc").Translate());
        listing.Gap(4);
    }
}
