using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using Forms = System.Windows.Forms;

namespace RoutineRest.App;

/// <summary>
/// Dedicated message-loop thread keeps the hook callbacks tiny. Only an in-memory emergency
/// chord is inspected; no key text or input history is stored. A stale UI heartbeat fails open.
/// </summary>
internal sealed class InputGuard : IDisposable
{
    private readonly Thread thread;
    private readonly ManualResetEventSlim ready = new(false);
    private readonly Native.HookProc keyboardCallback;
    private readonly Native.HookProc mouseCallback;
    private readonly Action<string> released;
    private readonly RoutineRest.Core.EmergencyChord chord = new();
    private readonly long absoluteDeadline;
    private nint keyboard;
    private nint mouse;
    private uint threadId;
    private long heartbeat = Environment.TickCount64;
    private long heldMs;
    private Exception? startupError;
    private int disposed;
    private long keyboardEvents;
    private long mouseEvents;
    public long KeyboardEvents => Interlocked.Read(ref keyboardEvents);
    public long MouseEvents => Interlocked.Read(ref mouseEvents);
    public double EmergencyProgress => Math.Clamp(Interlocked.Read(ref heldMs) / 8000d, 0, 1);

    public InputGuard(double maximumSeconds, Action<string> onRelease)
    {
        released = onRelease;
        absoluteDeadline = Environment.TickCount64 + (long)(maximumSeconds * 1000);
        keyboardCallback = OnKeyboard;
        mouseCallback = OnMouse;
        thread = new Thread(Run) { IsBackground = true, Name = "RoutineRest input guard" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!ready.Wait(TimeSpan.FromSeconds(5))) { Dispose(); throw new TimeoutException("输入拦截线程启动超时。"); }
        if (startupError is not null) { Dispose(); throw new InvalidOperationException("无法启用键鼠拦截。", startupError); }
    }
    public void Pulse() => Interlocked.Exchange(ref heartbeat, Environment.TickCount64);

    private void Run()
    {
        try
        {
            threadId = Native.GetCurrentThreadId();
            nint module = Native.GetModuleHandle(null);
            keyboard = Native.SetWindowsHookEx(13, keyboardCallback, module, 0);
            if (keyboard == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            mouse = Native.SetWindowsHookEx(14, mouseCallback, module, 0);
            if (mouse == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            using Forms.Timer timer = new() { Interval = 50 };
            timer.Tick += CheckHealth;
            timer.Start();
            ready.Set();
            if (Volatile.Read(ref disposed) == 0) Forms.Application.Run();
        }
        catch (Exception ex) { startupError = ex; ready.Set(); }
        finally { Unhook(); }
    }
    private nint OnKeyboard(int code, nint message, nint data)
    {
        if (code < 0) return Native.CallNextHookEx(keyboard, code, message, data);
        Interlocked.Increment(ref keyboardEvents);
        Native.KeyboardData input = Marshal.PtrToStructure<Native.KeyboardData>(data);
        uint msg = unchecked((uint)message);
        bool down = msg == 0x100 || msg == 0x104;
        chord.Key(input.Key, down, (input.Flags & 0x10) != 0, Environment.TickCount64);
        return 1;
    }
    private nint OnMouse(int code, nint message, nint data) {
        if (code < 0) return Native.CallNextHookEx(mouse, code, message, data);
        Interlocked.Increment(ref mouseEvents); return 1;
    }

    private void CheckHealth(object? sender, EventArgs e)
    {
        long now = Environment.TickCount64;
        Interlocked.Exchange(ref heldMs, chord.HeldMilliseconds(now));
        string reason = "";
        if (Interlocked.Read(ref heldMs) >= 8000) reason = "紧急组合键长按8秒";
        else if (now - Interlocked.Read(ref heartbeat) > 8000) reason = "界面心跳超时，安全恢复输入";
        else if (now >= absoluteDeadline) reason = "输入拦截达到安全时间上限";
        if (reason.Length == 0) return;
        Unhook();
        released(reason);
        Forms.Application.ExitThread();
    }
    private void Unhook()
    {
        if (keyboard != 0) { Native.UnhookWindowsHookEx(keyboard); keyboard = 0; }
        if (mouse != 0) { Native.UnhookWindowsHookEx(mouse); mouse = 0; }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        if (threadId != 0) Native.PostThreadMessage(threadId, 0x12, 0, 0);
        if (thread.IsAlive && Thread.CurrentThread != thread) thread.Join(1500);
        // Keep delegates/ready alive until the thread's finally has released native hooks.
        GC.KeepAlive(keyboardCallback);
        GC.KeepAlive(mouseCallback);
    }
}
