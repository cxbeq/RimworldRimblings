using System.Globalization;
using Rimblings.Core;

if (args.Length < 3)
{
    Console.Error.WriteLine("Usage: VoiceLab bank.wav-or-directory output.wav text [sex] [body] [head] [age]");
    return 2;
}
try
{
    var traits = new VoiceTraits
    {
        Identity = "VoiceLab_1", Sex = args.Length > 3 ? args[3] : "Female",
        Body = args.Length > 4 ? args[4] : "Thin", Head = args.Length > 5 ? args[5] : "Female_AverageNormal",
        BiologicalAge = args.Length > 6 && double.TryParse(args[6], NumberStyles.Float, CultureInfo.InvariantCulture, out double age) ? age : 28
    };
    var voice = VoiceProfile.From(traits);
    string bankPath = Directory.Exists(args[0]) ? Path.Combine(args[0], voice.BankName + ".wav") : args[0];
    var bank = LetterBank.ReadWave(File.ReadAllBytes(bankPath));
    float[] samples = Animalese.Render(bank, args[2], voice);
    using var output = new BinaryWriter(File.Create(args[1]));
    void Tag(string value) => output.Write(System.Text.Encoding.ASCII.GetBytes(value));
    Tag("RIFF"); output.Write(36 + samples.Length * 2); Tag("WAVE");
    Tag("fmt "); output.Write(16); output.Write((ushort)1); output.Write((ushort)1);
    output.Write(bank.SampleRate); output.Write(bank.SampleRate * 2); output.Write((ushort)2); output.Write((ushort)16);
    Tag("data"); output.Write(samples.Length * 2);
    foreach (float sample in samples) output.Write((short)(Math.Clamp(sample, -1, 1) * 32767));
    Console.WriteLine($"Wrote {args[1]}: bank={voice.BankName}, pitch={voice.Pitch:F3}, cadence={voice.Cadence:F3}, duration={samples.Length / (double)bank.SampleRate:F2}s");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("VoiceLab: " + ex.Message);
    return 1;
}
