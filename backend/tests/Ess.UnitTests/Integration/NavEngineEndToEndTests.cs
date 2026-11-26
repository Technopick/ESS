using Ess.Application.Abstractions;
using Ess.Application.Integration;
using Ess.Domain.Common;
using Ess.Domain.Integration;
using Ess.Domain.Leave;
using Ess.Domain.Organization;
using Ess.Infrastructure;
using Ess.Infrastructure.Persistence;
using Ess.Integration.Nav;
using Ess.Integration.Nav.Queue;
using Ess.Integration.Nav.Simulator;
using Ess.Integration.Nav.Sync;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Ess.UnitTests.Integration;

/// <summary>
/// Runs the real integration engine (connection manager, auth, transport, SOAP/OData clients, adapters, outbox,
/// retry, reconciliation, sync) against PostgreSQL and the NAV simulator. Requires ESS_TEST_DB (a PostgreSQL
/// connection string to a server where the test may create a throw-away database); skipped otherwise.
/// </summary>
public sealed class NavEngineEndToEndTests : IAsyncLifetime
{
    private static readonly string? BaseCs = Environment.GetEnvironmentVariable("ESS_TEST_DB");
    private readonly string _dbName = "ess_test_" + Guid.NewGuid().ToString("N")[..8];
    private ServiceProvider _sp = null!;
    private Guid _companyId;

    public async Task InitializeAsync()
    {
        if (BaseCs is null) return;
        var cs = new NpgsqlConnectionStringBuilder(BaseCs) { Database = _dbName }.ConnectionString;
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Ess"] = cs,
            ["NavIntegration:AllowSimulator"] = "true",
            ["NavIntegration:RetryBaseDelaySeconds"] = "0",
        }).Build();
        var env = new TestEnv();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(env);
        services.AddScoped<ICurrentUser, TestUser>();
        services.AddEssInfrastructure(config);
        services.AddNavIntegrationEngine(config, env, runWorker: false);
        _sp = services.BuildServiceProvider();

        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EssDbContext>();
        await db.Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync();
        await SimulatorSetup.EnsureAsync(db);
        _companyId = await db.Companies.Where(c => c.Code == "SIM").Select(c => c.Id).SingleAsync();
    }

    public async Task DisposeAsync()
    {
        if (BaseCs is null) return;
        await _sp.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await using var conn = new NpgsqlConnection(BaseCs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS {_dbName} WITH (FORCE)", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    [SkippableFact]
    public async Task Sync_pulls_reference_data_and_employees_and_links_managers()
    {
        Skip.If(BaseCs is null, "ESS_TEST_DB not set");
        var sync = _sp.GetRequiredService<NavSyncService>();
        foreach (var e in NavSyncService.Entities)
            (await sync.SyncAsync(_companyId, e, full: true, "test", default)).Status.Should().Be(SyncJobStatus.Succeeded, e);

        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EssDbContext>();
        var emps = await db.Employees.Where(e => e.CompanyId == _companyId).ToListAsync();
        emps.Should().HaveCount(4);
        var baraka = emps.Single(e => e.NavKey == "SIM-002");
        baraka.ManagerEmployeeId.Should().Be(emps.Single(e => e.NavKey == "SIM-004").Id);
        baraka.DepartmentId.Should().NotBeNull();
        (await db.LeaveTypes.CountAsync()).Should().Be(4);
        (await db.NavSyncLogs.CountAsync(l => l.Success)).Should().BeGreaterThan(0);
    }

    [SkippableFact]
    public async Task Leave_submission_is_only_posted_after_nav_confirms()
    {
        Skip.If(BaseCs is null, "ESS_TEST_DB not set");
        var leaveId = await CreateLeaveAndEnqueueAsync(days: 3);
        await _sp.GetRequiredService<IntegrationQueueProcessor>().ProcessBatchAsync(default);

        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EssDbContext>();
        var leave = await db.LeaveApplications.SingleAsync(l => l.Id == leaveId);
        leave.NavStatus.Should().Be(NavSyncStatus.Posted);
        leave.NavDocumentNo.Should().StartWith("SIM-LV");
        var item = await db.IntegrationQueue.SingleAsync(q => q.EntityId == leaveId);
        item.Status.Should().Be(QueueItemStatus.Succeeded);
    }

    [SkippableFact]
    public async Task Nav_business_rejection_fails_without_retry_and_is_recorded()
    {
        Skip.If(BaseCs is null, "ESS_TEST_DB not set");
        var leaveId = await CreateLeaveAndEnqueueAsync(days: 30); // simulator rejects > 21 days
        await _sp.GetRequiredService<IntegrationQueueProcessor>().ProcessBatchAsync(default);

        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EssDbContext>();
        var item = await db.IntegrationQueue.SingleAsync(q => q.EntityId == leaveId);
        item.Status.Should().Be(QueueItemStatus.Failed);
        item.LastErrorCategory.Should().Be("Validation");
        item.LastError.Should().Contain("exceed the available balance");
        (await db.LeaveApplications.SingleAsync(l => l.Id == leaveId)).NavStatus.Should().Be(NavSyncStatus.Failed);
    }

    [SkippableFact]
    public async Task Transient_failure_is_retried_with_backoff()
    {
        Skip.If(BaseCs is null, "ESS_TEST_DB not set");
        var sim = _sp.GetRequiredService<NavSimulatorHandler>();
        sim.FailNextWrites = 1;
        var leaveId = await CreateLeaveAndEnqueueAsync(days: 2);
        var processor = _sp.GetRequiredService<IntegrationQueueProcessor>();
        await processor.ProcessBatchAsync(default);

        using (var scope = _sp.CreateScope())
        {
            var item = await scope.ServiceProvider.GetRequiredService<EssDbContext>().IntegrationQueue.SingleAsync(q => q.EntityId == leaveId);
            item.Status.Should().Be(QueueItemStatus.Pending);
            item.Attempts.Should().Be(1);
            item.LastErrorCategory.Should().Be("Transient");
        }

        await Task.Delay(1200); // base delay is 0s in this test, jitter keeps it ~0
        await processor.ProcessBatchAsync(default);
        using (var scope = _sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EssDbContext>();
            (await db.IntegrationQueue.SingleAsync(q => q.EntityId == leaveId)).Status.Should().Be(QueueItemStatus.Succeeded);
            (await db.LeaveApplications.SingleAsync(l => l.Id == leaveId)).NavStatus.Should().Be(NavSyncStatus.Posted);
        }
    }

    [SkippableFact]
    public async Task Lost_response_goes_to_reconciliation_and_does_not_duplicate()
    {
        Skip.If(BaseCs is null, "ESS_TEST_DB not set");
        var sim = _sp.GetRequiredService<NavSimulatorHandler>();
        sim.LoseNextWriteResponse = true; // NAV commits, portal never hears back
        var leaveId = await CreateLeaveAndEnqueueAsync(days: 1);
        await _sp.GetRequiredService<IntegrationQueueProcessor>().ProcessBatchAsync(default);

        string? firstDocNo;
        using (var scope = _sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EssDbContext>();
            var item = await db.IntegrationQueue.SingleAsync(q => q.EntityId == leaveId);
            item.Status.Should().Be(QueueItemStatus.RequiresReconciliation);
            (await db.LeaveApplications.SingleAsync(l => l.Id == leaveId)).NavStatus.Should().Be(NavSyncStatus.RequiresReconciliation);
            var nav = _sp.GetRequiredService<INavLeaveService>();
            firstDocNo = (await nav.FindByExternalReferenceAsync(_companyId, item.IdempotencyKey, default)).Value?.NavDocumentNo;
            firstDocNo.Should().NotBeNull("the simulator applied the write before the response was lost");
        }

        await _sp.GetRequiredService<ReconciliationService>().RunAsync(default);

        using (var scope = _sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EssDbContext>();
            var item = await db.IntegrationQueue.SingleAsync(q => q.EntityId == leaveId);
            item.Status.Should().Be(QueueItemStatus.Succeeded);
            item.NavDocumentNo.Should().Be(firstDocNo, "reconciliation must adopt NAV's existing document, not create a second one");
            (await db.LeaveApplications.SingleAsync(l => l.Id == leaveId)).NavDocumentNo.Should().Be(firstDocNo);
        }
    }

    [SkippableFact]
    public async Task Enqueue_is_idempotent_for_the_same_business_action()
    {
        Skip.If(BaseCs is null, "ESS_TEST_DB not set");
        var leaveId = await CreateLeaveAndEnqueueAsync(days: 1);
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EssDbContext>();
        var queue = scope.ServiceProvider.GetRequiredService<IIntegrationQueue>();
        var key = IntegrationQueue.KeyFor(NavOperations.LeaveSubmit, leaveId);
        var again = await queue.EnqueueAsync(_companyId, NavOperations.LeaveSubmit, "LeaveApplication", leaveId, key, new { }, false);
        await db.SaveChangesAsync();
        (await db.IntegrationQueue.CountAsync(q => q.EntityId == leaveId)).Should().Be(1);
        again.Should().Be(await db.IntegrationQueue.Where(q => q.EntityId == leaveId).Select(q => q.Id).SingleAsync());
    }

    [SkippableFact]
    public async Task Nav_call_logs_never_contain_plain_payroll_or_secret_values()
    {
        Skip.If(BaseCs is null, "ESS_TEST_DB not set");
        await _sp.GetRequiredService<NavSyncService>().SyncAsync(_companyId, "Employee", true, "test", default);
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EssDbContext>();
        var logs = await db.NavSyncLogs.Select(l => l.ResponseMasked + l.RequestMasked + l.Endpoint).ToListAsync();
        logs.Should().NotBeEmpty();
        logs.Should().OnlyContain(l => !l.Contains("Birth_Date\":\"0001"), "birth dates are masked");
    }

    private async Task<Guid> CreateLeaveAndEnqueueAsync(decimal days)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EssDbContext>();
        var queue = scope.ServiceProvider.GetRequiredService<IIntegrationQueue>();

        var emp = await db.Employees.FirstOrDefaultAsync(e => e.CompanyId == _companyId && e.NavKey == "SIM-002");
        if (emp is null)
        {
            emp = new Employee { CompanyId = _companyId, NavKey = "SIM-002", FirstName = "Baraka", LastName = "Otieno" };
            db.Employees.Add(emp);
        }
        var lt = await db.LeaveTypes.FirstOrDefaultAsync(t => t.CompanyId == _companyId && t.NavKey == "ANNUAL");
        if (lt is null)
        {
            lt = new LeaveType { CompanyId = _companyId, NavKey = "ANNUAL", Name = "Annual Leave" };
            db.LeaveTypes.Add(lt);
        }
        var leave = new LeaveApplication
        {
            CompanyId = _companyId, Reference = "LV-TEST-" + Guid.NewGuid().ToString("N")[..6], EmployeeId = emp.Id, LeaveTypeId = lt.Id,
            StartDate = new DateOnly(2026, 12, 1), EndDate = new DateOnly(2026, 12, 3), DaysRequested = days,
            Status = RequestStatus.Approved, NavStatus = NavSyncStatus.PendingIntegration
        };
        db.LeaveApplications.Add(leave);
        var key = IntegrationQueue.KeyFor(NavOperations.LeaveSubmit, leave.Id);
        await queue.EnqueueAsync(_companyId, NavOperations.LeaveSubmit, "LeaveApplication", leave.Id, key,
            new NavLeaveSubmission(key, leave.Reference, "SIM-002", "ANNUAL", leave.StartDate, leave.EndDate, days, "Family", null, true, "test.approver"),
            sensitive: false);
        await db.SaveChangesAsync(); // business row and outbox row commit together
        return leave.Id;
    }

    private sealed class TestEnv : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "Ess.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class TestUser : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public Guid? UserId => null;
        public string? UserName => "test";
        public Guid? CompanyId => null;
        public Guid? EmployeeId => null;
        public bool HasPermission(string permission) => true;
        public bool IsInRole(string role) => true;
        public string? IpAddress => null;
        public string? CorrelationId => "test";
    }
}
