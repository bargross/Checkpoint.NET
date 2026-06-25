namespace StateCheckpoint.NET.Stores;

/// <summary>
/// PostgreSQL queries specific to the Training (Model) domain.
/// Handles metadata, blob references, and schema for model checkpoints.
/// </summary>
internal static class PostgresTrainingQueries
{
    // --- Schema ---
    public const string EnsureModelSchema = @"
        CREATE EXTENSION IF NOT EXISTS lo;

        CREATE TABLE IF NOT EXISTS model_manifests (
            model_id UUID PRIMARY KEY,
            hyper_params JSONB NOT NULL,
            tokenizer JSONB NOT NULL,
            epoch INT NOT NULL,
            loss FLOAT NOT NULL,
            created_at TIMESTAMPTZ NOT NULL,
            tags JSONB NOT NULL
        );
        CREATE TABLE IF NOT EXISTS model_blobs (
            model_id UUID PRIMARY KEY REFERENCES model_manifests(model_id) ON DELETE CASCADE,
            weights_oid OID NOT NULL,
            optimizer_oid OID NOT NULL
        );";

    // --- Metadata Operations ---
    public const string UpsertModelManifest = @"
        INSERT INTO model_manifests 
            (model_id, hyper_params, tokenizer, epoch, loss, created_at, tags)
        VALUES 
            (@id, @hp::jsonb, @tok::jsonb, @epoch, @loss, @now, @tags::jsonb)
        ON CONFLICT (model_id) DO UPDATE SET 
            hyper_params = EXCLUDED.hyper_params,
            tokenizer = EXCLUDED.tokenizer,
            epoch = EXCLUDED.epoch,
            loss = EXCLUDED.loss,
            tags = EXCLUDED.tags;";

    public const string DeleteModelManifest =
        "DELETE FROM model_manifests WHERE model_id = @id;";

    // --- Blob Reference Operations ---
    public const string SelectModelBlobOids =
        "SELECT weights_oid, optimizer_oid FROM model_blobs WHERE model_id = @id;";

    public const string UpsertModelBlobRefs = @"
        INSERT INTO model_blobs (model_id, weights_oid, optimizer_oid)
        VALUES (@id, @wOid, @oOid)
        ON CONFLICT (model_id) DO UPDATE SET 
            weights_oid = EXCLUDED.weights_oid,
            optimizer_oid = EXCLUDED.optimizer_oid;";

    // --- Full Load (Join) ---
    public const string SelectFullModelManifest = @"
        SELECT m.hyper_params, m.tokenizer, m.epoch, m.loss, m.created_at, m.tags,
               b.weights_oid, b.optimizer_oid
        FROM model_manifests m
        JOIN model_blobs b ON m.model_id = b.model_id
        WHERE m.model_id = @id;";

    // --- Listing ---
    public const string ListAllModelIds = "SELECT model_id FROM model_manifests;";
    public const string ListModelIdsByTag = "SELECT model_id FROM model_manifests WHERE tags @> jsonb_build_object(@key, @value);";
}