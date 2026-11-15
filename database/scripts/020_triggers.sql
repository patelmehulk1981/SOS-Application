/* =====================================================================================
   020 - Integrity triggers
   Every trigger listed here is also declared in the EF model via ToTable(t => t.HasTrigger(...)),
   which stops EF Core from using an OUTPUT clause that SQL Server rejects on tables with triggers.

   Error numbers (caught and mapped by the API):
     51001  Users.ReferredByAdminId is immutable
     51002  Append-only table modified
   ===================================================================================== */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* A user's referring admin is permanent: it may never be changed or cleared once set. */
CREATE OR ALTER TRIGGER dbo.TR_Users_ReferralImmutable
ON dbo.Users
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF UPDATE(ReferredByAdminId)
       AND EXISTS (
            SELECT 1
            FROM inserted AS i
            JOIN deleted  AS d ON d.Id = i.Id
            WHERE d.ReferredByAdminId IS NOT NULL
              AND (i.ReferredByAdminId IS NULL OR i.ReferredByAdminId <> d.ReferredByAdminId))
    BEGIN
        THROW 51001, N'Users.ReferredByAdminId is immutable once set.', 1;
    END
END
GO

/* ---------------------------------------------------------------------------------
   Append-only tables. UPDATE is never allowed. DELETE is allowed only for the
   retention purge job, which sets SESSION_CONTEXT(N'CommunitySos.AllowPurge') = 1
   for its own connection.
   --------------------------------------------------------------------------------- */

CREATE OR ALTER TRIGGER dbo.TR_ReferralHistory_AppendOnly
ON dbo.ReferralHistory
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1 FROM inserted)
        THROW 51002, N'ReferralHistory is append-only: updates are not allowed.', 1;

    IF EXISTS (SELECT 1 FROM deleted)
       AND ISNULL(CAST(SESSION_CONTEXT(N'CommunitySos.AllowPurge') AS bit), 0) = 0
        THROW 51002, N'ReferralHistory is append-only: deletes are not allowed.', 1;
END
GO

CREATE OR ALTER TRIGGER dbo.TR_SosStatusHistory_AppendOnly
ON dbo.SosStatusHistory
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1 FROM inserted)
        THROW 51002, N'SosStatusHistory is append-only: updates are not allowed.', 1;

    IF EXISTS (SELECT 1 FROM deleted)
       AND ISNULL(CAST(SESSION_CONTEXT(N'CommunitySos.AllowPurge') AS bit), 0) = 0
        THROW 51002, N'SosStatusHistory is append-only: deletes are not allowed.', 1;
END
GO

/* Consent ledger is legal evidence (DPDP s.6(10) / GDPR Art. 7(1)): grants and withdrawals are new rows. */
CREATE OR ALTER TRIGGER dbo.TR_UserConsents_AppendOnly
ON dbo.UserConsents
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1 FROM inserted)
        THROW 51002, N'UserConsents is append-only: updates are not allowed.', 1;

    IF EXISTS (SELECT 1 FROM deleted)
       AND ISNULL(CAST(SESSION_CONTEXT(N'CommunitySos.AllowPurge') AS bit), 0) = 0
        THROW 51002, N'UserConsents is append-only: deletes are not allowed.', 1;
END
GO

CREATE OR ALTER TRIGGER dbo.TR_AuditLogs_AppendOnly
ON dbo.AuditLogs
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1 FROM inserted)
        THROW 51002, N'AuditLogs is append-only: updates are not allowed.', 1;

    IF EXISTS (SELECT 1 FROM deleted)
       AND ISNULL(CAST(SESSION_CONTEXT(N'CommunitySos.AllowPurge') AS bit), 0) = 0
        THROW 51002, N'AuditLogs is append-only: deletes are not allowed.', 1;
END
GO
