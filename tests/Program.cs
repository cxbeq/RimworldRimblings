using Rimblings.Core;

int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAILED: " + name);
    checks++;
    Console.WriteLine("PASS: " + name);
}
void Reject(Action action, string name)
{
    bool rejected = false;
    try { action(); } catch (InvalidDataException) { rejected = true; }
    Check(rejected, name);
}
var traits = new VoiceTraits { Identity = "Pawn_42", Sex = "Male", Body = "Male", Head = "Male_Average_Normal", BiologicalAge = 32 };
var voice = VoiceProfile.From(traits);
Check(voice.Pitch == VoiceProfile.From(traits).Pitch && voice.Cadence == VoiceProfile.From(traits).Cadence, "stable voice");
for (int i = 0; i < 2000; i++)
{
    var v = VoiceProfile.From(new VoiceTraits { Identity = "Pawn_" + i, Sex = "mod-gender-" + i, Body = "alien/body/" + i, Head = "unknown-head-" + i, BiologicalAge = i % 2 == 0 ? double.NaN : -1, LifeExpectancy = 0 });
    if (v.Pitch < 0.7f || v.Pitch > 1.8f || v.Cadence < 0.75f || v.Cadence > 1.35f || float.IsNaN(v.Pitch)) throw new Exception("Unsafe fallback");
}
Check(true, "2000 unknown/modded trait combinations stay bounded");
Check(VoiceProfile.From(new VoiceTraits { Identity = "Pawn_A" }).Pitch != VoiceProfile.From(new VoiceTraits { Identity = "Pawn_B" }).Pitch, "individual identities vary");
Check(MathEx.ZoomGain(10, 12, 18) == 1 && MathEx.ZoomGain(18, 12, 18) == 0, "zoom endpoints");
Check(Math.Abs(MathEx.ZoomGain(15, 12, 18) - 0.5f) < 0.00001, "zoom midpoint");
Check(MathEx.ZoomGain(float.NaN, 12, 18) == 0, "invalid zoom fails silent");
float previous = 1;
for (float z = 10; z < 22; z += 0.05f) { float g = MathEx.ZoomGain(z, 12, 18); if (g > previous + 0.0001) throw new Exception("Nonmonotonic fade"); previous = g; }
Check(true, "monotonic zoom fade");
Check(MathEx.CentreGain(0.5f, 0.5f) > MathEx.CentreGain(0, 0), "centre is prominent");
Check(MathEx.Headroom(new float[] { 1, 1, 1, 1, 1, 1, 1, 1 }) <= 0.301f, "overlapping sources retain mix headroom");
Check(MathEx.Headroom(Enumerable.Repeat(1f, 25).ToArray()) >= 0.169f, "25 speakers do not become nearly silent");
Check(SpeechText.Tokens("<color=red>Héllo</color>!").SequenceEqual(SpeechText.Tokens("HELLO")), "tags and Latin accents");
Check(SpeechText.Tokens("こんにちは世界").Count > 0, "unsupported scripts have audible deterministic fallback");
Check(SpeechText.Tokens("<b> </b> 😀").Count == 0, "no empty/emoji-only speech");
Check(SpeechText.Tokens("\ud800A\udfff").SequenceEqual(new[] { 0 }), "malformed UTF-16 is safe");
Check(SpeechText.Tokens(new string('A', 10000)).Count == 96, "bounded text");
Check(SpeechText.Tokens("2").SequenceEqual(SpeechText.Tokens("TWO")), "digits expand");
var letters = Enumerable.Range(0, 26).Select(n => Enumerable.Range(0, 6615).Select(i => (float)Math.Sin(i * (0.02 + n * 0.001)) * 0.8f).ToArray()).ToArray();
var bank = new LetterBank(44100, letters);
var pcm = Animalese.Render(bank, "HELLO WORLD", voice);
Check(pcm.Length > 0 && pcm.All(x => float.IsFinite(x) && Math.Abs(x) <= 0.73f), "finite bounded synthesis");
Check(pcm.SequenceEqual(Animalese.Render(bank, "HELLO WORLD", voice)), "deterministic PCM");
Check(pcm[^1] == 0 && pcm[0] == 0, "click-reducing endpoint fades");
Check(Animalese.Render(bank, new string('A', 1000), voice).Length <= 44100 * 4, "maximum utterance duration");
Check(Animalese.Render(bank, "", voice).Length == 0, "empty synthesis");
byte[] MakeWave(bool extraChunk)
{
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream);
    void Tag(string tag) => writer.Write(System.Text.Encoding.ASCII.GetBytes(tag));
    Tag("RIFF"); writer.Write(0); Tag("WAVE");
    if (extraChunk) { Tag("JUNK"); writer.Write(1); writer.Write((byte)42); writer.Write((byte)0); }
    Tag("fmt "); writer.Write(16); writer.Write((ushort)1); writer.Write((ushort)1); writer.Write(44100); writer.Write(44100); writer.Write((ushort)1); writer.Write((ushort)8);
    Tag("data"); writer.Write(26 * 6615); writer.Write(Enumerable.Repeat((byte)128, 26 * 6615).ToArray());
    stream.Position = 4; writer.Write((int)stream.Length - 8); return stream.ToArray();
}
Check(LetterBank.ReadWave(MakeWave(false)).Letters[0].All(x => x == 0), "PCM8 silence decoding");
Check(LetterBank.ReadWave(MakeWave(true)).Letters.Length == 26, "unknown odd-length RIFF chunk handled");
Reject(() => LetterBank.ReadWave(new byte[10]), "short WAV rejected");
var truncated = MakeWave(false)[..100];
Reject(() => LetterBank.ReadWave(truncated), "truncated WAV rejected");
checks += FeatureChecks.Run();
Console.WriteLine($"All {checks} checks passed. This does not test the Unity/RimWorld runtime.");
