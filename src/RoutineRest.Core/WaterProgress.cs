using System;
using System.Collections.Generic;

namespace RoutineRest.Core;

/// <summary>Shows consumed water in 300 ml glasses, independently of the daily goal. At most twelve glasses are drawn.</summary>
public sealed record WaterProgress(IReadOnlyList<double> Cups, double Fraction)
{
    public const int CupMillilitres = 300;
    public int AdditionalMillilitres { get; init; }
    public static WaterProgress From(int waterMl, int goalCups)
    {
        if (waterMl < 0) throw new ArgumentOutOfRangeException(nameof(waterMl));
        if (goalCups < 1 || goalCups > 12) throw new ArgumentOutOfRangeException(nameof(goalCups));
        int cupCount = (int)Math.Min(12, (waterMl + (long)CupMillilitres - 1) / CupMillilitres);
        double[] cups = new double[cupCount];
        for (int i = 0; i < cups.Length; i++)
            cups[i] = Math.Clamp((waterMl - i * CupMillilitres) / (double)CupMillilitres, 0, 1);
        return new WaterProgress(Array.AsReadOnly(cups), Math.Clamp(waterMl / (double)(goalCups * CupMillilitres), 0, 1))
        {
            AdditionalMillilitres = Math.Max(0, waterMl - 12 * CupMillilitres)
        };
    }
}
