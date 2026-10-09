using System;
using System.Collections.Generic;
using System.IO;

namespace RoutineRest.Core;

public enum Phase { Waiting, Working, Resting, Hydration }

/// <summary>Daily durations are seconds, water is millilitres; dates use the machine's local calendar.</summary>
public sealed class DaySummary
{
    public double WorkSeconds { get; set; }
    public double RestSeconds { get; set; }
    public int CompletedBreaks { get; set; }
    public int InterruptedBreaks { get; set; }
    public int WaterMl { get; set; }
}
public sealed record WaterEntry(DateTimeOffset At, int Millilitres);
public sealed record ReleaseEntry(DateTimeOffset At, string Reason);
public sealed class AppSettings
{
    public int DailyWaterCups { get; set; } = 6;
    public double Volume { get; set; } = 0.22;
    public bool MusicEnabled { get; set; } = true;
    public List<string> MusicFiles { get; set; } = new();
}
/// <summary>Versioned, local-only persisted state. No keyboard contents are recorded.</summary>
public sealed class AppState
{
    public int Version { get; set; } = 1;
    public Phase Phase { get; set; }
    public double WorkSeconds { get; set; }
    public double RestSeconds { get; set; }
    public double AwaySeconds { get; set; }
    public int PendingWaterCount { get; set; }
    public DateTimeOffset LastCheckpoint { get; set; }
    public Dictionary<string, DaySummary> Days { get; set; } = new(StringComparer.Ordinal);
    public List<WaterEntry> WaterEntries { get; set; } = new();
    public List<ReleaseEntry> Releases { get; set; } = new();
    public AppSettings Settings { get; set; } = new();
}
/// <summary>Reject corrupt or unsupported data before it reaches timers or UI.</summary>
public static class StateValidation
{
    public static void Validate(AppState state)
    {
        if (state.Version != 1 || state.PendingWaterCount < 0 || state.PendingWaterCount > 10000 || !Enum.IsDefined(state.Phase) ||
            !ValidSeconds(state.WorkSeconds, 3000) || !ValidSeconds(state.RestSeconds, 600) ||
            !ValidSeconds(state.AwaySeconds, 600) || state.Days is null ||
            state.WaterEntries is null || state.Releases is null || state.Settings is null)
            throw new InvalidDataException("状态文件字段不合法或版本不受支持。");
        if (!double.IsFinite(state.Settings.Volume) || state.Settings.Volume < 0 || state.Settings.Volume > 1 ||
            state.Settings.MusicFiles is null || state.Settings.MusicFiles.Count > 1000)
            throw new InvalidDataException("音乐设置不合法。");
        if (state.Settings.DailyWaterCups < 1 || state.Settings.DailyWaterCups > 12) throw new InvalidDataException("每日饮水目标必须为1至12杯。");
        foreach (KeyValuePair<string, DaySummary> item in state.Days)
        {
            DaySummary day = item.Value;
            if (!DateOnly.TryParseExact(item.Key, "yyyy-MM-dd", out _) || day is null ||
                !ValidSeconds(day.WorkSeconds, 172800) || !ValidSeconds(day.RestSeconds, 172800) ||
                day.WaterMl < 0 || day.CompletedBreaks < 0 || day.InterruptedBreaks < 0)
                throw new InvalidDataException("每日统计不合法。");
        }
        foreach (WaterEntry entry in state.WaterEntries)
            if (entry is null || entry.Millilitres < 0 || entry.Millilitres > 5000)
                throw new InvalidDataException("喝水记录不合法。");
        foreach (string file in state.Settings.MusicFiles)
            if (string.IsNullOrWhiteSpace(file) || !Path.IsPathFullyQualified(file))
                throw new InvalidDataException("音乐路径不合法。");
        foreach (ReleaseEntry entry in state.Releases)
            if (entry is null || entry.Reason is null || entry.Reason.Length > 200)
                throw new InvalidDataException("解除记录不合法。");
    }
    private static bool ValidSeconds(double value, double max) => double.IsFinite(value) && value >= 0 && value <= max;
}
