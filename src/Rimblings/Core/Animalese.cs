using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Rimblings.Core;

public static class SpeechText
{
    private static readonly Regex Tags = new Regex("<[^>]*>", RegexOptions.Compiled);
    private static readonly string[] Digits = { "ZERO", "ONE", "TWO", "THREE", "FOUR", "FIVE", "SIX", "SEVEN", "EIGHT", "NINE" };
    public static List<int> Tokens(string? raw, int limit = 96, bool shortenWords = false)
    {
        limit = Math.Max(1, Math.Min(limit, 128));
        var result = new List<int>(limit);
        if (string.IsNullOrWhiteSpace(raw)) return result;
        raw = raw!.Substring(0, Math.Min(raw.Length, 2048));
        var safe = new StringBuilder();
        for (int i = 0; i < raw.Length; i++)
        {
            if (char.IsHighSurrogate(raw[i]) && i + 1 < raw.Length && char.IsLowSurrogate(raw[i + 1])) { safe.Append(' '); i++; }
            else safe.Append(char.IsSurrogate(raw[i]) ? ' ' : raw[i]);
        }
        string text = Tags.Replace(safe.ToString(), "").Normalize(NormalizationForm.FormD).ToUpperInvariant();
        if (shortenWords) return Shortened(text, limit);
        foreach (char c in text)
        {
            if (result.Count >= limit) break;
            if (Combining(c)) continue;
            if (c >= 'A' && c <= 'Z') result.Add(c - 'A');
            else if (c >= '0' && c <= '9')
            {
                foreach (char d in Digits[c - '0']) { if (result.Count >= limit) break; result.Add(d - 'A'); }
            }
            else if (char.IsLetter(c)) result.Add((int)(StableHash.Of(c.ToString()) % 26));
            else if (char.IsWhiteSpace(c) || char.IsPunctuation(c))
            {
                if (result.Count > 0 && result[result.Count - 1] >= 0) result.Add(c == '.' || c == '!' || c == '?' ? -2 : -1);
            }
        }
        while (result.Count > 0 && result[result.Count - 1] < 0) result.RemoveAt(result.Count - 1);
        return result;
    }
    private static bool Combining(char c)
    {
        UnicodeCategory category = char.GetUnicodeCategory(c);
        return category == UnicodeCategory.NonSpacingMark || category == UnicodeCategory.SpacingCombiningMark || category == UnicodeCategory.EnclosingMark;
    }
    private static List<int> Shortened(string text, int limit)
    {
        // Acedio shorten behaviour: first + last character of each word, a
        // one-character word once, concatenate WITHOUT spaces. Normalisation,
        // number expansion and deterministic non-Latin fallback remain supported.
        var result = new List<int>(limit);
        int first = -1, last = -1, count = 0;
        void Add(int token) { if (count == 0) first = token; last = token; count++; }
        void Flush()
        {
            if (count > 0 && result.Count < limit) result.Add(first);
            if (count > 1 && result.Count < limit) result.Add(last);
            first = last = -1; count = 0;
        }
        foreach (char c in text)
        {
            if (result.Count >= limit) break;
            if (Combining(c)) continue;
            if (c >= 'A' && c <= 'Z') Add(c - 'A');
            else if (c >= '0' && c <= '9') { foreach (char digit in Digits[c - '0']) Add(digit - 'A'); }
            else if (char.IsLetter(c)) Add((int)(StableHash.Of(c.ToString()) % 26));
            else Flush();
        }
        Flush();
        return result;
    }
}

public sealed class LetterBank
{
    public readonly int SampleRate;
    public readonly float[][] Letters;
    public LetterBank(int sampleRate, float[][] letters)
    {
        if (sampleRate < 8000 || sampleRate > 96000 || letters.Length != 26) throw new ArgumentException("A bank must contain 26 letters at 8-96 kHz.");
        int required = (int)(sampleRate * 0.15);
        foreach (float[] letter in letters) if (letter == null || letter.Length < required) throw new ArgumentException("Each letter needs at least 150 ms.");
        SampleRate = sampleRate;
        Letters = letters;
    }
    public static LetterBank ReadWave(byte[] bytes)
    {
        if (bytes == null || bytes.Length < 44 || bytes.Length > 4 * 1024 * 1024) throw new InvalidDataException("Invalid or oversized voice bank.");
        using (var stream = new MemoryStream(bytes, false))
        using (var reader = new BinaryReader(stream))
        {
            if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "RIFF") throw new InvalidDataException("Expected RIFF WAV.");
            uint riffLength = reader.ReadUInt32();
            if ((long)riffLength + 8 > bytes.Length || Encoding.ASCII.GetString(reader.ReadBytes(4)) != "WAVE") throw new InvalidDataException("Truncated WAV.");
            int format = 0, channels = 0, rate = 0, bits = 0, align = 0;
            byte[]? pcm = null;
            long end = (long)riffLength + 8;
            while (stream.Position + 8 <= end)
            {
                string id = Encoding.ASCII.GetString(reader.ReadBytes(4));
                uint size = reader.ReadUInt32();
                long next = stream.Position + size + (size & 1);
                if (next > end || size > int.MaxValue) throw new InvalidDataException("Invalid WAV chunk length.");
                if (id == "fmt ")
                {
                    if (size < 16) throw new InvalidDataException("Missing WAV format.");
                    format = reader.ReadUInt16(); channels = reader.ReadUInt16(); rate = reader.ReadInt32();
                    reader.ReadInt32(); align = reader.ReadUInt16(); bits = reader.ReadUInt16();
                }
                else if (id == "data") pcm = reader.ReadBytes((int)size);
                stream.Position = next;
            }
            if (format != 1 || channels < 1 || channels > 2 || (bits != 8 && bits != 16) || rate < 8000 || rate > 96000 || pcm == null || align != channels * (bits / 8))
                throw new InvalidDataException("Use PCM 8/16-bit mono/stereo WAV at 8-96 kHz.");
            int stride = channels * (bits / 8), perLetter = (int)(rate * 0.15);
            if (pcm.Length < perLetter * 26 * stride) throw new InvalidDataException("Voice bank is shorter than 26 x 150 ms.");
            var letters = new float[26][];
            for (int letter = 0; letter < 26; letter++)
            {
                letters[letter] = new float[perLetter];
                for (int i = 0; i < perLetter; i++)
                {
                    float value = 0;
                    for (int channel = 0; channel < channels; channel++)
                    {
                        int offset = ((letter * perLetter + i) * channels + channel) * (bits / 8);
                        value += bits == 8 ? (pcm[offset] - 128) / 128f : (short)(pcm[offset] | pcm[offset + 1] << 8) / 32768f;
                    }
                    letters[letter][i] = value / channels;
                }
            }
            return new LetterBank(rate, letters);
        }
    }
}

public static class Animalese
{
    // Algorithmic reference: Acedio/animalese.js (MIT), 150 ms source letters,
    // 75 ms output units. See LICENSES/Animalese.txt for attribution.
    public static float[] Render(LetterBank bank, string text, VoiceProfile voice, float maxSeconds = 4, bool shortenWords = true)
    {
        var tokens = SpeechText.Tokens(text, shortenWords: shortenWords);
        if (tokens.Count == 0) return Array.Empty<float>();
        int slot = Math.Max(1, (int)(bank.SampleRate * 0.075f / voice.Cadence));
        int budget = (int)(bank.SampleRate * MathEx.Clamp(maxSeconds, 0.1f, 6));
        int units = 0;
        foreach (int token in tokens) units += token == -2 ? 2 : 1;
        var output = new float[Math.Min(budget, units * slot)];
        int cursor = 0, ordinal = 0;
        uint utterance = StableHash.Of(voice.Seed.ToString(CultureInfo.InvariantCulture) + "|" + text);
        foreach (int token in tokens)
        {
            int length = Math.Min(slot * (token == -2 ? 2 : 1), output.Length - cursor);
            if (length <= 0) break;
            if (token >= 0)
            {
                float[] source = bank.Letters[token];
                float pitch = MathEx.Clamp(voice.Pitch * (0.975f + 0.05f * StableHash.Unit(utterance, ordinal.ToString(CultureInfo.InvariantCulture))), 0.7f, 1.8f);
                int voiced = Math.Min(length, (int)((source.Length - 1) / pitch));
                int fade = Math.Min((int)(bank.SampleRate * 0.002), voiced / 2);
                var filter = new VoiceColour(voice.Tone, bank.SampleRate);
                for (int i = 0; i < voiced; i++)
                {
                    float position = i * pitch;
                    int a = (int)position;
                    if (a + 1 >= source.Length) break;
                    float envelope = fade == 0 ? 1 : Math.Min(1, Math.Min(i / (float)fade, (voiced - 1 - i) / (float)fade));
                    float sample = source[a] + (source[a + 1] - source[a]) * (position - a);
                    output[cursor + i] = filter.Process(sample, i) * envelope * 0.72f;
                }
            }
            cursor += length;
            ordinal++;
        }
        int tail = Math.Min(output.Length, (int)(bank.SampleRate * 0.005));
        for (int i = 0; i < tail; i++) output[output.Length - 1 - i] *= i / (float)Math.Max(1, tail);
        return output;
    }
}

// Stable timbral variation from the same licensed bank, not six new recordings.
internal struct VoiceColour
{
    private readonly VoiceTone tone;
    private readonly float alpha;
    private readonly double wobble;
    private float low;
    internal VoiceColour(VoiceTone tone, int rate)
    {
        this.tone = tone;
        float cutoff = tone == VoiceTone.Soft ? 1500 : tone == VoiceTone.Warm ? 2400
            : tone == VoiceTone.Gravelly ? 2800 : tone == VoiceTone.Nasal ? 650 : tone == VoiceTone.Bright ? 5200 : 900;
        alpha = (float)(1 - Math.Exp(-2 * Math.PI * cutoff / rate));
        wobble = 2 * Math.PI * 65 / rate;
        low = 0;
    }
    internal float Process(float sample, int index)
    {
        low += alpha * (sample - low);
        float value;
        switch (tone)
        {
            case VoiceTone.Gravelly:
                value = low * (0.85f + 0.15f * (float)Math.Sin(index * wobble));
                value = value * 1.6f / (1 + 0.6f * Math.Abs(value));
                break;
            case VoiceTone.Nasal: value = 0.9f * sample - 0.55f * low; break;
            case VoiceTone.Light: value = sample - 0.2f * low; break;
            default: value = low; break;
        }
        return MathEx.Clamp(value, -1, 1);
    }
}
