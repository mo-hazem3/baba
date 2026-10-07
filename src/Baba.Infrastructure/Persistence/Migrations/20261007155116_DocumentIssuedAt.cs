using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Baba.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DocumentIssuedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "IssuedAt",
                table: "Documents",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IssuedAt",
                table: "Documents");
        }
    }
}
