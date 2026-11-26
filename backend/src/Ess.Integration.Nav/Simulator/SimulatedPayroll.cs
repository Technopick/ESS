namespace Ess.Integration.Nav.Simulator;

/// <summary>
/// DEVELOPMENT/TEST ONLY. Produces plausible Kenyan payslips for the simulator so the payslip screens can be built
/// and tested. Real payroll is always calculated by NAV; the portal never calculates pay. Statutory figures follow the
/// rules in force in 2026 (NSSF Year 3 tiers, SHIF 2.75%, Affordable Housing Levy 1.5%, PAYE bands, with NSSF, SHIF and
/// the levy deducted before PAYE), but they are illustrations, not payroll advice.
/// </summary>
internal static class SimulatedPayroll
{
    private static readonly Dictionary<string, (decimal Basic, decimal Sacco)> Pay = new()
    {
        ["SIM-001"] = (350_000m, 10_000m),
        ["SIM-002"] = (120_000m, 5_000m),
        ["SIM-003"] = (85_000m, 0m),
        ["SIM-004"] = (420_000m, 15_000m),
    };

    private static readonly (string Code, int Month)[] Periods = { ("2026-10", 10), ("2026-11", 11) };

    public static IEnumerable<object> Headers(IEnumerable<string> employeeNos) =>
        from no in employeeNos
        where Pay.ContainsKey(no)
        from p in Periods
        let s = Slip(no, p.Month)
        select new Dictionary<string, object?>
        {
            ["Employee_No"] = no, ["Payroll_Period"] = p.Code, ["Currency_Code"] = "KES", ["Basic_Pay"] = s.Basic, ["Gross_Pay"] = s.Gross,
            ["Total_Deductions"] = s.Deductions, ["Net_Pay"] = s.Net, ["Payment_Date"] = $"2026-{p.Month:00}-{(p.Month == 10 ? 28 : 27)}"
        };

    public static IEnumerable<object> Lines(IEnumerable<string> employeeNos) =>
        from no in employeeNos
        where Pay.ContainsKey(no)
        from p in Periods
        from l in Slip(no, p.Month).Lines
        select new Dictionary<string, object?>
        {
            ["Employee_No"] = no, ["Payroll_Period"] = p.Code, ["Code"] = l.Code, ["Description"] = l.Description, ["Type"] = l.Group,
            ["Amount"] = l.Amount, ["YTD_Amount"] = Math.Round(l.Amount * p.Month, 2), ["Line_No"] = l.Sort
        };

    private sealed record Line(string Code, string Description, string Group, decimal Amount, int Sort);
    private sealed record Payslip(decimal Basic, decimal Gross, decimal Deductions, decimal Net, IReadOnlyList<Line> Lines);

    private static Payslip Slip(string no, int month)
    {
        var (basic, sacco) = Pay[no];
        var house = Math.Round(basic * 0.15m, 2);
        const decimal transport = 8_000m;
        var gross = basic + house + transport;

        var nssfTier1 = Math.Round(Math.Min(gross, 8_000m) * 0.06m, 2);
        var nssfTier2 = Math.Round(Math.Max(Math.Min(gross, 72_000m) - 8_000m, 0) * 0.06m, 2);
        var shif = Math.Max(Math.Round(gross * 0.0275m, 2), 300m);
        var ahl = Math.Round(gross * 0.015m, 2);
        var taxable = gross - nssfTier1 - nssfTier2 - shif - ahl;
        var paye = Math.Max(Math.Round(Tax(taxable) - 2_400m, 2), 0);

        var lines = new List<Line>
        {
            new("BASIC", "Basic salary", "Earning", basic, 10),
            new("HOUSE", "House allowance", "Allowance", house, 20),
            new("TRANSPORT", "Transport allowance", "Allowance", transport, 30),
            new("NSSF-I", "NSSF Tier I", "Statutory", nssfTier1, 100),
            new("NSSF-II", "NSSF Tier II", "Statutory", nssfTier2, 110),
            new("SHIF", "Social Health Insurance Fund", "Statutory", shif, 120),
            new("AHL", "Affordable Housing Levy", "Statutory", ahl, 130),
            new("PAYE", "PAYE", "Statutory", paye, 140),
            new("RELIEF", "Personal relief", "Relief", 2_400m, 200),
            new("TAXABLE", "Taxable pay", "Information", taxable, 300),
        };
        if (sacco > 0) lines.Add(new("SACCO", "SIM Staff SACCO contribution", "Deduction", sacco, 150));
        var deductions = nssfTier1 + nssfTier2 + shif + ahl + paye + sacco;
        return new Payslip(basic, gross, deductions, gross - deductions, lines);
    }

    /// <summary>Monthly PAYE before relief, 2026 bands.</summary>
    private static decimal Tax(decimal taxable)
    {
        (decimal Upto, decimal Rate)[] bands = { (24_000m, 0.10m), (32_333m, 0.25m), (500_000m, 0.30m), (800_000m, 0.325m), (decimal.MaxValue, 0.35m) };
        decimal tax = 0, lower = 0;
        foreach (var (upto, rate) in bands)
        {
            if (taxable <= lower) break;
            tax += (Math.Min(taxable, upto) - lower) * rate;
            lower = upto;
        }
        return tax;
    }
}
