using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Rimblings.Core;
using Verse;
using Verse.Profile;

namespace Rimblings;

public static class VoiceAdapters
{
    private sealed class Adapter
    {
        internal string Id = string.Empty;
        internal Func<Pawn, VoiceTraits?> Resolve = null!;
        internal bool Failed;
    }
    private static readonly List<Adapter> Adapters = new List<Adapter>();
    // Register on the main thread during mod startup. First non-null match wins.
    public static void Register(string id, Func<Pawn, VoiceTraits?> resolver)
    {
        if (string.IsNullOrWhiteSpace(id) || resolver == null) throw new ArgumentException("An adapter needs an ID and resolver.");
        Adapters.RemoveAll(x => x.Id == id);
        Adapters.Add(new Adapter { Id = id, Resolve = resolver });
    }
    internal static VoiceTraits Get(Pawn pawn)
    {
        foreach (Adapter adapter in Adapters)
        {
            if (adapter.Failed) continue;
            try
            {
                VoiceTraits? result = adapter.Resolve(pawn);
                if (result != null) { result.Identity = pawn.GetUniqueLoadID(); return result; }
            }
            catch (Exception ex)
            {
                adapter.Failed = true;
                Log.Warning("[Rimblings] Disabled voice adapter " + adapter.Id + ": " + ex.Message);
            }
        }
        return new VoiceTraits
        {
            Identity = pawn.GetUniqueLoadID(), Sex = pawn.gender.ToString(),
            Body = pawn.story?.bodyType?.defName ?? "unknown-body",
            Head = pawn.story?.headType?.defName ?? "unknown-head",
            BiologicalAge = pawn.ageTracker?.AgeBiologicalYearsFloat ?? double.NaN,
            LifeExpectancy = pawn.RaceProps?.lifeExpectancy ?? 80
        };
    }
}

[HarmonyPatch(typeof(PlayLog), nameof(PlayLog.Add))]
internal static class SocialLogHook
{
    private static readonly FieldInfo? InteractionInitiator = AccessTools.Field(typeof(PlayLogEntry_Interaction), "initiator");
    private static readonly FieldInfo? SingleInitiator = AccessTools.Field(typeof(PlayLogEntry_InteractionSinglePawn), "initiator");
    private static ConditionalWeakTable<LogEntry, object> Seen = new ConditionalWeakTable<LogEntry, object>();
    internal static void Reset() => Seen = new ConditionalWeakTable<LogEntry, object>();
    private static void Postfix(LogEntry __0)
    {
        try
        {
            if (__0 == null || Seen.TryGetValue(__0, out _)) return;
            Seen.Add(__0, new object());
            Pawn? speaker = __0 is PlayLogEntry_Interaction ? InteractionInitiator?.GetValue(__0) as Pawn
                : __0 is PlayLogEntry_InteractionSinglePawn ? SingleInitiator?.GetValue(__0) as Pawn : null;
            SpeechController? controller = Current.Game?.GetComponent<SpeechController>();
            if (speaker == null || controller == null || !controller.CanQueue(speaker)) return;
            string text;
            // Log grammar can use Verse.Rand. Isolate it from simulation RNG.
            Rand.PushState(unchecked((int)StableHash.Of(speaker.GetUniqueLoadID() + "|" + __0.Tick)));
            try { text = __0.ToGameStringFromPOV(speaker); }
            finally { Rand.PopState(); }
            controller.Queue(speaker, text);
        }
        catch (Exception ex) { Log.WarningOnce("[Rimblings] Social-log event skipped: " + ex.Message, 194072701); }
    }
    private static bool Prepare()
    {
        if (InteractionInitiator == null || SingleInitiator == null)
            Log.Warning("[Rimblings] A social-log initiator field is missing. The affected entry type will be ignored; check the game's current API.");
        return true;
    }
}

[HarmonyPatch(typeof(MemoryUtility), nameof(MemoryUtility.ClearAllMapsAndWorld))]
internal static class CleanupHook
{
    private static void Prefix()
    {
        Current.Game?.GetComponent<SpeechController>()?.Clear();
        SocialLogHook.Reset();
    }
}
