using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Baba.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PrintSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PrintSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShowCompanyName = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowLogo = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowStamp = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowSignatures = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowAmountInWords = table.Column<bool>(type: "INTEGER", nullable: false),
                    HeaderTextEn = table.Column<string>(type: "TEXT", nullable: true),
                    HeaderTextAr = table.Column<string>(type: "TEXT", nullable: true),
                    FooterTextEn = table.Column<string>(type: "TEXT", nullable: true),
                    FooterTextAr = table.Column<string>(type: "TEXT", nullable: true),
                    DefaultLayout = table.Column<string>(type: "TEXT", nullable: false),
                    ArabicIndicDigits = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrintSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PrintSettings_CompanyId",
                table: "PrintSettings",
                column: "CompanyId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PrintSettings");
        }
    }
}
