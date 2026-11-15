/* =====================================================================================
   031 - dbo.usp_GetDashboardStats

   All admin-dashboard counters in one round trip.
     @AdminId NULL      -> global scope (Master Admin)
     @AdminId = <id>    -> that Sub-Admin's scope: users they referred, SOS assigned to them
     @FromUtc / @ToUtc  -> optional SOS date window (CreatedAt, half-open [From, To))

   Status codes: 1 Pending, 2 Approved, 3 Rejected, 4 Cancelled, 5 Escalated, 6 Resolved, 7 Expired
   ===================================================================================== */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER PROCEDURE dbo.usp_GetDashboardStats
    @AdminId uniqueidentifier = NULL,
    @FromUtc datetime2(3)     = NULL,
    @ToUtc   datetime2(3)     = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Now datetime2(3) = SYSUTCDATETIME();

    ;WITH U AS
    (
        SELECT u.Role, u.CreatedAt, u.ReferredByAdminId
        FROM dbo.Users AS u
        WHERE u.Status <> 3 /* Deleted */
          AND (@AdminId IS NULL OR u.ReferredByAdminId = @AdminId)
    ),
    S AS
    (
        SELECT s.Status, s.IsAutoApproved, s.CreatedAt, s.ReviewedAt
        FROM dbo.SosRequests AS s
        WHERE (@AdminId IS NULL OR s.AssignedAdminId = @AdminId)
          AND (@FromUtc IS NULL OR s.CreatedAt >= @FromUtc)
          AND (@ToUtc   IS NULL OR s.CreatedAt <  @ToUtc)
    )
    SELECT
        -- Users
        us.TotalUsers,
        us.SubAdmins,
        us.NormalUsers,
        us.ReferredUsers,
        us.NewUsersLast7Days,
        -- SOS
        ss.TotalSos,
        ss.PendingSos,
        ss.EscalatedSos,
        ss.ApprovedSos,
        ss.AutoApprovedSos,
        ss.RejectedSos,
        ss.ActiveSos,
        ss.ResolvedSos,
        ss.CancelledSos,
        ss.ExpiredSos,
        ss.SosLast24Hours,
        ss.AvgReviewSeconds
    FROM
    (
        SELECT
            COUNT(*)                                                                   AS TotalUsers,
            ISNULL(SUM(CASE WHEN Role = 2 THEN 1 ELSE 0 END), 0)                       AS SubAdmins,
            ISNULL(SUM(CASE WHEN Role = 1 THEN 1 ELSE 0 END), 0)                       AS NormalUsers,
            ISNULL(SUM(CASE WHEN ReferredByAdminId IS NOT NULL THEN 1 ELSE 0 END), 0)  AS ReferredUsers,
            ISNULL(SUM(CASE WHEN CreatedAt >= DATEADD(DAY, -7, @Now) THEN 1 ELSE 0 END), 0) AS NewUsersLast7Days
        FROM U
    ) AS us
    CROSS JOIN
    (
        SELECT
            COUNT(*)                                                                         AS TotalSos,
            ISNULL(SUM(CASE WHEN Status = 1 THEN 1 ELSE 0 END), 0)                           AS PendingSos,
            ISNULL(SUM(CASE WHEN Status = 5 THEN 1 ELSE 0 END), 0)                           AS EscalatedSos,
            -- "Approved" = was approved at some point (now Approved, Resolved, or later Cancelled/Expired).
            ISNULL(SUM(CASE WHEN ReviewedAt IS NOT NULL AND Status <> 3 THEN 1 ELSE 0 END), 0) AS ApprovedSos,
            ISNULL(SUM(CASE WHEN IsAutoApproved = 1 THEN 1 ELSE 0 END), 0)                   AS AutoApprovedSos,
            ISNULL(SUM(CASE WHEN Status = 3 THEN 1 ELSE 0 END), 0)                           AS RejectedSos,
            ISNULL(SUM(CASE WHEN Status = 2 THEN 1 ELSE 0 END), 0)                           AS ActiveSos,
            ISNULL(SUM(CASE WHEN Status = 6 THEN 1 ELSE 0 END), 0)                           AS ResolvedSos,
            ISNULL(SUM(CASE WHEN Status = 4 THEN 1 ELSE 0 END), 0)                           AS CancelledSos,
            ISNULL(SUM(CASE WHEN Status = 7 THEN 1 ELSE 0 END), 0)                           AS ExpiredSos,
            ISNULL(SUM(CASE WHEN CreatedAt >= DATEADD(HOUR, -24, @Now) THEN 1 ELSE 0 END), 0) AS SosLast24Hours,
            AVG(CASE WHEN ReviewedAt IS NOT NULL AND IsAutoApproved = 0
                     THEN CAST(DATEDIFF(SECOND, CreatedAt, ReviewedAt) AS float) END)        AS AvgReviewSeconds
        FROM S
    ) AS ss;
END
GO
