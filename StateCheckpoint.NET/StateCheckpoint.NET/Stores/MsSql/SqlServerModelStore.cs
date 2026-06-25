using StateCheckpoint.NET.Models;
using Microsoft.Data.SqlClient;
using System.Text.Json;

namespace StateCheckpoint.NET.Stores.Mysql;

public class SqlServerModelStore : SqlServerStoreBase, IModelStore
{
    private static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = false };

    public SqlServerModelStore(string connectionString) : base(connectionString) { }
    public SqlServerModelStore(SqlConnection connection) : base(connection) { }

    /// <summary>
    /// Ensures the schema for the model is created
    /// </summary>
    /// <param name="CancellationToken"></param>
    /// <returns></returns>
    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);

        await using var command = new SqlCommand(SqlServerTrainingQueries.EnsureModelSchema, connection);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Saves a model checkpoint
    /// </summary>
    /// <param name="checkpoint"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>id for model checkpoint</returns>
    public async Task SaveAsync(ModelCheckpoint checkpoint, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);

        await using var command = new SqlCommand(SqlServerTrainingQueries.UpsertModelManifest, connection, tx as SqlTransaction);

        command.Parameters.AddWithValue("@Id", checkpoint.ModelId);
        command.Parameters.AddWithValue("@HyperParams", JsonSerializer.Serialize(checkpoint.HyperParams, _jsonOpts));
        command.Parameters.AddWithValue("@Tokenizer", JsonSerializer.Serialize(checkpoint.Tokenizer, _jsonOpts));
        command.Parameters.AddWithValue("@Epoch", checkpoint.CurrentEpoch);
        command.Parameters.AddWithValue("@Loss", checkpoint.LastTrainingLoss);
        command.Parameters.AddWithValue("@CreatedAt", checkpoint.CreatedAt);
        command.Parameters.AddWithValue("@Tags", JsonSerializer.Serialize(checkpoint.Tags, _jsonOpts));

        await command.ExecuteNonQueryAsync(cancellationToken);

        await using var blobCmd = new SqlCommand(SqlServerTrainingQueries.UpsertModelBlobs, connection, tx as SqlTransaction);

        blobCmd.Parameters.AddWithValue("@Id", checkpoint.ModelId);
        blobCmd.Parameters.AddWithValue("@WeightsData", checkpoint.WeightsBytes);
        blobCmd.Parameters.AddWithValue("@OptimizerData", checkpoint.OptimizerBytes);

        await blobCmd.ExecuteNonQueryAsync(cancellationToken);

        await tx.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Loads a training model by model id
    /// </summary>
    /// <param name="modelId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<ModelCheckpoint?> LoadAsync(Guid modelId, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);

        await using var command = new SqlCommand(SqlServerTrainingQueries.SelectFullModelManifest, connection);

        command.Parameters.AddWithValue("@Id", modelId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken)) return null;

        var hyperParams = JsonSerializer.Deserialize<HyperParameters>(reader.GetString(0))!;
        var tokenizer = JsonSerializer.Deserialize<TokenizerData>(reader.GetString(1))!;
        var epoch = reader.GetInt32(2);
        var loss = (float)reader.GetDouble(3);
        var createdAt = reader.GetDateTime(4);
        var tags = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(5))!;
        var weights = reader.GetFieldValue<byte[]>(6);
        var optimizer = reader.GetFieldValue<byte[]>(7);

        return new ModelCheckpoint
        {
            ModelId = modelId,
            WeightsBytes = weights,
            OptimizerBytes = optimizer,
            HyperParams = hyperParams,
            Tokenizer = tokenizer,
            CurrentEpoch = epoch,
            LastTrainingLoss = loss,
            CreatedAt = createdAt,
            Tags = tags
        };
    }

    /// <summary>
    /// Deletes a saved training model by training model id (modelId)
    /// </summary>
    /// <param name="modelId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task DeleteAsync(Guid modelId, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);

        await using var command = new SqlCommand(SqlServerTrainingQueries.DeleteModelManifest, connection);

        command.Parameters.AddWithValue("@Id", modelId);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// list all training model ids generated
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

        if (string.IsNullOrEmpty(tagKey) || string.IsNullOrEmpty(tagValue))
        {
            sql = SqlServerTrainingQueries.ListAllModelIds;
            command = new SqlCommand(sql, connection);
        }
        else
        {
            sql = SqlServerTrainingQueries.ListModelIdsByTag;
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