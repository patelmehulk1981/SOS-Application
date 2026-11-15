/* =====================================================================================
   030 - dbo.usp_SelectSosRecipients

   Finds every eligible user within @RadiusMeters of an APPROVED SOS, records them in
   dbo.SosNotifications, and returns the (recipient, device token) pairs that still need
   a push. Safe to call repeatedly for the same SOS (job retries, re-dispatch):

     * sp_getapplock serialises concurrent dispatches of the same SOS;
     * NOT EXISTS + UX_SosNotifications_Sos_Recipient mean a user is inserted at most once;
     * only rows in Queued(1)/Failed(3) with attempts left are returned for sending, and each
       returned row is CLAIMED for @ClaimSeconds inside the locked transaction: an overlapping run
       (Hangfire retry, two approvals) gets only unclaimed rows, so nobody is pushed twice.
       Recording the outcome releases the claim; a crashed worker's claims simply expire.

   Eligible recipient:
     Users.Status = Active(1), AlertsEnabled = 1, LocationSharingEnabled = 1,
     not the requester, last-known location captured within @FreshHours,
     and reported accuracy no worse than @MaxAccuracyMeters.

   Result set (ordered nearest first):
     SosNotificationId bigint, RecipientUserId uniqueidentifier, DistanceMeters float,
     DeviceId uniqueidentifier, FcmToken varchar(512) NULL, InstallationId varchar(200) NULL,
     Platform tinyint
   Exactly one of FcmToken / InstallationId is set per device (CK_UserDevices_Target): current app
   versions register a Firebase Installation ID, older builds a legacy FCM token.
   ===================================================================================== */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER PROCEDURE dbo.usp_SelectSosRecipients
    @SosRequestId       uniqueidentifier,
    @RadiusMeters       float = 5000,
    @FreshHours         int   = 12,
    @MaxAccuracyMeters  float = 500,
    @MaxAttempts        int   = 5,
    @ClaimSeconds       int   = 120
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @RadiusMeters <= 0 OR @RadiusMeters > 50000
        THROW 51020, N'@RadiusMeters must be between 0 and 50000.', 1;

    DECLARE @Now             datetime2(3) = SYSUTCDATETIME();
    DECLARE @Location        geography;
    DECLARE @RequesterUserId uniqueidentifier;
    DECLARE @Status          tinyint;
    DECLARE @LockResult      int;
    DECLARE @Claimed         TABLE (Id bigint PRIMARY KEY);
    DECLARE @LockResource   nvarchar(255) = N'SosDispatch:' + CONVERT(nvarchar(36), @SosRequestId);

    BEGIN TRANSACTION;

    EXEC @LockResult = sys.sp_getapplock
         @Resource    = @LockResource,
         @LockMode    = N'Exclusive',
         @LockOwner   = N'Transaction',
         @LockTimeout = 15000;

    IF @LockResult < 0
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 51021, N'Could not acquire the dispatch lock for this SOS.', 1;
    END

    SELECT @Location        = s.Location,
           @RequesterUserId = s.RequesterUserId,
           @Status          = s.Status
    FROM dbo.SosRequests AS s
    WHERE s.Id = @SosRequestId;

    IF @Location IS NULL
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 51022, N'SOS request not found.', 1;
    END

    IF @Status = 2 /* Approved */
    BEGIN
        ;WITH Nearby AS
        (
            -- "Location.STDistance(@p) <= @r" is the sargable form that uses SIX_UserLocations_Location.
            SELECT ul.UserId,
                   ul.Location.STDistance(@Location) AS DistanceMeters
            FROM dbo.UserLocations AS ul
            WHERE ul.Location.STDistance(@Location) <= @RadiusMeters
              AND ul.CapturedAt >= DATEADD(HOUR, -@FreshHours, @Now)
              AND (ul.AccuracyMeters IS NULL OR ul.AccuracyMeters <= @MaxAccuracyMeters)
        )
        INSERT INTO dbo.SosNotifications
               (SosRequestId, RecipientUserId, DistanceMeters, DeliveryStatus, AttemptCount, ResponseType, CreatedAt)
        SELECT @SosRequestId, n.UserId, n.DistanceMeters, 1 /* Queued */, 0, 0 /* None */, @Now
        FROM Nearby AS n
        JOIN dbo.Users AS u ON u.Id = n.UserId
        WHERE u.Status = 1
          AND u.AlertsEnabled = 1
          AND u.LocationSharingEnabled = 1
          AND u.Id <> @RequesterUserId
          AND NOT EXISTS (SELECT 1
                          FROM dbo.SosNotifications AS x
                          WHERE x.SosRequestId = @SosRequestId
                            AND x.RecipientUserId = n.UserId);

        -- Recipients with no active device cannot be pushed; keep them for in-app feed and metrics.
        UPDATE sn
           SET DeliveryStatus = 4 /* NoDevice */
        FROM dbo.SosNotifications AS sn
        WHERE sn.SosRequestId = @SosRequestId
          AND sn.DeliveryStatus = 1
          AND NOT EXISTS (SELECT 1 FROM dbo.UserDevices AS d
                          WHERE d.UserId = sn.RecipientUserId AND d.IsActive = 1);

        UPDATE dbo.SosRequests
           SET RecipientCount = (SELECT COUNT(*) FROM dbo.SosNotifications WHERE SosRequestId = @SosRequestId)
        WHERE Id = @SosRequestId;

        -- Hand the undelivered recipients to THIS run (see header).
        UPDATE sn
           SET ClaimedUntil = DATEADD(SECOND, @ClaimSeconds, @Now)
        OUTPUT inserted.Id INTO @Claimed (Id)
        FROM dbo.SosNotifications AS sn
        WHERE sn.SosRequestId = @SosRequestId
          AND sn.DeliveryStatus IN (1 /* Queued */, 3 /* Failed */)
          AND sn.AttemptCount < @MaxAttempts
          AND (sn.ClaimedUntil IS NULL OR sn.ClaimedUntil <= @Now)
          AND EXISTS (SELECT 1 FROM dbo.UserDevices AS d
                      WHERE d.UserId = sn.RecipientUserId AND d.IsActive = 1);
    END

    COMMIT TRANSACTION;

    SELECT sn.Id              AS SosNotificationId,
           sn.RecipientUserId,
           sn.DistanceMeters,
           d.Id               AS DeviceId,
           d.FcmToken,
           d.InstallationId,
           d.Platform
    FROM @Claimed             AS c
    JOIN dbo.SosNotifications AS sn ON sn.Id = c.Id
    JOIN dbo.UserDevices      AS d  ON d.UserId = sn.RecipientUserId AND d.IsActive = 1
    ORDER BY sn.DistanceMeters;
END
GO
