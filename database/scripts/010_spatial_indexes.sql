/* =====================================================================================
   010 - Spatial indexes
   EF Core cannot create SQL Server spatial indexes, so they live here. Idempotent.
   Requirements: table has a clustered primary key; column is geography (SRID 4326).
   ===================================================================================== */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* Last-known user locations: drives the 5 km recipient search
   (dbo.usp_SelectSosRecipients -> Location.STDistance(@p) <= @RadiusMeters). */
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'SIX_UserLocations_Location'
                 AND object_id = OBJECT_ID(N'dbo.UserLocations'))
BEGIN
    CREATE SPATIAL INDEX SIX_UserLocations_Location
        ON dbo.UserLocations (Location)
        USING GEOGRAPHY_AUTO_GRID
        WITH (CELLS_PER_OBJECT = 12);
END
GO

/* SOS locations: admin dashboard map (bounding-box / radius queries, heat-maps). */
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'SIX_SosRequests_Location'
                 AND object_id = OBJECT_ID(N'dbo.SosRequests'))
BEGIN
    CREATE SPATIAL INDEX SIX_SosRequests_Location
        ON dbo.SosRequests (Location)
        USING GEOGRAPHY_AUTO_GRID
        WITH (CELLS_PER_OBJECT = 12);
END
GO
