using System;
using System.Collections.Generic;

namespace RoutineRest.Core;

/// <summary>Each glass is exactly 300 ml. Visual fill saturates at the goal; recorded amounts remain unchanged.</summary>
public sealed record WaterProgress(IReadOnlyList<double> Cups, double Fraction)
{
    public const int CupMillilitres = 300;
    public static WaterProgress From(int waterMl, int goalCups)
    {
        if (waterMl < 0) throw new ArgumentOutOfRangeException(nameof(waterMl));
        if (goalCups < 1 || goalCups > 12) throw new ArgumentOutOfRangeException(nameof(goalCups));
        double[] cups = new double[goalCups];
        for (int i = 0; i < cups.Length; i++)
            cups[i] = Math.Clamp((waterMl - i * CupMillilitres) / (double)CupMillilitres, 0, 1);
        return new WaterProgress(Array.AsReadOnly(cups), Math.Clamp(waterMl / (double)(goalCups * CupMillilitres), 0, 1));
    }
}
