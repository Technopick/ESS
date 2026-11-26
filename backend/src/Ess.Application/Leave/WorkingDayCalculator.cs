namespace Ess.Application.Leave;

/// <summary>
/// Counts leave days between two dates, excluding non-working weekdays and public holidays.
/// This is only used for the portal-side preview and validation; NAV's own calculation
/// is authoritative where NAV exposes one (the NAV result is stored on submission).
/// </summary>
public sealed class WorkingDayCalculator
{
    private readonly HashSet<DayOfWeek> _nonWorkingDays;

    public WorkingDayCalculator(IEnumerable<DayOfWeek>? nonWorkingDays = null)
    {
        _nonWorkingDays = new HashSet<DayOfWeek>(nonWorkingDays ?? new[] { DayOfWeek.Saturday, DayOfWeek.Sunday });
    }

    public decimal Calculate(DateOnly start, DateOnly end, IReadOnlySet<DateOnly> holidays,
        bool workingDaysOnly = true, bool startHalfDay = false, bool endHalfDay = false)
    {
        if (end < start) throw new ArgumentException("End date must be on or after start date.", nameof(end));

        decimal days = 0;
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            if (workingDaysOnly && (_nonWorkingDays.Contains(d.DayOfWeek) || holidays.Contains(d))) continue;
            days += 1;
        }

        bool Counts(DateOnly d) => !workingDaysOnly || (!_nonWorkingDays.Contains(d.DayOfWeek) && !holidays.Contains(d));
        if (startHalfDay && Counts(start)) days -= 0.5m;
        if (endHalfDay && end != start && Counts(end)) days -= 0.5m;
        return Math.Max(days, 0);
    }
}

/// <summary>Detects overlap between a new application and the employee's existing active applications.</summary>
public static class LeaveOverlap
{
    public static bool Overlaps(DateOnly aStart, DateOnly aEnd, DateOnly bStart, DateOnly bEnd) =>
        aStart <= bEnd && bStart <= aEnd;
}
