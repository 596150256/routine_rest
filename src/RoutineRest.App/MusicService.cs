using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Media;
using RoutineRest.Core;

namespace RoutineRest.App;

/// <summary>Offline playback. User-selected local files take precedence over the original built-in ambient loop.</summary>
internal sealed class MusicService : IDisposable
{
    private readonly MediaPlayer player = new();
    private readonly string ambientPath;
    private readonly AppSettings settings;
    private List<string> queue = new();
    private int current;
    private bool active;
    private int failures;
    public bool IsPlaying => active && player.HasAudio && player.Position.TotalSeconds > 0;
    public string Title { get; private set; } = "林间慢呼吸";
    public string Status { get; private set; } = "原创环境音 · 离线播放";
    public double Progress => player.NaturalDuration.HasTimeSpan && player.NaturalDuration.TimeSpan.TotalSeconds > 0
        ? Math.Clamp(player.Position.TotalSeconds / player.NaturalDuration.TimeSpan.TotalSeconds, 0, 1) : 0;

    public MusicService(string directory, AppSettings settings)
    {
        this.settings = settings;
        ambientPath = Path.Combine(directory, "quiet-garden.wav");
        if (!File.Exists(ambientPath)) CreateAmbient(ambientPath);
        player.MediaEnded += (_, _) => { if (active) { current = (current + 1) % queue.Count; PlayCurrent(); } };
        player.MediaOpened += (_, _) => { failures = 0; Status = current < queue.Count && queue[current] == ambientPath ? "原创环境音 · 离线循环" : "本地音乐 · 自动播放"; };
        player.MediaFailed += (_, _) => {
            failures++;
            if (active && failures < queue.Count) { current = (current + 1) % queue.Count; PlayCurrent(); }
            else { Status = "音频无法播放，可在设置中更换文件"; active = false; player.Close(); }
        };
    }
    public void Start()
    {
        if (!settings.MusicEnabled) { Status = "音乐已关闭"; return; }
        queue = settings.MusicFiles.Where(File.Exists).ToList();
        if (queue.Count == 0) queue.Add(ambientPath);
        current = 0;
        failures = 0;
        active = true;
        player.Volume = settings.Volume;
        PlayCurrent();
    }
    private void PlayCurrent()
    {
        string path = queue[current];
        Title = path == ambientPath ? "林间慢呼吸" : Path.GetFileNameWithoutExtension(path);
        Status = "正在播放";
        player.Open(new Uri(path, UriKind.Absolute));
        player.Play();
    }
    public void Stop() { active = false; player.Stop(); player.Close(); Status = "休息时自动播放"; }
    public void Dispose() { active = false; player.Close(); }

    // A quiet, original synthesized pad, not downloaded music. Smooth fade at both ends.
    private static void CreateAmbient(string path)
    {
        const int rate = 22050;
        const int seconds = 48;
        const int samples = rate * seconds;
        using FileStream stream = new(path, FileMode.Create, FileAccess.Write);
        using BinaryWriter writer = new(stream, Encoding.ASCII);
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples * 2);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
        writer.Write((short)1); writer.Write((short)1); writer.Write(rate);
        writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(samples * 2);
        double[] tones = { 130.81, 196.0, 261.63, 329.63, 392.0 };
        for (int i = 0; i < samples; i++)
        {
            double t = i / (double)rate;
            double value = 0;
            for (int k = 0; k < tones.Length; k++)
                value += Math.Sin(2 * Math.PI * tones[k] * t + 0.18 * Math.Sin(t * 0.21)) *
                    (0.025 + 0.012 * Math.Sin(t * 0.16 + k));
            double fade = Math.Min(1, Math.Min(t / 3, (seconds - t) / 3));
            writer.Write((short)(Math.Clamp(value * fade, -1, 1) * short.MaxValue));
        }
    }
}
