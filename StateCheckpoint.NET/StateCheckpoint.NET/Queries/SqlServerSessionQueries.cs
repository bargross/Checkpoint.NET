namespace StateCheckpoint.NET.Stores;

/// <summary>
/// SQL Server queries specific to the Inference (Session) domain.
/// Stores KV-cache as VARBINARY(MAX).
/// </summary>
internal static class SqlServerSessionQueries
{
    // --- Schema ---
    public const string EnsureSessionSchema = @"
        IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='InferenceSessions' AND xtype='U')
        BEGIN
            CREATE TABLE InferenceSessions (
                SessionId UNIQUEIDENTIFIER PRIMARY KEY,
                ModelFingerprint NVARCHAR(255) NOT NULL,
                TokenHistory NVARCHAR(MAX) NOT NULL,   -- JSON array
                SamplingConfig NVARCHAR(MAX) NOT NULL, -- JSON
                KvCacheData VARBINARY(MAX) NOT NULL,
                LastUpdated DATETIME2 NOT NULL,
                Tags NVARCHAR(MAX) NOT NULL            -- JSON
            );
        END";

    // --- CRUD ---
    public const string UpsertInferenceSession = @"
        MERGE INTO InferenceSessions AS target
        USING (SELECT @Id AS SessionId) AS source
        ON target.SessionId = source.SessionId
        WHEN MATCHED THEN
            UPDATE SET
                ModelFingerprint = @ModelFingerprint,
                TokenHistory = @TokenHistory,
                SamplingConfig = @SamplingConfig,
                KvCacheData = @KvCacheData,
                LastUpdated = @LastUpdated,
                Tags = @Tags
        WHEN NOT MATCHED THEN
            INSERT (SessionId, ModelFingerprint, TokenHistory, SamplingConfig, KvCacheData, LastUpdated, Tags)
            VALUES (@Id, @ModelFingerprint, @TokenHistory, @SamplingConfig, @KvCacheData, @LastUpdated, @Tags);";

    public const string SelectInferenceSession = @"
        SELECT
            ModelFingerprint,
            TokenHistory,
            SamplingConfig,
            KvCacheData,
            LastUpdated,
            Tags
        FROM InferenceSessions
        WHERE SessionId = @Id;";

    public const string DeleteInferenceSession =
        "DELETE FROM InferenceSessions WHERE SessionId = @Id;";

    // --- Listing ---
    public const string ListAllSessionIds = "SELECT SessionId FROM InferenceSessions;";
    public const string ListSessionIdsByTag = "SELECT SessionId FROM InferenceSessions WHERE Tags LIKE @TagPattern;";
}