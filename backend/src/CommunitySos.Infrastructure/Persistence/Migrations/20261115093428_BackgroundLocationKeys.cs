using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CommunitySos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BackgroundLocationKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LocationKeys",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KeyHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    DeviceId = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RevokedReason = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocationKeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LocationKeys_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "UX_LocationKeys_KeyHash",
                schema: "dbo",
                table: "LocationKeys",
                column: "KeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_LocationKeys_User_Device_Active",
                schema: "dbo",
                table: "LocationKeys",
                columns: new[] { "UserId", "DeviceId" },
                unique: true,
                filter: "[RevokedAt] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LocationKeys",
                schema: "dbo");
        }
    }
}
