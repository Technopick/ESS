using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ess.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase6Performance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_leave_applications_company_id_submitted_at_utc",
                table: "leave_applications",
                columns: new[] { "company_id", "submitted_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_expense_claims_company_id_submitted_at_utc",
                table: "expense_claims",
                columns: new[] { "company_id", "submitted_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_leave_applications_company_id_submitted_at_utc",
                table: "leave_applications");

            migrationBuilder.DropIndex(
                name: "ix_expense_claims_company_id_submitted_at_utc",
                table: "expense_claims");
        }
    }
}
