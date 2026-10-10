using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace RoutineRest.Core;

/// <summary>Copies validated legacy snapshots once, retaining raw evidence and never summing ambiguous daily durations.</summary>
public static class StateMigration
{
    private const string ReviewNote = "历史计时存在两份累计，已保留较大值，待核对。";

    /// <summary>The caller must hold the destination writer lease. Existing unified records always take precedence over legacy copies.</summary>
    public static bool ImportIfEmpty(JsonStateStore destination, IReadOnlyList<string> sourceFiles)
    {
        if (File.Exists(destination.FilePath) || File.Exists(destination.FilePath + ".bak")) return false;
        string[] files = sourceFiles.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (files.Length == 0) return false;
        AppState[] snapshots = files.Select(JsonStateStore.ReadSnapshot).ToArray();
        AppState merged = Merge(snapshots);
        string backupDirectory = Path.Combine(Path.GetDirectoryName(destination.FilePath) ?? throw new IOException("数据路径无效。"),
            "migration-backups", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backupDirectory);
        for (int index = 0; index < files.Length; index++)
            File.Copy(files[index], Path.Combine(backupDirectory, $"source-{index + 1}.json"), false);
        destination.Save(merged);
        return true;
    }

    /// <summary>Preserves the newest live timer/settings, unions timestamped entries and retains the largest untraceable daily counters.</summary>
    public static AppState Merge(IReadOnlyList<AppState> sources)
    {
        if (sources.Count == 0) throw new ArgumentException("至少需要一份旧记录。", nameof(sources));
        foreach (AppState source in sources) StateValidation.Validate(source);
        AppState newest = sources.OrderByDescending(source => source.LastCheckpoint).First();
        AppState result = JsonSerializer.Deserialize<AppState>(JsonSerializer.Serialize(newest))
            ?? throw new InvalidDataException("无法复制旧记录。");
        result.Days = new Dictionary<string, DaySummary>(StringComparer.Ordinal);
        result.WaterEntries = sources.SelectMany(source => source.WaterEntries).Distinct().OrderBy(entry => entry.At).ToList();
        result.Releases = sources.SelectMany(source => source.Releases).Distinct().OrderBy(entry => entry.At).ToList();
        Dictionary<string, int> waterByDay = new(StringComparer.Ordinal);
        foreach (WaterEntry entry in result.WaterEntries)
        {
            string key = DayKey(entry.At);
            waterByDay[key] = checked(waterByDay.GetValueOrDefault(key) + entry.Millilitres);
        }
        foreach (string key in sources.SelectMany(source => source.Days.Keys).Concat(waterByDay.Keys).Distinct(StringComparer.Ordinal))
        {
            DaySummary[] days = sources.Where(source => source.Days.ContainsKey(key)).Select(source => source.Days[key]).ToArray();
            long untrackedWater = sources.Where(source => source.Days.ContainsKey(key))
                .Select(source => Math.Max(0L, source.Days[key].WaterMl - source.WaterEntries.Where(entry => DayKey(entry.At) == key).Sum(entry => (long)entry.Millilitres)))
                .DefaultIfEmpty(0L).Max();
            DaySummary day = new()
            {
                WorkSeconds = days.Length == 0 ? 0 : days.Max(item => item.WorkSeconds),
                RestSeconds = days.Length == 0 ? 0 : days.Max(item => item.RestSeconds),
                CompletedBreaks = days.Length == 0 ? 0 : days.Max(item => item.CompletedBreaks),
                InterruptedBreaks = Math.Max(days.Length == 0 ? 0 : days.Max(item => item.InterruptedBreaks),
                    result.Releases.Count(entry => DayKey(entry.At) == key)),
                WaterMl = checked((int)(waterByDay.GetValueOrDefault(key) + untrackedWater)),
                HistoryNote = days.Select(item => item.HistoryNote).FirstOrDefault(note => note.Length > 0) ?? ""
            };
            if (days.Select(item => (item.WorkSeconds, item.RestSeconds, item.CompletedBreaks)).Distinct().Count() > 1)
                day.HistoryNote = ReviewNote;
            result.Days.Add(key, day);
        }
        StateValidation.Validate(result);
        return result;
    }
    private static string DayKey(DateTimeOffset at) => at.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
