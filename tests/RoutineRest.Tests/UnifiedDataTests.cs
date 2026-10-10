using System;
using System.IO;
using RoutineRest.Core;

internal static class UnifiedDataTests
{
    internal static int Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "routine-unified-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string executableDirectory = Path.Combine(root, "app");
            string dataDirectory = DataLocation.ForExecutable(executableDirectory);
            Require(dataDirectory == Path.Combine(executableDirectory, "data"));
            Require(DataLocation.ForExecutable(executableDirectory + Path.DirectorySeparatorChar) == dataDirectory);
            bool rejectedRelative = false;
            try { DataLocation.ForExecutable("app"); } catch (ArgumentException) { rejectedRelative = true; }
            Require(rejectedRelative);
            Console.WriteLine("PASS 统一目录只由EXE绝对目录决定");

            using (FileStream first = DataLocation.AcquireWriter(dataDirectory))
            {
                bool rejectedSecond = false;
                try { using FileStream second = DataLocation.AcquireWriter(dataDirectory); }
                catch (IOException) { rejectedSecond = true; }
                Require(rejectedSecond);
            }
            using (FileStream reopened = DataLocation.AcquireWriter(dataDirectory)) { }
            Console.WriteLine("PASS 同一路径只允许一个写入实例且退出后可重开");

            DateTimeOffset day = new(2026, 10, 9, 12, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 10, 9)));
            AppState desktop = new() { LastCheckpoint = day.AddDays(1), WorkSeconds = 35 };
            desktop.Settings.DailyWaterCups = 1;
            desktop.Days.Add("2026-10-09", new DaySummary { WorkSeconds = 5500, RestSeconds = 600, WaterMl = 300, CompletedBreaks = 1 });
            desktop.Days.Add("2026-10-10", new DaySummary { WorkSeconds = 35 });
            desktop.WaterEntries.Add(new WaterEntry(day, 300));
            AppState cache = new() { LastCheckpoint = day.AddHours(10) };
            cache.Days.Add("2026-09-28", new DaySummary { WorkSeconds = 6000 });
            cache.Days.Add("2026-10-09", new DaySummary { WorkSeconds = 19400, RestSeconds = 3000, WaterMl = 1100, CompletedBreaks = 5 });
            cache.WaterEntries.Add(new WaterEntry(day.AddHours(4), 300));
            cache.WaterEntries.Add(new WaterEntry(day.AddHours(7), 300));
            cache.WaterEntries.Add(new WaterEntry(day.AddHours(8), 500));
            AppState merged = StateMigration.Merge(new[] { desktop, cache, cache });
            Require(merged.Days.Count == 3 && merged.Days["2026-10-10"].WorkSeconds == 35);
            Require(merged.WorkSeconds == 35 && merged.Settings.DailyWaterCups == 1);
            Require(merged.Days["2026-10-09"].WaterMl == 1400 && merged.WaterEntries.Count == 4);
            Require(merged.Days["2026-10-09"].WorkSeconds == 19400 && merged.Days["2026-10-09"].HistoryNote.Length > 0);
            Require(desktop.Days["2026-10-09"].WaterMl == 300 && desktop.Days.Count == 2);
            Console.WriteLine("PASS 两份历史保留日期与最新计时设置，饮水去重且不相加模糊时长");

            AppState repeated = StateMigration.Merge(new[] { merged, cache });
            Require(repeated.Days["2026-10-09"].WaterMl == 1400 && repeated.WaterEntries.Count == 4);
            Console.WriteLine("PASS 已合并数据再次遇到旧副本不重复累计");

            AppState aggregateOnly = new();
            aggregateOnly.Days.Add("2026-10-09", new DaySummary { WaterMl = 100 });
            AppState withUntracked = StateMigration.Merge(new[] { merged, aggregateOnly });
            Require(withUntracked.Days["2026-10-09"].WaterMl == 1500);
            Require(StateMigration.Merge(new[] { withUntracked, aggregateOnly }).Days["2026-10-09"].WaterMl == 1500);
            Console.WriteLine("PASS 旧版缺失饮水明细的累计仍保留且重复合并稳定");

            aggregateOnly.Days["2026-10-09"].WaterMl = int.MaxValue;
            bool rejectedOverflow = false;
            try { StateMigration.Merge(new[] { merged, aggregateOnly }); } catch (OverflowException) { rejectedOverflow = true; }
            Require(rejectedOverflow && merged.Days["2026-10-09"].WaterMl == 1400);
            Console.WriteLine("PASS 合并溢出拒绝且不改写源状态");

            AppState historicalOffset = new();
            historicalOffset.WaterEntries.Add(new WaterEntry(new DateTimeOffset(2026, 10, 9, 0, 5, 0, TimeSpan.FromHours(8)), 250));
            Require(StateMigration.Merge(new[] { historicalOffset }).Days["2026-10-09"].WaterMl == 250);
            Console.WriteLine("PASS 迁移饮水按原记录时区的日期归属");

            JsonStateStore legacy = new(Path.Combine(root, "legacy"));
            legacy.Save(desktop);
            string original = File.ReadAllText(legacy.FilePath);
            JsonStateStore unified = new(dataDirectory);
            Require(StateMigration.ImportIfEmpty(unified, new[] { legacy.FilePath }));
            Require(unified.Load().Days.Count == 2);
            Require(File.ReadAllText(legacy.FilePath) == original);
            Require(Directory.GetFiles(Path.Combine(dataDirectory, "migration-backups"), "*.json", SearchOption.AllDirectories).Length == 1);
            cache.Days["2026-10-09"].WaterMl = 999;
            legacy.Save(cache);
            Require(!StateMigration.ImportIfEmpty(unified, new[] { legacy.FilePath }));
            Require(unified.Load().Days["2026-10-09"].WaterMl == 300);
            Console.WriteLine("PASS 首次迁移保留原文件和备份，已有统一记录不被旧副本覆盖");

            unified.Save(desktop);
            File.Move(unified.FilePath, unified.FilePath + ".removed");
            Require(!StateMigration.ImportIfEmpty(unified, new[] { legacy.FilePath }));
            Require(unified.Load().Days["2026-10-09"].WaterMl == 300 && File.Exists(unified.FilePath));
            Console.WriteLine("PASS 统一主文件缺失时恢复统一备份，不重新导入旧副本");

            string broken = Path.Combine(root, "broken.json");
            File.WriteAllText(broken, "{\"Version\":999}");
            JsonStateStore empty = new(Path.Combine(root, "empty"));
            bool rejectedBroken = false;
            try { StateMigration.ImportIfEmpty(empty, new[] { broken }); } catch (InvalidDataException) { rejectedBroken = true; }
            Require(rejectedBroken && !File.Exists(empty.FilePath));
            Require(File.ReadAllText(broken) == "{\"Version\":999}");
            Console.WriteLine("PASS 损坏旧记录阻止迁移，不能创建空白记录覆盖证据");
            return 10;
        }
        finally
        {
            string resolved = Path.GetFullPath(root);
            Require(resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase));
            Directory.Delete(resolved, true);
        }
    }
    private static void Require(bool condition) { if (!condition) throw new Exception("Unified data regression failed."); }
}
