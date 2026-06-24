namespace Checkpoint.NET.Stores;

/// <summary>
/// Low-level PostgreSQL Large Object SQL functions.
/// Used exclusively by PostgresModelStore (Training half) to store weights/optimizer > 2GB.
/// </summary>
internal static class PostgresLargeObjectQueries
{
    // --- Large Object Lifecycle ---
    public const string CreateLargeObject = "SELECT pg_catalog.lo_create(0);";
    public const string CloseLargeObject = "SELECT pg_catalog.lo_close(@fd);";
    public const string UnlinkLargeObject = "SELECT pg_catalog.lo_unlink(@oid);";

    // --- Opening Modes ---
    public const string OpenWrite = "SELECT pg_catalog.lo_open(@oid, 131072);";
    public const string OpenRead = "SELECT pg_catalog.lo_open(@oid, 262144);";
    
    // --- Read/Write Operations ---
    public const string WriteChunk = "SELECT pg_catalog.lowrite(@fd, @data);";
    public const string ReadChunk = "SELECT pg_catalog.loread(@fd, @length);";
    
    // --- Seeking / Sizing ---
    public const string GetSize = "SELECT pg_catalog.lo_lseek(@fd, 0, 2);";
    public const string SeekStart = "SELECT pg_catalog.lo_lseek(@fd, 0, 0);";
}