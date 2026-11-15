using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace CommunitySos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "dbo");

            migrationBuilder.CreateSequence(
                name: "SosReferenceSeq",
                schema: "dbo");

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Action = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    EntityName = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    EntityId = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    OldValues = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewValues = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IpAddress = table.Column<string>(type: "varchar(45)", unicode: false, maxLength: 45, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CorrelationId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PhoneNumber = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    FirebaseUid = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: true),
                    PhoneVerifiedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    FullName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ProfilePhotoBlobName = table.Column<string>(type: "varchar(300)", unicode: false, maxLength: 300, nullable: true),
                    BloodGroup = table.Column<string>(type: "varchar(5)", unicode: false, maxLength: 5, nullable: true),
                    EmergencyContactName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    EmergencyContactPhone = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: true),
                    Role = table.Column<byte>(type: "tinyint", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    ReferredByAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AlertsEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    LocationSharingEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    AcceptedPolicyVersion = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    PolicyAcceptedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    AdultConfirmedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    LastLoginAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.CheckConstraint("CK_Users_Referral", "([Role] = 3 AND [ReferredByAdminId] IS NULL) OR ([Role] <> 3 AND [ReferredByAdminId] IS NOT NULL)");
                    table.CheckConstraint("CK_Users_Role", "[Role] IN (1, 2, 3)");
                    table.CheckConstraint("CK_Users_Status", "[Status] IN (1, 2, 3)");
                    table.ForeignKey(
                        name: "FK_Users_Users_ReferredByAdminId",
                        column: x => x.ReferredByAdminId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Invitations",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InviteeRole = table.Column<byte>(type: "tinyint", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    MaxUses = table.Column<int>(type: "int", nullable: true),
                    UsedCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    IsRevoked = table.Column<bool>(type: "bit", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invitations", x => x.Id);
                    table.CheckConstraint("CK_Invitations_InviteeRole", "[InviteeRole] IN (1, 2)");
                    table.CheckConstraint("CK_Invitations_MaxUses", "[MaxUses] IS NULL OR [MaxUses] > 0");
                    table.CheckConstraint("CK_Invitations_UsedCount", "[UsedCount] >= 0 AND ([MaxUses] IS NULL OR [UsedCount] <= [MaxUses])");
                    table.ForeignKey(
                        name: "FK_Invitations_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    FamilyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeviceId = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RevokedReason = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    ReplacedByTokenId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefreshTokens_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SosRequests",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReferenceSeq = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "NEXT VALUE FOR [dbo].[SosReferenceSeq]"),
                    ReferenceNo = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false, computedColumnSql: "'SOS-' + RIGHT('00000000' + CAST([ReferenceSeq] AS varchar(20)), 8)", stored: true),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequesterUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignedAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Category = table.Column<byte>(type: "tinyint", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Location = table.Column<Point>(type: "geography", nullable: false),
                    Latitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    Longitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    AccuracyMeters = table.Column<double>(type: "float", nullable: true),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ClientCapturedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    IsAutoApproved = table.Column<bool>(type: "bit", nullable: false),
                    EscalatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    ReviewRemarks = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RecipientCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    ClosedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SosRequests", x => x.Id);
                    table.CheckConstraint("CK_SosRequests_Category", "[Category] BETWEEN 1 AND 5");
                    table.CheckConstraint("CK_SosRequests_Latitude", "[Latitude] BETWEEN -90 AND 90");
                    table.CheckConstraint("CK_SosRequests_Longitude", "[Longitude] BETWEEN -180 AND 180");
                    table.CheckConstraint("CK_SosRequests_RejectRemarks", "[Status] <> 3 OR [ReviewRemarks] IS NOT NULL");
                    table.CheckConstraint("CK_SosRequests_Status", "[Status] BETWEEN 1 AND 7");
                    table.ForeignKey(
                        name: "FK_SosRequests_Users_AssignedAdminId",
                        column: x => x.AssignedAdminId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SosRequests_Users_RequesterUserId",
                        column: x => x.RequesterUserId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SosRequests_Users_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserConsents",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<byte>(type: "tinyint", nullable: false),
                    PolicyVersion = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Granted = table.Column<bool>(type: "bit", nullable: false),
                    CapturedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    IpAddress = table.Column<string>(type: "varchar(45)", unicode: false, maxLength: 45, nullable: true),
                    Source = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserConsents", x => x.Id);
                    table.CheckConstraint("CK_UserConsents_Type", "[Type] BETWEEN 1 AND 3");
                    table.ForeignKey(
                        name: "FK_UserConsents_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserDevices",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FcmToken = table.Column<string>(type: "varchar(512)", unicode: false, maxLength: 512, nullable: false),
                    Platform = table.Column<byte>(type: "tinyint", nullable: false),
                    AppVersion = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    DeviceModel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    LastSeenAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserDevices", x => x.Id);
                    table.CheckConstraint("CK_UserDevices_Platform", "[Platform] IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_UserDevices_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserLocations",
                schema: "dbo",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Location = table.Column<Point>(type: "geography", nullable: false),
                    AccuracyMeters = table.Column<double>(type: "float", nullable: true),
                    CapturedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserLocations", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_UserLocations_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReferralHistory",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InvitationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReferrerAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReferredUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignedRole = table.Column<byte>(type: "tinyint", nullable: false),
                    RegisteredAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    IpAddress = table.Column<string>(type: "varchar(45)", unicode: false, maxLength: 45, nullable: true),
                    DeviceInfo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReferralHistory", x => x.Id);
                    table.CheckConstraint("CK_ReferralHistory_AssignedRole", "[AssignedRole] IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_ReferralHistory_Invitations_InvitationId",
                        column: x => x.InvitationId,
                        principalSchema: "dbo",
                        principalTable: "Invitations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReferralHistory_Users_ReferredUserId",
                        column: x => x.ReferredUserId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReferralHistory_Users_ReferrerAdminId",
                        column: x => x.ReferrerAdminId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SosAttachments",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SosRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BlobName = table.Column<string>(type: "varchar(300)", unicode: false, maxLength: 300, nullable: false),
                    ContentType = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    Width = table.Column<int>(type: "int", nullable: false),
                    Height = table.Column<int>(type: "int", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SosAttachments", x => x.Id);
                    table.CheckConstraint("CK_SosAttachments_Size", "[SizeBytes] > 0");
                    table.ForeignKey(
                        name: "FK_SosAttachments_SosRequests_SosRequestId",
                        column: x => x.SosRequestId,
                        principalSchema: "dbo",
                        principalTable: "SosRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SosNotifications",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SosRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DistanceMeters = table.Column<double>(type: "float", nullable: false),
                    DeliveryStatus = table.Column<byte>(type: "tinyint", nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    LastError = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SentAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    AcknowledgedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    ResponseType = table.Column<byte>(type: "tinyint", nullable: false),
                    ResponseNote = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    RespondedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SosNotifications", x => x.Id);
                    table.CheckConstraint("CK_SosNotifications_DeliveryStatus", "[DeliveryStatus] BETWEEN 1 AND 4");
                    table.CheckConstraint("CK_SosNotifications_Distance", "[DistanceMeters] >= 0");
                    table.CheckConstraint("CK_SosNotifications_ResponseType", "[ResponseType] BETWEEN 0 AND 4");
                    table.ForeignKey(
                        name: "FK_SosNotifications_SosRequests_SosRequestId",
                        column: x => x.SosRequestId,
                        principalSchema: "dbo",
                        principalTable: "SosRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SosNotifications_Users_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SosStatusHistory",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SosRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromStatus = table.Column<byte>(type: "tinyint", nullable: true),
                    ToStatus = table.Column<byte>(type: "tinyint", nullable: false),
                    ChangedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Remarks = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ChangedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SosStatusHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SosStatusHistory_SosRequests_SosRequestId",
                        column: x => x.SosRequestId,
                        principalSchema: "dbo",
                        principalTable: "SosRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SosStatusHistory_Users_ChangedByUserId",
                        column: x => x.ChangedByUserId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_CreatedAt",
                schema: "dbo",
                table: "AuditLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Entity",
                schema: "dbo",
                table: "AuditLogs",
                columns: new[] { "EntityName", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_User_CreatedAt",
                schema: "dbo",
                table: "AuditLogs",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_CreatedBy_CreatedAt",
                schema: "dbo",
                table: "Invitations",
                columns: new[] { "CreatedByUserId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "UX_Invitations_Code",
                schema: "dbo",
                table: "Invitations",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReferralHistory_InvitationId",
                schema: "dbo",
                table: "ReferralHistory",
                column: "InvitationId");

            migrationBuilder.CreateIndex(
                name: "IX_ReferralHistory_Referrer_RegisteredAt",
                schema: "dbo",
                table: "ReferralHistory",
                columns: new[] { "ReferrerAdminId", "RegisteredAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "UX_ReferralHistory_ReferredUserId",
                schema: "dbo",
                table: "ReferralHistory",
                column: "ReferredUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_ExpiresAt",
                schema: "dbo",
                table: "RefreshTokens",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_FamilyId",
                schema: "dbo",
                table: "RefreshTokens",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_UserId",
                schema: "dbo",
                table: "RefreshTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "UX_RefreshTokens_TokenHash",
                schema: "dbo",
                table: "RefreshTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SosAttachments_SosRequestId",
                schema: "dbo",
                table: "SosAttachments",
                column: "SosRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_SosNotifications_Recipient_CreatedAt",
                schema: "dbo",
                table: "SosNotifications",
                columns: new[] { "RecipientUserId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "UX_SosNotifications_Sos_Recipient",
                schema: "dbo",
                table: "SosNotifications",
                columns: new[] { "SosRequestId", "RecipientUserId" },
                unique: true)
                .Annotation("SqlServer:Include", new[] { "DeliveryStatus", "ResponseType" });

            migrationBuilder.CreateIndex(
                name: "IX_SosRequests_Admin_Status_CreatedAt",
                schema: "dbo",
                table: "SosRequests",
                columns: new[] { "AssignedAdminId", "Status", "CreatedAt" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_SosRequests_Requester_CreatedAt",
                schema: "dbo",
                table: "SosRequests",
                columns: new[] { "RequesterUserId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_SosRequests_ReviewedByUserId",
                schema: "dbo",
                table: "SosRequests",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SosRequests_Status_CreatedAt",
                schema: "dbo",
                table: "SosRequests",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_SosRequests_ReferenceNo",
                schema: "dbo",
                table: "SosRequests",
                column: "ReferenceNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_SosRequests_Requester_ClientRequestId",
                schema: "dbo",
                table: "SosRequests",
                columns: new[] { "RequesterUserId", "ClientRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SosStatusHistory_ChangedByUserId",
                schema: "dbo",
                table: "SosStatusHistory",
                column: "ChangedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SosStatusHistory_Sos_ChangedAt",
                schema: "dbo",
                table: "SosStatusHistory",
                columns: new[] { "SosRequestId", "ChangedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_UserConsents_User_Type_CapturedAt",
                schema: "dbo",
                table: "UserConsents",
                columns: new[] { "UserId", "Type", "CapturedAt" },
                descending: new[] { false, false, true })
                .Annotation("SqlServer:Include", new[] { "Granted", "PolicyVersion" });

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

            migrationBuilder.CreateIndex(
                name: "IX_UserLocations_CapturedAt",
                schema: "dbo",
                table: "UserLocations",
                column: "CapturedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Users_CreatedAt",
                schema: "dbo",
                table: "Users",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Users_ReferredByAdminId_Status",
                schema: "dbo",
                table: "Users",
                columns: new[] { "ReferredByAdminId", "Status" })
                .Annotation("SqlServer:Include", new[] { "Role", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_Users_FirebaseUid",
                schema: "dbo",
                table: "Users",
                column: "FirebaseUid",
                unique: true,
                filter: "[FirebaseUid] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Users_PhoneNumber",
                schema: "dbo",
                table: "Users",
                column: "PhoneNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Users_SingleMasterAdmin",
                schema: "dbo",
                table: "Users",
                column: "Role",
                unique: true,
                filter: "[Role] = 3");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogs",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "ReferralHistory",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "RefreshTokens",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "SosAttachments",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "SosNotifications",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "SosStatusHistory",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "UserConsents",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "UserDevices",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "UserLocations",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Invitations",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "SosRequests",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Users",
                schema: "dbo");

            migrationBuilder.DropSequence(
                name: "SosReferenceSeq",
                schema: "dbo");
        }
    }
}
