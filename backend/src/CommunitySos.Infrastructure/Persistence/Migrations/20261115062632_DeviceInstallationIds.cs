using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CommunitySos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeviceInstallationIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserDevices_UserId_Active",
                schema: "dbo",
                table: "UserDevices");

            migrationBuilder.DropIndex(
                name: "UX_UserDevices_FcmToken",
                schema: "dbo",
                table: "UserDevices");

            migrationBuilder.AlterColumn<string>(
                name: "FcmToken",
                schema: "dbo",
                table: "UserDevices",
                type: "varchar(512)",
                unicode: false,
                maxLength: 512,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(512)",
                oldUnicode: false,
                oldMaxLength: 512);

            migrationBuilder.AddColumn<string>(
                name: "InstallationId",
                schema: "dbo",
                table: "UserDevices",
                type: "varchar(200)",
                unicode: false,
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserDevices_UserId_Active",
                schema: "dbo",
                table: "UserDevices",
                column: "UserId",
                filter: "[IsActive] = 1")
                .Annotation("SqlServer:Include", new[] { "FcmToken", "InstallationId", "Platform" });

            migrationBuilder.CreateIndex(
                name: "UX_UserDevices_FcmToken",
                schema: "dbo",
                table: "UserDevices",
                column: "FcmToken",
                unique: true,
                filter: "[FcmToken] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_UserDevices_InstallationId",
                schema: "dbo",
                table: "UserDevices",
                column: "InstallationId",
                unique: true,
                filter: "[InstallationId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserDevices_Target",
                schema: "dbo",
                table: "UserDevices",
                sql: "([FcmToken] IS NOT NULL AND [InstallationId] IS NULL) OR ([FcmToken] IS NULL AND [InstallationId] IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserDevices_UserId_Active",
                schema: "dbo",
                table: "UserDevices");

            migrationBuilder.DropIndex(
                name: "UX_UserDevices_FcmToken",
                schema: "dbo",
                table: "UserDevices");

            migrationBuilder.DropIndex(
                name: "UX_UserDevices_InstallationId",
                schema: "dbo",
                table: "UserDevices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UserDevices_Target",
                schema: "dbo",
                table: "UserDevices");

            // Devices known only by FID cannot be represented without the column; they re-register on next app start.
            migrationBuilder.Sql("DELETE FROM [dbo].[UserDevices] WHERE [FcmToken] IS NULL;");

            migrationBuilder.DropColumn(
                name: "InstallationId",
                schema: "dbo",
                table: "UserDevices");

            migrationBuilder.AlterColumn<string>(
                name: "FcmToken",
                schema: "dbo",
                table: "UserDevices",
                type: "varchar(512)",
                unicode: false,
                maxLength: 512,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "varchar(512)",
                oldUnicode: false,
                oldMaxLength: 512,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserDevices_UserId_Active",
                schema: "dbo",
                table: "UserDevices",
                column: "UserId",
                filter: "[IsActive] = 1")
                .Annotation("SqlServer:Include", new[] { "FcmToken", "Platform" });

            migrationBuilder.CreateIndex(
                name: "UX_UserDevices_FcmToken",
                schema: "dbo",
                table: "UserDevices",
                column: "FcmToken",
                unique: true);
        }
    }
}
