using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGateway.Api.Migrations
{
    /// <inheritdoc />
    public partial class PersistTelegramFloodControl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RetryNotBeforeUtc",
                table: "Deliveries",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RetryNotBeforeUtc",
                table: "Deliveries");
        }
    }
}
