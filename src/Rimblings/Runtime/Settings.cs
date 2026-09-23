using Rimblings.Core;
using Verse;

namespace Rimblings;

public sealed class RimblingsSettings : ModSettings
{
    public const float DefaultVolume = 0.7f;
    public const float DefaultFullZoom = 12;
    public const float DefaultSilentZoom = 18;
    public const float DefaultBackgroundGain = 0.28f;
    public const float DefaultHearingRadius = 60;
    public const int DefaultMaxVoices = 6;
    public const bool DefaultLimitVoices = false;
    public bool Enabled = true;
    public bool NonPlayer = true;
    public bool ShortenWords = true;
    public float Volume = DefaultVolume;
    public float FullZoom = DefaultFullZoom;
    public float SilentZoom = DefaultSilentZoom;
    public float BackgroundGain = DefaultBackgroundGain;
    public float HearingRadius = DefaultHearingRadius;
    public int MaxVoices = DefaultMaxVoices;
    public bool LimitVoices = DefaultLimitVoices;

    public void Reset()
    {
        Enabled = NonPlayer = ShortenWords = true;
        Volume = DefaultVolume;
        FullZoom = DefaultFullZoom;
        SilentZoom = DefaultSilentZoom;
        BackgroundGain = DefaultBackgroundGain;
        HearingRadius = DefaultHearingRadius;
        MaxVoices = DefaultMaxVoices;
        LimitVoices = DefaultLimitVoices;
    }
    public void Clamp()
    {
        Volume = MathEx.Clamp(Volume, 0, 1);
        FullZoom = MathEx.Clamp(FullZoom, 4, 30);
        SilentZoom = MathEx.Clamp(SilentZoom, FullZoom + 0.5f, 60);
        BackgroundGain = MathEx.Clamp(BackgroundGain, 0.05f, 1);
        HearingRadius = MathEx.Clamp(HearingRadius, 20, 120);
        MaxVoices = System.Math.Max(1, System.Math.Min(64, MaxVoices));
    }
    public override void ExposeData()
    {
        // Preserve explicit saved values, including volume. Reset opts existing
        // installations into all new defaults instead of overwriting user choices.
        Scribe_Values.Look(ref Enabled, "enabled", true);
        Scribe_Values.Look(ref NonPlayer, "nonPlayer", true);
        Scribe_Values.Look(ref ShortenWords, "shortenWords", true);
        Scribe_Values.Look(ref Volume, "volume", DefaultVolume);
        Scribe_Values.Look(ref FullZoom, "fullZoom", DefaultFullZoom);
        Scribe_Values.Look(ref SilentZoom, "silentZoom", DefaultSilentZoom);
        Scribe_Values.Look(ref BackgroundGain, "backgroundGain", DefaultBackgroundGain);
        Scribe_Values.Look(ref HearingRadius, "hearingRadius", DefaultHearingRadius);
        Scribe_Values.Look(ref MaxVoices, "maxVoices", DefaultMaxVoices);
        Scribe_Values.Look(ref LimitVoices, "limitVoices", DefaultLimitVoices);
        Clamp();
    }
}
