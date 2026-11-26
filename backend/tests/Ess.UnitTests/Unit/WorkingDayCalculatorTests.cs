using Ess.Application.Leave;
using FluentAssertions;

namespace Ess.UnitTests.Unit;

public class WorkingDayCalculatorTests
{
    private static readonly IReadOnlySet<DateOnly> NoHolidays = new HashSet<DateOnly>();
    private readonly WorkingDayCalculator _calc = new();

    [Fact]
    public void Monday_to_Friday_is_five_days() =>
        _calc.Calculate(new DateOnly(2026, 11, 23), new DateOnly(2026, 11, 27), NoHolidays).Should().Be(5);

    [Fact]
    public void Weekends_are_excluded() =>
        _calc.Calculate(new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 24), NoHolidays).Should().Be(3); // Fri, Mon, Tue

    [Fact]
    public void Public_holidays_are_excluded()
    {
        // Jamhuri Day, Saturday 12 Dec 2026 falls on a weekend; Christmas Fri 25 Dec and Boxing Day Sat 26 Dec.
        var holidays = new HashSet<DateOnly> { new(2026, 12, 25) };
        _calc.Calculate(new DateOnly(2026, 12, 21), new DateOnly(2026, 12, 28), holidays).Should().Be(5); // 21-24, 28
    }

    [Fact]
    public void Calendar_days_when_leave_type_counts_all_days() =>
        _calc.Calculate(new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 24), NoHolidays, workingDaysOnly: false).Should().Be(5);

    [Fact]
    public void Half_days_reduce_count() =>
        _calc.Calculate(new DateOnly(2026, 11, 23), new DateOnly(2026, 11, 25), NoHolidays, startHalfDay: true, endHalfDay: true).Should().Be(2);

    [Fact]
    public void Half_day_on_a_weekend_does_not_subtract() =>
        _calc.Calculate(new DateOnly(2026, 11, 21), new DateOnly(2026, 11, 23), NoHolidays, startHalfDay: true).Should().Be(1);

    [Fact]
    public void End_before_start_throws() =>
        FluentActions.Invoking(() => _calc.Calculate(new DateOnly(2026, 11, 23), new DateOnly(2026, 11, 22), NoHolidays))
            .Should().Throw<ArgumentException>();

    [Theory]
    [InlineData("2026-11-01", "2026-11-05", "2026-11-05", "2026-11-10", true)]
    [InlineData("2026-11-01", "2026-11-04", "2026-11-05", "2026-11-10", false)]
    [InlineData("2026-11-06", "2026-11-07", "2026-11-05", "2026-11-10", true)]
    public void Overlap_detection(string aS, string aE, string bS, string bE, bool expected) =>
        LeaveOverlap.Overlaps(DateOnly.Parse(aS), DateOnly.Parse(aE), DateOnly.Parse(bS), DateOnly.Parse(bE)).Should().Be(expected);
}
