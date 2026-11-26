using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ess.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase3Workflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "link_url",
                table: "workflow_instances",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "summary",
                table: "workflow_instances",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "escalated_at_utc",
                table: "approval_tasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "escalated_from_task_id",
                table: "approval_tasks",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "link_url",
                table: "workflow_instances");

            migrationBuilder.DropColumn(
                name: "summary",
                table: "workflow_instances");

            migrationBuilder.DropColumn(
                name: "escalated_at_utc",
                table: "approval_tasks");

            migrationBuilder.DropColumn(
                name: "escalated_from_task_id",
                table: "approval_tasks");
        }
    }
}
