using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Baba.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TaxCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TaxCodeId",
                table: "Products",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TaxCodeId",
                table: "DocumentLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "TaxRateScaled",
                table: "DocumentLines",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "TaxCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    NameAr = table.Column<string>(type: "TEXT", nullable: false),
                    NameEn = table.Column<string>(type: "TEXT", nullable: false),
                    RateScaled = table.Column<long>(type: "INTEGER", nullable: false),
                    Treatment = table.Column<string>(type: "TEXT", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    EffectiveTo = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    OutputAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    InputAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    FromPack = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaxCodes_Accounts_InputAccountId",
                        column: x => x.InputAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaxCodes_Accounts_OutputAccountId",
                        column: x => x.OutputAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Products_TaxCodeId",
                table: "Products",
                column: "TaxCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentLines_TaxCodeId",
                table: "DocumentLines",
                column: "TaxCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_TaxCodes_CompanyId_Code",
                table: "TaxCodes",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaxCodes_InputAccountId",
                table: "TaxCodes",
                column: "InputAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_TaxCodes_OutputAccountId",
                table: "TaxCodes",
                column: "OutputAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentLines_TaxCodes_TaxCodeId",
                table: "DocumentLines",
                column: "TaxCodeId",
                principalTable: "TaxCodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Products_TaxCodes_TaxCodeId",
                table: "Products",
                column: "TaxCodeId",
                principalTable: "TaxCodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DocumentLines_TaxCodes_TaxCodeId",
                table: "DocumentLines");

            migrationBuilder.DropForeignKey(
                name: "FK_Products_TaxCodes_TaxCodeId",
                table: "Products");

            migrationBuilder.DropTable(
                name: "TaxCodes");

            migrationBuilder.DropIndex(
                name: "IX_Products_TaxCodeId",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_DocumentLines_TaxCodeId",
                table: "DocumentLines");

            migrationBuilder.DropColumn(
                name: "TaxCodeId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "TaxCodeId",
                table: "DocumentLines");

            migrationBuilder.DropColumn(
                name: "TaxRateScaled",
                table: "DocumentLines");
        }
    }
}
