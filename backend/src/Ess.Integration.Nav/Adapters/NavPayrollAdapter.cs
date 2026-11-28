using Ess.Application.Integration;
using Ess.Integration.Nav.Clients;
using Ess.Integration.Nav.Configuration;
using Microsoft.Extensions.Logging;

namespace Ess.Integration.Nav.Adapters;

/// <summary>
/// Payroll adapter (read-only). Standard NAV/BC has no payroll; Kenyan installs use a localisation or partner add-on.
/// The portal never calculates or alters figures: it reads posted payslip lines for a closed, published period.
/// Endpoints: PayrollPeriod (read), Payslip.Header (read), Payslip.Lines (read), Payslip.Pdf (codeunit, optional).
/// </summary>
public sealed class NavPayrollAdapter : NavAdapterBase, INavPayrollService
{
    internal static readonly IReadOnlyDictionary<string, string> PeriodOData = new Dictionary<string, string>
    { ["Code"] = "Code", ["Name"] = "Name", ["Start"] = "Starting_Date", ["End"] = "Ending_Date", ["PayDate"] = "Pay_Date", ["Closed"] = "Closed" };
    internal static readonly IReadOnlyDictionary<string, string> PeriodBc = new Dictionary<string, string>
    { ["Code"] = "code", ["Name"] = "name", ["Start"] = "startingDate", ["End"] = "endingDate", ["PayDate"] = "payDate", ["Closed"] = "closed" };
    internal static readonly IReadOnlyDictionary<string, string> HeadOData = new Dictionary<string, string>
    {
        ["EmployeeNo"] = "Employee_No", ["PeriodCode"] = "Payroll_Period", ["Currency"] = "Currency_Code", ["Basic"] = "Basic_Pay",
        ["Gross"] = "Gross_Pay", ["Deductions"] = "Total_Deductions", ["Net"] = "Net_Pay", ["PaymentDate"] = "Payment_Date"
    };
    internal static readonly IReadOnlyDictionary<string, string> HeadBc = new Dictionary<string, string>
    {
        ["EmployeeNo"] = "employeeNumber", ["PeriodCode"] = "payrollPeriod", ["Currency"] = "currencyCode", ["Basic"] = "basicPay",
        ["Gross"] = "grossPay", ["Deductions"] = "totalDeductions", ["Net"] = "netPay", ["PaymentDate"] = "paymentDate"
    };
    internal static readonly IReadOnlyDictionary<string, string> LineOData = new Dictionary<string, string>
    {
        ["EmployeeNo"] = "Employee_No", ["PeriodCode"] = "Payroll_Period", ["Code"] = "Code", ["Description"] = "Description",
        ["Group"] = "Type", ["Amount"] = "Amount", ["YTD"] = "YTD_Amount", ["Sort"] = "Line_No"
    };
    internal static readonly IReadOnlyDictionary<string, string> LineBc = new Dictionary<string, string>
    {
        ["EmployeeNo"] = "employeeNumber", ["PeriodCode"] = "payrollPeriod", ["Code"] = "code", ["Description"] = "description",
        ["Group"] = "group", ["Amount"] = "amount", ["YTD"] = "yearToDate", ["Sort"] = "lineNo"
    };

    public NavPayrollAdapter(NavConnectionManager c, NavODataClient o, NavSoapClient s, ILogger<NavPayrollAdapter> log) : base(c, o, s, log) { }

    public Task<NavResult<IReadOnlyList<NavPayrollPeriodRecord>>> GetPeriodsAsync(Guid companyId, CancellationToken ct) =>
        RunAsync(companyId, "PayrollPeriod.List", async ctx =>
        {
            var m = Mapper(ctx, "PayrollPeriod", PeriodOData, PeriodBc);
            var rows = await OData.GetAllAsync(ctx, "PayrollPeriod", new NavODataClient.Query(), ct);
            return rows.Map<IReadOnlyList<NavPayrollPeriodRecord>>(l => l.Select(r => new NavPayrollPeriodRecord(
                m.RequireString(r, "Code"), m.GetString(r, "Name") ?? m.RequireString(r, "Code"),
                m.GetDate(r, "Start") ?? throw new FormatException("Payroll period without start date."),
                m.GetDate(r, "End") ?? throw new FormatException("Payroll period without end date."),
                m.GetDate(r, "PayDate"), m.GetBool(r, "Closed") ?? false, Str(r, "@odata.etag"))).ToList());
        }, ct);

    public Task<NavResult<NavPayslip?>> GetPayslipAsync(Guid companyId, string employeeNo, string periodCode, CancellationToken ct) =>
        RunAsync(companyId, "Payslip.Get", async ctx =>
        {
            var hm = Mapper(ctx, "Payslip.Header", HeadOData, HeadBc);
            var lm = Mapper(ctx, "Payslip.Lines", LineOData, LineBc);
            var hf = $"{hm.NavField("EmployeeNo")} eq {NavODataClient.Literal(employeeNo)} and {hm.NavField("PeriodCode")} eq {NavODataClient.Literal(periodCode)}";
            var head = await OData.GetPageAsync(ctx, "Payslip.Header", new NavODataClient.Query(Filter: hf, Top: 1), null, ct);
            if (!head.Success) return NavResult<NavPayslip?>.Fail(head.Error!);
            if (head.Value!.Rows.Count == 0) return NavResult<NavPayslip?>.Ok(null);
            var h = head.Value.Rows[0];

            // Defence in depth: NAV must have returned this employee's record, nobody else's.
            if (!string.Equals(hm.GetString(h, "EmployeeNo"), employeeNo, StringComparison.OrdinalIgnoreCase))
                return NavResult<NavPayslip?>.Fail(NavErrorCategory.Configuration, "Payslip header filter returned another employee's record.");

            var lf = $"{lm.NavField("EmployeeNo")} eq {NavODataClient.Literal(employeeNo)} and {lm.NavField("PeriodCode")} eq {NavODataClient.Literal(periodCode)}";
            var lines = await OData.GetAllAsync(ctx, "Payslip.Lines", new NavODataClient.Query(Filter: lf), ct);
            if (!lines.Success) return NavResult<NavPayslip?>.Fail(lines.Error!);
            if (lines.Value!.Any(r => !string.Equals(lm.GetString(r, "EmployeeNo"), employeeNo, StringComparison.OrdinalIgnoreCase)))
                return NavResult<NavPayslip?>.Fail(NavErrorCategory.Configuration, "Payslip line filter returned another employee's lines.");

            var slip = new NavPayslip(employeeNo, periodCode, hm.GetString(h, "Currency") ?? "KES",
                hm.GetDecimal(h, "Basic"), hm.GetDecimal(h, "Gross"), hm.GetDecimal(h, "Deductions"), hm.GetDecimal(h, "Net"),
                hm.GetDate(h, "PaymentDate"),
                lines.Value.Select(r => new NavPayslipLine(lm.GetString(r, "Code") ?? "", lm.GetString(r, "Description") ?? "",
                    lm.GetString(r, "Group") ?? "Information", lm.GetDecimal(r, "Amount"), lm.GetDecimal(r, "YTD"), (int)lm.GetDecimal(r, "Sort")))
                    .OrderBy(l => l.SortOrder).ToList());
            return NavResult<NavPayslip?>.Ok(slip);
        }, ct);

    public Task<NavResult<IReadOnlyList<NavPayslipTotals>>> GetPeriodTotalsAsync(Guid companyId, string periodCode, CancellationToken ct) =>
        RunAsync(companyId, "Payslip.PeriodTotals", async ctx =>
        {
            var hm = Mapper(ctx, "Payslip.Header", HeadOData, HeadBc);
            var lm = Mapper(ctx, "Payslip.Lines", LineOData, LineBc);
            var heads = await OData.GetAllAsync(ctx, "Payslip.Header",
                new NavODataClient.Query(Filter: $"{hm.NavField("PeriodCode")} eq {NavODataClient.Literal(periodCode)}"), ct);
            if (!heads.Success) return NavResult<IReadOnlyList<NavPayslipTotals>>.Fail(heads.Error!);
            var lines = await OData.GetAllAsync(ctx, "Payslip.Lines",
                new NavODataClient.Query(Filter: $"{lm.NavField("PeriodCode")} eq {NavODataClient.Literal(periodCode)} and {lm.NavField("Group")} eq 'Statutory'"), ct);
            if (!lines.Success) return NavResult<IReadOnlyList<NavPayslipTotals>>.Fail(lines.Error!);
            var statutory = lines.Value!
                .Where(r => string.Equals(lm.GetString(r, "PeriodCode"), periodCode, StringComparison.OrdinalIgnoreCase))
                .GroupBy(r => lm.GetString(r, "EmployeeNo") ?? "", StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => (IReadOnlyDictionary<string, decimal>)g.GroupBy(r => lm.GetString(r, "Code") ?? "").ToDictionary(c => c.Key, c => c.Sum(r => lm.GetDecimal(r, "Amount"))), StringComparer.OrdinalIgnoreCase);
            var empty = new Dictionary<string, decimal>();
            return NavResult<IReadOnlyList<NavPayslipTotals>>.Ok(heads.Value!
                .Where(h => string.Equals(hm.GetString(h, "PeriodCode"), periodCode, StringComparison.OrdinalIgnoreCase))
                .Select(h =>
                {
                    var no = hm.RequireString(h, "EmployeeNo");
                    return new NavPayslipTotals(no, hm.GetString(h, "Currency") ?? "KES", hm.GetDecimal(h, "Gross"), hm.GetDecimal(h, "Deductions"), hm.GetDecimal(h, "Net"),
                        statutory.TryGetValue(no, out var s) ? s : empty);
                }).ToList());
        }, ct);

    public Task<NavResult<byte[]?>> GetPayslipPdfAsync(Guid companyId, string employeeNo, string periodCode, CancellationToken ct) =>
        RunAsync(companyId, "Payslip.Pdf", async ctx =>
        {
            if (ctx.FindEndpoint("Payslip.Pdf") is null) return NavResult<byte[]?>.Ok(null); // portal renders the PDF instead
            var r = await Soap.InvokeCodeunitAsync(ctx, "Payslip.Pdf", new (string, object?)[] { ("employeeNo", employeeNo), ("periodCode", periodCode) }, false, ct);
            if (!r.Success) return NavResult<byte[]?>.Fail(r.Error!);
            return NavResult<byte[]?>.Ok(string.IsNullOrEmpty(r.Value!.ReturnValue) ? null : Convert.FromBase64String(r.Value.ReturnValue));
        }, ct);
}
