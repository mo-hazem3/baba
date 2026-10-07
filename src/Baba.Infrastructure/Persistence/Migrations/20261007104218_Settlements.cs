using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Baba.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Settlements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Allocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PaymentVoucherId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InvoiceVoucherId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SettlementVoucherId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AmountScaled = table.Column<long>(type: "INTEGER", nullable: false),
                    InvoiceBaseScaled = table.Column<long>(type: "INTEGER", nullable: false),
                    PaymentBaseScaled = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Allocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Allocations_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Allocations_Vouchers_PaymentVoucherId",
                        column: x => x.PaymentVoucherId,
                        principalTable: "Vouchers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Allocations_DocumentId",
                table: "Allocations",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_Allocations_PaymentVoucherId",
                table: "Allocations",
                column: "PaymentVoucherId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Allocations");
        }
    }
}
