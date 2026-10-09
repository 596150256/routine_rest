using System;
using System.IO;
using RoutineRest.Core;

internal static class Program
{
    private static int count;
    private static readonly DateTimeOffset Start = LocalAt(28, 9, 0, 0);
    private static void Main()
    {
        Run("首次输入才开始", () => {
            RoutineEngine e = New();
            e.Advance(Start, 30, false, true);
            Equal(Phase.Waiting, e.State.Phase);
            e.Advance(Start.AddSeconds(30), 1, true, true);
            Equal(Phase.Working, e.State.Phase);
            Equal(0d, e.State.WorkSeconds);
        });
        Run("50分钟自动休息，无需点击", () => {
            RoutineEngine e = Working();
            for (int second = 60; second <= 2940; second += 60)
                e.Advance(Start.AddSeconds(second), 60, true, true);
            e.Advance(Start.AddSeconds(2999), 59, true, true);
            Equal(Phase.Working, e.State.Phase);
            e.Advance(Start.AddSeconds(3000), 1, false, true);
            Equal(Phase.Resting, e.State.Phase);
            Equal(600d, e.RemainingSeconds);
            Equal(3000d, e.Today(Start).WorkSeconds);
        });
        Run("休息到期释放并等待喝水", () => {
            RoutineEngine e = Resting();
            e.Advance(Start.AddSeconds(3599), 599, false, true);
            Equal(Phase.Resting, e.State.Phase);
            e.Advance(Start.AddSeconds(3600), 1, false, true);
            Equal(Phase.Hydration, e.State.Phase);
            Equal(600d, e.Today(Start).RestSeconds);
            Equal(1, e.Today(Start).CompletedBreaks);
            e.RecordWater(250, Start.AddHours(1));
            Equal(250, e.Today(Start).WaterMl);
            Equal(Phase.Waiting, e.State.Phase);
            Equal(1, e.State.WaterEntries.Count);
        });
        Run("无操作超过5分钟暂停，半小时离开不增加工作", () => {
            RoutineEngine e = Working();
            e.Advance(Start.AddMinutes(30), 1800, false, true);
            Equal(300d, e.State.WorkSeconds);
            Equal(300d, e.Today(Start).WorkSeconds);
            Equal(Phase.Working, e.State.Phase);
            Equal(0, e.Today(Start).CompletedBreaks);
            Equal(0d, e.Today(Start).RestSeconds);
        });
        Run("跨过5分钟边界只累计剩余宽限秒数", () => {
            RoutineEngine e = Working();
            e.Advance(Start.AddSeconds(299), 299, false, true);
            Equal(299d, e.State.WorkSeconds);
            e.Advance(Start.AddSeconds(301), 2, false, true);
            Equal(300d, e.State.WorkSeconds);
            e.Advance(Start.AddSeconds(901), 600, false, true);
            Equal(300d, e.State.WorkSeconds);
        });
        Run("恢复操作保留累计，不补算离开时间", () => {
            RoutineEngine e = Working();
            e.Advance(Start.AddSeconds(900), 900, false, true);
            e.Advance(Start.AddSeconds(1800), 900, true, true);
            Equal(300d, e.State.WorkSeconds);
            e.Advance(Start.AddSeconds(1801), 1, false, true);
            Equal(301d, e.State.WorkSeconds);
        });
        Run("累计40分钟后离开不会误触发50分钟休息", () => {
            RoutineEngine e = Working();
            for (int second = 60; second <= 2400; second += 60)
                e.Advance(Start.AddSeconds(second), 60, true, true);
            e.Advance(Start.AddSeconds(6000), 3600, false, true);
            Equal(2700d, e.State.WorkSeconds);
            Equal(Phase.Working, e.State.Phase);
            Equal(0, e.State.PendingWaterCount);
        });
        Run("闲置跨午夜按暂停发生时间归属工作统计", () => {
            RoutineEngine e = Working();
            DateTimeOffset beforeMidnight = LocalAt(28, 23, 58, 0);
            e.Advance(beforeMidnight.AddMinutes(30), 1800, false, true);
            Equal(120d, e.State.Days["2026-09-28"].WorkSeconds);
            Equal(180d, e.State.Days["2026-09-29"].WorkSeconds);
        });
        Run("重启时读取系统空闲时间，不重新赠送5分钟", () => {
            RoutineEngine e = Working();
            e.Advance(Start.AddSeconds(300), 300, false, true);
            RoutineEngine loaded = new(e.State);
            loaded.Recover(Start.AddMinutes(30), 1800);
            True(loaded.IsWorkPaused);
            loaded.Advance(Start.AddMinutes(31), 60, false, true, 1860);
            Equal(300d, loaded.State.WorkSeconds);
            loaded.Advance(Start.AddMinutes(31).AddSeconds(1), 1, true, true, 0);
            True(!loaded.IsWorkPaused);
            Equal(300d, loaded.State.WorkSeconds);
            loaded.Advance(Start.AddMinutes(31).AddSeconds(2), 1, false, true, 1);
            Equal(301d, loaded.State.WorkSeconds);
        });
        Run("延迟采样后恢复操作只累计输入后的实际秒数", () => {
            RoutineEngine e = Working();
            e.Advance(Start.AddSeconds(900), 900, false, true, 900);
            e.Advance(Start.AddSeconds(3600), 2700, true, true, 5);
            Equal(305d, e.State.WorkSeconds);
            True(!e.IsWorkPaused);
        });
        Run("午夜后恢复操作不会把恢复后的工作算到前一天", () => {
            RoutineEngine e = Working();
            DateTimeOffset midnight = LocalAt(29, 0, 0, 0);
            e.Recover(midnight.AddSeconds(-60), 600);
            e.Advance(midnight.AddSeconds(60), 120, true, true, 5);
            Equal(5d, e.State.WorkSeconds);
            Equal(5d, e.State.Days["2026-09-29"].WorkSeconds);
            True(!e.State.Days.ContainsKey("2026-09-28"));
        });
        Run("无效空闲时间被拒绝且不改变累计", () => {
            RoutineEngine e = Working();
            Throws<ArgumentOutOfRangeException>(() => e.Advance(Start, 1, false, true, -1));
            Throws<ArgumentOutOfRangeException>(() => e.Advance(Start, 1, false, true, double.NaN));
            Throws<ArgumentOutOfRangeException>(() => e.Recover(Start, double.PositiveInfinity));
            Equal(0d, e.State.WorkSeconds);
        });
        Run("短暂锁屏暂停工作并保留累计", () => {
            RoutineEngine e = Working();
            e.Advance(Start.AddMinutes(5), 300, false, true);
            e.Advance(Start.AddMinutes(6), 60, false, false);
            e.Advance(Start.AddMinutes(7), 60, false, false);
            Equal(300d, e.State.WorkSeconds);
            e.Advance(Start.AddMinutes(7).AddSeconds(1), 1, true, true);
            Equal(300d, e.State.WorkSeconds);
            e.Advance(Start.AddMinutes(7).AddSeconds(2), 1, false, true);
            Equal(301d, e.State.WorkSeconds);
        });
        Run("锁屏满10分钟自然完成休息", () => {
            RoutineEngine e = Working();
            e.Advance(Start.AddMinutes(5), 300, false, true);
            e.Advance(Start.AddMinutes(15), 600, false, false);
            Equal(Phase.Hydration, e.State.Phase);
            Equal(0d, e.State.WorkSeconds);
            Equal(1, e.Today(Start).CompletedBreaks);
        });
        Run("午夜拆分工作和休息", () => {
            RoutineEngine e = Working();
            DateTimeOffset midnight = LocalAt(29, 0, 0, 0);
            e.Advance(midnight.AddSeconds(20), 40, false, true);
            Equal(20d, e.State.Days["2026-09-28"].WorkSeconds);
            Equal(20d, e.State.Days["2026-09-29"].WorkSeconds);
        });
        Run("喝水范围校验与重复提交阻止", () => {
            RoutineEngine e = Resting();
            e.Advance(Start.AddHours(1), 600, false, true);
            Throws<ArgumentOutOfRangeException>(() => e.RecordWater(-1, Start));
            Throws<ArgumentOutOfRangeException>(() => e.RecordWater(5001, Start));
            e.RecordWater(0, Start);
            Throws<InvalidOperationException>(() => e.RecordWater(250, Start));
        });
        Run("紧急解除记次数而不记完成", () => {
            RoutineEngine e = Resting();
            e.Advance(Start.AddSeconds(3010), 10, false, true);
            e.EmergencyRelease(Start.AddSeconds(3010), "测试");
            Equal(Phase.Waiting, e.State.Phase);
            Equal(1, e.Today(Start).InterruptedBreaks);
            Equal(0, e.Today(Start).CompletedBreaks);
            Equal(10d, e.Today(Start).RestSeconds);
        });
        Run("重启不清零工作计时，不补算退出时间", () => {
            RoutineEngine e = Working();
            e.Advance(Start.AddMinutes(5), 300, false, true);
            RoutineEngine loaded = new(e.State);
            loaded.Recover(Start.AddHours(1));
            Equal(300d, loaded.State.WorkSeconds);
            Equal(Phase.Working, loaded.State.Phase);
        });
        Run("重启恢复休息剩余时间", () => {
            RoutineEngine e = Resting();
            e.Recover(Start.AddSeconds(3300));
            Equal(300d, e.RemainingSeconds);
        });
        Run("原子保存及损坏主文件备份恢复", () => {
            string folder = Path.Combine(Path.GetTempPath(), "routine-rest-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try {
                JsonStateStore store = new(folder);
                RoutineEngine e = Working();
                store.Save(e.State);
                e.Advance(Start.AddMinutes(5), 300, false, true);
                store.Save(e.State);
                Equal(300d, store.Load().WorkSeconds);
                File.WriteAllText(store.FilePath, "{ invalid");
                Equal(0d, store.Load().WorkSeconds);
                True(store.RecoveryNotice.Length > 0);
            } finally { Directory.Delete(folder, true); }
        });
        Run("损坏字段拒绝读取", () => {
            AppState state = new() { WorkSeconds = double.NaN };
            Throws<InvalidDataException>(() => StateValidation.Validate(state));
        });
        Run("喝水表单未提交也继续下一轮计时", () => {
            RoutineEngine e = Resting();
            e.Advance(Start.AddHours(1), 600, false, true);
            e.Advance(Start.AddHours(1).AddSeconds(1), 1, true, true);
            e.Advance(Start.AddHours(1).AddSeconds(61), 60, false, true);
            Equal(Phase.Working, e.State.Phase);
            Equal(60d, e.State.WorkSeconds);
            e.RecordWater(200, Start.AddHours(1).AddSeconds(62));
            Equal(Phase.Working, e.State.Phase);
            Equal(60d, e.State.WorkSeconds);
        });
        Run("紧急解除不丢失以前待填的喝水记录", () => {
            RoutineEngine e = Resting();
            e.Advance(Start.AddHours(1), 600, false, true);
            e.StartRest(Start.AddHours(1));
            e.EmergencyRelease(Start.AddHours(1), "测试");
            Equal(1, e.State.PendingWaterCount);
            StateValidation.Validate(e.State);
        });
        Run("系统时钟倒退不增加重启后的休息剩余时间", () => {
            RoutineEngine e = Resting();
            e.Advance(Start.AddMinutes(51), 60, false, true);
            e.Recover(Start);
            Equal(540d, e.RemainingSeconds);
        });
        Run("锁屏被打断不能累计为连续休息", () => {
            RoutineEngine e = Working();
            e.Advance(Start.AddMinutes(5), 300, false, false);
            e.Advance(Start.AddMinutes(5).AddSeconds(1), 1, true, true);
            e.Advance(Start.AddMinutes(10).AddSeconds(1), 300, false, false);
            Equal(Phase.Working, e.State.Phase);
            Equal(300d, e.State.AwaySeconds);
        });
        Run("紧急组合键必须完整连续保持8秒", () => {
            EmergencyChord chord = new();
            chord.Key(0xA2, true, false, 0);
            chord.Key(0xA0, true, false, 0);
            chord.Key(0x7B, true, false, 0);
            True(!chord.IsReleased(7999));
            True(chord.IsReleased(8000));
            chord.Key(0xA0, false, false, 8001);
            True(!chord.IsReleased(9000));
        });
        Run("软件注入不能触发紧急解除", () => {
            EmergencyChord chord = new();
            chord.Key(0xA2, true, true, 0);
            chord.Key(0xA0, true, true, 0);
            chord.Key(0x7B, true, true, 0);
            True(!chord.IsReleased(9000));
        });

        Run("离线跨日恢复按实际休息发生日归属", () => {
            DateTimeOffset beforeMidnight = LocalAt(28, 23, 45, 0);
            RoutineEngine e = New();
            e.StartRest(beforeMidnight);
            e.Recover(beforeMidnight.AddHours(1));
            Equal(600d, e.State.Days["2026-09-28"].RestSeconds);
            Equal(1, e.State.Days["2026-09-28"].CompletedBreaks);
            True(!e.State.Days.ContainsKey("2026-09-29"));
        });

        Run("语法正确但字段损坏的JSON也恢复备份", () => {
            string folder = Path.Combine(Path.GetTempPath(), "routine-rest-invalid-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try {
                JsonStateStore store = new(folder);
                store.Save(new AppState());
                store.Save(new AppState());
                File.WriteAllText(store.FilePath, "{\"Version\":999}");
                Equal(1, store.Load().Version);
                True(store.RecoveryNotice.Length > 0);
            } finally { Directory.Delete(folder, true); }
        });

        Run("300ml一杯，部分饮水保留杯内水位", () => {
            WaterProgress p = WaterProgress.From(650, 6);
            Equal(6, p.Cups.Count);
            Equal(1d, p.Cups[0]);
            Equal(1d, p.Cups[1]);
            True(Math.Abs(p.Cups[2] - 1d / 6) < 0.0001);
            Equal(0d, p.Cups[3]);
            True(Math.Abs(p.Fraction - 650d / 1800) < 0.0001);
        });
        Run("空杯和超过目标的进度正确封顶", () => {
            WaterProgress empty = WaterProgress.From(0, 6);
            Equal(0d, empty.Fraction);
            foreach (double cup in empty.Cups) Equal(0d, cup);
            WaterProgress full = WaterProgress.From(2100, 6);
            Equal(1d, full.Fraction);
            foreach (double cup in full.Cups) Equal(1d, cup);
        });
        Run("每日杯数可配置且无效目标被拒绝", () => {
            Equal(0.5d, WaterProgress.From(750, 5).Fraction);
            Throws<ArgumentOutOfRangeException>(() => WaterProgress.From(0, 0));
            Throws<ArgumentOutOfRangeException>(() => WaterProgress.From(0, 13));
            AppState state = new();
            state.Settings.DailyWaterCups = 0;
            Throws<InvalidDataException>(() => StateValidation.Validate(state));
        });
        Run("旧版本数据缺少饮水目标时默认6杯", () => {
            AppState state = System.Text.Json.JsonSerializer.Deserialize<AppState>("{\"Settings\":{\"Volume\":0.4}}")
                ?? throw new Exception("Deserialize failed");
            Equal(6, state.Settings.DailyWaterCups);
            Equal(0.4d, state.Settings.Volume);
            StateValidation.Validate(state);
        });

        Console.WriteLine($"PASS: {count} behavioral tests");
    }
    private static RoutineEngine New() => new(new AppState());
    private static DateTimeOffset LocalAt(int day, int hour, int minute, int second)
    {
        DateTime local = new(2026, 9, day, hour, minute, second, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }
    private static RoutineEngine Working() { RoutineEngine e = New(); e.Advance(Start, 0, true, true); return e; }
    private static RoutineEngine Resting() { RoutineEngine e = Working(); e.StartRest(Start.AddMinutes(50)); return e; }
    private static void Run(string name, Action test) { test(); count++; Console.WriteLine("PASS " + name); }
    private static void Equal<T>(T expected, T actual) where T : IEquatable<T> { if (!expected.Equals(actual)) throw new Exception($"Expected {expected}, got {actual}"); }
    private static void Equal(Phase expected, Phase actual) { if (expected != actual) throw new Exception($"Expected {expected}, got {actual}"); }
    private static void True(bool value) { if (!value) throw new Exception("Assertion failed"); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}"); }
}
