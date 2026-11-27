using System.Text;
using System.Threading.RateLimiting;
using Asp.Versioning;
using Ess.Api.Hubs;
using Ess.Api.Infrastructure;
using Ess.Application.Abstractions;
using Ess.Infrastructure;
using Ess.Infrastructure.Persistence;
using Ess.Integration.Nav;
using Ess.Integration.Nav.Queue;
using Ess.Integration.Nav.Sync;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    var config = builder.Configuration;
    var env = builder.Environment;

    builder.Host.UseSerilog((ctx, sp, lc) => lc
        .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(sp)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "Ess.Api"));

    // Windows Service hosting for IIS-less installs (see deploy/windows).
    builder.Host.UseWindowsService();

    // ---------- Core services ----------
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
    builder.Services.AddScoped<Ess.Api.Auth.TokenService>();
    builder.Services.AddScoped<Ess.Api.Auth.PasswordResetService>();
    builder.Services.AddScoped<Ess.Api.Services.EmployeeContext>();
    builder.Services.AddScoped<Ess.Api.Services.LeaveBalanceService>();
    builder.Services.AddScoped<Ess.Api.Services.LeaveService>();
    builder.Services.AddScoped<Ess.Api.Services.PayrollService>();
    builder.Services.AddScoped<Ess.Api.Services.DocumentService>();
    builder.Services.AddScoped<Ess.Api.Services.ExpenseService>();
    builder.Services.AddScoped<Ess.Api.Reporting.ReportRunner>();
    builder.Services.AddScoped<Ess.Api.Services.ManagementDashboardService>();
    builder.Services.AddScoped<Ess.Api.Services.AnnouncementService>();
    builder.Services.AddScoped<Ess.Api.Services.HrAlertService>();
    Ess.Api.Reporting.PortalFontResolver.ConfiguredRegularPath = config["Reporting:FontPath"];
    Ess.Api.Reporting.PortalFontResolver.ConfiguredBoldPath = config["Reporting:BoldFontPath"];
    builder.Services.AddScoped<Ess.Application.Workflow.IWorkflowDocumentHandler, Ess.Api.Workflow.ExpenseWorkflowHandler>();
    builder.Services.AddScoped<Ess.Application.Workflow.IWorkflowDocumentHandler, Ess.Api.Workflow.HrRequestWorkflowHandler>();
    builder.Services.AddScoped<Ess.Application.Workflow.IWorkflowDocumentHandler, Ess.Api.Workflow.AttendanceWorkflowHandler>();
    builder.Services.AddScoped<Ess.Application.Workflow.IWorkflowDocumentHandler, Ess.Api.Workflow.TrainingWorkflowHandler>();
    builder.Services.AddScoped<Ess.Application.Workflow.IWorkflowDocumentHandler, Ess.Api.Workflow.LeaveWorkflowHandler>();
    builder.Services.AddScoped<Ess.Application.Workflow.IWorkflowDocumentHandler, Ess.Api.Workflow.ProfileChangeWorkflowHandler>();
    builder.Services.AddSingleton<Microsoft.AspNetCore.SignalR.IUserIdProvider, Ess.Api.Hubs.SubUserIdProvider>();
    if (config.GetValue("Hosting:RunBackgroundWorkers", true) && config.GetValue("Notifications:DispatcherEnabled", true))
        builder.Services.AddHostedService<Ess.Api.Notifications.NotificationDispatcher>();
    builder.Services.AddScoped<Microsoft.AspNetCore.Identity.IPasswordHasher<Ess.Domain.Identity.User>, Microsoft.AspNetCore.Identity.PasswordHasher<Ess.Domain.Identity.User>>();
    builder.Services.AddEssInfrastructure(config);
    var isWorkerNode = config.GetValue("Hosting:RunBackgroundWorkers", true);
    builder.Services.AddNavIntegrationEngine(config, env, runWorker: isWorkerNode);

    // ---------- API ----------
    builder.Services.AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();
    builder.Services.AddApiVersioning(o =>
    {
        o.DefaultApiVersion = new ApiVersion(1, 0);
        o.AssumeDefaultVersionWhenUnspecified = true;
        o.ReportApiVersions = true;
    }).AddMvc().AddApiExplorer(o =>
    {
        o.GroupNameFormat = "'v'VVV";
        o.SubstituteApiVersionInUrl = true;
    });
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(o =>
    {
        o.SwaggerDoc("v1", new OpenApiInfo { Title = "ESS Portal API", Version = "v1", Description = "Employee Self-Service portal for Microsoft Dynamics NAV / Business Central" });
        o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", In = ParameterLocation.Header
        });
        o.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }, Array.Empty<string>() }
        });
        o.CustomSchemaIds(t => t.FullName?.Replace("+", "."));
    });
    builder.Services.AddSignalR();

    // ---------- Security ----------
    var jwtKey = config["Jwt:SigningKey"];
    if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32)
    {
        if (!env.IsDevelopment()) throw new InvalidOperationException("Jwt:SigningKey must be set (at least 32 characters) outside Development.");
        jwtKey = "development-only-signing-key-change-me-0123456789";
    }
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = config["Jwt:Issuer"] ?? "ess-portal",
            ValidAudience = config["Jwt:Audience"] ?? "ess-portal",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "name", RoleClaimType = "role"
        };
        // SignalR sends the token in the query string for WebSockets.
        o.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var token = ctx.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs")) ctx.Token = token;
                return Task.CompletedTask;
            }
        };
    });
    builder.Services.AddAuthorization(o => o.AddPermissionPolicies());
    builder.Services.AddRateLimiter(o =>
    {
        o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = config.GetValue("RateLimiting:AuthPerMinute", 10), Window = TimeSpan.FromMinutes(1) }));
        o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx => RateLimitPartition.GetFixedWindowLimiter(
            ctx.User.Identity?.Name ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = config.GetValue("RateLimiting:PerUserPerMinute", 300), Window = TimeSpan.FromMinutes(1) }));
    });
    builder.Services.AddCors(o => o.AddPolicy("frontend", p => p
        .WithOrigins(config.GetSection("Cors:Origins").Get<string[]>() ?? Array.Empty<string>())
        .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

    // ---------- Background jobs ----------
    var cs = config.GetConnectionString("Ess")!;
    builder.Services.AddHangfire(h => h
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UsePostgreSqlStorage(o => o.UseNpgsqlConnection(cs), new PostgreSqlStorageOptions { SchemaName = "hangfire" }));
    if (isWorkerNode) builder.Services.AddHangfireServer(o => o.WorkerCount = config.GetValue("Hosting:HangfireWorkers", 5));

    // ---------- Health ----------
    builder.Services.AddHealthChecks().AddNpgSql(cs, name: "postgres", tags: new[] { "ready" });

    var app = builder.Build();

    // ---------- Database ----------
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<EssDbContext>();
        if (config.GetValue("Database:MigrateOnStartup", env.IsDevelopment()))
            await db.Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync();
        if (config.GetValue("DemoData:Enabled", false) && !env.IsProduction())
            await Ess.Integration.Nav.Simulator.SimulatorSetup.EnsureAsync(db);
        await Ess.Api.Auth.BootstrapAdmin.EnsureAsync(scope.ServiceProvider, config, app.Logger);
    }

    // ---------- Pipeline ----------
    app.UseExceptionHandler();
    app.UseSerilogRequestLogging(o => o.GetLevel = (ctx, _, ex) =>
        ex is not null || ctx.Response.StatusCode >= 500 ? Serilog.Events.LogEventLevel.Error : Serilog.Events.LogEventLevel.Information);
    if (!env.IsDevelopment()) app.UseHsts();
    app.UseHttpsRedirection();
    app.Use(async (ctx, next) =>
    {
        var h = ctx.Response.Headers;
        h["X-Content-Type-Options"] = "nosniff";
        h["X-Frame-Options"] = "DENY";
        h["Referrer-Policy"] = "no-referrer";
        h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(self)";
        if (!ctx.Request.Path.StartsWithSegments("/swagger") && !ctx.Request.Path.StartsWithSegments("/admin/jobs"))
            h["Content-Security-Policy"] = "default-src 'self'; img-src 'self' data: blob:; style-src 'self' 'unsafe-inline'; connect-src 'self' wss:; frame-ancestors 'none'";
        await next();
    });
    // Single-site hosting (Windows/IIS): when the built frontend is in wwwroot the API serves it too, so IIS needs one site.
    // Unknown /api, /hubs, /health and /admin/jobs paths still return 404 rather than the app shell.
    var serveSpa = File.Exists(Path.Combine(env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot"), "index.html"));
    if (serveSpa)
    {
        app.UseDefaultFiles();
        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = c =>
            {
                var path = c.Context.Request.Path.Value ?? "";
                c.Context.Response.Headers.CacheControl = path.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase)
                    ? "public, max-age=31536000, immutable"
                    : "no-cache";
            }
        });
    }
    // Routing after static files, so files are served before the app-shell fallback endpoint is chosen.
    app.UseRouting();
    if (config.GetValue("Swagger:Enabled", !env.IsProduction()))
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }
    app.UseCors("frontend");
    app.UseAuthentication();
    app.UseRateLimiter();
    app.UseMiddleware<Ess.Api.Auth.AccountGateMiddleware>();
    app.UseAuthorization();

    app.MapControllers();
    app.MapHub<NotificationsHub>("/hubs/notifications");
    if (serveSpa)
        app.MapFallbackToFile("{*path:regex(^(?!api/|hubs/|health/|admin/jobs|swagger).*$)}", "index.html",
            new StaticFileOptions { OnPrepareResponse = c => c.Context.Response.Headers.CacheControl = "no-cache" }).AllowAnonymous();

    app.MapHealthChecks("/health/live", new() { Predicate = _ => false }).AllowAnonymous();
    app.MapHealthChecks("/health/ready", new() { Predicate = c => c.Tags.Contains("ready") }).AllowAnonymous();
    app.MapHangfireDashboard("/admin/jobs", new DashboardOptions { Authorization = new[] { new HangfireDashboardAuthorization() } });

    if (isWorkerNode)
    {
        var jobs = app.Services.GetRequiredService<IRecurringJobManager>();
        var syncCron = config["NavIntegration:SyncCron"] ?? "*/15 * * * *";
        jobs.AddOrUpdate<NavSyncService>("nav-sync-incremental", s => s.SyncAllCompaniesAsync(false, "schedule", CancellationToken.None), syncCron);
        jobs.AddOrUpdate<NavSyncService>("nav-sync-full", s => s.SyncAllCompaniesAsync(true, "schedule", CancellationToken.None), config["NavIntegration:FullSyncCron"] ?? "30 1 * * *");
        jobs.AddOrUpdate<ReconciliationService>("nav-reconciliation", s => s.RunAsync(CancellationToken.None), "*/5 * * * *");
        jobs.AddOrUpdate<Ess.Application.Workflow.IWorkflowEngine>("workflow-deadlines", e => e.ProcessDeadlinesAsync(CancellationToken.None), "*/15 * * * *");
        jobs.AddOrUpdate<Ess.Api.Services.DocumentService>("documents-expiry", s => s.NotifyExpiringAsync(CancellationToken.None), "0 6 * * *");
        jobs.AddOrUpdate<Ess.Api.Services.ExpenseService>("expense-nav-status", s => s.MirrorNavStatusesAsync(CancellationToken.None), "*/30 * * * *");
        jobs.AddOrUpdate<Ess.Api.Services.LeaveService>("leave-nav-status", s => s.MirrorNavStatusesAsync(CancellationToken.None), "*/10 * * * *");
        jobs.AddOrUpdate<Ess.Api.Services.AnnouncementService>("announcements-publish", s => s.NotifyDueAsync(CancellationToken.None), "*/10 * * * *");
        jobs.AddOrUpdate<Ess.Api.Services.HrAlertService>("contracts-expiry", s => s.NotifyContractExpiriesAsync(CancellationToken.None), "15 6 * * *");
    }

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "ESS API terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program;
