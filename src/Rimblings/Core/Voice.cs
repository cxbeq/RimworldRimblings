using System;
using System.Collections.Generic;

namespace Rimblings.Core;

public sealed class VoiceTraits
{
    public string Identity = "unknown-pawn";
    public string Sex = "unknown";
    public string Body = "unknown";
    public string Head = "unknown";
    public double BiologicalAge = double.NaN;
    public double LifeExpectancy = 80;
}

public enum VoiceTone { Warm, Soft, Gravelly, Nasal, Bright, Light }

public readonly struct VoiceProfile
{
    public readonly float Pitch;
    public readonly float Cadence;
    public readonly uint Seed;
    public readonly VoiceTone Tone;
    public readonly int BankIndex;
    public string BankName => (BankIndex < 4 ? "female_" : "male_") + (BankIndex % 4 + 1);
    public VoiceProfile(float pitch, float cadence, uint seed, VoiceTone tone = VoiceTone.Warm, int bankIndex = 0)
    {
        Pitch = MathEx.Clamp(pitch, 0.7f, 1.8f);
        Cadence = MathEx.Clamp(cadence, 0.75f, 1.35f);
        Seed = seed;
        Tone = (int)tone >= 0 && (int)tone < 6 ? tone : VoiceTone.Warm;
        BankIndex = bankIndex >= 0 && bankIndex < 8 ? bankIndex : 0;
    }

    // Artistic presets, not assertions about real people. The v2 distribution is
    // deliberately lower and broader than v1, with independent spectral colours.
    public static VoiceProfile From(VoiceTraits traits)
    {
        uint seed = StableHash.Of("rimblings-voice-v2|" + traits.Identity);
        var tone = (VoiceTone)(StableHash.Of(seed.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|tone") % 6);
        float pitch;
        switch (tone)
        {
            case VoiceTone.Warm: pitch = 0.84f; break;
            case VoiceTone.Soft: pitch = 0.94f; break;
            case VoiceTone.Gravelly: pitch = 0.90f; break;
            case VoiceTone.Nasal: pitch = 1.02f; break;
            case VoiceTone.Bright: pitch = 1.13f; break;
            default: pitch = 1.22f; break;
        }
        string sex = (traits.Sex ?? "unknown").Trim();
        int variant = (int)(StableHash.Of(seed.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|bank") % 4);
        int bankIndex = sex.Equals("Female", StringComparison.OrdinalIgnoreCase) ? variant
            : sex.Equals("Male", StringComparison.OrdinalIgnoreCase) ? 4 + variant
            : (int)(StableHash.Of(seed.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|bank-gender") % 2) * 4 + variant;
        pitch += sex.Equals("Male", StringComparison.OrdinalIgnoreCase) ? -0.10f : sex.Equals("Female", StringComparison.OrdinalIgnoreCase) ? 0.04f
            : (StableHash.Unit(seed, "sex|" + traits.Sex) - 0.5f) * 0.20f;
        float cadence = 0.86f + StableHash.Unit(seed, "cadence") * 0.28f;
        cadence += sex.Equals("Male", StringComparison.OrdinalIgnoreCase) ? -0.02f : sex.Equals("Female", StringComparison.OrdinalIgnoreCase) ? 0.02f : 0;
        string body = (traits.Body ?? "unknown").Trim().ToUpperInvariant();
        switch (body)
        {
            case "MALE": break;
            case "FEMALE": pitch += 0.02f; break;
            case "THIN": pitch += 0.08f; cadence += 0.04f; break;
            case "FAT": pitch -= 0.07f; cadence -= 0.03f; break;
            case "HULK": pitch -= 0.12f; cadence -= 0.06f; break;
            case "CHILD": pitch += 0.13f; cadence += 0.06f; break;
            case "BABY": pitch += 0.18f; cadence += 0.08f; break;
            default:
                pitch += (StableHash.Unit(seed, "body|" + body) - 0.5f) * 0.24f;
                cadence += (StableHash.Unit(seed, "body-cadence|" + body) - 0.5f) * 0.12f;
                break;
        }
        string head = traits.Head ?? "unknown";
        bool vanilla = false;
        // Match HeadTypeDef.defName, not the underscored graphicPath suffix.
        foreach (string prefix in new[] { "Male_", "Female_" })
        foreach (string shape in new[] { "AverageNormal", "AveragePointy", "AverageWide", "NarrowNormal", "NarrowPointy", "NarrowWide" })
            if (head == prefix + shape) vanilla = true;
        if (vanilla)
        {
            if (head.Contains("Narrow")) pitch += 0.06f;
            if (head.EndsWith("Pointy", StringComparison.Ordinal)) { pitch += 0.05f; cadence += 0.03f; }
            if (head.EndsWith("Wide", StringComparison.Ordinal)) { pitch -= 0.05f; cadence -= 0.02f; }
        }
        else
        {
            pitch += (StableHash.Unit(seed, "head|" + head) - 0.5f) * 0.18f;
            cadence += (StableHash.Unit(seed, "head-cadence|" + head) - 0.5f) * 0.10f;
        }
        double life = traits.LifeExpectancy;
        double age = traits.BiologicalAge;
        if (double.IsNaN(life) || double.IsInfinity(life) || life <= 0) life = 80;
        float fraction = double.IsNaN(age) || double.IsInfinity(age) || age < 0
            ? 0.3f + StableHash.Unit(seed, "unknown-age") * 0.3f
            : MathEx.Clamp((float)(age / life), 0, 1);
        pitch += 0.16f * (1 - MathEx.Smooth(0.05f, 0.25f, fraction));
        pitch -= 0.10f * MathEx.Smooth(0.65f, 1, fraction);
        cadence -= 0.08f * MathEx.Smooth(0.65f, 1, fraction);
        pitch += (StableHash.Unit(seed, "individual-pitch") - 0.5f) * 0.20f;
        return new VoiceProfile(pitch, cadence, seed, tone, bankIndex);
    }
}

public static class StableHash
{
    public static uint Of(string? text)
    {
        uint h = 2166136261;
        foreach (char c in text ?? string.Empty) { unchecked { h = (h ^ c) * 16777619; } }
        return h;
    }
    public static float Unit(uint seed, string key) => ((Of(seed.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + key) >> 8) & 0xffffff) / 16777215f;
}

public static class MathEx
{
    public static float Clamp(float x, float lo, float hi) => float.IsNaN(x) ? lo : Math.Max(lo, Math.Min(hi, x));
    public static float Smooth(float lo, float hi, float x)
    {
        if (hi <= lo) return x >= hi ? 1 : 0;
        float t = Clamp((x - lo) / (hi - lo), 0, 1);
        return t * t * (3 - 2 * t);
    }
    public static float ZoomGain(float size, float full, float silent)
    {
        if (float.IsNaN(size) || float.IsInfinity(size) || size <= 0) return 0;
        return 1 - Smooth(full, Math.Max(full + 0.5f, silent), size);
    }
    public static float DistanceGain(float distance, float radius)
    {
        if (float.IsNaN(distance) || float.IsInfinity(distance) || distance < 0 || float.IsNaN(radius) || float.IsInfinity(radius) || radius <= 5) return 0;
        float gain = 1 - Smooth(5, radius, distance);
        return gain * gain;
    }
    public static bool OnScreen(float x, float y) => x >= 0 && x <= 1 && y >= 0 && y <= 1;
    public static float CentreGain(float x, float y)
    {
        float r = (float)Math.Sqrt((x - 0.5f) * (x - 0.5f) + (y - 0.5f) * (y - 0.5f));
        return 0.25f + 0.75f * (1 - Smooth(0.08f, 0.65f, r));
    }
    public static float Approach(float current, float target, float seconds, float dt) => current + (target - current) * (1 - (float)Math.Exp(-Math.Max(0, dt) / Math.Max(0.005f, seconds)));
    public static float Headroom(IReadOnlyList<float> gains)
    {
        float energy = 0;
        for (int i = 0; i < gains.Count; i++)
        {
            float gain = Clamp(gains[i], 0, 1);
            energy += gain * gain;
        }
        return 0.85f / (float)Math.Sqrt(Math.Max(1, energy));
    }
}
