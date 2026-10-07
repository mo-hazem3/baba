using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Baba.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PartiesAndCostCenters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CostCenterId",
                table: "VoucherLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PartyId",
                table: "VoucherLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CostCenterId",
                table: "LedgerEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PartyId",
                table: "LedgerEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CostCenters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    NameAr = table.Column<string>(type: "TEXT", nullable: false),
                    NameEn = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CostCenters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Parties",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    NameAr = table.Column<string>(type: "TEXT", nullable: false),
                    NameEn = table.Column<string>(type: "TEXT", nullable: false),
                    Phone = table.Column<string>(type: "TEXT", nullable: true),
                    Email = table.Column<string>(type: "TEXT", nullable: true),
                    Address = table.Column<string>(type: "TEXT", nullable: true),
                    TaxNumber = table.Column<string>(type: "TEXT", nullable: true),
                    CreditLimitScaled = table.Column<long>(type: "INTEGER", nullable: false),
                    PaymentTermsDays = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Parties", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VoucherLines_CostCenterId",
                table: "VoucherLines",
                column: "CostCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_VoucherLines_PartyId",
                table: "VoucherLines",
                column: "PartyId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_CostCenterId",
                table: "LedgerEntries",
                column: "CostCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_PartyId_Date",
                table: "LedgerEntries",
                columns: new[] { "PartyId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_CostCenters_CompanyId_Code",
                table: "CostCenters",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Parties_CompanyId_Code",
                table: "Parties",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_LedgerEntries_CostCenters_CostCenterId",
                table: "LedgerEntries",
                column: "CostCenterId",
                principalTable: "CostCenters",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_LedgerEntries_Parties_PartyId",
                table: "LedgerEntries",
                column: "PartyId",
                principalTable: "Parties",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VoucherLines_CostCenters_CostCenterId",
                table: "VoucherLines",
                column: "CostCenterId",
                principalTable: "CostCenters",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VoucherLines_Parties_PartyId",
                table: "VoucherLines",
                column: "PartyId",
                principalTable: "Parties",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LedgerEntries_CostCenters_CostCenterId",
                table: "LedgerEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_LedgerEntries_Parties_PartyId",
                table: "LedgerEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_VoucherLines_CostCenters_CostCenterId",
                table: "VoucherLines");

            migrationBuilder.DropForeignKey(
                name: "FK_VoucherLines_Parties_PartyId",
                table: "VoucherLines");

            migrationBuilder.DropTable(
                name: "CostCenters");

            migrationBuilder.DropTable(
                name: "Parties");

            migrationBuilder.DropIndex(
                name: "IX_VoucherLines_CostCenterId",
                table: "VoucherLines");

            migrationBuilder.DropIndex(
                name: "IX_VoucherLines_PartyId",
                table: "VoucherLines");

            migrationBuilder.DropIndex(
                name: "IX_LedgerEntries_CostCenterId",
                table: "LedgerEntries");

            migrationBuilder.DropIndex(
                name: "IX_LedgerEntries_PartyId_Date",
                table: "LedgerEntries");

            migrationBuilder.DropColumn(
                name: "CostCenterId",
                table: "VoucherLines");

            migrationBuilder.DropColumn(
                name: "PartyId",
                table: "VoucherLines");

            migrationBuilder.DropColumn(
                name: "CostCenterId",
                table: "LedgerEntries");

            migrationBuilder.DropColumn(
                name: "PartyId",
                table: "LedgerEntries");
        }
    }
}
