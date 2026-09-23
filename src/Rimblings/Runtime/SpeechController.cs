using System;
using System.Collections.Generic;
using System.IO;
using Rimblings.Core;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Object = UnityEngine.Object;

namespace Rimblings;

// Presentation-only. Unity operations remain on the main thread.
public sealed class SpeechController : GameComponent
{
    // Safety bound for a burst of log events, not a simultaneous playback cap.
    private const int MaxPendingRequests = 128;
    private sealed class Request
    {
        internal Pawn Pawn = null!;
        internal string Text = string.Empty;
        internal float Time;
    }
    private sealed class Voice
    {
        internal Pawn Pawn = null!;
        internal GameObject Object = null!;
        internal AudioSource Source = null!;
        internal AudioClip Clip = null!;
        internal LetterBank Bank = null!;
        internal float[] Original = Array.Empty<float>();
        internal int Offset;
        internal float Speed = 1;
        internal Vector3 View;
        internal float Distance;
        internal float Target;
        internal float Started;
    }
    private readonly List<Request> pending = new List<Request>(32);
    private readonly List<Voice> voices = new List<Voice>(8);
    private readonly List<float> gains = new List<float>(8);
    private readonly Dictionary<string, float> cooldowns = new Dictionary<string, float>();
    private readonly LetterBank?[] banks = new LetterBank?[8];
    private bool bankFailed;
    private Map? map;
    private float nextCooldownCleanup;
    public SpeechController(Game game) { }
    private static RimblingsSettings Settings => RimblingsMod.Settings;
    private static bool MapView()
    {
        if (Current.ProgramState != ProgramState.Playing || Current.Game == null || Find.CurrentMap == null) return false;
        var mode = WorldRendererUtility.CurrentWorldRenderMode;
        return mode == WorldRenderMode.None || mode == WorldRenderMode.Background;
    }
    private static float Zoom()
    {
        Camera camera = Find.Camera;
        return camera != null && camera.orthographic ? MathEx.ZoomGain(camera.orthographicSize, Settings.FullZoom, Settings.SilentZoom) : 0;
    }
    private static float Speed() => SpeechTempo.SafeSpeed(Find.TickManager.TickRateMultiplier);
    private static bool Eligible(Pawn pawn)
    {
        if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Map != Find.CurrentMap || pawn.RaceProps?.Humanlike != true) return false;
        if (pawn.Map.fogGrid.IsFogged(pawn.Position)) return false;
        return Settings.NonPlayer || pawn.Faction?.IsPlayer == true;
    }
    private static float DistanceGain(Pawn pawn)
    {
        Camera camera = Find.Camera;
        if (camera == null) return 0;
        // The listener is the camera centre projected onto the map plane, not
        // the elevated camera transform. No viewport bounds test: edges fade.
        Ray ray = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
        var ground = new Plane(Vector3.up, Vector3.zero);
        if (!ground.Raycast(ray, out float enter)) return 0;
        Vector3 listener = ray.GetPoint(enter);
        Vector3 p = pawn.DrawPos;
        float dx = p.x - listener.x, dz = p.z - listener.z;
        return MathEx.DistanceGain(Mathf.Sqrt(dx * dx + dz * dz), Settings.HearingRadius);
    }
    private static bool UnderMouse(Pawn pawn, Vector3 view, out float squaredDistance)
    {
        squaredDistance = float.MaxValue;
        if (view.z <= 0 || !MathEx.OnScreen(view.x, view.y)) return false;
        Vector3 mouse = Input.mousePosition;
        if (mouse.x < 0 || mouse.y < 0 || mouse.x > Screen.width || mouse.y > Screen.height) return false;
        Vector3 screen = Find.Camera.WorldToScreenPoint(pawn.DrawPos);
        squaredDistance = (screen.x - mouse.x) * (screen.x - mouse.x) + (screen.y - mouse.y) * (screen.y - mouse.y);
        return squaredDistance < 48 * 48;
    }
    private static float Priority(Pawn pawn)
    {
        float distance = DistanceGain(pawn);
        Vector3 view = Find.Camera.WorldToViewportPoint(pawn.DrawPos);
        if (UnderMouse(pawn, view, out _)) return distance * 3;
        if (Find.Selector.SingleSelectedObject == pawn) return distance * 2;
        return distance * MathEx.CentreGain(view.x, view.y);
    }
    public bool CanQueue(Pawn pawn)
    {
        if (!Settings.Enabled || Settings.Volume <= 0 || bankFailed || !MapView() || Find.TickManager.Paused || Zoom() <= 0.001f || !Eligible(pawn) || DistanceGain(pawn) <= 0.001f) return false;
        string id = pawn.GetUniqueLoadID();
        if (cooldowns.TryGetValue(id, out float until) && Time.realtimeSinceStartup < until) return false;
        foreach (Voice voice in voices) if (voice.Pawn == pawn) return false;
        foreach (Request request in pending) if (request.Pawn == pawn) return false;
        return pending.Count < MaxPendingRequests;
    }
    public void Queue(Pawn pawn, string text)
    {
        if (!CanQueue(pawn) || string.IsNullOrWhiteSpace(text)) return;
        pending.Add(new Request { Pawn = pawn, Text = text, Time = Time.realtimeSinceStartup });
        cooldowns[pawn.GetUniqueLoadID()] = Time.realtimeSinceStartup + 1.5f / Speed();
    }
    private LetterBank? LoadBank(VoiceProfile profile)
    {
        if (bankFailed) return null;
        if (banks[profile.BankIndex] != null) return banks[profile.BankIndex];
        try
        {
            string path = Path.Combine(RimblingsMod.Root, "Voices", "Extension", profile.BankName + ".wav");
            if (!File.Exists(path))
            {
                Log.WarningOnce("[Rimblings] Extension voice bank missing; using the original voice bank. Rebuild/install the complete package.", 194072703);
                path = Path.Combine(RimblingsMod.Root, "Voices", "default.wav");
            }
            if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("Voice bank exceeds 4 MB.");
            banks[profile.BankIndex] = LetterBank.ReadWave(File.ReadAllBytes(path));
            return banks[profile.BankIndex];
        }
        catch (Exception ex)
        {
            bankFailed = true;
            pending.Clear();
            Log.Warning("[Rimblings] Extension voice bank unavailable; speech is disabled for this session. Rebuild/install the complete package. " + ex.Message);
            return null;
        }
    }
    public override void GameComponentUpdate()
    {
        try { UpdateAudio(); }
        catch (Exception ex)
        {
            Clear();
            Log.WarningOnce("[Rimblings] Audio reset after an error: " + ex.Message, 194072702);
        }
    }
    private void UpdateAudio()
    {
        if (!MapView() || !Settings.Enabled || Find.TickManager.Paused)
        {
            Clear();
            return;
        }
        if (map != Find.CurrentMap) { Clear(); map = Find.CurrentMap; }
        float now = Time.realtimeSinceStartup;
        if (now >= nextCooldownCleanup)
        {
            var expired = new List<string>();
            foreach (var pair in cooldowns) if (pair.Value <= now) expired.Add(pair.Key);
            foreach (string key in expired) cooldowns.Remove(key);
            nextCooldownCleanup = now + 5;
        }
        float speed = Speed();
        for (int i = voices.Count - 1; i >= 0; i--)
        {
            Voice voice = voices[i];
            if (!Eligible(voice.Pawn) || (now - voice.Started > 0.1f && !voice.Source.isPlaying) || (Settings.LimitVoices && i >= Settings.MaxVoices))
            { RemoveAt(i); continue; }
            if (Math.Abs(voice.Speed - speed) > 0.0001f && !Retempo(voice, speed, now)) RemoveAt(i);
        }
        // Drain each fresh burst in this update. A one-per-frame drain can
        // discard a crowded colony's requests before their turn arrives.
        // Never scan all map pawns or replay a stale backlog.
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            Request request = pending[i];
            if (now - request.Time > 2f || !Eligible(request.Pawn) || Zoom() <= 0.001f || DistanceGain(request.Pawn) <= 0.001f)
            { pending.RemoveAt(i); continue; }
        }
        while (pending.Count > 0)
        {
            int best = 0;
            float bestPriority = Priority(pending[0].Pawn);
            for (int i = 1; i < pending.Count; i++)
            {
                float priority = Priority(pending[i].Pawn);
                if (priority > bestPriority) { best = i; bestPriority = priority; }
            }
            Request request = pending[best];
            pending.RemoveAt(best);
            if (Settings.LimitVoices && voices.Count >= Settings.MaxVoices)
            {
                int quietest = -1;
                float lowest = bestPriority * 0.8f;
                for (int i = 0; i < voices.Count; i++)
                {
                    float priority = Priority(voices[i].Pawn);
                    if (priority < lowest) { quietest = i; lowest = priority; }
                }
                if (quietest >= 0) RemoveAt(quietest);
            }
            if (!Settings.LimitVoices || voices.Count < Settings.MaxVoices) Start(request, speed);
        }
        Voice? focus = null;
        float bestMouse = 48 * 48;
        foreach (Voice voice in voices)
        {
            voice.View = Find.Camera.WorldToViewportPoint(voice.Pawn.DrawPos);
            voice.Distance = DistanceGain(voice.Pawn);
            if (voice.Distance > 0.001f && UnderMouse(voice.Pawn, voice.View, out float distance) && distance < bestMouse)
            { focus = voice; bestMouse = distance; }
        }
        if (focus == null)
        {
            Pawn? selected = Find.Selector.SingleSelectedObject as Pawn;
            foreach (Voice voice in voices) if (voice.Pawn == selected && voice.Distance > 0.001f) { focus = voice; break; }
        }
        gains.Clear();
        foreach (Voice voice in voices)
        {
            float centre = MathEx.CentreGain(voice.View.x, voice.View.y);
            voice.Target = voice.Distance * (focus == voice ? 1 : centre * (focus == null ? 1 : Settings.BackgroundGain));
            gains.Add(voice.Target);
        }
        float master = Settings.Volume * MathEx.Clamp(Prefs.VolumeGame, 0, 1) * Zoom() * MathEx.Headroom(gains);
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
        foreach (Voice voice in voices)
        {
            float target = master * voice.Target;
            voice.Source.volume = MathEx.Approach(voice.Source.volume, target, target > voice.Source.volume ? 0.045f : 0.12f, dt);
            voice.Source.panStereo = MathEx.Approach(voice.Source.panStereo, MathEx.Clamp((voice.View.x - 0.5f) * 1.8f, -0.9f, 0.9f), 0.08f, dt);
            voice.Object.transform.position = voice.Pawn.DrawPos;
        }
    }
    private AudioClip MakeClip(float[] samples, int sampleRate)
    {
        AudioClip clip = AudioClip.Create("Rimblings speech", samples.Length, 1, sampleRate, false);
        try
        {
            if (!clip.SetData(samples, 0)) throw new InvalidOperationException("Could not fill speech audio clip.");
            return clip;
        }
        catch { Object.Destroy(clip); throw; }
    }
    private void Start(Request request, float speed)
    {
        VoiceProfile profile = VoiceProfile.From(VoiceAdapters.Get(request.Pawn));
        LetterBank? bank = LoadBank(profile);
        if (bank == null) return;
        float[] original = Animalese.Render(bank, request.Text, profile, shortenWords: Settings.ShortenWords);
        if (original.Length == 0) return;
        AudioClip? clip = null;
        GameObject? obj = null;
        try
        {
            clip = MakeClip(SpeechTempo.Render(original, bank.SampleRate, speed), bank.SampleRate);
            obj = new GameObject("Rimblings voice");
            var source = obj.AddComponent<AudioSource>();
            source.playOnAwake = false; source.loop = false; source.dopplerLevel = 0;
            // Tempo is rendered separately: do not use pitch=speed (raises
            // voices and cannot represent all game speed multipliers).
            source.spatialBlend = 0; source.pitch = 1; source.volume = 0; source.clip = clip;
            source.Play();
            voices.Add(new Voice { Pawn = request.Pawn, Object = obj, Source = source, Clip = clip, Bank = bank, Original = original, Speed = speed, Started = Time.realtimeSinceStartup });
        }
        catch
        {
            if (obj != null) Object.Destroy(obj);
            if (clip != null) Object.Destroy(clip);
            throw;
        }
    }
    private bool Retempo(Voice voice, float speed, float now)
    {
        if (!voice.Source.isPlaying) return false;
        int offset = SpeechTempo.SourceOffset(voice.Offset, voice.Source.timeSamples, voice.Speed, voice.Original.Length);
        if (offset >= voice.Original.Length) return false;
        AudioClip replacement = MakeClip(SpeechTempo.Render(voice.Original, voice.Bank.SampleRate, speed, offset), voice.Bank.SampleRate);
        AudioClip old = voice.Clip;
        voice.Source.Stop();
        voice.Source.clip = replacement;
        voice.Clip = replacement;
        voice.Offset = offset;
        voice.Speed = speed;
        voice.Started = now;
        voice.Source.Play();
        Object.Destroy(old);
        return true;
    }
    private void RemoveAt(int index)
    {
        Voice voice = voices[index];
        if (voice.Source != null) voice.Source.Stop();
        if (voice.Object != null) Object.Destroy(voice.Object);
        if (voice.Clip != null) Object.Destroy(voice.Clip);
        voices.RemoveAt(index);
    }
    public void Clear()
    {
        for (int i = voices.Count - 1; i >= 0; i--) RemoveAt(i);
        pending.Clear(); cooldowns.Clear(); map = null;
    }
}
