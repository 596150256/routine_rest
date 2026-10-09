using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Interop;
using RoutineRest.Core;

namespace RoutineRest.App;

internal sealed class RestViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Countdown { get; private set; } = "10:00";
    public string DateLabel { get; private set; } = "";
    public string ModeText { get; set; } = "正在休息";
    public string WorkLabel { get; private set; } = "";
    public string RestLabel { get; private set; } = "";
    public string WaterLabel { get; private set; } = "";
    public string WaterStatus { get; private set; } = "";
    public string WaterAccessibleLabel { get; private set; } = "";
    public string WaterAmountText { get; private set; } = "";
    public string WaterGoalText { get; private set; } = "";
    public System.Collections.Generic.IReadOnlyList<CupView> WaterCups { get; private set; } = Array.Empty<CupView>();
    public int WaterCupColumns { get; private set; } = 6;
    private int cachedWater = -1;
    private int cachedGoal = -1;
    internal sealed record CupView(double FillHeight);

    public string BreakLabel { get; private set; } = "";
    public string ActivityEyebrow { get; private set; } = "";
    public string ActivityTitle { get; private set; } = "";
    public string ActivityBody { get; private set; } = "";
    public string MusicTitle { get; private set; } = "";
    public string MusicStatus { get; private set; } = "";
    public string EmergencyText { get; private set; } = "";
    public double RestProgress { get; private set; }
    public double MusicProgress { get; private set; }
    private static readonly (string Tag, string Title, string Body)[] Activities =
    {
        ("01 / 给自己一点照顾", "先喝口水吧。", "起身接一杯水，慢慢喝。\n让视线离开屏幕，看看窗外的远处。"),
        ("02 / 身体也需要换个姿势", "站起来，走一走。", "放松肩膀，轻轻活动手腕。\n按自己舒服的节奏，在房间里走几步。"),
        ("03 / 让生活轻一点", "做件小小的家务。", "收好桌上的杯子，整理一个角落，\n或者顺手给植物看看水。做一点就好。"),
        ("04 / 猫猫可能正在等你", "去陪陪猫猫。", "摸摸脑袋，玩一会儿逗猫棒。\n如果它正在睡觉，安静陪着也很好。"),
        ("05 / 留一点什么都不做的时间", "不用急着回来。", "再伸个懒腰，慢慢呼吸。\n等这首音乐播一会儿，工作可以再等等。")
    };
    public void Update(RoutineEngine engine, MusicService music, double emergencyProgress, DateTimeOffset now)
    {
        int seconds = (int)Math.Ceiling(engine.RemainingSeconds);
        Countdown = $"{seconds / 60:00}:{seconds % 60:00}";
        DateLabel = now.ToString("MM月dd日  dddd");
        DaySummary day = engine.Today(now);
        WorkLabel = FormatMinutes(day.WorkSeconds);
        RestLabel = FormatMinutes(day.RestSeconds);
        int goal = engine.State.Settings.DailyWaterCups;
        if (cachedWater != day.WaterMl || cachedGoal != goal)
        {
            WaterProgress progress = WaterProgress.From(day.WaterMl, goal);
            WaterCups = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(progress.Cups, fill => new CupView(fill * 42)));
            WaterCupColumns = Math.Clamp(progress.Cups.Count, 1, 6);
            WaterStatus = progress.Fraction >= 1 ? "今日目标已完成" : progress.Fraction >= 0.75 ? "快到目标了" : progress.Fraction >= 0.5 ? "已经过半" : progress.Fraction > 0 ? "慢慢积累中" : "今天还没有记录饮水";
            if (progress.AdditionalMillilitres > 0) WaterStatus = $"另有 {progress.AdditionalMillilitres} ml 已计入总量";
            WaterAmountText = day.WaterMl > 0 && day.WaterMl % WaterProgress.CupMillilitres == 0
                ? $"{day.WaterMl} ml · {day.WaterMl / WaterProgress.CupMillilitres} 杯" : $"{day.WaterMl} ml";
            WaterGoalText = $"每日目标 {goal} 杯（{goal * WaterProgress.CupMillilitres} ml）· 每杯 300 ml";
            WaterAccessibleLabel = $"今日已喝{day.WaterMl}毫升，目标{goal * 300}毫升，完成{progress.Fraction:P0}";
            cachedWater = day.WaterMl; cachedGoal = goal;
        }

        WaterLabel = $"{day.WaterMl} ml";
        BreakLabel = $"已休息 · 完成 {day.CompletedBreaks} 次";
        int index = Math.Clamp((int)(engine.State.RestSeconds / 120), 0, Activities.Length - 1);
        (ActivityEyebrow, ActivityTitle, ActivityBody) = Activities[index];
        RestProgress = engine.State.RestSeconds / RoutineEngine.RestDuration * 100;
        MusicTitle = music.Title;
        MusicStatus = music.Status;
        MusicProgress = music.Progress * 100;
        EmergencyText = emergencyProgress > 0 ? $"紧急解除：继续按住 {Math.Ceiling((1 - emergencyProgress) * 8)} 秒"
            : "紧急情况：Ctrl + Shift + F12 持续按住 8 秒";
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
    public static string FormatMinutes(double seconds) => seconds >= 3600
        ? $"{(int)(seconds / 3600)}时 {(int)(seconds % 3600 / 60)}分" : $"{(int)(seconds / 60)} 分钟";
}

public partial class RestWindow : Window
{
    private bool allowClose;
    public RestWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowMessage);
        if (SystemParameters.ClientAreaAnimation)
        {
            DoubleAnimation breathe = new(0.96, 1.05, TimeSpan.FromSeconds(4)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            BreathingCircle.RenderTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, breathe);
            BreathingCircle.RenderTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, breathe);
        }
    }
    private nint WindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        // Suppress screensaver/display-power commands only for a real rest window.
        long command = wParam.ToInt64() & 0xFFF0;
        if (!allowClose && message == 0x0112 && (command == 0xF140 || command == 0xF170)) handled = true;
        return 0;
    }
    private void OnClosing(object? sender, CancelEventArgs e) { if (!allowClose) e.Cancel = true; }
    internal void EnablePreviewClose() => allowClose = true;
    internal void Finish() { allowClose = true; Close(); }
}
