using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ess.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "announcements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    body_html = table.Column<string>(type: "text", nullable: false),
                    publish_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    target_department_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_pinned = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_announcements", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "approval_delegations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    starts_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ends_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    document_type = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    reason = table.Column<string>(type: "text", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_approval_delegations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "approval_workflows",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    document_type = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    owner = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_approval_workflows", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    company_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    entity_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    changes_json = table.Column<string>(type: "text", nullable: true),
                    success = table.Column<bool>(type: "boolean", nullable: false),
                    ip_address = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "document_access_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    accessed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    action = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ip_address = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_access_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "document_categories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    employee_can_upload = table.Column<bool>(type: "boolean", nullable: false),
                    retention_months = table.Column<int>(type: "integer", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "employee_qualifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nav_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    institution = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_professional_certification = table.Column<bool>(type: "boolean", nullable: false),
                    awarded_on = table.Column<DateOnly>(type: "date", nullable: true),
                    expires_on = table.Column<DateOnly>(type: "date", nullable: true),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_synced_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    nav_version_token = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_qualifications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "expense_categories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    nav_expense_code = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    requires_receipt = table.Column<bool>(type: "boolean", nullable: false),
                    max_amount_per_line = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expense_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "integration_queue",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    payload_json = table.Column<string>(type: "text", nullable: false),
                    payload_encrypted = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    max_attempts = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    next_attempt_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    locked_until_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    locked_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    nav_document_no = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    last_error_category = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_integration_queue", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "locations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nav_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    last_synced_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    nav_version_token = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_locations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "nav_integration_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    platform = table.Column<int>(type: "integer", nullable: false),
                    server_host = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    service_instance = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    soap_base_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    o_data_base_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    api_base_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    environment_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    tenant_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    auth_method = table.Column<int>(type: "integer", nullable: false),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    domain = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    password_encrypted = table.Column<string>(type: "text", nullable: true),
                    client_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    client_secret_encrypted = table.Column<string>(type: "text", nullable: true),
                    timeout_seconds = table.Column<int>(type: "integer", nullable: false),
                    sync_interval_minutes = table.Column<int>(type: "integer", nullable: false),
                    validate_server_certificate = table.Column<bool>(type: "boolean", nullable: false),
                    max_concurrency = table.Column<int>(type: "integer", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    use_simulator = table.Column<bool>(type: "boolean", nullable: false),
                    last_health_check_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_health_check_ok = table.Column<bool>(type: "boolean", nullable: true),
                    last_health_check_message = table.Column<string>(type: "text", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_nav_integration_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "nav_sync_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    is_full_sync = table.Column<bool>(type: "boolean", nullable: false),
                    triggered_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    finished_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    records_read = table.Column<int>(type: "integer", nullable: false),
                    records_created = table.Column<int>(type: "integer", nullable: false),
                    records_updated = table.Column<int>(type: "integer", nullable: false),
                    records_deactivated = table.Column<int>(type: "integer", nullable: false),
                    records_failed = table.Column<int>(type: "integer", nullable: false),
                    watermark = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_nav_sync_jobs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "nav_sync_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sync_job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    queue_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    protocol = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    operation = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    http_method = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    endpoint = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    status_code = table.Column<int>(type: "integer", nullable: true),
                    success = table.Column<bool>(type: "boolean", nullable: false),
                    duration_ms = table.Column<long>(type: "bigint", nullable: false),
                    request_masked = table.Column<string>(type: "text", nullable: true),
                    response_masked = table.Column<string>(type: "text", nullable: true),
                    error = table.Column<string>(type: "text", nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    occurred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_nav_sync_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notification_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_code = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    channel = table.Column<int>(type: "integer", nullable: false),
                    subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    body = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payroll_periods_cache",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nav_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    pay_date = table.Column<DateOnly>(type: "date", nullable: true),
                    is_closed = table.Column<bool>(type: "boolean", nullable: false),
                    is_published = table.Column<bool>(type: "boolean", nullable: false),
                    published_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_synced_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    nav_version_token = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payroll_periods_cache", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "permissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    module = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_permissions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "positions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nav_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    department_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_synced_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    nav_version_token = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_positions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "public_holidays",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    nav_calendar_code = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_public_holidays", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "reference_sequences",
                columns: table => new
                {
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prefix = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    last_value = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reference_sequences", x => new { x.company_id, x.prefix, x.year });
                });

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    is_system = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "shifts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    end_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    grace_minutes = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shifts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "system_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: true),
                    key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    value = table.Column<string>(type: "text", nullable: false),
                    is_secret = table.Column<bool>(type: "boolean", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_system_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "training_programs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    provider = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    starts_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ends_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    venue = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    capacity = table.Column<int>(type: "integer", nullable: true),
                    cost_per_participant = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    requires_approval = table.Column<bool>(type: "boolean", nullable: false),
                    is_published = table.Column<bool>(type: "boolean", nullable: false),
                    nav_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_training_programs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "hr_request_types",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    form_schema_json = table.Column<string>(type: "text", nullable: true),
                    workflow_definition_id = table.Column<Guid>(type: "uuid", nullable: true),
                    nav_operation = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_sensitive = table.Column<bool>(type: "boolean", nullable: false),
                    sla_hours = table.Column<int>(type: "integer", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_request_types", x => x.id);
                    table.ForeignKey(
                        name: "fk_hr_request_types_approval_workflows_workflow_definition_id",
                        column: x => x.workflow_definition_id,
                        principalTable: "approval_workflows",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "leave_types",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nav_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    counts_working_days_only = table.Column<bool>(type: "boolean", nullable: false),
                    requires_attachment = table.Column<bool>(type: "boolean", nullable: false),
                    max_days_per_application = table.Column<int>(type: "integer", nullable: true),
                    min_notice_days = table.Column<int>(type: "integer", nullable: false),
                    allow_negative_balance = table.Column<bool>(type: "boolean", nullable: false),
                    allow_half_day = table.Column<bool>(type: "boolean", nullable: false),
                    eligible_gender = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    workflow_definition_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_synced_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    nav_version_token = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_types", x => x.id);
                    table.ForeignKey(
                        name: "fk_leave_types_approval_workflows_workflow_definition_id",
                        column: x => x.workflow_definition_id,
                        principalTable: "approval_workflows",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "workflow_instances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_version = table.Column<int>(type: "integer", nullable: false),
                    document_type = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_reference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    requester_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requester_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    current_sequence = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_instances", x => x.id);
                    table.ForeignKey(
                        name: "fk_workflow_instances_approval_workflows_workflow_definition_id",
                        column: x => x.workflow_definition_id,
                        principalTable: "approval_workflows",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "workflow_steps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    approver_type = table.Column<int>(type: "integer", nullable: false),
                    approver_role = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    approver_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    mode = table.Column<int>(type: "integer", nullable: false),
                    min_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    max_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    deadline_hours = table.Column<int>(type: "integer", nullable: true),
                    reminder_every_hours = table.Column<int>(type: "integer", nullable: true),
                    escalate_to_role = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    skip_if_approver_is_requester = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_steps", x => x.id);
                    table.ForeignKey(
                        name: "fk_workflow_steps_approval_workflows_workflow_definition_id",
                        column: x => x.workflow_definition_id,
                        principalTable: "approval_workflows",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "companies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    display_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    nav_company_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    nav_company_guid = table.Column<Guid>(type: "uuid", nullable: true),
                    integration_settings_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_companies", x => x.id);
                    table.ForeignKey(
                        name: "fk_companies_nav_integration_settings_integration_settings_id",
                        column: x => x.integration_settings_id,
                        principalTable: "nav_integration_settings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "nav_endpoint_mappings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    integration_settings_id = table.Column<Guid>(type: "uuid", nullable: false),
                    logical_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    protocol = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    service_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    method = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    field_map_json = table.Column<string>(type: "text", nullable: true),
                    modified_field = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_nav_endpoint_mappings", x => x.id);
                    table.ForeignKey(
                        name: "fk_nav_endpoint_mappings_nav_integration_settings_integration_",
                        column: x => x.integration_settings_id,
                        principalTable: "nav_integration_settings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "role_permissions",
                columns: table => new
                {
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_permissions", x => new { x.role_id, x.permission_id });
                    table.ForeignKey(
                        name: "fk_role_permissions_permissions_permission_id",
                        column: x => x.permission_id,
                        principalTable: "permissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_role_permissions_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "approval_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow_instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approval_task_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<int>(type: "integer", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    on_behalf_of_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    comment = table.Column<string>(type: "text", nullable: true),
                    occurred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ip_address = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_approval_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_approval_history_workflow_instances_workflow_instance_id",
                        column: x => x.workflow_instance_id,
                        principalTable: "workflow_instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "departments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nav_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    parent_department_id = table.Column<Guid>(type: "uuid", nullable: true),
                    head_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_synced_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    nav_version_token = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_departments", x => x.id);
                    table.ForeignKey(
                        name: "fk_departments_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_departments_departments_parent_department_id",
                        column: x => x.parent_department_id,
                        principalTable: "departments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    normalized_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    phone_number = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    mfa_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    mfa_secret_encrypted = table.Column<string>(type: "text", nullable: true),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false),
                    lockout_end_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    security_stamp = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    external_subject_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    external_provider = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    last_login_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    default_company_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                    table.ForeignKey(
                        name: "fk_users_companies_default_company_id",
                        column: x => x.default_company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "employees",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nav_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    first_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    middle_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    last_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    work_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    mobile_phone = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    department_id = table.Column<Guid>(type: "uuid", nullable: true),
                    position_id = table.Column<Guid>(type: "uuid", nullable: true),
                    location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    manager_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    employment_date = table.Column<DateOnly>(type: "date", nullable: true),
                    contract_end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    birth_date = table.Column<DateOnly>(type: "date", nullable: true),
                    photo_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_synced_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    nav_version_token = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employees", x => x.id);
                    table.ForeignKey(
                        name: "fk_employees_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_employees_departments_department_id",
                        column: x => x.department_id,
                        principalTable: "departments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_employees_employees_manager_employee_id",
                        column: x => x.manager_employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_employees_locations_location_id",
                        column: x => x.location_id,
                        principalTable: "locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_employees_positions_position_id",
                        column: x => x.position_id,
                        principalTable: "positions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "approval_tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow_instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow_step_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    assignee_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    on_behalf_of_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    due_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_reminder_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    acted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    comment = table.Column<string>(type: "text", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_approval_tasks", x => x.id);
                    table.ForeignKey(
                        name: "fk_approval_tasks_users_assignee_user_id",
                        column: x => x.assignee_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_approval_tasks_workflow_instances_workflow_instance_id",
                        column: x => x.workflow_instance_id,
                        principalTable: "workflow_instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_approval_tasks_workflow_steps_workflow_step_id",
                        column: x => x.workflow_step_id,
                        principalTable: "workflow_steps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_code = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    channel = table.Column<int>(type: "integer", nullable: false),
                    subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    body = table.Column<string>(type: "text", nullable: false),
                    link_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    sent_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    read_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    provider_message_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                    table.ForeignKey(
                        name: "fk_notifications_users_recipient_user_id",
                        column: x => x.recipient_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    family_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revoked_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    replaced_by_hash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    created_by_ip = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_refresh_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: true),
                    scope_department_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_roles", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_roles_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_user_roles_departments_scope_department_id",
                        column: x => x.scope_department_id,
                        principalTable: "departments",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_user_roles_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_roles_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "attendance_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_date = table.Column<DateOnly>(type: "date", nullable: false),
                    clock_in_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    clock_out_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    source = table.Column<int>(type: "integer", nullable: false),
                    source_device_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    external_ref = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    late_minutes = table.Column<int>(type: "integer", nullable: false),
                    worked_hours = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    shift_code = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    latitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    longitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attendance_records", x => x.id);
                    table.ForeignKey(
                        name: "fk_attendance_records_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "attendance_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    work_date = table.Column<DateOnly>(type: "date", nullable: false),
                    requested_clock_in_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    requested_clock_out_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    overtime_hours = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    reason = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    workflow_instance_id = table.Column<Guid>(type: "uuid", nullable: true),
                    nav_status = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attendance_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_attendance_requests_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "employee_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    visibility = table.Column<int>(type: "integer", nullable: false),
                    department_id = table.Column<Guid>(type: "uuid", nullable: true),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    current_version = table.Column<int>(type: "integer", nullable: false),
                    owner_entity_type = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    owner_entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_documents", x => x.id);
                    table.ForeignKey(
                        name: "fk_employee_documents_document_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "document_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_employee_documents_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "employee_mappings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nav_employee_no = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    verified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_mappings", x => x.id);
                    table.ForeignKey(
                        name: "fk_employee_mappings_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_employee_mappings_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_employee_mappings_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "employee_training",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    training_program_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    workflow_instance_id = table.Column<Guid>(type: "uuid", nullable: true),
                    feedback_rating = table.Column<int>(type: "integer", nullable: true),
                    feedback_comment = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    certificate_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_training", x => x.id);
                    table.ForeignKey(
                        name: "fk_employee_training_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_employee_training_training_programs_training_program_id",
                        column: x => x.training_program_id,
                        principalTable: "training_programs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "expense_claims",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    currency = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    project_code = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    cost_centre_code = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    workflow_instance_id = table.Column<Guid>(type: "uuid", nullable: true),
                    nav_status = table.Column<int>(type: "integer", nullable: false),
                    nav_document_no = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    payment_reference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    paid_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    submitted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expense_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_expense_claims_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hr_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    payload_json = table.Column<string>(type: "text", nullable: true),
                    payload_encrypted = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    workflow_instance_id = table.Column<Guid>(type: "uuid", nullable: true),
                    nav_status = table.Column<int>(type: "integer", nullable: false),
                    assigned_to_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    submitted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    resolved_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    resolution = table.Column<string>(type: "text", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_hr_requests_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_hr_requests_hr_request_types_request_type_id",
                        column: x => x.request_type_id,
                        principalTable: "hr_request_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leave_applications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    leave_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    start_half_day = table.Column<bool>(type: "boolean", nullable: false),
                    end_half_day = table.Column<bool>(type: "boolean", nullable: false),
                    days_requested = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    reliever_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    workflow_instance_id = table.Column<Guid>(type: "uuid", nullable: true),
                    nav_status = table.Column<int>(type: "integer", nullable: false),
                    nav_document_no = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    submitted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_applications", x => x.id);
                    table.ForeignKey(
                        name: "fk_leave_applications_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leave_applications_employees_reliever_employee_id",
                        column: x => x.reliever_employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_leave_applications_leave_types_leave_type_id",
                        column: x => x.leave_type_id,
                        principalTable: "leave_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leave_balances_cache",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    leave_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    leave_period_code = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    entitlement = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                    brought_forward = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                    taken = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                    pending_approval = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                    available = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                    retrieved_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_balances_cache", x => x.id);
                    table.ForeignKey(
                        name: "fk_leave_balances_cache_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_leave_balances_cache_leave_types_leave_type_id",
                        column: x => x.leave_type_id,
                        principalTable: "leave_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payslip_metadata",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payroll_period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nav_employee_no = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    nav_period_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    net_pay = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    currency = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    snapshot_encrypted = table.Column<string>(type: "text", nullable: true),
                    pdf_storage_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    pdf_sha256 = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    retrieved_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payslip_metadata", x => x.id);
                    table.ForeignKey(
                        name: "fk_payslip_metadata_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payslip_metadata_payroll_periods_cache_payroll_period_id",
                        column: x => x.payroll_period_id,
                        principalTable: "payroll_periods_cache",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "profile_change_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    section = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    changes_json = table.Column<string>(type: "text", nullable: false),
                    changes_encrypted = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    workflow_instance_id = table.Column<Guid>(type: "uuid", nullable: true),
                    nav_status = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_profile_change_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_profile_change_requests_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    file_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    content_type = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    sha256 = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    uploaded_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    uploaded_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    malware_scan_passed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_document_versions_employee_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "employee_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "expense_claim_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    expense_claim_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expense_date = table.Column<DateOnly>(type: "date", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    exchange_rate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    receipt_document_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expense_claim_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_expense_claim_lines_employee_documents_receipt_document_id",
                        column: x => x.receipt_document_id,
                        principalTable: "employee_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_expense_claim_lines_expense_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "expense_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_expense_claim_lines_expense_claims_expense_claim_id",
                        column: x => x.expense_claim_id,
                        principalTable: "expense_claims",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_announcements_company_id_publish_at_utc",
                table: "announcements",
                columns: new[] { "company_id", "publish_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_approval_delegations_from_user_id_starts_at_utc_ends_at_utc",
                table: "approval_delegations",
                columns: new[] { "from_user_id", "starts_at_utc", "ends_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_approval_history_workflow_instance_id_occurred_at_utc",
                table: "approval_history",
                columns: new[] { "workflow_instance_id", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_approval_tasks_assignee_user_id_status",
                table: "approval_tasks",
                columns: new[] { "assignee_user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_approval_tasks_status_due_at_utc",
                table: "approval_tasks",
                columns: new[] { "status", "due_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_approval_tasks_workflow_instance_id",
                table: "approval_tasks",
                column: "workflow_instance_id");

            migrationBuilder.CreateIndex(
                name: "ix_approval_tasks_workflow_step_id",
                table: "approval_tasks",
                column: "workflow_step_id");

            migrationBuilder.CreateIndex(
                name: "ix_approval_workflows_company_id_code",
                table: "approval_workflows",
                columns: new[] { "company_id", "code" },
                unique: true,
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_approval_workflows_company_id_document_type_is_active",
                table: "approval_workflows",
                columns: new[] { "company_id", "document_type", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_attendance_records_employee_id_work_date",
                table: "attendance_records",
                columns: new[] { "employee_id", "work_date" });

            migrationBuilder.CreateIndex(
                name: "ix_attendance_records_source_external_ref",
                table: "attendance_records",
                columns: new[] { "source", "external_ref" },
                unique: true,
                filter: "external_ref IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_requests_company_id_reference",
                table: "attendance_requests",
                columns: new[] { "company_id", "reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_attendance_requests_employee_id",
                table: "attendance_requests",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_action",
                table: "audit_logs",
                column: "action");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_entity_type_entity_id",
                table: "audit_logs",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_occurred_at_utc",
                table: "audit_logs",
                column: "occurred_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_user_id_occurred_at_utc",
                table: "audit_logs",
                columns: new[] { "user_id", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_companies_code",
                table: "companies",
                column: "code",
                unique: true,
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_companies_integration_settings_id",
                table: "companies",
                column: "integration_settings_id");

            migrationBuilder.CreateIndex(
                name: "ix_departments_company_id_nav_key",
                table: "departments",
                columns: new[] { "company_id", "nav_key" },
                unique: true,
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_departments_parent_department_id",
                table: "departments",
                column: "parent_department_id");

            migrationBuilder.CreateIndex(
                name: "ix_document_access_logs_document_id_accessed_at_utc",
                table: "document_access_logs",
                columns: new[] { "document_id", "accessed_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_document_categories_company_id_code",
                table: "document_categories",
                columns: new[] { "company_id", "code" },
                unique: true,
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_document_versions_document_id_version",
                table: "document_versions",
                columns: new[] { "document_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_employee_documents_category_id",
                table: "employee_documents",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_documents_company_id_employee_id_category_id",
                table: "employee_documents",
                columns: new[] { "company_id", "employee_id", "category_id" });

            migrationBuilder.CreateIndex(
                name: "ix_employee_documents_employee_id",
                table: "employee_documents",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_documents_expiry_date",
                table: "employee_documents",
                column: "expiry_date");

            migrationBuilder.CreateIndex(
                name: "ix_employee_documents_owner_entity_type_owner_entity_id",
                table: "employee_documents",
                columns: new[] { "owner_entity_type", "owner_entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_employee_mappings_company_id_nav_employee_no",
                table: "employee_mappings",
                columns: new[] { "company_id", "nav_employee_no" },
                unique: true,
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_employee_mappings_employee_id",
                table: "employee_mappings",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_mappings_user_id_company_id",
                table: "employee_mappings",
                columns: new[] { "user_id", "company_id" },
                unique: true,
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_employee_qualifications_employee_id_nav_key",
                table: "employee_qualifications",
                columns: new[] { "employee_id", "nav_key" },
                unique: true,
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_employee_qualifications_expires_on",
                table: "employee_qualifications",
                column: "expires_on");

            migrationBuilder.CreateIndex(
                name: "ix_employee_training_company_id_reference",
                table: "employee_training",
                columns: new[] { "company_id", "reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_employee_training_employee_id_training_program_id",
                table: "employee_training",
                columns: new[] { "employee_id", "training_program_id" },
                unique: true,
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_employee_training_training_program_id",
                table: "employee_training",
                column: "training_program_id");

            migrationBuilder.CreateIndex(
                name: "ix_employees_company_id_department_id_status",
                table: "employees",
                columns: new[] { "company_id", "department_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_employees_company_id_last_name_first_name",
                table: "employees",
                columns: new[] { "company_id", "last_name", "first_name" });

            migrationBuilder.CreateIndex(
                name: "ix_employees_company_id_nav_key",
                table: "employees",
                columns: new[] { "company_id", "nav_key" },
                unique: true,
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_employees_department_id",
                table: "employees",
                column: "department_id");

            migrationBuilder.CreateIndex(
                name: "ix_employees_location_id",
                table: "employees",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_employees_manager_employee_id",
                table: "employees",
                column: "manager_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_employees_position_id",
                table: "employees",
                column: "position_id");

            migrationBuilder.CreateIndex(
                name: "ix_expense_categories_company_id_code",
                table: "expense_categories",
                columns: new[] { "company_id", "code" },
                unique: true,
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_expense_claim_lines_category_id",
                table: "expense_claim_lines",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_expense_claim_lines_expense_claim_id_line_no",
                table: "expense_claim_lines",
                columns: new[] { "expense_claim_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_expense_claim_lines_receipt_document_id",
                table: "expense_claim_lines",
                column: "receipt_document_id");

            migrationBuilder.CreateIndex(
                name: "ix_expense_claims_company_id_reference",
                table: "expense_claims",
                columns: new[] { "company_id", "reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_expense_claims_employee_id_status",
                table: "expense_claims",
                columns: new[] { "employee_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_expense_claims_nav_status",
                table: "expense_claims",
                column: "nav_status");

            migrationBuilder.CreateIndex(
                name: "ix_hr_request_types_company_id_code",
                table: "hr_request_types",
                columns: new[] { "company_id", "code" },
                unique: true,
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_hr_request_types_workflow_definition_id",
                table: "hr_request_types",
                column: "workflow_definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_hr_requests_assigned_to_user_id_status",
                table: "hr_requests",
                columns: new[] { "assigned_to_user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_hr_requests_company_id_reference",
                table: "hr_requests",
                columns: new[] { "company_id", "reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hr_requests_employee_id_status",
                table: "hr_requests",
                columns: new[] { "employee_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_hr_requests_request_type_id",
                table: "hr_requests",
                column: "request_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_integration_queue_company_id_operation_idempotency_key",
                table: "integration_queue",
                columns: new[] { "company_id", "operation", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_integration_queue_entity_type_entity_id",
                table: "integration_queue",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_integration_queue_status_next_attempt_at_utc_priority",
                table: "integration_queue",
                columns: new[] { "status", "next_attempt_at_utc", "priority" });

            migrationBuilder.CreateIndex(
                name: "ix_leave_applications_company_id_reference",
                table: "leave_applications",
                columns: new[] { "company_id", "reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_leave_applications_company_id_status",
                table: "leave_applications",
                columns: new[] { "company_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_leave_applications_employee_id_start_date_end_date",
                table: "leave_applications",
                columns: new[] { "employee_id", "start_date", "end_date" });

            migrationBuilder.CreateIndex(
                name: "ix_leave_applications_leave_type_id",
                table: "leave_applications",
                column: "leave_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_applications_nav_status",
                table: "leave_applications",
                column: "nav_status");

            migrationBuilder.CreateIndex(
                name: "ix_leave_applications_reliever_employee_id",
                table: "leave_applications",
                column: "reliever_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_balances_cache_employee_id_leave_type_id_leave_period",
                table: "leave_balances_cache",
                columns: new[] { "employee_id", "leave_type_id", "leave_period_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_leave_balances_cache_leave_type_id",
                table: "leave_balances_cache",
                column: "leave_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_types_company_id_nav_key",
                table: "leave_types",
                columns: new[] { "company_id", "nav_key" },
                unique: true,
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_leave_types_workflow_definition_id",
                table: "leave_types",
                column: "workflow_definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_locations_company_id_nav_key",
                table: "locations",
                columns: new[] { "company_id", "nav_key" },
                unique: true,
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_nav_endpoint_mappings_integration_settings_id_logical_name",
                table: "nav_endpoint_mappings",
                columns: new[] { "integration_settings_id", "logical_name" },
                unique: true,
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_nav_integration_settings_name",
                table: "nav_integration_settings",
                column: "name",
                unique: true,
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_nav_sync_jobs_company_id_entity_name_started_at_utc",
                table: "nav_sync_jobs",
                columns: new[] { "company_id", "entity_name", "started_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_nav_sync_jobs_status",
                table: "nav_sync_jobs",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_nav_sync_logs_correlation_id",
                table: "nav_sync_logs",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "ix_nav_sync_logs_occurred_at_utc",
                table: "nav_sync_logs",
                column: "occurred_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_nav_sync_logs_queue_item_id",
                table: "nav_sync_logs",
                column: "queue_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_nav_sync_logs_success_occurred_at_utc",
                table: "nav_sync_logs",
                columns: new[] { "success", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_nav_sync_logs_sync_job_id",
                table: "nav_sync_logs",
                column: "sync_job_id");

            migrationBuilder.CreateIndex(
                name: "ix_notification_templates_company_id_event_code_channel",
                table: "notification_templates",
                columns: new[] { "company_id", "event_code", "channel" },
                unique: true,
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_recipient_user_id_channel_read_at_utc",
                table: "notifications",
                columns: new[] { "recipient_user_id", "channel", "read_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_status_next_attempt_at_utc",
                table: "notifications",
                columns: new[] { "status", "next_attempt_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_payroll_periods_cache_company_id_nav_key",
                table: "payroll_periods_cache",
                columns: new[] { "company_id", "nav_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payroll_periods_cache_company_id_start_date",
                table: "payroll_periods_cache",
                columns: new[] { "company_id", "start_date" });

            migrationBuilder.CreateIndex(
                name: "ix_payslip_metadata_employee_id_payroll_period_id",
                table: "payslip_metadata",
                columns: new[] { "employee_id", "payroll_period_id" },
                unique: true,
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_payslip_metadata_payroll_period_id",
                table: "payslip_metadata",
                column: "payroll_period_id");

            migrationBuilder.CreateIndex(
                name: "ix_permissions_code",
                table: "permissions",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_positions_company_id_nav_key",
                table: "positions",
                columns: new[] { "company_id", "nav_key" },
                unique: true,
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_profile_change_requests_company_id_reference",
                table: "profile_change_requests",
                columns: new[] { "company_id", "reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_profile_change_requests_employee_id_status",
                table: "profile_change_requests",
                columns: new[] { "employee_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_public_holidays_company_id_date",
                table: "public_holidays",
                columns: new[] { "company_id", "date" },
                unique: true,
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_family_id",
                table: "refresh_tokens",
                column: "family_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_hash",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_user_id_expires_at_utc",
                table: "refresh_tokens",
                columns: new[] { "user_id", "expires_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_role_permissions_permission_id",
                table: "role_permissions",
                column: "permission_id");

            migrationBuilder.CreateIndex(
                name: "ix_roles_normalized_name",
                table: "roles",
                column: "normalized_name",
                unique: true,
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_shifts_company_id_code",
                table: "shifts",
                columns: new[] { "company_id", "code" },
                unique: true,
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_system_settings_company_id_key",
                table: "system_settings",
                columns: new[] { "company_id", "key" },
                unique: true,
                filter: "NOT is_deleted")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_training_programs_company_id_code",
                table: "training_programs",
                columns: new[] { "company_id", "code" },
                unique: true,
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_company_id",
                table: "user_roles",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_role_id",
                table: "user_roles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_scope_department_id",
                table: "user_roles",
                column: "scope_department_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_user_id_role_id_company_id_scope_department_id",
                table: "user_roles",
                columns: new[] { "user_id", "role_id", "company_id", "scope_department_id" },
                unique: true,
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_users_default_company_id",
                table: "users",
                column: "default_company_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_external_provider_external_subject_id",
                table: "users",
                columns: new[] { "external_provider", "external_subject_id" });

            migrationBuilder.CreateIndex(
                name: "ix_users_normalized_email",
                table: "users",
                column: "normalized_email",
                unique: true,
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_users_normalized_user_name",
                table: "users",
                column: "normalized_user_name",
                unique: true,
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_instances_document_type_document_id",
                table: "workflow_instances",
                columns: new[] { "document_type", "document_id" });

            migrationBuilder.CreateIndex(
                name: "ix_workflow_instances_requester_user_id_status",
                table: "workflow_instances",
                columns: new[] { "requester_user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_workflow_instances_workflow_definition_id",
                table: "workflow_instances",
                column: "workflow_definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_steps_workflow_definition_id_sequence",
                table: "workflow_steps",
                columns: new[] { "workflow_definition_id", "sequence" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "announcements");

            migrationBuilder.DropTable(
                name: "approval_delegations");

            migrationBuilder.DropTable(
                name: "approval_history");

            migrationBuilder.DropTable(
                name: "approval_tasks");

            migrationBuilder.DropTable(
                name: "attendance_records");

            migrationBuilder.DropTable(
                name: "attendance_requests");

            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "document_access_logs");

            migrationBuilder.DropTable(
                name: "document_versions");

            migrationBuilder.DropTable(
                name: "employee_mappings");

            migrationBuilder.DropTable(
                name: "employee_qualifications");

            migrationBuilder.DropTable(
                name: "employee_training");

            migrationBuilder.DropTable(
                name: "expense_claim_lines");

            migrationBuilder.DropTable(
                name: "hr_requests");

            migrationBuilder.DropTable(
                name: "integration_queue");

            migrationBuilder.DropTable(
                name: "leave_applications");

            migrationBuilder.DropTable(
                name: "leave_balances_cache");

            migrationBuilder.DropTable(
                name: "nav_endpoint_mappings");

            migrationBuilder.DropTable(
                name: "nav_sync_jobs");

            migrationBuilder.DropTable(
                name: "nav_sync_logs");

            migrationBuilder.DropTable(
                name: "notification_templates");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "payslip_metadata");

            migrationBuilder.DropTable(
                name: "profile_change_requests");

            migrationBuilder.DropTable(
                name: "public_holidays");

            migrationBuilder.DropTable(
                name: "reference_sequences");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "role_permissions");

            migrationBuilder.DropTable(
                name: "shifts");

            migrationBuilder.DropTable(
                name: "system_settings");

            migrationBuilder.DropTable(
                name: "user_roles");

            migrationBuilder.DropTable(
                name: "workflow_instances");

            migrationBuilder.DropTable(
                name: "workflow_steps");

            migrationBuilder.DropTable(
                name: "training_programs");

            migrationBuilder.DropTable(
                name: "employee_documents");

            migrationBuilder.DropTable(
                name: "expense_categories");

            migrationBuilder.DropTable(
                name: "expense_claims");

            migrationBuilder.DropTable(
                name: "hr_request_types");

            migrationBuilder.DropTable(
                name: "leave_types");

            migrationBuilder.DropTable(
                name: "payroll_periods_cache");

            migrationBuilder.DropTable(
                name: "permissions");

            migrationBuilder.DropTable(
                name: "roles");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "document_categories");

            migrationBuilder.DropTable(
                name: "employees");

            migrationBuilder.DropTable(
                name: "approval_workflows");

            migrationBuilder.DropTable(
                name: "departments");

            migrationBuilder.DropTable(
                name: "locations");

            migrationBuilder.DropTable(
                name: "positions");

            migrationBuilder.DropTable(
                name: "companies");

            migrationBuilder.DropTable(
                name: "nav_integration_settings");
        }
    }
}
