namespace Ess.Api.Reporting;

public enum ReportColumnType { Text, Number, Decimal, Money, Date, DateTime, Percent }

public sealed record ReportColumn(string Key, string Label, ReportColumnType Type = ReportColumnType.Text);

/// <summary>A report result: what the screen shows and what every export format is generated from, so they always agree.</summary>
public sealed record ReportTable(
    string Title,
    IReadOnlyList<ReportColumn> Columns,
    IReadOnlyList<object?[]> Rows,
    IReadOnlyList<KeyValuePair<string, string>> Filters,
    string? Note = null,
    bool Truncated = false)
{
    /// <summary>Optional totals row, aligned with <see cref="Columns"/>.</summary>
    public object?[]? Totals { get; init; }
}

public sealed record ReportFilter(
    DateOnly? From = null,
    DateOnly? To = null,
    Guid? DepartmentId = null,
    Guid? LocationId = null,
    Guid? EmployeeId = null,
    string? Status = null,
    string? PeriodCode = null);

/// <summary>A report cannot run with these filters (for example an open payroll period). Shown to the user as a 409.</summary>
public sealed class ReportUnavailableException : Exception
{
    public ReportUnavailableException(string message) : base(message) { }
}
