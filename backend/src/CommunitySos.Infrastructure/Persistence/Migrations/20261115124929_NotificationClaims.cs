using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CommunitySos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NotificationClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ClaimedUntil",
                schema: "dbo",
                table: "SosNotifications",
                type: "datetime2(3)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClaimedUntil",
                schema: "dbo",
                table: "SosNotifications");
        }
    }
}
