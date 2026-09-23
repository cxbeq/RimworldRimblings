// Test-only serializer boundary. Compile the ACTUAL runtime Settings.cs so that
// defaults and Reset cannot silently diverge from the code loaded by RimWorld.
// This is not a substitute for testing Verse's real XML serializer in the game.
namespace Verse;
public abstract class ModSettings { public virtual void ExposeData() { } }
public static class Scribe_Values
{
    public static Dictionary<string, object> Saved = new();
    public static void Look<T>(ref T value, string key, T defaultValue)
        => value = Saved.TryGetValue(key, out object? found) ? (T)found : defaultValue;
}
