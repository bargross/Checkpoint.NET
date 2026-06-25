using System.Text.Json;
using Npgsql;
using StateCheckpoint.NET.Models;

namespace StateCheckpoint.NET.Stores.Postgres;

public class PostgresSessionStore : PostgresStoreBase, ISessionStore
{
    private static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = false };

    public PostgresSessionStore(string connectionString) : base(connectionString) { }

    public PostgresSessionStore(NpgsqlDataSource dataSource) : base(dataSource) { }

    /// <summary>
    /// Ensures schema is created
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);

        await using var command = new NpgsqlCommand(PostgresSessionQueries.EnsureSessionSchema, connection);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }


    /// <summary>
    /// Saves a new session
    /// </summary>
    /// <param name="session"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>Session id created for the given session</returns>
    public async Task SaveAsync(SessionCheckpoint session, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);

        await using var command = new NpgsqlCommand(PostgresSessionQueries.UpsertInferenceSession, connection);

        command.Parameters.AddWithValue("@id", session.SessionId);
        command.Parameters.AddWithValue("@fp", session.ModelFingerprint);
        command.Parameters.AddWithValue("@history", session.TokenHistory);
        command.Parameters.AddWithValue("@config", JsonSerializer.Serialize(session.SamplingConfig, _jsonOpts));
        command.Parameters.AddWithValue("@kv", session.KvCacheBytes);
        command.Parameters.AddWithValue("@now", session.LastUpdated);
        command.Parameters.AddWithValue("@tags", JsonSerializer.Serialize(session.Tags, _jsonOpts));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Loads a specific session checkpoint by session Id
    /// </summary>
    /// <param name="sessionId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<SessionCheckpoint?> LoadAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);

        await using var command = new NpgsqlCommand(PostgresSessionQueries.SelectInferenceSession, connection);
        command.Parameters.AddWithValue("@id", sessionId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        return new SessionCheckpoint
        {
            SessionId = sessionId,
            ModelFingerprint = reader.GetString(0),
            TokenHistory = reader.GetFieldValue<int[]>(1),
            SamplingConfig = JsonSerializer.Deserialize<SamplingData>(reader.GetString(2))!,
            KvCacheBytes = reader.GetFieldValue<byte[]>(3),
            LastUpdated = reader.GetDateTime(4),
            Tags = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(5))!
        };
    }

    /// <summary>
    /// Deletes a saved session by session Id
    /// </summary>
    /// <param name="sessionId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task DeleteAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);

        await using var command = new NpgsqlCommand(PostgresSessionQueries.DeleteInferenceSession, connection);
        command.Parameters.AddWithValue("@id", sessionId);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Gets a list of all session ids
    /// </summary>
    /// <param name="tagKey"></param>
    /// <param name="tagValue"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<List<Guid>> ListAsync(string? tagKey = null, string? tagValue = null, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);
        string sqlQuery;
        NpgsqlCommand command;

        if (string.IsNullOrWhiteSpace(tagKey) || string.IsNullOrWhiteSpace(tagValue))
        {
            sqlQuery = PostgresSessionQueries.ListAllSessionIds;
            command = new NpgsqlCommand(sqlQuery, connection);
        }
        else
        {
            // Build the JSON object in C# and pass it as a JSONB parameter
            var jsonObject = $"{{ \"{tagKey}\": \"{tagValue}\" }}";
            sqlQuery = "SELECT session_id FROM inference_sessions WHERE tags @> @tag::jsonb";

            command = new NpgsqlCommand(sqlQuery, connection);
            command.Parameters.AddWithValue("@tag", jsonObject);
        }

        await using var dataReader = await command.ExecuteReaderAsync(cancellationToken);

        var sessionIds = new List<Guid>();
        while (await dataReader.ReadAsync(cancellationToken))
            sessionIds.Add(dataReader.GetGuid(0));

        return sessionIds;
    }
}