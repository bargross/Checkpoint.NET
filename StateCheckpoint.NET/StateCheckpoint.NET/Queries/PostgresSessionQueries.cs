namespace StateCheckpoint.NET.Stores;

/// <summary>
/// PostgreSQL queries specific to the Inference (Session) domain.
/// Handles KV-cache storage using BYTEA.
/// </summary>
internal static class PostgresSessionQueries
{
    // --- Schema ---
    public const string EnsureSessionSchema = @"
        CREATE TABLE IF NOT EXISTS inference_sessions (
            session_id UUID PRIMARY KEY,
            model_fingerprint TEXT NOT NULL,
            token_history INTEGER[] NOT NULL,
            sampling_config JSONB NOT NULL,
            kv_cache_bytes BYTEA NOT NULL,
            last_updated TIMESTAMPTZ NOT NULL,
            tags JSONB NOT NULL
        );
        CREATE INDEX IF NOT EXISTS idx_inference_tags ON inference_sessions USING GIN (tags);";

    // --- CRUD Operations ---
    public const string UpsertInferenceSession = @"
        INSERT INTO inference_sessions 
            (session_id, model_fingerprint, token_history, sampling_config, kv_cache_bytes, last_updated, tags)
        VALUES 
            (@id, @fp, @history::integer[], @config::jsonb, @kv, @now, @tags::jsonb)
        ON CONFLICT (session_id) DO UPDATE SET 
            model_fingerprint = EXCLUDED.model_fingerprint,
            token_history = EXCLUDED.token_history,
            sampling_config = EXCLUDED.sampling_config,
            kv_cache_bytes = EXCLUDED.kv_cache_bytes,
            last_updated = EXCLUDED.last_updated,
            tags = EXCLUDED.tags;";

    public const string SelectInferenceSession = @"
        SELECT model_fingerprint, token_history, sampling_config, kv_cache_bytes, last_updated, tags
        FROM inference_sessions WHERE session_id = @id;";

    public const string DeleteInferenceSession =
        "DELETE FROM inference_sessions WHERE session_id = @id;";

    // --- Listing ---
    public const string ListAllSessionIds = "SELECT session_id FROM inference_sessions;";
    public const string ListSessionIdsByTag = "SELECT session_id FROM inference_sessions WHERE tags @> jsonb_build_object(@key, @value);";
}