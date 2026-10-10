using System;
using System.Globalization;

namespace RoutineRest.Core;

/// <summary>Deterministic 50/10 state machine. Call Advance with a monotonic elapsed duration.</summary>
public sealed class RoutineEngine
{
    public const double WorkDuration = 50 * 60;
    public const double RestDuration = 10 * 60;
    public const double IdleTimeout = 3 * 60;
    public AppState State { get; }
    public double IdleSeconds { get; private set; }
    public bool IsWorkPaused => State.Phase == Phase.Working && IdleSeconds >= IdleTimeout;
    public double RemainingSeconds => State.Phase == Phase.Resting
        ? Math.Max(0, RestDuration - State.RestSeconds) : Math.Max(0, WorkDuration - State.WorkSeconds);

    public RoutineEngine(AppState state) { StateValidation.Validate(state); State = state; }

    /// <summary>Counts only the first three idle minutes. An optional OS idle snapshot prevents restart and delayed-tick overcounting.</summary>
    public void Advance(DateTimeOffset now, double elapsedSeconds, bool newInput, bool sessionAvailable, double? secondsSinceLastInput = null)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        if (secondsSinceLastInput.HasValue && (!double.IsFinite(secondsSinceLastInput.Value) || secondsSinceLastInput.Value < 0))
            throw new ArgumentOutOfRangeException(nameof(secondsSinceLastInput));
        double idleAtStart = IdleSeconds;
        double currentIdle = secondsSinceLastInput ?? (newInput ? 0 : idleAtStart + elapsedSeconds);
        // Without a new input timestamp, a stale or decreasing OS snapshot must never restart the grace period.
        if (!newInput) currentIdle = Math.Max(currentIdle, idleAtStart + elapsedSeconds);
        IdleSeconds = sessionAvailable ? Math.Min(IdleTimeout, currentIdle) : IdleTimeout;
        State.LastCheckpoint = now;
        switch (State.Phase)
        {
            case Phase.Waiting:
                if (newInput && sessionAvailable) { State.Phase = Phase.Working; State.AwaySeconds = 0; }
                break;
            case Phase.Working:
                if (!sessionAvailable)
                {
                    double away = Math.Min(elapsedSeconds, RestDuration - State.AwaySeconds);
                    DateTimeOffset awayEnd = now.AddSeconds(-elapsedSeconds + away);
                    AddDuration(awayEnd, away, false);
                    State.AwaySeconds += away;
                    if (State.AwaySeconds >= RestDuration) CompleteRest(awayEnd);
                    break;
                }
                State.AwaySeconds = 0;
                // A returning input does not make a long, unobserved idle interval count as work.
                double afterInput = newInput ? Math.Min(elapsedSeconds, currentIdle) : 0;
                double beforeInput = elapsedSeconds - afterInput;
                double beforeWork = Math.Min(beforeInput, Math.Max(0, IdleTimeout - idleAtStart));
                beforeWork = Math.Min(beforeWork, WorkDuration - State.WorkSeconds);
                AddDuration(now.AddSeconds(-elapsedSeconds + beforeWork), beforeWork, true);
                State.WorkSeconds += beforeWork;
                if (State.WorkSeconds < WorkDuration && newInput)
                {
                    double afterWork = Math.Min(Math.Min(afterInput, IdleTimeout), WorkDuration - State.WorkSeconds);
                    AddDuration(now.AddSeconds(-afterInput + afterWork), afterWork, true);
                    State.WorkSeconds += afterWork;
                }
                if (State.WorkSeconds >= WorkDuration) StartRest(now);
                break;
            case Phase.Resting:
                double rest = Math.Min(elapsedSeconds, RestDuration - State.RestSeconds);
                DateTimeOffset restEnd = now.AddSeconds(-elapsedSeconds + rest);
                AddDuration(restEnd, rest, false);
                State.RestSeconds += rest;
                if (State.RestSeconds >= RestDuration) CompleteRest(restEnd);
                break;
            case Phase.Hydration:
                if (newInput && sessionAvailable) State.Phase = Phase.Working;
                break;
        }
    }

    public void StartRest(DateTimeOffset now)
    {
        if (State.Phase == Phase.Resting) return;
        State.Phase = Phase.Resting;
        State.RestSeconds = 0;
        State.AwaySeconds = 0;
        State.LastCheckpoint = now;
    }

    /// <summary>Records a water entry, rolling back its in-memory changes if the supplied persistence operation fails.</summary>
    public void RecordWater(int millilitres, DateTimeOffset now, Action<AppState>? persist = null)
    {
        if (millilitres < 0 || millilitres > 5000) throw new ArgumentOutOfRangeException(nameof(millilitres));
        if (State.PendingWaterCount == 0) throw new InvalidOperationException("当前没有待填写的休息记录。");
        string dayKey = now.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        bool hadDay = State.Days.ContainsKey(dayKey);
        DaySummary day = Today(now);
        int previousWater = day.WaterMl;
        int updatedWater = checked(previousWater + millilitres);
        int previousPending = State.PendingWaterCount;
        Phase previousPhase = State.Phase;
        DateTimeOffset previousCheckpoint = State.LastCheckpoint;
        int entryIndex = State.WaterEntries.Count;
        State.WaterEntries.Add(new WaterEntry(now, millilitres));
        try
        {
            day.WaterMl = updatedWater;
            State.PendingWaterCount--;
            if (State.Phase == Phase.Hydration) State.Phase = Phase.Waiting;
            State.LastCheckpoint = now;
            persist?.Invoke(State);
        }
        catch
        {
            State.WaterEntries.RemoveAt(entryIndex);
            day.WaterMl = previousWater;
            State.PendingWaterCount = previousPending;
            State.Phase = previousPhase;
            State.LastCheckpoint = previousCheckpoint;
            if (!hadDay) State.Days.Remove(dayKey);
            throw;
        }
    }

    public void EmergencyRelease(DateTimeOffset now, string reason)
    {
        if (State.Phase != Phase.Resting) return;
        Today(now).InterruptedBreaks++;
        State.Releases.Add(new ReleaseEntry(now, reason.Length > 200 ? reason[..200] : reason));
        State.WorkSeconds = 0;
        State.RestSeconds = 0;
        State.AwaySeconds = 0;
        State.Phase = Phase.Waiting;
        State.LastCheckpoint = now;
    }

    /// <summary>Only a rest may advance while the app was offline; offline time is never counted as work.</summary>
    public void Recover(DateTimeOffset now, double secondsSinceLastInput = IdleTimeout)
    {
        if (!double.IsFinite(secondsSinceLastInput) || secondsSinceLastInput < 0)
            throw new ArgumentOutOfRangeException(nameof(secondsSinceLastInput));
        if (State.Phase == Phase.Resting && State.LastCheckpoint != default)
            Advance(now, Math.Max(0, (now - State.LastCheckpoint).TotalSeconds), false, false);
        State.AwaySeconds = 0;
        IdleSeconds = Math.Min(IdleTimeout, secondsSinceLastInput);
        State.LastCheckpoint = now;
    }

    public DaySummary Today(DateTimeOffset now)
    {
        string key = now.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (!State.Days.TryGetValue(key, out DaySummary? day)) { day = new DaySummary(); State.Days.Add(key, day); }
        return day;
    }

    private void CompleteRest(DateTimeOffset now)
    {
        Today(now).CompletedBreaks++;
        State.PendingWaterCount++;
        State.WorkSeconds = 0;
        State.RestSeconds = 0;
        State.AwaySeconds = 0;
        State.Phase = Phase.Hydration;
    }

    private void AddDuration(DateTimeOffset end, double seconds, bool work)
    {
        DateTimeOffset cursor = end.AddSeconds(-seconds);
        while (seconds > 0.00001)
        {
            DateTime local = cursor.LocalDateTime;
            DateTime nextDate = local.Date.AddDays(1);
            DateTimeOffset nextMidnight = new(nextDate, TimeZoneInfo.Local.GetUtcOffset(nextDate));
            double slice = Math.Min(seconds, Math.Max(0.001, (nextMidnight - cursor).TotalSeconds));
            DaySummary day = Today(cursor);
            if (work) day.WorkSeconds += slice; else day.RestSeconds += slice;
            cursor = cursor.AddSeconds(slice);
            seconds -= slice;
        }
    }
}
