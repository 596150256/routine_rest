using System;
namespace RoutineRest.Core;

/// <summary>Tracks only modifier/F12 state in memory, and never accepts software-injected emergency input.</summary>
public sealed class EmergencyChord
{
    private readonly bool[] pressed = new bool[256];
    private long? heldSince;
    public void Key(uint key, bool down, bool injected, long monotonicMilliseconds)
    {
        if (injected || key >= 256) return;
        pressed[key] = down;
        bool control = pressed[0x11] || pressed[0xA2] || pressed[0xA3];
        bool shift = pressed[0x10] || pressed[0xA0] || pressed[0xA1];
        if (control && shift && pressed[0x7B]) heldSince ??= monotonicMilliseconds;
        else heldSince = null;
    }
    public long HeldMilliseconds(long now) => heldSince.HasValue ? Math.Max(0, now - heldSince.Value) : 0;
    public bool IsReleased(long now) => HeldMilliseconds(now) >= 8000;
}
