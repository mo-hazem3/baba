using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Baba.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecurringSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RecurringSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Template = table.Column<string>(type: "TEXT", nullable: false),
                    TemplateKind = table.Column<string>(type: "TEXT", nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                    Frequency = table.Column<string>(type: "TEXT", nullable: false),
                    NextRunDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    AnchorDay = table.Column<int>(type: "INTEGER", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    AutoIssue = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastRunDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    RunCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringSchedules", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringSchedules_NextRunDate",
                table: "RecurringSchedules",
                column: "NextRunDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecurringSchedules");
        }
    }
}
