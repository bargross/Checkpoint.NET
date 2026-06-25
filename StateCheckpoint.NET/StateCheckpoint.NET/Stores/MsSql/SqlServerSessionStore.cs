using StateCheckpoint.NET.Models;
using StateCheckpoint.NET.Stores.Mysql;
using Microsoft.Data.SqlClient;
using System.Text.Json;

namespace StateCheckpoint.NET.Stores;

public class SqlServerSessionStore : SqlServerStoreBase, ISessionStore
{
    private static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = false };

    public SqlServerSessionStore(string connectionString) : base(connectionString) { }
    public SqlServerSessionStore(SqlConnection connection) : base(connection) { }

    /// <summary>
    /// Ensures schema is created
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);

        await using var command = new SqlCommand(SqlServerSessionQueries.EnsureSessionSchema, connection);

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

        await using var tx = await connection.BeginTransactionAsync(cancellationToken);

        var sqlTx = (SqlTransaction)tx;

        await using var command = new SqlCommand(SqlServerSessionQueries.UpsertInferenceSession, connection, sqlTx);

        command.Parameters.AddWithValue("@Id", session.SessionId);
        command.Parameters.AddWithValue("@ModelFingerprint", session.ModelFingerprint);
        command.Parameters.AddWithValue("@TokenHistory", JsonSerializer.Serialize(session.TokenHistory, _jsonOpts));
        command.Parameters.AddWithValue("@SamplingConfig", JsonSerializer.Serialize(session.SamplingConfig, _jsonOpts));
        command.Parameters.AddWithValue("@KvCacheData", session.KvCacheBytes);
        command.Parameters.AddWithValue("@LastUpdated", session.LastUpdated);
        command.Parameters.AddWithValue("@Tags", JsonSerializer.Serialize(session.Tags, _jsonOpts));

        await command.ExecuteNonQueryAsync(cancellationToken);

        await tx.CommitAsync(cancellationToken);
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

        await using var command = new SqlCommand(SqlServerSessionQueries.SelectInferenceSession, connection);

        command.Parameters.AddWithValue("@Id", sessionId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken)) return null;

        return new SessionCheckpoint
        {
            SessionId = sessionId,
            ModelFingerprint = reader.GetString(0),
            TokenHistory = JsonSerializer.Deserialize<int[]>(reader.GetString(1))!,
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

        await using var command = new SqlCommand(SqlServerSessionQueries.DeleteInferenceSession, connection);

        command.Parameters.AddWithValue("@Id", sessionId);

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

        string sql;
        SqlCommand command;

        if (string.IsNullOrWhiteSpace(tagKey) || string.IsNullOrWhiteSpace(tagValue))
        {
            sql = SqlServerSessionQueries.ListAllSessionIds;
            command = new SqlCommand(sql, connection);
        }
        else
        {
            sql = SqlServerSessionQueries.ListSessionIdsByTag;
            command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@TagPattern", $"%\"{tagKey}\":\"{tagValue}\"%");
        }

        await using (command)
        {
            var list = new List<Guid>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
                list.Add(reader.GetGuid(0));

            return list;
        }
    }
}