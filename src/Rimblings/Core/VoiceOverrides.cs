using System;
using System.Collections.Generic;
using System.Globalization;

namespace Rimblings.Core;

// Only primitive pawn IDs and versioned values go into the save. No Pawn
// references, defs, appearance changes, external files, or global voice state.
public sealed class VoiceOverrides
{
    public Dictionary<string, string> Records = new Dictionary<string, string>();

    public VoiceProfile Resolve(string pawnId, VoiceProfile generated)
        => Records.TryGetValue(pawnId, out string? value) && TryDecode(value, generated.Seed, out VoiceProfile profile)
            ? profile : generated;

    public bool HasOverride(string pawnId) => Records.TryGetValue(pawnId, out string? value) && TryDecode(value, 0, out _);

    public void Set(string pawnId, VoiceProfile profile)
    {
        if (string.IsNullOrEmpty(pawnId)) throw new ArgumentException("A voice needs a pawn ID.", nameof(pawnId));
        Records[pawnId] = string.Join("|", "1", profile.Pitch.ToString("R", CultureInfo.InvariantCulture),
            profile.Cadence.ToString("R", CultureInfo.InvariantCulture), ((int)profile.Tone).ToString(CultureInfo.InvariantCulture),
            profile.BankIndex.ToString(CultureInfo.InvariantCulture), profile.Depth.ToString("R", CultureInfo.InvariantCulture));
    }

    public void Reset(string pawnId) => Records.Remove(pawnId);

    public void Validate()
    {
        if (Records == null) { Records = new Dictionary<string, string>(); return; }
        var invalid = new List<string>();
        foreach (var record in Records)
            if (string.IsNullOrEmpty(record.Key) || !TryDecode(record.Value, 0, out _)) invalid.Add(record.Key);
        foreach (string key in invalid) Records.Remove(key);
    }

    private static bool TryDecode(string? value, uint seed, out VoiceProfile profile)
    {
        profile = default;
        if (value == null || value.Length > 128) return false;
        string[] parts = value.Split('|');
        if (parts.Length != 6 || parts[0] != "1"
            || !Float(parts[1], out float pitch) || !Float(parts[2], out float cadence)
            || !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int tone)
            || !int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int bank)
            || !Float(parts[5], out float depth) || tone < 0 || tone >= 6 || bank < 0 || bank >= 8) return false;
        profile = new VoiceProfile(pitch, cadence, seed, (VoiceTone)tone, bank, depth);
        return true;
    }

    private static bool Float(string value, out float result)
        => float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result)
            && !float.IsNaN(result) && !float.IsInfinity(result);
}
