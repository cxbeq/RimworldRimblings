using Rimblings;
using Rimblings.Core;

internal static class FeatureChecks
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool value, string name)
        {
            if (!value) throw new Exception("FAILED: " + name);
            checks++; Console.WriteLine("PASS: " + name);
        }
        bool Defaults(RimblingsSettings s) => s.Enabled && s.NonPlayer && s.ShortenWords && !s.LimitVoices && s.Volume == 0.7f && s.FullZoom == 12 && s.SilentZoom == 18 && s.HearingRadius == 60 && s.BackgroundGain == 0.28f && s.MaxVoices == 6;
        var settings = new RimblingsSettings();
        Check(Defaults(settings), "new settings use 40 percent and shortening");
        settings.Enabled = settings.NonPlayer = settings.ShortenWords = false;
        settings.LimitVoices = true;
        settings.Volume = 1; settings.FullZoom = 25; settings.SilentZoom = 55;
        settings.HearingRadius = 115; settings.BackgroundGain = 0.9f; settings.MaxVoices = 1;
        settings.Reset();
        Check(Defaults(settings), "reset restores every runtime setting");
        settings.Reset();
        Check(Defaults(settings), "reset is idempotent");
        Verse.Scribe_Values.Saved.Clear();
        settings.Volume = 0.9f; settings.ExposeData();
        Check(Defaults(settings), "missing serialized settings use the same defaults");
        Verse.Scribe_Values.Saved["volume"] = 0.7f;
        Verse.Scribe_Values.Saved["enabled"] = false;
        settings.ExposeData();
        Check(settings.Volume == 0.7f && !settings.Enabled && settings.ShortenWords && settings.HearingRadius == 60, "existing settings retained and new fields default safely");
        Check(!settings.LimitVoices, "old saved voice count does not enable a simultaneous cap");
        Verse.Scribe_Values.Saved["limitVoices"] = true;
        Verse.Scribe_Values.Saved["maxVoices"] = 25;
        settings.ExposeData();
        Check(settings.LimitVoices && settings.MaxVoices == 25, "explicit simultaneous cap survives serialization");
        Verse.Scribe_Values.Saved.Clear();
        settings.Volume = float.NaN; settings.HearingRadius = float.PositiveInfinity; settings.MaxVoices = -1;
        settings.Clamp();
        Check(settings.Volume == 0 && settings.HearingRadius == 120 && settings.MaxVoices == 1, "malformed settings stay bounded");
        settings.MaxVoices = 1000; settings.Clamp();
        Check(settings.MaxVoices == 64, "optional simultaneous cap remains bounded");

        Check(MathEx.DistanceGain(0, 60) == 1 && MathEx.DistanceGain(5, 60) == 1 && MathEx.DistanceGain(60, 60) == 0, "hearing radius endpoints");
        Check(MathEx.DistanceGain(25, 60) > 0 && MathEx.DistanceGain(25, 60) < MathEx.DistanceGain(10, 60), "nearby off-screen distance remains audible and quieter");
        float previous = 1;
        bool monotonic = true;
        for (float d = 0; d < 100; d += 0.1f)
        {
            float gain = MathEx.DistanceGain(d, 60);
            monotonic &= gain <= previous + 0.000001f && gain >= 0;
            previous = gain;
        }
        Check(monotonic, "distance falloff is bounded and monotonic");
        Check(MathEx.DistanceGain(float.NaN, 60) == 0 && MathEx.DistanceGain(float.PositiveInfinity, 60) == 0 && MathEx.DistanceGain(-1, 60) == 0, "invalid distance fails silent");
        float left = MathEx.DistanceGain(25, 60) * MathEx.CentreGain(0.99999f, 0.5f);
        float right = MathEx.DistanceGain(25, 60) * MathEx.CentreGain(1.00001f, 0.5f);
        Check(right > 0 && Math.Abs(left - right) < 0.0001, "screen edge does not create a gain discontinuity");
        Check(!MathEx.OnScreen(1.01f, 0.5f) && MathEx.OnScreen(0.5f, 0.5f), "hover eligibility stays on screen without gating hearing");

        int[] Short(string text, int limit = 96) => SpeechText.Tokens(text, limit, true).ToArray();
        Check(Short("Hello there!").SequenceEqual(SpeechText.Tokens("HOTE")), "Acedio first-last shortening removes spaces");
        Check(Short("I a to").SequenceEqual(SpeechText.Tokens("IATO")), "single-letter words are not duplicated");
        Check(Short("<color=red>Héllo</color>, world!").SequenceEqual(SpeechText.Tokens("HOWD")), "shortening sanitizes tags accents and punctuation");
        Check(Short("don't").SequenceEqual(SpeechText.Tokens("DNT")), "shortening splits punctuation like the reference");
        Check(Short("2").SequenceEqual(SpeechText.Tokens("TO")), "shortened numeric fallback");
        Check(Short("A" + new string('B', 1000) + "Z").SequenceEqual(new[] { 0, 25 }), "long words keep their actual final letter");
        Check(Short("こんにちは").Length > 0 && Short("\ud800\udfff😀<b> </b>").Length == 0, "shortening keeps safe non-Latin fallback and rejects malformed-only text");
        Check(Short(string.Join(" ", Enumerable.Repeat("hello", 500)), 3).Length == 3, "shortening respects its output budget");

        var profiles = Enumerable.Range(0, 1024).Select(i => VoiceProfile.From(new VoiceTraits { Identity = "Pawn_" + i, Sex = i % 2 == 0 ? "Male" : "Female", Body = "Male", Head = "Male_AverageNormal", BiologicalAge = 30 })).ToArray();
        Check(profiles.All(v => float.IsFinite(v.Pitch) && float.IsFinite(v.Cadence)) && profiles.Select(v => v.Seed).Distinct().Count() > 1000, "over 1000 pawn identities produce stable finite profiles");
        Check(profiles.Select(v => (v.Tone, v.Pitch, v.Cadence)).Distinct().Count() == profiles.Length, "1024 pawns retain distinct voice profiles");
        var femaleBanks = Enumerable.Range(0, 512).Select(i => VoiceProfile.From(new VoiceTraits { Identity = "FemalePawn_" + i, Sex = "Female" }).BankIndex).ToArray();
        var maleBanks = Enumerable.Range(0, 512).Select(i => VoiceProfile.From(new VoiceTraits { Identity = "MalePawn_" + i, Sex = "Male" }).BankIndex).ToArray();
        Check(femaleBanks.Distinct().OrderBy(i => i).SequenceEqual(new[] { 0, 1, 2, 3 }) && maleBanks.Distinct().OrderBy(i => i).SequenceEqual(new[] { 4, 5, 6, 7 }), "all eight extension banks are assigned by stable pawn identity and gender");
        Check(profiles.Select(v => v.Tone).Distinct().Count() == 6, "all six stable tone families are represented");
        Check(profiles.Average(v => v.Pitch) < 1.2 && profiles.Max(v => v.Pitch) - profiles.Min(v => v.Pitch) > 0.4, "voice distribution is lower and varied");
        // Match the runtime's float constants: 0.7f is slightly below double 0.7.
        Check(profiles.All(v => v.Pitch >= 0.7f && v.Pitch <= 1.8f && v.Cadence >= 0.75f && v.Cadence <= 1.35f), "all profile controls remain bounded");
        Check(VoiceProfile.From(new VoiceTraits { Identity = "Pawn_0", BiologicalAge = double.PositiveInfinity, LifeExpectancy = double.NaN }).Tone == VoiceProfile.From(new VoiceTraits { Identity = "Pawn_0", BiologicalAge = double.PositiveInfinity, LifeExpectancy = double.NaN }).Tone, "tone identity is deterministic for malformed custom traits");
        VoiceTraits traits = new VoiceTraits { Identity = "trait-check", Sex = "Male", Body = "Male", Head = "Male_AverageNormal", BiologicalAge = 30, LifeExpectancy = 80 };
        float normalPitch = VoiceProfile.From(traits).Pitch;
        traits.Head = "Male_NarrowPointy";
        Check(VoiceProfile.From(traits).Pitch > normalPitch, "actual vanilla narrow pointy head def affects pitch");
        traits.Head = "Male_AverageNormal"; traits.Sex = "Female";
        Check(VoiceProfile.From(traits).Pitch > normalPitch, "vanilla gender affects pitch");
        traits.Sex = "Male"; traits.Body = "Hulk";
        Check(VoiceProfile.From(traits).Pitch < normalPitch, "vanilla heavy body affects pitch");
        traits.Body = "Male"; traits.BiologicalAge = 5;
        Check(VoiceProfile.From(traits).Pitch > normalPitch, "biological childhood affects pitch");
        traits.BiologicalAge = 75;
        Check(VoiceProfile.From(traits).Pitch < normalPitch, "biological old age affects pitch");
        traits.BiologicalAge = 30; traits.Body = "child";
        float childPitch = VoiceProfile.From(traits).Pitch;
        traits.Body = "Child";
        Check(childPitch == VoiceProfile.From(traits).Pitch, "body form names are case-insensitive");
        Check(new[] { "male", "female", "Thin", "Hulk", "Fat", "child", "Baby" }.All(form =>
        {
            traits.Body = form;
            VoiceProfile first = VoiceProfile.From(traits), second = VoiceProfile.From(traits);
            return first.Seed == second.Seed && first.Pitch == second.Pitch && first.Cadence == second.Cadence;
        }), "pictured body forms produce repeatable profiles");
        var picturedHeads = new[]
        {
            "Skull", "Stump",
            "Male_AverageNormal", "Male_AveragePointy", "Male_AverageWide", "Male_NarrowNormal", "Male_NarrowPointy", "Male_NarrowWide",
            "Female_AverageNormal", "Female_AveragePointy", "Female_AverageWide", "Female_NarrowNormal", "Female_NarrowPointy", "Female_NarrowWide",
            "Gaunt", "Male_HeavyJawNormal", "Female_HeavyJawNormal",
            "Furskin_Average1", "Furskin_Average2", "Furskin_Average3", "Furskin_Gaunt",
            "Furskin_Narrow1", "Furskin_Narrow2", "Furskin_Narrow3", "Furskin_Heavy1", "Furskin_Heavy2", "Furskin_Heavy3",
            "CultEscapee", "TimelessOne", "DarkScholar_Female", "DarkScholar_Male", "Leathery_Female", "Leathery_Male"
        };
        Check(picturedHeads.All(head =>
        {
            traits.Head = head;
            VoiceProfile first = VoiceProfile.From(traits), second = VoiceProfile.From(traits);
            return first.Seed == second.Seed && first.Tone == second.Tone && first.Pitch == second.Pitch && first.Cadence == second.Cadence;
        }), "pictured core and DLC heads retain deterministic profiles");
        traits.Head = "Furskin_Heavy3";
        VoiceProfile beforeAppearanceChange = VoiceProfile.From(traits);
        traits.Body = "Hulk"; traits.Head = "Leathery_Male";
        VoiceProfile afterAppearanceChange = VoiceProfile.From(traits);
        Check(beforeAppearanceChange.Seed == afterAppearanceChange.Seed && beforeAppearanceChange.Tone == afterAppearanceChange.Tone && beforeAppearanceChange.BankIndex == afterAppearanceChange.BankIndex, "pawn identity and recording bank survive appearance changes");
        var letters = Enumerable.Range(0, 26).Select(n => Enumerable.Range(0, 6615).Select(i => (float)(0.5 * Math.Sin(2 * Math.PI * 220 * i / 44100) + 0.2 * Math.Sin(2 * Math.PI * 3100 * i / 44100))).ToArray()).ToArray();
        var bank = new LetterBank(44100, letters);
        float[][] colours = Enum.GetValues<VoiceTone>().Select(t => Animalese.Render(bank, "hello there", new VoiceProfile(1, 1, 42, t))).ToArray();
        Check(colours.Skip(1).All(p => !p.SequenceEqual(colours[0])), "tone families change PCM independently of pitch");
        Check(colours.All(p => p.All(x => float.IsFinite(x) && Math.Abs(x) <= 0.721f)), "all timbres are finite and peak bounded");
        Check(Animalese.Render(bank, "hello there", profiles[0]).Length < Animalese.Render(bank, "hello there", profiles[0], shortenWords: false).Length, "runtime synthesis defaults to shortened words");
        Check(Animalese.Render(bank, new string('A', 1000), profiles[0], shortenWords: false).Length <= 44100 * 4, "full-word mode still respects duration limits");

        var sine = Enumerable.Range(0, 44100).Select(i => (float)Math.Sin(2 * Math.PI * 440 * i / 44100) * 0.5f).ToArray();
        foreach (float speed in new[] { 0.5f, 1f, 3f, 6f, 12f, 15f, 150f })
        {
            float[] scaled = SpeechTempo.Render(sine, 44100, speed);
            Check(scaled.Length == (int)Math.Ceiling(sine.Length / (double)speed), "tempo duration matches multiplier " + speed);
            Check(scaled.All(x => float.IsFinite(x) && Math.Abs(x) <= 0.5001f), "tempo stays finite and bounded at " + speed);
        }
        Check(SpeechTempo.Render(sine, 44100, 1).SequenceEqual(sine), "normal tempo preserves the original PCM");
        foreach (float speed in new[] { 3f, 6f })
        {
            float[] scaled = SpeechTempo.Render(sine, 44100, speed);
            int margin = 441, crossings = 0;
            for (int i = margin + 1; i < scaled.Length - margin; i++) if (scaled[i - 1] <= 0 && scaled[i] > 0) crossings++;
            double frequency = crossings * 44100.0 / (scaled.Length - 2 * margin);
            Check(Math.Abs(frequency - 440) < 30, "tempo does not multiply pitch at " + speed);
        }
        Check(SpeechTempo.SourceOffset(100, 100, 3, 1000) == 400 && SpeechTempo.SourceOffset(400, 100, 6, 1000) == 1000, "mid-utterance speed changes advance instead of restarting");
        Check(SpeechTempo.Render(sine, 44100, 6, 22050).Length == 3675, "remaining speech has the exact new tempo duration");
        Check(SpeechTempo.Render(sine, 44100, 3, sine.Length).Length == 0, "finished speech is not replayed");
        Check(SpeechTempo.SafeSpeed(float.NaN) == 1 && SpeechTempo.SafeSpeed(0) == 1 && SpeechTempo.SafeSpeed(float.PositiveInfinity) == 1 && SpeechTempo.SafeSpeed(1000) == 150, "invalid or extreme speed mods fail safely");
        return checks;
    }
}
