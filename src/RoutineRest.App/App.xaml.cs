using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using RoutineRest.Core;
using Forms = System.Windows.Forms;

namespace RoutineRest.App;

public partial class App : System.Windows.Application
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly List<RestWindow> covers = new();
    private readonly RestViewModel restModel = new();
    private JsonStateStore? store;
    private MusicService? music;
    private InputGuard? guard;
    private Forms.NotifyIcon? tray;
    private MainWindow? dashboard;
    private WaterWindow? water;
    private Mutex? singleton;
    private bool ownsMutex;
    private bool locked;
    private bool suspended;
    private bool exiting;
    private bool savingFailed;
    private bool warnedTwo;
    private bool warnedOne;
    private bool previewOnly;
    private bool savePending;
    private long lastTick;
    private long lastSave;
    private uint lastInput;
    private long lastCoverRefresh;
    private long nextWaterReminder;
    public RoutineEngine Engine { get; private set; } = new(new AppState());
    public string DataDirectory { get; private set; } = "";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public bool IsAutoStartEnabled
    {
        get { using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey); return key?.GetValue("RoutineRest") is string; }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) => {
            CleanupBlocking();
            File.AppendAllText(Path.Combine(DataDirectory, "error.log"), DateTimeOffset.Now + " " + args.Exception + Environment.NewLine);
            MessageBox.Show("Routine Rest 出现错误，已恢复输入。详情保存在数据目录的 error.log。");
            args.Handled = true;
            ExitApp();
        };
        previewOnly = e.Args.Contains("--preview") || e.Args.Contains("--smoke-test");
        DataDirectory = previewOnly ? Path.Combine(AppContext.BaseDirectory, "test-data")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RoutineRest");
        try
        {
            Directory.CreateDirectory(DataDirectory);
            if (!previewOnly)
            {
                singleton = new Mutex(true, @"Local\RoutineRest-" + Environment.UserName, out bool created);
                ownsMutex = created;
                if (!created) { MessageBox.Show("Routine Rest 已在托盘运行。双击托盘叶子图标打开。"); Shutdown(); return; }
                store = new JsonStateStore(DataDirectory);
                Engine = new RoutineEngine(store.Load());
                Engine.Recover(DateTimeOffset.Now, Native.InputIdleSeconds(Native.LastInputTick()));
            }
            music = new MusicService(DataDirectory, Engine.State.Settings);
            if (e.Args.Contains("--smoke-test")) { RunSmokeTest(); return; }
            if (previewOnly) { RunPreviewExport(); return; }
            CreateTray();
            SystemEvents.SessionSwitch += SessionChanged;
            SystemEvents.PowerModeChanged += PowerChanged;
            SystemEvents.DisplaySettingsChanged += DisplaysChanged;
            lastTick = Environment.TickCount64;
            lastSave = lastTick;
            lastInput = Native.LastInputTick();
            timer.Tick += Tick;
            timer.Start();
            if (Engine.State.Phase == Phase.Resting) EnterRest();
            else if (!e.Args.Contains("--background")) ShowDashboard();
            if (Engine.State.PendingWaterCount > 0) ShowWater();
            if (!string.IsNullOrEmpty(store?.RecoveryNotice)) Notify(store.RecoveryNotice);
        }
        catch (Exception ex)
        {
            CleanupBlocking();
            MessageBox.Show("Routine Rest 启动失败，未保留输入限制。\n" + ex.Message);
            ExitApp();
        }
    }

    private void CreateTray()
    {
        Forms.ContextMenuStrip menu = new();
        menu.Items.Add("今日统计与设置", null, (_, _) => Dispatcher.Invoke(ShowDashboard));
        menu.Items.Add("预览休息界面（不拦截）", null, (_, _) => Dispatcher.Invoke(ShowPreview));
        menu.Items.Add("填写喝水量", null, (_, _) => Dispatcher.Invoke(ShowWater));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出 Routine Rest", null, (_, _) => Dispatcher.Invoke(() => {
            if (Engine.State.Phase == Phase.Resting) { Notify("休息期间请使用紧急解除组合键。"); return; }
            ExitApp();
        }));
        tray = new Forms.NotifyIcon { Icon = CreateTrayIcon(), Text = "Routine Rest · 50 / 10", Visible = true, ContextMenuStrip = menu };
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowDashboard);
        Notify("已开始守护你的工作节奏。满 50 分钟自动休息 10 分钟。");
    }

    private static Icon CreateTrayIcon()
    {
        using Bitmap bitmap = new(32, 32);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.Clear(System.Drawing.Color.Transparent);
        using SolidBrush green = new(System.Drawing.Color.FromArgb(45, 83, 62));
        using System.Drawing.Pen line = new(System.Drawing.Color.FromArgb(230, 240, 218), 2.4f);
        graphics.FillEllipse(green, 1, 1, 30, 30);
        graphics.DrawArc(line, 9, 6, 16, 19, 90, 230);
        graphics.DrawLine(line, 9, 25, 22, 10);
        nint handle = bitmap.GetHicon();
        try { using Icon borrowed = Icon.FromHandle(handle); return (Icon)borrowed.Clone(); }
        finally { DestroyIcon(handle); }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint icon);

    private void Tick(object? sender, EventArgs args)
    {
        if (exiting || music is null) return;
        long tick = Environment.TickCount64;
        double elapsed = Math.Max(0, (tick - lastTick) / 1000d);
        lastTick = tick;
        uint input = Native.LastInputTick();
        bool newInput = input != lastInput;
        lastInput = input;
        Phase before = Engine.State.Phase;
        Engine.Advance(DateTimeOffset.Now, elapsed, newInput, !locked && !suspended, Native.InputIdleSeconds(input));
        if (Engine.State.Phase == Phase.Resting)
        {
            if (covers.Count == 0) EnterRest();
            guard?.Pulse();
            restModel.Update(Engine, music, guard?.EmergencyProgress ?? 0, DateTimeOffset.Now);
            if (tick - lastCoverRefresh > 2000) { ReassertCovers(); lastCoverRefresh = tick; }
        }
        else if (covers.Count > 0)
        {
            CleanupBlocking();
            SaveState();
            if (!locked && !suspended) ShowWater();
        }
        if (before != Engine.State.Phase)
        {
            SaveState();
            if (Engine.State.Phase == Phase.Hydration && !locked && !suspended) ShowWater();
            if (Engine.State.Phase is Phase.Waiting or Phase.Hydration) { warnedOne = false; warnedTwo = false; }
        }
        if (Engine.State.Phase == Phase.Working && !Engine.IsWorkPaused)
        {
            if (Engine.RemainingSeconds <= 120 && !warnedTwo) { warnedTwo = true; Notify("还有 2 分钟休息，可以先保存当前工作。"); }
            if (Engine.RemainingSeconds <= 60 && !warnedOne) { warnedOne = true; Notify("还有 1 分钟，将自动休息 10 分钟。紧急解除：Ctrl+Shift+F12 长按8秒。"); }
        }
        if (Engine.State.PendingWaterCount > 0 && water is null && tick > nextWaterReminder && !locked && !suspended && Engine.State.Phase != Phase.Resting)
        { nextWaterReminder = tick + 300000; Notify("上次休息的喝水量还没有记录，可在托盘菜单中填写。"); }
        if (tick - lastSave >= (savePending ? 1000 : 5000)) SaveState();
        if (dashboard?.IsVisible == true) dashboard.Refresh();
        if (tray is not null) tray.Text = Engine.State.Phase == Phase.Resting ? "Routine Rest · 休息中"
            : Engine.IsWorkPaused ? "Routine Rest · 无操作5分钟，计时已暂停"
            : $"Routine Rest · 距休息 {Math.Ceiling(Engine.RemainingSeconds / 60)} 分钟";
    }

    internal void BeginRest() { if (Engine.State.Phase == Phase.Resting) return; Engine.StartRest(DateTimeOffset.Now); SaveState(); EnterRest(); }
    private void EnterRest()
    {
        if (music is null || covers.Count > 0) return;
        try
        {
            dashboard?.Hide(); water?.Hide();
            Native.KeepDisplayOn();
            music.Start();
            restModel.Update(Engine, music, 0, DateTimeOffset.Now);
            foreach (Forms.Screen screen in Forms.Screen.AllScreens)
            {
                RestWindow window = new() { DataContext = restModel };
                covers.Add(window);
                window.Show();
                Native.Cover(window, screen.Bounds);
            }
            if (covers.Count > 0) Native.Foreground(covers[0]);
            warnedOne = false; warnedTwo = false;
            guard = new InputGuard(Engine.RemainingSeconds + 15, reason => Dispatcher.BeginInvoke(() => EmergencyRelease(reason)));
            lastTick = Environment.TickCount64;
        }
        catch (Exception ex) { EmergencyRelease("启动休息界面失败：" + ex.Message); }
    }
    private void EmergencyRelease(string reason)
    {
        CleanupBlocking();
        Engine.EmergencyRelease(DateTimeOffset.Now, reason);
        warnedOne = false; warnedTwo = false;
        SaveState();
        Notify("已紧急解除休息，输入已恢复。此次休息记为中断。");
        ShowDashboard();
    }
    private void CleanupBlocking()
    {
        guard?.Dispose(); guard = null;
        Native.ReleaseDisplay();
        music?.Stop();
        foreach (RestWindow window in covers.ToArray()) window.Finish();
        covers.Clear();
    }
    private void ReassertCovers()
    {
        Forms.Screen[] screens = Forms.Screen.AllScreens;
        if (screens.Length != covers.Count) { RebuildCovers(); return; }
        for (int i = 0; i < screens.Length; i++) Native.Cover(covers[i], screens[i].Bounds);
    }
    private void RebuildCovers()
    {
        if (Engine.State.Phase != Phase.Resting || music is null) return;
        foreach (RestWindow window in covers.ToArray()) window.Finish();
        covers.Clear();
        foreach (Forms.Screen screen in Forms.Screen.AllScreens)
        {
            RestWindow window = new() { DataContext = restModel }; covers.Add(window); window.Show(); Native.Cover(window, screen.Bounds);
        }
    }

    private void SessionChanged(object sender, SessionSwitchEventArgs args) => Dispatcher.BeginInvoke(() => {
        if (args.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.RemoteDisconnect or SessionSwitchReason.ConsoleDisconnect) locked = true;
        if (args.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.RemoteConnect or SessionSwitchReason.ConsoleConnect)
        {
            locked = false;
            if (Engine.State.Phase == Phase.Resting) { Native.KeepDisplayOn(); ReassertCovers(); }
            else if (Engine.State.PendingWaterCount > 0) ShowWater();
        }
    });
    private void PowerChanged(object sender, PowerModeChangedEventArgs args) => Dispatcher.BeginInvoke(() => {
        if (args.Mode == PowerModes.Suspend) { suspended = true; SaveState(); }
        if (args.Mode == PowerModes.Resume)
        {
            // Attribute the sleep gap to being away, never to active work.
            long now = Environment.TickCount64;
            Engine.Advance(DateTimeOffset.Now, Math.Max(0, (now - lastTick) / 1000d), false, false);
            lastTick = now; suspended = false;
            if (Engine.State.Phase == Phase.Resting) { Native.KeepDisplayOn(); guard?.Pulse(); }
            SaveState();
        }
    });
    private void DisplaysChanged(object? sender, EventArgs args) => Dispatcher.BeginInvoke(RebuildCovers);

    internal void ShowDashboard()
    {
        if (Engine.State.Phase == Phase.Resting) return;
        dashboard ??= new MainWindow(this);
        dashboard.Show(); dashboard.WindowState = WindowState.Normal; dashboard.Activate(); dashboard.Refresh();
    }
    internal void ShowWater()
    {
        if (Engine.State.PendingWaterCount == 0 || Engine.State.Phase == Phase.Resting) return;
        if (water is not null) { water.Show(); water.Activate(); return; }
        water = new WaterWindow(this);
        water.Closed += (_, _) => { water = null; nextWaterReminder = Environment.TickCount64 + 300000; };
        water.Show(); water.Activate();
    }
    internal void ShowPreview()
    {
        if (music is null || Engine.State.Phase == Phase.Resting) return;
        RoutineEngine sample = CreatePreviewEngine();
        RestViewModel model = new() { ModeText = "预览 · 不拦截输入" };
        model.Update(sample, music, 0, DateTimeOffset.Now);
        RestWindow preview = new() { DataContext = model, Width = 1152, Height = 720, WindowStyle = WindowStyle.SingleBorderWindow, ResizeMode = ResizeMode.CanResize, ShowInTaskbar = true, Topmost = false, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        preview.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) preview.Finish(); };
        // Preview is ordinary UI; permit window close without exposing a real-rest skip.
        preview.EnablePreviewClose();
        preview.Show();
    }
    private static RoutineEngine CreatePreviewEngine()
    {
        RoutineEngine sample = new(new AppState());
        DaySummary day = sample.Today(DateTimeOffset.Now);
        day.WorkSeconds = 7500; day.RestSeconds = 1260; day.WaterMl = 650; day.CompletedBreaks = 2;
        sample.StartRest(DateTimeOffset.Now);
        sample.State.RestSeconds = 83;
        return sample;
    }
    internal void ScheduleSave() => savePending = true;
    public void SaveState()
    {
        if (store is null) return;
        try { store.Save(Engine.State); savingFailed = false; lastSave = Environment.TickCount64; savePending = false; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!savingFailed) Notify("数据暂时无法保存：" + ex.Message);
            savingFailed = true; lastSave = Environment.TickCount64;
        }
    }
    private void Notify(string message) => tray?.ShowBalloonTip(6000, "Routine Rest", message, Forms.ToolTipIcon.Info);
    public void SetAutoStart(bool enable)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enable) {
            string executable = Environment.ProcessPath ?? throw new IOException("找不到程序路径。");
            key.SetValue("RoutineRest", "\"" + executable + "\" --background");
        } else key.DeleteValue("RoutineRest", false);
    }
    private void ExitApp()
    {
        if (exiting) return;
        exiting = true;
        timer.Stop(); SaveState(); CleanupBlocking();
        SystemEvents.SessionSwitch -= SessionChanged;
        SystemEvents.PowerModeChanged -= PowerChanged;
        SystemEvents.DisplaySettingsChanged -= DisplaysChanged;
        water?.Close(); dashboard?.Finish();
        if (tray is not null) { tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Icon?.Dispose(); tray.Dispose(); }
        music?.Dispose();
        if (ownsMutex) { singleton?.ReleaseMutex(); ownsMutex = false; }
        singleton?.Dispose();
        Shutdown();
    }
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e) { SaveState(); CleanupBlocking(); base.OnSessionEnding(e); }
    protected override void OnExit(ExitEventArgs e) { CleanupBlocking(); base.OnExit(e); }

    private void RunPreviewExport()
    {
        if (music is null) return;
        RoutineEngine sample = CreatePreviewEngine();
        restModel.ModeText = "预览 · 不拦截输入";
        restModel.Update(sample, music, 0, DateTimeOffset.Now);
        RestWindow preview = new() { DataContext = restModel, Width = 1440, Height = 900, Topmost = false, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        preview.Show();
        DispatcherTimer capture = new() { Interval = TimeSpan.FromSeconds(1) };
        capture.Tick += async (_, _) => {
            capture.Stop();
            preview.UpdateLayout();
            RenderTargetBitmap bitmap = new(1440, 900, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(preview.Content as System.Windows.Media.Visual ?? preview);
            PngBitmapEncoder encoder = new(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream output = File.Create(Path.Combine(AppContext.BaseDirectory, "rest-preview.png"))) encoder.Save(output);
            preview.Finish();
            Engine = sample;
            Engine.State.Phase = Phase.Working;
            Engine.State.WorkSeconds = 1140;
            dashboard = new MainWindow(this);
            dashboard.Show();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            dashboard.UpdateLayout();
            CaptureWindow(dashboard, "dashboard-preview.png");
            water = new WaterWindow(this);
            water.Show();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            water.UpdateLayout();
            CaptureWindow(water, "water-preview.png");
            ExitApp();
        };
        capture.Start();
    }

    private static void CaptureWindow(Window window, string name)
    {
        RenderTargetBitmap bitmap = new((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream output = File.Create(Path.Combine(AppContext.BaseDirectory, name));
        encoder.Save(output);
    }

    private async void RunSmokeTest()
    {
        string resultPath = Path.Combine(AppContext.BaseDirectory, "smoke-result.txt");
        try
        {
            if (music is null) throw new InvalidOperationException("No music service");
            Engine.StartRest(DateTimeOffset.Now);
            Native.KeepDisplayOn();
            music.Start();
            restModel.Update(Engine, music, 0, DateTimeOffset.Now);
            foreach (Forms.Screen screen in Forms.Screen.AllScreens) {
                RestWindow window = new() { DataContext = restModel }; covers.Add(window); window.Show(); Native.Cover(window, screen.Bounds);
            }
            string releaseReason = "";
            guard = new InputGuard(3, reason => releaseReason = reason);
            Native.InjectHarmlessTestEvents();
            await System.Threading.Tasks.Task.Delay(3500);
            if (guard.KeyboardEvents < 2 || guard.MouseEvents < 1) throw new InvalidOperationException("Injected keyboard/mouse events were not intercepted");
            if (!music.IsPlaying) throw new InvalidOperationException("Audio did not start playing");
            uint previousDisplayState = Native.ReleaseDisplay();
            if ((previousDisplayState & 2) == 0) throw new InvalidOperationException("Display request was not active");
            if ((Native.ReleaseDisplay() & 2) != 0) throw new InvalidOperationException("Display request was not released");
            if (releaseReason != "输入拦截达到安全时间上限") throw new InvalidOperationException("Guard deadline did not release");
            CleanupBlocking();
            Engine.Advance(DateTimeOffset.Now, 600, false, true);
            Engine.RecordWater(250, DateTimeOffset.Now);
            if (Engine.Today(DateTimeOffset.Now).WaterMl != 250) throw new InvalidOperationException("Water record failed");
            File.WriteAllText(resultPath, "PASS: real Windows hooks installed, injected keyboard/mouse events intercepted and timed release completed; display request released; " +
                Forms.Screen.AllScreens.Length + " monitor overlays shown and closed; audio playback advanced; hydration flow persisted in isolated memory.");
        }
        catch (Exception ex) { File.WriteAllText(resultPath, "FAIL: " + ex); }
        finally { CleanupBlocking(); ExitApp(); }
    }
}
