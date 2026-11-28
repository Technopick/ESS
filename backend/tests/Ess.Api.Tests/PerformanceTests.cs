using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Ess.Domain.Identity;
using FluentAssertions;
using Npgsql;
using Xunit.Abstractions;

namespace Ess.Api.Tests;

[CollectionDefinition("perf")]
public sealed class PerfCollection : ICollectionFixture<EssApiFactory> { }

/// <summary>
/// Phase 6 performance test. Loads a company the size of the target client (10,000 employees with two years of leave,
/// expense claims, payslip records, notifications and audit history) into its own database, then times the endpoints
/// people use most. It runs only when ESS_PERF=1 because the seed takes a minute; ESS_PERF_REPORT=path writes the
/// results as Markdown. Budgets are deliberately loose so a busy CI runner does not flap: the report shows real numbers.
/// </summary>
[Collection("perf")]
public sealed class PerformanceTests : ApiTestBase
{
    private const int Employees = 10_000;
    private const int Samples = 30;
    private static readonly bool Enabled = Environment.GetEnvironmentVariable("ESS_PERF") == "1";
    private readonly ITestOutputHelper _out;

    public PerformanceTests(EssApiFactory f, ITestOutputHelper output) : base(f) => _out = output;

    private sealed record Probe(string Name, HttpClient Client, string Url, double P95BudgetMs);
    private sealed record Result(string Name, string Url, int Status, double P50, double P95, double Max, long Bytes, double Budget)
    {
        public string Scans { get; set; } = "";
    }

    [SkippableFact]
    public async Task Key_endpoints_stay_fast_with_ten_thousand_employees()
    {
        Skip.If(EssApiFactory.BaseCs is null || !Enabled, "Set ESS_TEST_DB and ESS_PERF=1 to run the load test.");
        var baraka = await UserAsync(Baraka);
        var daudi = await UserAsync(Daudi);
        var amani = await UserAsync(Amani);
        await GrantRoleAsync(Amani, SystemRoles.HrManager);
        await GrantRoleAsync(Amani, SystemRoles.ManagementViewer);
        await GrantRoleAsync(Amani, SystemRoles.FinanceManager);
        amani = await _f.SignInAsync(Amani, Password); // pick up the new roles
        var admin = await _f.SignInAsync(EssApiFactory.AdminUser, EssApiFactory.AdminPassword);

        var seedTimer = Stopwatch.StartNew();
        var counts = await SeedAsync(await SimCompanyAsync());
        seedTimer.Stop();
        _out.WriteLine($"Seeded in {seedTimer.Elapsed.TotalSeconds:0}s: {string.Join(", ", counts.Select(c => $"{c.Key} {c.Value:N0}"))}");

        var from = DateTime.UtcNow.AddMonths(-12).ToString("yyyy-MM-dd");
        var to = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var probes = new List<Probe>
        {
            new("Employee dashboard", baraka, "/api/dashboard/me", 500),
            new("My notifications", baraka, "/api/notifications?page=1&pageSize=20", 300),
            new("Unread count (polled)", baraka, "/api/notifications/unread-count", 150),
            new("My leave applications", baraka, "/api/leave/mine", 300),
            new("Manager approvals inbox", daudi, "/api/approvals/inbox", 500),
            new("HR employee list, page 1", amani, "/api/hr/employees?page=1&pageSize=25", 500),
            new("HR employee search", amani, "/api/hr/employees?search=wanjiku&page=1&pageSize=25", 500),
            new("HR employees, contracts ending", amani, "/api/hr/employees?contractEndingSoon=true&sort=contract", 500),
            new("HR employee list, page 200", amani, "/api/hr/employees?page=200&pageSize=25", 500),
            new("HR leave applications", amani, "/api/hr/leave/applications?page=1&pageSize=25", 500),
            new("Management dashboard", amani, "/api/management/dashboard", 1500),
            new("Report: headcount", amani, $"/api/reports/headcount?from={from}&to={to}", 1500),
            new("Report: leave applications (12 months)", amani, $"/api/reports/leave-applications?from={from}&to={to}", 2500),
            new("Report: employee directory (10k rows)", amani, "/api/reports/employee-directory", 2500),
            new("Report: expense claims (12 months)", amani, $"/api/reports/expense-claims?from={from}&to={to}", 2500),
            new("Report: pending approvals", amani, "/api/reports/pending-approvals", 1500),
            new("Admin user list", admin, "/api/admin/users?page=1&pageSize=25", 500),
        };

        var before = await TableScanStatsAsync();
        var results = new List<Result>();
        foreach (var p in probes)
        {
            var pre = await TableScanStatsAsync();
            var res = await MeasureAsync(p);
            var post = await TableScanStatsAsync();
            // Rows read by sequential scans per request, by table: the tell-tale of a missing index.
            res.Scans = string.Join(", ", post.Select(kv => (kv.Key, Rows: (kv.Value.SeqRows - (pre.TryGetValue(kv.Key, out var b) ? b.SeqRows : 0)) / (Samples + 3)))
                .Where(x => x.Rows >= 1000).OrderByDescending(x => x.Rows).Select(x => $"{x.Key} {x.Rows:N0}"));
            results.Add(res);
        }
        var after = await TableScanStatsAsync();

        // Concurrency: 50 people refreshing their dashboard and notifications at once.
        var burstTimer = Stopwatch.StartNew();
        var burst = await Task.WhenAll(Enumerable.Range(0, 50).Select(async i =>
        {
            var c = i % 2 == 0 ? baraka : daudi;
            var sw = Stopwatch.StartNew();
            using var r = await c.GetAsync(i % 3 == 0 ? "/api/notifications/unread-count" : "/api/dashboard/me");
            return ((int)r.StatusCode, sw.Elapsed.TotalMilliseconds);
        }));
        burstTimer.Stop();
        var burstTimes = burst.Select(b => b.Item2).OrderBy(x => x).ToArray();

        var report = Render(counts, seedTimer.Elapsed, results, before, after, burstTimer.Elapsed, burstTimes, burst.Count(b => b.Item1 >= 500));
        _out.WriteLine(report);
        var path = Environment.GetEnvironmentVariable("ESS_PERF_REPORT");
        if (!string.IsNullOrWhiteSpace(path)) await File.WriteAllTextAsync(path, report);

        results.Should().OnlyContain(r => r.Status == 200, "every probe must succeed under load");
        burst.Should().OnlyContain(b => b.Item1 == 200, "parallel dashboard refreshes must not fail");
        results.Where(r => r.P95 > r.Budget).Select(r => $"{r.Name}: p95 {r.P95:0} ms > {r.Budget:0} ms").Should().BeEmpty();
    }

    private static async Task<Result> MeasureAsync(Probe p)
    {
        var status = 0;
        long bytes = 0;
        for (var i = 0; i < 3; i++) // warm-up: JIT, EF query cache, PostgreSQL buffers
        {
            using var w = await p.Client.GetAsync(p.Url);
            status = (int)w.StatusCode;
        }
        var times = new double[Samples];
        for (var i = 0; i < Samples; i++)
        {
            var sw = Stopwatch.StartNew();
            using var r = await p.Client.GetAsync(p.Url);
            var body = await r.Content.ReadAsByteArrayAsync();
            times[i] = sw.Elapsed.TotalMilliseconds;
            status = (int)r.StatusCode;
            bytes = body.LongLength;
        }
        Array.Sort(times);
        return new Result(p.Name, p.Url, status, Pct(times, 0.50), Pct(times, 0.95), times[^1], bytes, p.P95BudgetMs);
    }

    private static double Pct(double[] sorted, double q) => sorted[Math.Clamp((int)Math.Ceiling(q * sorted.Length) - 1, 0, sorted.Length - 1)];

    /// <summary>Bulk-loads realistic volume with set-based SQL (the portal never writes NAV; this is the portal's own cache).</summary>
    private async Task<Dictionary<string, long>> SeedAsync(Guid companyId)
    {
        await using var conn = new NpgsqlConnection(_f.ConnectionString);
        await conn.OpenAsync();
        async Task Exec(string sql)
        {
            await using var cmd = new NpgsqlCommand(sql, conn) { CommandTimeout = 600 };
            cmd.Parameters.AddWithValue("c", companyId);
            cmd.Parameters.AddWithValue("n", Employees);
            await cmd.ExecuteNonQueryAsync();
        }

        // 10,000 employees spread over the company's departments, positions and locations; managers form a tree.
        await Exec("""
            CREATE TEMP TABLE d AS SELECT id, row_number() OVER (ORDER BY id) - 1 AS k FROM departments WHERE company_id = @c;
            CREATE TEMP TABLE p AS SELECT id, row_number() OVER (ORDER BY id) - 1 AS k FROM positions WHERE company_id = @c;
            CREATE TEMP TABLE l AS SELECT id, row_number() OVER (ORDER BY id) - 1 AS k FROM locations WHERE company_id = @c;
            CREATE TEMP TABLE fn(k int, v text); INSERT INTO fn SELECT row_number() OVER () - 1, v FROM unnest(ARRAY['Wanjiku','Otieno','Achieng','Kamau','Mutua','Njeri','Kiprop','Wafula','Atieno','Mwangi','Chebet','Odhiambo','Nyambura','Kariuki','Jepkemboi','Omondi']) v;
            CREATE TEMP TABLE ln(k int, v text); INSERT INTO ln SELECT row_number() OVER () - 1, v FROM unnest(ARRAY['Kimani','Ochieng','Wekesa','Muthoni','Korir','Onyango','Njoroge','Barasa','Rotich','Akinyi','Gitau','Owino','Langat','Mugo','Auma','Ndungu','Ruto']) v;
            INSERT INTO employees (id, company_id, nav_key, first_name, last_name, work_email, mobile_phone, department_id, position_id, location_id, status,
                                   employment_date, contract_end_date, birth_date, gender, last_synced_at_utc, created_at_utc, is_deleted)
            SELECT md5('perf-emp-' || g)::uuid, @c, 'PERF-' || lpad(g::text, 5, '0'),
                   (SELECT v FROM fn WHERE k = g % 16), (SELECT v FROM ln WHERE k = (g / 16) % 17),
                   'perf' || g || '@example.invalid', '2547' || lpad((10000000 + g)::text, 8, '0'),
                   (SELECT id FROM d WHERE k = g % greatest((SELECT count(*) FROM d), 1)),
                   (SELECT id FROM p WHERE k = g % greatest((SELECT count(*) FROM p), 1)),
                   (SELECT id FROM l WHERE k = g % greatest((SELECT count(*) FROM l), 1)),
                   CASE WHEN g % 50 = 0 THEN 2 WHEN g % 23 = 0 THEN 3 ELSE 0 END,
                   date '2010-01-01' + (g % 5000), CASE WHEN g % 7 = 0 THEN current_date + (g % 400) END,
                   date '1965-01-01' + (g * 7 % 13000), CASE WHEN g % 2 = 0 THEN 'Female' ELSE 'Male' END, now(), now(), false
            FROM generate_series(1, @n) g;
            UPDATE employees e SET manager_employee_id = md5('perf-emp-' || (substring(e.nav_key from 6)::int / 10))::uuid
             WHERE e.company_id = @c AND e.nav_key LIKE 'PERF-%' AND substring(e.nav_key from 6)::int >= 10;
            -- Daudi manages 25 of them so his inbox and team views have real volume.
            UPDATE employees SET manager_employee_id = (SELECT id FROM employees WHERE company_id = @c AND nav_key = 'SIM-004')
             WHERE company_id = @c AND nav_key IN (SELECT 'PERF-' || lpad(g::text, 5, '0') FROM generate_series(1, 25) g);
            """);

        // Leave: ~6 applications per employee over two years, mixed statuses.
        await Exec("""
            INSERT INTO leave_applications (id, company_id, reference, employee_id, leave_type_id, start_date, end_date, start_half_day, end_half_day,
                                            days_requested, reason, status, nav_status, submitted_at_utc, created_at_utc, is_deleted)
            SELECT gen_random_uuid(), @c, 'PERF-LV-' || e.n || '-' || s, e.id,
                   (SELECT id FROM leave_types WHERE company_id = @c ORDER BY nav_key OFFSET (s % greatest((SELECT count(*) FROM leave_types WHERE company_id = @c), 1)) LIMIT 1),
                   current_date - 700 + ((e.n * 13 + s * 113) % 730), current_date - 700 + ((e.n * 13 + s * 113) % 730) + (s % 5),
                   false, false, (s % 5) + 1, 'Load test', (ARRAY[1,2,3,4,5,6,7])[1 + (e.n + s) % 7], CASE WHEN (e.n + s) % 7 = 3 THEN 3 ELSE 0 END,
                   now() - make_interval(days => (e.n + s * 97) % 700), now() - make_interval(days => (e.n + s * 97) % 700), false
            FROM (SELECT id, substring(nav_key from 6)::int AS n FROM employees WHERE company_id = @c AND nav_key LIKE 'PERF-%') e, generate_series(1, 6) s;
            """);

        // Expenses: one claim per employee per quarter for a third of staff.
        await Exec("""
            INSERT INTO expense_claims (id, company_id, reference, employee_id, purpose, currency, total_amount, status, nav_status, submitted_at_utc, created_at_utc, is_deleted)
            SELECT gen_random_uuid(), @c, 'PERF-EX-' || e.n || '-' || q, e.id, 'Field visit', 'KES', 1500 + (e.n * q) % 25000,
                   (ARRAY[1,2,3,4,5])[1 + (e.n + q) % 5], 0, now() - make_interval(days => q * 90 + e.n % 60), now() - make_interval(days => q * 90 + e.n % 60), false
            FROM (SELECT id, substring(nav_key from 6)::int AS n FROM employees WHERE company_id = @c AND nav_key LIKE 'PERF-%' AND substring(nav_key from 6)::int % 3 = 0) e,
                 generate_series(0, 7) q;
            """);

        // Payslip index rows: 12 months per employee (metadata only; payslip content stays in NAV).
        await Exec("""
            INSERT INTO payroll_periods_cache (id, company_id, nav_key, name, start_date, end_date, is_closed, is_published, last_synced_at_utc)
            SELECT md5('perf-period-' || m)::uuid, @c, 'PERF' || to_char(date_trunc('month', current_date) - make_interval(months => m), 'YYYYMM'),
                   to_char(date_trunc('month', current_date) - make_interval(months => m), 'Mon YYYY'),
                   (date_trunc('month', current_date) - make_interval(months => m))::date,
                   (date_trunc('month', current_date) - make_interval(months => m - 1))::date - 1, true, true, now()
            FROM generate_series(1, 12) m
            ON CONFLICT DO NOTHING;
            INSERT INTO payslip_metadata (id, company_id, employee_id, payroll_period_id, nav_employee_no, nav_period_key, net_pay, currency, retrieved_at_utc, created_at_utc, is_deleted)
            SELECT gen_random_uuid(), @c, e.id, md5('perf-period-' || m)::uuid, e.nav_key, 'PERF' || m, NULL, 'KES', now(), now(), false
            FROM employees e, generate_series(1, 12) m WHERE e.company_id = @c AND e.nav_key LIKE 'PERF-%'
              AND EXISTS (SELECT 1 FROM payroll_periods_cache pc WHERE pc.id = md5('perf-period-' || m)::uuid);
            """);

        // Notifications: 400 for Baraka (a long history) plus 10 per seeded employee's manager chain owner; audit: 200,000 rows.
        await Exec("""
            INSERT INTO notifications (id, company_id, recipient_user_id, event_code, channel, subject, body, status, attempts, read_at_utc, created_at_utc, is_deleted)
            SELECT gen_random_uuid(), @c, u.id, 'request.approved', 0, 'Leave approved', 'Your leave was approved.', 2, 1,
                   CASE WHEN g % 4 = 0 THEN NULL ELSE now() END, now() - make_interval(hours => g), false
            FROM users u, generate_series(1, 400) g WHERE u.normalized_email IN ('BARAKA.OTIENO@EXAMPLE.INVALID', 'DAUDI.MWANGI@EXAMPLE.INVALID');
            INSERT INTO audit_logs (id, occurred_at_utc, user_id, user_name, company_id, action, entity_type, entity_id, success, ip_address)
            SELECT gen_random_uuid(), now() - make_interval(mins => g), NULL, 'perf' || (g % @n), @c,
                   (ARRAY['auth.login','leave.submitted','payslip.viewed','report.viewed','profile.viewed'])[1 + g % 5], 'Employee', (g % @n)::text, true, '10.0.0.' || (g % 250)
            FROM generate_series(1, 200000) g;
            ANALYZE;
            """);

        var counts = new Dictionary<string, long>();
        foreach (var t in new[] { "employees", "leave_applications", "expense_claims", "payslip_metadata", "notifications", "audit_logs" })
        {
            await using var cmd = new NpgsqlCommand($"SELECT count(*) FROM {t}", conn);
            counts[t] = (long)(await cmd.ExecuteScalarAsync())!;
        }
        return counts;
    }

    private async Task<Dictionary<string, (long Seq, long SeqRows, long Idx)>> TableScanStatsAsync()
    {
        await Task.Delay(700); // statistics are flushed to pg_stat asynchronously
        await using var conn = new NpgsqlConnection(_f.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT relname, seq_scan, seq_tup_read, coalesce(idx_scan, 0) FROM pg_stat_user_tables
             WHERE relname IN ('employees','leave_applications','expense_claims','payslip_metadata','notifications','audit_logs','employee_mappings','approval_tasks','workflow_instances')
            """, conn);
        var map = new Dictionary<string, (long, long, long)>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) map[r.GetString(0)] = (r.GetInt64(1), r.GetInt64(2), r.GetInt64(3));
        return map;
    }

    private static string Render(Dictionary<string, long> counts, TimeSpan seed, List<Result> results,
        Dictionary<string, (long Seq, long SeqRows, long Idx)> before, Dictionary<string, (long Seq, long SeqRows, long Idx)> after,
        TimeSpan burst, double[] burstTimes, int burstErrors)
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("## Data volume").AppendLine();
        sb.AppendLine("| Table | Rows |").AppendLine("|---|---:|");
        foreach (var (k, v) in counts) sb.AppendLine(ci, $"| {k} | {v:N0} |");
        sb.AppendLine().AppendLine(ci, $"Seeded in {seed.TotalSeconds:0} s.").AppendLine();
        sb.AppendLine(ci, $"## Response times ({Samples} sequential requests per endpoint after 3 warm-up requests)").AppendLine();
        sb.AppendLine("| Endpoint | p50 ms | p95 ms | max ms | Budget p95 | Response size | Rows scanned per request | Result |").AppendLine("|---|---:|---:|---:|---:|---:|---|---|");
        foreach (var r in results)
            sb.AppendLine(ci, $"| {r.Name} | {r.P50:0} | {r.P95:0} | {r.Max:0} | {r.Budget:0} | {r.Bytes / 1024.0:0.#} KB | {(r.Scans.Length == 0 ? "-" : r.Scans)} | {(r.Status != 200 ? $"HTTP {r.Status}" : r.P95 <= r.Budget ? "Pass" : "Over budget")} |");
        sb.AppendLine().AppendLine("## Parallel load").AppendLine();
        sb.AppendLine(ci, $"50 simultaneous dashboard and unread-count requests finished in {burst.TotalMilliseconds:0} ms; p50 {Pct(burstTimes, .5):0} ms, p95 {Pct(burstTimes, .95):0} ms, errors {burstErrors}.");
        sb.AppendLine().AppendLine("## Table access during the timed run").AppendLine();
        sb.AppendLine("Sequential scans on large tables point to a missing index. Small tables are scanned on purpose by PostgreSQL.").AppendLine();
        sb.AppendLine("| Table | Sequential scans | Rows read by them | Index scans |").AppendLine("|---|---:|---:|---:|");
        foreach (var (k, a) in after.OrderBy(x => x.Key))
        {
            var b = before.TryGetValue(k, out var x) ? x : (0, 0, 0);
            sb.AppendLine(ci, $"| {k} | {a.Seq - b.Seq:N0} | {a.SeqRows - b.SeqRows:N0} | {a.Idx - b.Idx:N0} |");
        }
        return sb.ToString();
    }
}
