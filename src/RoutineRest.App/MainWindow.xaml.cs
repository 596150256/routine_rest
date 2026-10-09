using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using RoutineRest.Core;

namespace RoutineRest.App;

public partial class MainWindow : Window
{
    private readonly App app;
    private bool initialized;
    private bool allowClose;
    private DateTime lastHistory;
    public MainWindow(App app)
    {
        this.app = app;
        InitializeComponent();
        MusicEnabled.IsChecked = app.Engine.State.Settings.MusicEnabled;
        Volume.Value = app.Engine.State.Settings.Volume * 100;
        AutoStart.IsChecked = app.IsAutoStartEnabled;
        DataPathLabel.Text = "本地数据：" + app.DataDirectory + "\\state.json（含每日统计、每次喝水和中断记录）";
        DailyWaterCups.ItemsSource = Enumerable.Range(1, 12);
        DailyWaterCups.SelectedItem = app.Engine.State.Settings.DailyWaterCups;
        WaterGoalLabel.Text = $"{app.Engine.State.Settings.DailyWaterCups} 杯 / {app.Engine.State.Settings.DailyWaterCups * 300} ml";
        RefreshPlaylist();
        Closing += OnClosing;
        initialized = true;
        Refresh();
    }
    internal void Refresh()
    {
        RoutineEngine engine = app.Engine;
        PhaseLabel.Text = engine.State.Phase switch {
            Phase.Working when engine.IsWorkPaused => "无操作已满 5 分钟 · 工作计时暂停",
            Phase.Working => "专注进行中 · 距离下次休息", Phase.Resting => "正在休息",
            Phase.Hydration => "休息完成 · 等待再次使用", _ => "等待你的下一次操作"
        };
        int seconds = (int)Math.Ceiling(engine.RemainingSeconds);
        TimerLabel.Text = $"{seconds / 60:00}:{seconds % 60:00}";
        HintLabel.Text = engine.IsWorkPaused ? "再次操作键盘或鼠标后继续累计，之前的工作时间会保留。"
            : engine.State.Phase == Phase.Working ? "累计工作 50 分钟后自动休息；连续无操作 5 分钟会暂停计时。"
            : "休息完成后，键鼠操作会开启下一轮。";
        DaySummary day = engine.Today(DateTimeOffset.Now);
        WorkToday.Text = RestViewModel.FormatMinutes(day.WorkSeconds);
        RestToday.Text = RestViewModel.FormatMinutes(day.RestSeconds);
        WaterToday.Text = $"{day.WaterMl} ml";
        PendingWaterButton.Visibility = engine.State.PendingWaterCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        if ((DateTime.Now - lastHistory).TotalSeconds >= 5)
        {
            HistoryGrid.ItemsSource = engine.State.Days.OrderByDescending(p => p.Key).Select(p =>
                new HistoryRow(p.Key, RestViewModel.FormatMinutes(p.Value.WorkSeconds), RestViewModel.FormatMinutes(p.Value.RestSeconds),
                    $"{p.Value.CompletedBreaks} / {p.Value.InterruptedBreaks}", $"{p.Value.WaterMl} ml")).ToList();
            lastHistory = DateTime.Now;
        }
    }
    private void WaterGoalChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!initialized || DailyWaterCups.SelectedItem is not int cups) return;
        app.Engine.State.Settings.DailyWaterCups = cups;
        WaterGoalLabel.Text = $"{cups} 杯 / {cups * WaterProgress.CupMillilitres} ml";
        app.SaveState();
    }

    private void PreviewClick(object sender, RoutedEventArgs e) => app.ShowPreview();
    private void RestClick(object sender, RoutedEventArgs e)
    {
        MessageBoxResult choice = MessageBox.Show(this,
            "即将开始真正的 10 分钟休息，键鼠会暂停响应。\n\n紧急解除：Ctrl + Shift + F12 持续按住 8 秒。",
            "现在开始休息", MessageBoxButton.OKCancel, MessageBoxImage.Information);
        if (choice == MessageBoxResult.OK) app.BeginRest();
    }
    private void WaterClick(object sender, RoutedEventArgs e) => app.ShowWater();
    private void SettingsChanged(object sender, RoutedEventArgs e)
    {
        if (!initialized) return;
        app.Engine.State.Settings.MusicEnabled = MusicEnabled.IsChecked == true;
        app.SaveState();
    }
    private void VolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!initialized) return;
        app.Engine.State.Settings.Volume = Volume.Value / 100;
        app.ScheduleSave();
    }
    private void ImportMusicClick(object sender, RoutedEventArgs e)
    {
        OpenFileDialog picker = new() { Filter = "本地音乐|*.mp3;*.wav;*.m4a;*.wma", Multiselect = true, CheckFileExists = true };
        if (picker.ShowDialog(this) != true) return;
        app.Engine.State.Settings.MusicFiles = picker.FileNames.Take(1000).ToList();
        app.SaveState(); RefreshPlaylist();
    }
    private void ResetMusicClick(object sender, RoutedEventArgs e)
    {
        app.Engine.State.Settings.MusicFiles.Clear();
        app.SaveState(); RefreshPlaylist();
    }
    private void RefreshPlaylist()
    {
        System.Collections.Generic.List<string> files = app.Engine.State.Settings.MusicFiles;
        PlaylistLabel.Text = files.Count == 0 ? "当前：林间慢呼吸 · 内置原创环境音"
            : $"已选择 {files.Count} 首：" + string.Join("、", files.Take(6).Select(Path.GetFileNameWithoutExtension));
    }
    private void AutoStartChanged(object sender, RoutedEventArgs e)
    {
        if (!initialized) return;
        try { app.SetAutoStart(AutoStart.IsChecked == true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            initialized = false; AutoStart.IsChecked = app.IsAutoStartEnabled; initialized = true;
            MessageBox.Show(this, "无法修改自动启动设置：" + ex.Message);
        }
    }
    private void ExportClick(object sender, RoutedEventArgs e)
    {
        SaveFileDialog picker = new() { Filter = "CSV 统计|*.csv", FileName = "routine-rest-daily.csv" };
        if (picker.ShowDialog(this) != true) return;
        try {
            StringBuilder csv = new("日期,工作分钟,休息分钟,完成休息次数,中断次数,喝水毫升\r\n");
            foreach (System.Collections.Generic.KeyValuePair<string, DaySummary> pair in app.Engine.State.Days.OrderBy(p => p.Key))
                csv.AppendLine(string.Join(",", pair.Key,
                    (pair.Value.WorkSeconds / 60).ToString("F1", CultureInfo.InvariantCulture),
                    (pair.Value.RestSeconds / 60).ToString("F1", CultureInfo.InvariantCulture),
                    pair.Value.CompletedBreaks, pair.Value.InterruptedBreaks, pair.Value.WaterMl));
            File.WriteAllText(picker.FileName, csv.ToString(), new UTF8Encoding(true));
            MessageBox.Show(this, "每日统计已导出。");
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { MessageBox.Show(this, "导出失败：" + ex.Message); }
    }
    private void OnClosing(object? sender, CancelEventArgs e) { if (!allowClose) { e.Cancel = true; Hide(); } }
    internal void Finish() { allowClose = true; Close(); }
    private sealed record HistoryRow(string Date, string Work, string Rest, string Breaks, string Water);
}
