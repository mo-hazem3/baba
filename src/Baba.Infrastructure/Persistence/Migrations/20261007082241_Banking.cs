using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Baba.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Banking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BankReconciliations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StatementDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    StatementBalanceScaled = table.Column<long>(type: "INTEGER", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankReconciliations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankReconciliations_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BankStatementLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Reference = table.Column<string>(type: "TEXT", nullable: true),
                    AmountScaled = table.Column<long>(type: "INTEGER", nullable: false),
                    BatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReconciliationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankStatementLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankStatementLines_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankStatementLines_BankReconciliations_ReconciliationId",
                        column: x => x.ReconciliationId,
                        principalTable: "BankReconciliations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ReconciledEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReconciliationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LedgerEntryId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconciledEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReconciledEntries_BankReconciliations_ReconciliationId",
                        column: x => x.ReconciliationId,
                        principalTable: "BankReconciliations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReconciledEntries_LedgerEntries_LedgerEntryId",
                        column: x => x.LedgerEntryId,
                        principalTable: "LedgerEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliations_AccountId_StatementDate",
                table: "BankReconciliations",
                columns: new[] { "AccountId", "StatementDate" });

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementLines_AccountId_Date",
                table: "BankStatementLines",
                columns: new[] { "AccountId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementLines_ReconciliationId",
                table: "BankStatementLines",
                column: "ReconciliationId");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciledEntries_LedgerEntryId",
                table: "ReconciledEntries",
                column: "LedgerEntryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReconciledEntries_ReconciliationId",
                table: "ReconciledEntries",
                column: "ReconciliationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BankStatementLines");

            migrationBuilder.DropTable(
                name: "ReconciledEntries");

            migrationBuilder.DropTable(
                name: "BankReconciliations");
        }
    }
}
