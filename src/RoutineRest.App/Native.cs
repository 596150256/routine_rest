using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace RoutineRest.App;

internal static class Native
{
    internal delegate nint HookProc(int code, nint message, nint data);
    [StructLayout(LayoutKind.Sequential)]
    private struct LastInput { public uint Size; public uint Time; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardData { public uint Key; public uint Scan; public uint Flags; public uint Time; public nuint Extra; }

    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInput info);
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint SetWindowsHookEx(int hook, HookProc callback, nint module, uint threadId);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")]
    internal static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint GetModuleHandle(string? module);
    [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll")] private static extern uint GetTickCount();
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostThreadMessage(uint threadId, uint message, nuint wParam, nint lParam);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint SetThreadExecutionState(uint flags);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    internal static uint LastInputTick()
    {
        LastInput input = new() { Size = (uint)Marshal.SizeOf<LastInput>() };
        if (!GetLastInputInfo(ref input)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return input.Time;
    }
    /// <summary>Uses the same wrapping 32-bit monotonic clock as GetLastInputInfo; system clock changes do not affect idle time.</summary>
    internal static double InputIdleSeconds(uint inputTick) => unchecked(GetTickCount() - inputTick) / 1000d;
    internal static void KeepDisplayOn()
    {
        // Only inhibit automatic display-off; the user's no-sleep policy is left intact.
        if (SetThreadExecutionState(0x80000002) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    internal static uint ReleaseDisplay() => SetThreadExecutionState(0x80000000);
    internal static void Cover(Window window, System.Drawing.Rectangle bounds)
    {
        nint handle = new WindowInteropHelper(window).Handle;
        if (!SetWindowPos(handle, -1, bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0040))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    internal static void Foreground(Window window) => SetForegroundWindow(new WindowInteropHelper(window).Handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct TestInput { public uint Type; public InputData Data; }
    [StructLayout(LayoutKind.Explicit)]
    private struct InputData
    {
        [FieldOffset(0)] public TestKeyboard Keyboard;
        [FieldOffset(0)] public TestMouse Mouse;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct TestKeyboard { public ushort Key; public ushort Scan; public uint Flags; public uint Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)]
    private struct TestMouse { public int X; public int Y; public uint MouseData; public uint Flags; public uint Time; public nuint Extra; }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, TestInput[] inputs, int size);
    internal static void InjectHarmlessTestEvents()
    {
        TestInput[] inputs = {
            new() { Type = 1, Data = new InputData { Keyboard = new TestKeyboard { Key = 0x87 } } },
            new() { Type = 1, Data = new InputData { Keyboard = new TestKeyboard { Key = 0x87, Flags = 2 } } },
            new() { Type = 0, Data = new InputData { Mouse = new TestMouse { Flags = 1 } } }
        };
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<TestInput>()) != inputs.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

}
