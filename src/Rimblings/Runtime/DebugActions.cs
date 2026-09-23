using LudeonTK;
using Verse;

namespace Rimblings;

internal static class RimblingsDebugActions
{
    [DebugAction("Rimblings", "Speak selected pawn", allowedGameStates = AllowedGameStates.PlayingOnMap)]
    private static void SpeakSelected()
    {
        Pawn? pawn = Find.Selector.SingleSelectedObject as Pawn;
        if (pawn == null) { Log.Message("[Rimblings] Select a humanlike pawn, zoom in and unpause first."); return; }
        Current.Game.GetComponent<SpeechController>().Queue(pawn, "Hello there! The colony is having a lovely day.");
    }
    [DebugAction("Rimblings", "Speak visible colonists", allowedGameStates = AllowedGameStates.PlayingOnMap)]
    private static void SpeakVisible()
    {
        var controller = Current.Game.GetComponent<SpeechController>();
        foreach (Pawn pawn in Find.CurrentMap.mapPawns.FreeColonistsSpawned)
            controller.Queue(pawn, "Testing several different pawn voices together.");
    }
}
