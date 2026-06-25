namespace StateCheckpoint.NET.Stores;

/// <summary>
/// SQL Server queries specific to the Training (Model) domain.
/// Uses VARBINARY(MAX) for weights/optimizer (up to 2 GB).
/// </summary>
internal static class SqlServerTrainingQueries
{
    // --- Schema ---
    public const string EnsureModelSchema = @"
        IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='ModelManifests' AND xtype='U')
        BEGIN
            CREATE TABLE ModelManifests (
                ModelId UNIQUEIDENTIFIER PRIMARY KEY,
                HyperParams NVARCHAR(MAX) NOT NULL,  -- JSON
                Tokenizer NVARCHAR(MAX) NOT NULL,    -- JSON
                Epoch INT NOT NULL,
                Loss FLOAT NOT NULL,
                CreatedAt DATETIME2 NOT NULL,
                Tags NVARCHAR(MAX) NOT NULL          -- JSON
            );
        END

        IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='ModelBlobs' AND xtype='U')
        BEGIN
            CREATE TABLE ModelBlobs (
                ModelId UNIQUEIDENTIFIER PRIMARY KEY,
                WeightsData VARBINARY(MAX) NOT NULL,
                OptimizerData VARBINARY(MAX) NOT NULL,
                CONSTRAINT FK_ModelBlobs_ModelManifests FOREIGN KEY (ModelId)
                    REFERENCES ModelManifests(ModelId) ON DELETE CASCADE
            );
        END";

    // --- Metadata Operations ---
    public const string UpsertModelManifest = @"
        MERGE INTO ModelManifests AS target
        USING (SELECT @Id AS ModelId) AS source
        ON target.ModelId = source.ModelId
        WHEN MATCHED THEN
            UPDATE SET
                HyperParams = @HyperParams,
                Tokenizer = @Tokenizer,
                Epoch = @Epoch,
                Loss = @Loss,
                Tags = @Tags
        WHEN NOT MATCHED THEN
            INSERT (ModelId, HyperParams, Tokenizer, Epoch, Loss, CreatedAt, Tags)
            VALUES (@Id, @HyperParams, @Tokenizer, @Epoch, @Loss, @CreatedAt, @Tags);";

    public const string DeleteModelManifest =
        "DELETE FROM ModelManifests WHERE ModelId = @Id;";

    // --- Blob Operations ---
    public const string SelectModelBlobs =
        "SELECT WeightsData, OptimizerData FROM ModelBlobs WHERE ModelId = @Id;";

    public const string UpsertModelBlobs = @"
        MERGE INTO ModelBlobs AS target
        USING (SELECT @Id AS ModelId) AS source
        ON target.ModelId = source.ModelId
        WHEN MATCHED THEN
            UPDATE SET
                WeightsData = @WeightsData,
                OptimizerData = @OptimizerData
        WHEN NOT MATCHED THEN
            INSERT (ModelId, WeightsData, OptimizerData)
            VALUES (@Id, @WeightsData, @OptimizerData);";

    // --- Full Load (Join) ---
    public const string SelectFullModelManifest = @"
        SELECT
            m.HyperParams,
            m.Tokenizer,
            m.Epoch,
            m.Loss,
            m.CreatedAt,
            m.Tags,
            b.WeightsData,
            b.OptimizerData
        FROM ModelManifests m
        INNER JOIN ModelBlobs b ON m.ModelId = b.ModelId
        WHERE m.ModelId = @Id;";

    // --- Listing ---
    public const string ListAllModelIds = "SELECT ModelId FROM ModelManifests;";
    public const string ListModelIdsByTag = "SELECT ModelId FROM ModelManifests WHERE Tags LIKE @TagPattern;";
}