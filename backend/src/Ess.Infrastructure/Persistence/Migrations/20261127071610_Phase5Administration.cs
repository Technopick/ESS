using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ess.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase5Administration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "gender",
                table: "employees",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "termination_date",
                table: "employees",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "notified_at_utc",
                table: "announcements",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "notify_by_email",
                table: "announcements",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "notify_on_publish",
                table: "announcements",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Power BI readiness: reporting views (kept in Sql/ReportingViews.sql).
            migrationBuilder.Sql(@"-- Read-only reporting views for Power BI and other BI tools (spec §13).
-- They expose only what reports need: no birth dates, contact details, bank data, pay, request contents or free-text reasons.
-- Grant BI accounts access to this schema only (see deploy/sql/reporting-role.sql), never to the application tables.
CREATE SCHEMA IF NOT EXISTS reporting;

CREATE OR REPLACE VIEW reporting.employees AS
SELECT e.id AS employee_id, e.company_id, e.nav_key AS employee_no, e.department_id, d.name AS department, e.position_id, p.title AS position,
       e.location_id, l.name AS location, e.manager_employee_id,
       CASE e.status WHEN 0 THEN 'Active' WHEN 1 THEN 'Inactive' WHEN 2 THEN 'Terminated' WHEN 3 THEN 'On probation' WHEN 4 THEN 'Suspended' END AS employment_status,
       e.employment_date, e.contract_end_date, e.termination_date, e.gender, e.last_synced_at_utc
FROM employees e
LEFT JOIN departments d ON d.id = e.department_id
LEFT JOIN positions p ON p.id = e.position_id
LEFT JOIN locations l ON l.id = e.location_id
WHERE NOT e.is_deleted;

CREATE OR REPLACE VIEW reporting.departments AS
SELECT id AS department_id, company_id, nav_key AS code, name, parent_department_id, head_employee_id
FROM departments WHERE NOT is_deleted;

CREATE OR REPLACE VIEW reporting.leave_applications AS
SELECT a.id AS leave_application_id, a.company_id, a.reference, a.employee_id, t.nav_key AS leave_type_code, t.name AS leave_type,
       a.start_date, a.end_date, a.days_requested,
       CASE a.status WHEN 0 THEN 'Draft' WHEN 1 THEN 'Submitted' WHEN 2 THEN 'In approval' WHEN 3 THEN 'Returned' WHEN 4 THEN 'Approved'
                     WHEN 5 THEN 'Rejected' WHEN 6 THEN 'Cancelled' WHEN 7 THEN 'Withdrawn' WHEN 8 THEN 'Completed' END AS status,
       CASE a.nav_status WHEN 0 THEN 'Not required' WHEN 1 THEN 'Pending' WHEN 2 THEN 'In progress' WHEN 3 THEN 'Posted' WHEN 4 THEN 'Failed' WHEN 5 THEN 'Reconciling' END AS nav_status,
       a.nav_document_no, a.submitted_at_utc
FROM leave_applications a
JOIN leave_types t ON t.id = a.leave_type_id
WHERE NOT a.is_deleted AND a.status <> 0;

CREATE OR REPLACE VIEW reporting.leave_balances AS
SELECT b.company_id, b.employee_id, t.nav_key AS leave_type_code, t.name AS leave_type, b.leave_period_code,
       b.entitlement, b.brought_forward, b.taken, b.pending_approval, b.available, b.retrieved_at_utc
FROM leave_balances_cache b
JOIN leave_types t ON t.id = b.leave_type_id;

CREATE OR REPLACE VIEW reporting.expense_claims AS
SELECT c.id AS expense_claim_id, c.company_id, c.reference, c.employee_id, c.currency, c.total_amount, c.project_code, c.cost_centre_code,
       CASE c.status WHEN 0 THEN 'Draft' WHEN 1 THEN 'Submitted' WHEN 2 THEN 'In approval' WHEN 3 THEN 'Returned' WHEN 4 THEN 'Approved'
                     WHEN 5 THEN 'Rejected' WHEN 6 THEN 'Cancelled' WHEN 7 THEN 'Withdrawn' WHEN 8 THEN 'Completed' END AS status,
       CASE c.nav_status WHEN 0 THEN 'Not required' WHEN 1 THEN 'Pending' WHEN 2 THEN 'In progress' WHEN 3 THEN 'Posted' WHEN 4 THEN 'Failed' WHEN 5 THEN 'Reconciling' END AS nav_status,
       c.nav_document_no, c.submitted_at_utc, c.paid_at_utc
FROM expense_claims c
WHERE NOT c.is_deleted AND c.status <> 0;

CREATE OR REPLACE VIEW reporting.hr_requests AS
SELECT r.id AS hr_request_id, r.company_id, r.reference, r.employee_id, t.code AS request_type_code, t.name AS request_type, t.sla_hours,
       CASE r.status WHEN 0 THEN 'Draft' WHEN 1 THEN 'Submitted' WHEN 2 THEN 'In approval' WHEN 3 THEN 'Returned' WHEN 4 THEN 'Approved'
                     WHEN 5 THEN 'Rejected' WHEN 6 THEN 'Cancelled' WHEN 7 THEN 'Withdrawn' WHEN 8 THEN 'Completed' END AS status,
       r.submitted_at_utc, r.resolved_at_utc
FROM hr_requests r
JOIN hr_request_types t ON t.id = r.request_type_id
WHERE NOT r.is_deleted;

CREATE OR REPLACE VIEW reporting.training_participation AS
SELECT et.id AS participation_id, et.company_id, et.employee_id, p.code AS programme_code, p.title AS programme, p.provider, p.starts_at_utc, p.ends_at_utc,
       CASE et.status WHEN 0 THEN 'Applied' WHEN 1 THEN 'Approved' WHEN 2 THEN 'Rejected' WHEN 3 THEN 'Registered' WHEN 4 THEN 'Attended'
                      WHEN 5 THEN 'Completed' WHEN 6 THEN 'No show' WHEN 7 THEN 'Cancelled' END AS status,
       et.feedback_rating, p.cost_per_participant
FROM employee_training et
JOIN training_programs p ON p.id = et.training_program_id
WHERE NOT et.is_deleted;

CREATE OR REPLACE VIEW reporting.attendance_daily AS
SELECT a.company_id, a.employee_id, a.work_date, a.clock_in_utc, a.clock_out_utc, a.worked_hours, a.late_minutes,
       CASE a.source WHEN 0 THEN 'Portal' WHEN 1 THEN 'Biometric' WHEN 2 THEN 'NAV' WHEN 3 THEN 'Import' END AS source
FROM attendance_records a
WHERE NOT a.is_deleted;

CREATE OR REPLACE VIEW reporting.approvals AS
SELECT t.id AS approval_task_id, i.company_id, i.document_type, i.document_reference, i.requester_employee_id, s.name AS step, t.sequence,
       CASE t.status WHEN 0 THEN 'Pending' WHEN 1 THEN 'Approved' WHEN 2 THEN 'Rejected' WHEN 3 THEN 'Returned' WHEN 4 THEN 'Skipped' WHEN 5 THEN 'Superseded' END AS status,
       t.created_at_utc AS assigned_at_utc, t.due_at_utc, t.acted_at_utc, (t.escalated_from_task_id IS NOT NULL) AS escalated
FROM approval_tasks t
JOIN workflow_instances i ON i.id = t.workflow_instance_id
JOIN workflow_steps s ON s.id = t.workflow_step_id
WHERE NOT t.is_deleted;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP SCHEMA IF EXISTS reporting CASCADE;");

            migrationBuilder.DropColumn(
                name: "gender",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "termination_date",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "notified_at_utc",
                table: "announcements");

            migrationBuilder.DropColumn(
                name: "notify_by_email",
                table: "announcements");

            migrationBuilder.DropColumn(
                name: "notify_on_publish",
                table: "announcements");
        }
    }
}
