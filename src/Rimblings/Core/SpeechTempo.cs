using System;

namespace Rimblings.Core;

// Small bounded, pitch-preserving overlap-add time scaler. Grains are copied at
// their original sample rate; speed changes their spacing in the source only.
// No Unity dependencies, native libraries, threads or gameplay RNG.
public static class SpeechTempo
{
    public static float SafeSpeed(float speed) => float.IsNaN(speed) || float.IsInfinity(speed) || speed <= 0
        ? 1 : MathEx.Clamp(speed, 0.1f, 150);

    public static int SourceOffset(int originalOffset, int playedSamples, float previousSpeed, int originalLength)
        => (int)Math.Min(originalLength, Math.Max(0, originalOffset + Math.Max(0, playedSamples) * (double)SafeSpeed(previousSpeed)));

    public static float[] Render(float[] source, int sampleRate, float speed, int start = 0)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (sampleRate < 8000 || sampleRate > 96000 || source.Length > sampleRate * 6) throw new ArgumentException("Invalid or oversized speech buffer.");
        speed = SafeSpeed(speed);
        start = Math.Max(0, Math.Min(start, source.Length));
        int remaining = source.Length - start;
        if (remaining == 0) return Array.Empty<float>();
        int length = Math.Max(1, (int)Math.Ceiling(remaining / (double)speed));
        var output = new float[length];
        if (speed == 1)
        {
            Array.Copy(source, start, output, 0, remaining);
            if (start > 0) Fade(output, sampleRate);
            return output;
        }
        var weights = new float[length];
        int hop = Math.Max(32, sampleRate / 100); // 10 ms hop, 20 ms grain
        int window = hop * 2;
        var envelope = new float[window];
        for (int i = 0; i < window; i++) envelope[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * (i + 0.5) / window));
        int previousOrigin = start;
        bool hasPrevious = false;
        for (int at = 0; at < length; at += hop)
        {
            int expected = start + (int)Math.Round(at * (double)speed);
            int origin = expected;
            if (hasPrevious && expected + hop < source.Length)
                origin = Align(source, previousOrigin + hop, expected, start, sampleRate / 200, hop);
            for (int i = 0; i < window && at + i < length; i++)
            {
                int read = origin + i;
                if (read < start || read >= source.Length) break;
                float weight = envelope[i];
                output[at + i] += source[read] * weight;
                weights[at + i] += weight;
            }
            previousOrigin = origin;
            hasPrevious = true;
        }
        for (int i = 0; i < output.Length; i++)
            output[i] = weights[i] > 0.000001f ? output[i] / weights[i] : 0;
        Fade(output, sampleRate);
        return output;
    }

    // Align the overlapping waveforms within +/-5 ms. This avoids the most
    // obvious phase cancellations of blind granular overlap-add on voiced audio.
    private static int Align(float[] source, int reference, int expected, int minimum, int search, int count)
    {
        if (reference < minimum || reference + count >= source.Length) return expected;
        int lo = Math.Max(minimum, expected - search);
        int hi = Math.Min(source.Length - count - 1, expected + search);
        int best = expected;
        double bestScore = double.NegativeInfinity;
        for (int candidate = lo; candidate <= hi; candidate += 4)
        {
            double dot = 0, a2 = 0, b2 = 0;
            for (int i = 0; i < count; i += 8)
            {
                float a = source[reference + i], b = source[candidate + i];
                dot += a * b; a2 += a * a; b2 += b * b;
            }
            double score = dot / Math.Sqrt(a2 * b2 + 1e-15) - 0.001 * Math.Abs(candidate - expected) / Math.Max(1, search);
            if (score > bestScore) { bestScore = score; best = candidate; }
        }
        return best;
    }

    private static void Fade(float[] samples, int sampleRate)
    {
        int fade = Math.Min(samples.Length / 2, Math.Max(1, sampleRate / 500));
        for (int i = 0; i < fade; i++)
        {
            float gain = i / (float)fade;
            samples[i] *= gain;
            samples[samples.Length - 1 - i] *= gain;
        }
    }
}
