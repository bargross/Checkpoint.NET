using Checkpoint.NET.Models;
using Npgsql;
using NpgsqlTypes;
using System.Text.Json;

namespace Checkpoint.NET.Stores.Postgres;

public class PostgresModelStore : PostgresStoreBase, IModelStore
{
    private static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = false };

    public PostgresModelStore(string connectionString) : base(connectionString) { }

    public PostgresModelStore(NpgsqlDataSource dataSource) : base(dataSource) { }

      /// <summary>
      /// Ensures the schema for the model is created
      /// </summary>
      /// <param name="CancellationToken"></param>
      /// <returns></returns>
    public async Task EnsureSchemaAsync(CancellationToken CancellationToken = default)
    {
        var connection = await GetConnectionAsync(CancellationToken);

        await using var command = new NpgsqlCommand(PostgresTrainingQueries.EnsureModelSchema, connection);

        await command.ExecuteNonQueryAsync(CancellationToken);
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

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await using var upsertCommand = new NpgsqlCommand(PostgresTrainingQueries.UpsertModelManifest, connection, transaction);

            upsertCommand.Parameters.AddWithValue("@id", checkpoint.ModelId);
            upsertCommand.Parameters.AddWithValue("@hp", JsonSerializer.Serialize(checkpoint.HyperParams, _jsonOpts));
            upsertCommand.Parameters.AddWithValue("@tok", JsonSerializer.Serialize(checkpoint.Tokenizer, _jsonOpts));
            upsertCommand.Parameters.AddWithValue("@epoch", checkpoint.CurrentEpoch);
            upsertCommand.Parameters.AddWithValue("@loss", checkpoint.LastTrainingLoss);
            upsertCommand.Parameters.AddWithValue("@now", checkpoint.CreatedAt);
            upsertCommand.Parameters.AddWithValue("@tags", JsonSerializer.Serialize(checkpoint.Tags, _jsonOpts));

            await upsertCommand.ExecuteNonQueryAsync(cancellationToken);

            uint oldWeightOid = 0, oldOptimizerOid = 0;
            await using var selectCommand = new NpgsqlCommand(PostgresTrainingQueries.SelectModelBlobOids, connection, transaction);
            selectCommand.Parameters.AddWithValue("@id", checkpoint.ModelId);

            await using var dataReader = await selectCommand.ExecuteReaderAsync(cancellationToken);
            if (await dataReader.ReadAsync(cancellationToken))
            {
                oldWeightOid = dataReader.GetFieldValue<uint>(0);
                oldOptimizerOid = dataReader.GetFieldValue<uint>(1);

                if (oldWeightOid == 0 || oldOptimizerOid == 0)
                    throw new InvalidOperationException($"Stored OID is zero: weight={oldWeightOid}, optimizer={oldOptimizerOid}");
            }
            await dataReader.CloseAsync();

            uint weightOid = await CreateLargeObjectAsync(connection, transaction, cancellationToken);
            uint optimizerOid = await CreateLargeObjectAsync(connection, transaction, cancellationToken);

            await WriteLargeObjectAsync(connection, transaction, weightOid, checkpoint.WeightsBytes, cancellationToken);
            await WriteLargeObjectAsync(connection, transaction, optimizerOid, checkpoint.OptimizerBytes, cancellationToken);

            if (oldWeightOid != 0) await UnlinkLargeObjectAsync(connection, transaction, oldWeightOid, cancellationToken);
            if (oldOptimizerOid != 0) await UnlinkLargeObjectAsync(connection, transaction, oldOptimizerOid, cancellationToken);

            await using var referenceCommand = new NpgsqlCommand(PostgresTrainingQueries.UpsertModelBlobRefs, connection, transaction);

            referenceCommand.Parameters.AddWithValue("@id", checkpoint.ModelId);
            referenceCommand.Parameters.AddWithValue("@wOid", weightOid).NpgsqlDbType = NpgsqlDbType.Oid;
            referenceCommand.Parameters.AddWithValue("@oOid", optimizerOid).NpgsqlDbType = NpgsqlDbType.Oid;

            await referenceCommand.ExecuteNonQueryAsync(cancellationToken);

            await using var verifyCommand = new NpgsqlCommand(
                "SELECT weights_oid, optimizer_oid FROM model_blobs WHERE model_id = @id",
                connection,
                transaction);

            verifyCommand.Parameters.AddWithValue("@id", checkpoint.ModelId);

            await using var verifyReader = await verifyCommand.ExecuteReaderAsync(cancellationToken);

            if (await verifyReader.ReadAsync(cancellationToken))
            {
                var storedWeightOid = verifyReader.GetFieldValue<uint>(0);
                var storedOptimizerOid = verifyReader.GetFieldValue<uint>(1);

                if (storedWeightOid == 0 || storedOptimizerOid == 0)
                    throw new InvalidOperationException($"Stored OID is zero after insert: weight={storedWeightOid}, optimizer={storedOptimizerOid}");
            }
            else
            {
                throw new InvalidOperationException("No OID references found after insert.");
            }
            await verifyReader.CloseAsync();

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);

            throw;
        }
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

        await using var command = new NpgsqlCommand(PostgresTrainingQueries.SelectFullModelManifest, connection);
        command.Parameters.AddWithValue("@id", modelId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken)) return null;

        var hyperParams = JsonSerializer.Deserialize<HyperParameters>(reader.GetString(0))!;
        var tokenizer = JsonSerializer.Deserialize<TokenizerData>(reader.GetString(1))!;
        var epoch = reader.GetInt32(2);
        var loss = (float)reader.GetDouble(3);
        var createdAt = reader.GetDateTime(4);
        var tags = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(5))!;
        var weightsOid = reader.GetFieldValue<uint>(6);
        var optimizerOid = reader.GetFieldValue<uint>(7);

        await reader.CloseAsync();

        var weights = await ReadLargeObjectAsync(connection, weightsOid, cancellationToken);
        var optimizer = await ReadLargeObjectAsync(connection, optimizerOid, cancellationToken);

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

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await using var selectCommand = new NpgsqlCommand(PostgresTrainingQueries.SelectModelBlobOids, connection, transaction);

            selectCommand.Parameters.AddWithValue("@id", modelId);

            await using var dataReader = await selectCommand.ExecuteReaderAsync(cancellationToken);

            if (await dataReader.ReadAsync(cancellationToken))
            {
                uint weightOid = dataReader.GetFieldValue<uint>(0);
                uint optimizerOid = dataReader.GetFieldValue<uint>(1);
                await dataReader.CloseAsync();

                await UnlinkLargeObjectAsync(connection, transaction, weightOid, cancellationToken);
                await UnlinkLargeObjectAsync(connection, transaction, optimizerOid, cancellationToken);
            }
            else await dataReader.CloseAsync();

            await using var deleteCommand = new NpgsqlCommand(PostgresTrainingQueries.DeleteModelManifest, connection, transaction);
            deleteCommand.Parameters.AddWithValue("@id", modelId);
            await deleteCommand.ExecuteNonQueryAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);

            throw;
        }
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
        string sqlQuery;
        NpgsqlCommand command;

        if (string.IsNullOrWhiteSpace(tagKey) || string.IsNullOrWhiteSpace(tagValue))
        {
            sqlQuery = PostgresTrainingQueries.ListAllModelIds;
            command = new NpgsqlCommand(sqlQuery, connection);
        }
        else
        {
            var jsonObject = $"{{ \"{tagKey}\": \"{tagValue}\" }}";
            sqlQuery = "SELECT model_id FROM model_manifests WHERE tags @> @tag::jsonb";

            command = new NpgsqlCommand(sqlQuery, connection);
            command.Parameters.AddWithValue("@tag", jsonObject);
        }

        await using var dataReader = await command.ExecuteReaderAsync(cancellationToken);

        var modelIds = new List<Guid>();
        while (await dataReader.ReadAsync(cancellationToken))
            modelIds.Add(dataReader.GetGuid(0));

        return modelIds;
    }

    //--------- private methods -----------------------------------

    // --- Large Object Helpers (Use PostgresLargeObjectQueries) ---
    private static async Task<uint> CreateLargeObjectAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(PostgresLargeObjectQueries.CreateLargeObject, connection, transaction);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        var oid = Convert.ToUInt32(result);

        if (oid == 0)
            throw new InvalidOperationException("Failed to create a new large object: returned OID is 0. Ensure the 'lo' extension is installed.");

        return oid;
    }

    private static async Task WriteLargeObjectAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        uint objectId,
        byte[] data,
        CancellationToken cancellationToken)
    {
        await using var openCommand = new NpgsqlCommand(PostgresLargeObjectQueries.OpenWrite, connection, transaction);

        openCommand.Parameters.AddWithValue("@oid", objectId).NpgsqlDbType = NpgsqlDbType.Oid;

        var fileDescriptor = Convert.ToInt32(await openCommand.ExecuteScalarAsync(cancellationToken));

        const int chunkSize = 8192;
        int offset = 0;
        while (offset < data.Length)
        {
            int bytesToWrite = Math.Min(chunkSize, data.Length - offset);
            byte[] dataChunk = new byte[bytesToWrite];

            Array.Copy(data, offset, dataChunk, 0, bytesToWrite);

            await using var writeCommand = new NpgsqlCommand(PostgresLargeObjectQueries.WriteChunk, connection, transaction);

            writeCommand.Parameters.AddWithValue("@fd", fileDescriptor).NpgsqlDbType = NpgsqlDbType.Integer;
            writeCommand.Parameters.AddWithValue("@data", dataChunk).NpgsqlDbType = NpgsqlDbType.Bytea;

            await writeCommand.ExecuteScalarAsync(cancellationToken);

            offset += bytesToWrite;
        }
    }

    private static async Task UnlinkLargeObjectAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        uint objectId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(PostgresLargeObjectQueries.UnlinkLargeObject, connection, transaction);

        command.Parameters.AddWithValue("@oid", objectId).NpgsqlDbType = NpgsqlDbType.Oid;

        await command.ExecuteScalarAsync(cancellationToken);
    }

    private static async Task<byte[]> ReadLargeObjectAsync(
        NpgsqlConnection connection,
        uint objectId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using var openCommand = new NpgsqlCommand(PostgresLargeObjectQueries.OpenRead, connection, transaction);
        openCommand.Parameters.AddWithValue("@oid", objectId).NpgsqlDbType = NpgsqlDbType.Oid;
        var fileDescriptor = Convert.ToInt32(await openCommand.ExecuteScalarAsync(cancellationToken));

        await using var sizeCommand = new NpgsqlCommand(PostgresLargeObjectQueries.GetSize, connection, transaction);
        sizeCommand.Parameters.AddWithValue("@fd", fileDescriptor).NpgsqlDbType = NpgsqlDbType.Integer;
        var size = Convert.ToInt64(await sizeCommand.ExecuteScalarAsync(cancellationToken));

        await using var seekCommand = new NpgsqlCommand(PostgresLargeObjectQueries.SeekStart, connection, transaction);
        seekCommand.Parameters.AddWithValue("@fd", fileDescriptor).NpgsqlDbType = NpgsqlDbType.Integer;
        await seekCommand.ExecuteScalarAsync(cancellationToken);

        using var memoryStream = new MemoryStream((int)size);
        const int chunkSize = 8192;
        var totalRead = 0;

        while (totalRead < size)
        {
            var bytesToRead = (int)Math.Min(chunkSize, size - totalRead);

            await using var readCommand = new NpgsqlCommand(PostgresLargeObjectQueries.ReadChunk, connection, transaction);
            readCommand.Parameters.AddWithValue("@fd", fileDescriptor).NpgsqlDbType = NpgsqlDbType.Integer;
            readCommand.Parameters.AddWithValue("@length", bytesToRead).NpgsqlDbType = NpgsqlDbType.Integer;

            var result = await readCommand.ExecuteScalarAsync(cancellationToken);

            if (result is byte[] dataChunk)
            {
                await memoryStream.WriteAsync(dataChunk, 0, dataChunk.Length, cancellationToken);
                totalRead += dataChunk.Length;
            }
            else break;
        }

        await transaction.CommitAsync(cancellationToken);

        return memoryStream.ToArray();
    }
}