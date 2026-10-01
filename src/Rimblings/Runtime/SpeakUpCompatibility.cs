using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace Rimblings;

// No compile-time dependency on SpeakUp or Interaction Bubbles.
internal static class SpeakUpCompatibility
{
    private sealed class Pending
    {
        internal float Added;
        internal bool Consumed;
    }
    private const float MaxDelay = 2f;
    private static ConditionalWeakTable<LogEntry, Pending> pending = new ConditionalWeakTable<LogEntry, Pending>();
    private static PropertyInfo? bubbleEntry;
    private static bool active;
    private static bool ready;

    internal static void Initialize(Harmony harmony)
    {
        active = ModsConfig.IsActive("jpt.speakup");
        if (!active || ready) return;
        try
        {
            Type? bubble = AccessTools.TypeByName("Bubbles.Core.Bubble");
            MethodInfo? getText = bubble == null ? null : AccessTools.DeclaredMethod(bubble, "GetText", Type.EmptyTypes);
            bubbleEntry = bubble == null ? null : AccessTools.Property(bubble, "Entry");
            if (getText == null || getText.ReturnType != typeof(string)
                || bubbleEntry?.GetGetMethod() == null || !typeof(LogEntry).IsAssignableFrom(bubbleEntry.PropertyType))
                throw new MissingMethodException("Interaction Bubbles' Bubble.GetText/Entry API is unavailable.");
            harmony.Patch(getText, postfix: new HarmonyMethod(typeof(SpeakUpCompatibility), nameof(BubbleText))
            {
                priority = Priority.Last
            });
            ready = true;
        }
        catch (Exception ex)
        {
            Log.Warning("[Rimblings] SpeakUp compatibility could not start; two-pawn speech is disabled to avoid generating extra dialogue. " + ex.Message);
        }
    }

    internal static bool Defer(LogEntry entry)
    {
        if (!active || !(entry is PlayLogEntry_Interaction)) return false;
        if (ready && !pending.TryGetValue(entry, out _))
            pending.Add(entry, new Pending { Added = Time.realtimeSinceStartup });
        return true;
    }

    private static void BubbleText(object __instance, string __result)
    {
        try
        {
            if (!(bubbleEntry?.GetValue(__instance, null) is LogEntry entry)
                || !pending.TryGetValue(entry, out Pending state) || state.Consumed) return;
            // Consume before checking audibility. Rebuilding a bubble, unpausing
            // or returning to a map must not replay an already observed event.
            state.Consumed = true;
            float delay = Time.realtimeSinceStartup - state.Added;
            if (delay < 0 || delay > MaxDelay || string.IsNullOrWhiteSpace(__result)) return;
            Pawn? speaker = SocialLogHook.Speaker(entry);
            if (speaker != null) Current.Game?.GetComponent<SpeechController>()?.Queue(speaker, __result);
        }
        catch (Exception ex)
        {
            Log.WarningOnce("[Rimblings] SpeakUp bubble skipped: " + ex.Message, 194072705);
        }
    }

    internal static void Reset() => pending = new ConditionalWeakTable<LogEntry, Pending>();
}
