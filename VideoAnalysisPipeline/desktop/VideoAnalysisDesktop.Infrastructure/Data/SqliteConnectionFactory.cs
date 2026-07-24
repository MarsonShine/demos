using Microsoft.Data.Sqlite;

namespace VideoAnalysisDesktop.Infrastructure.Data;

/// <summary>
/// Creates and manages SQLite connections for the job database.
/// Database location: the configured desktop data root's jobs\jobs.db.
/// </summary>
public sealed class SqliteConnectionFactory : IDisposable
{
    private readonly string _connectionString;
    private bool _disposed;

    public SqliteConnectionFactory(string? dataRoot = null)
    {
        var root = dataRoot ?? DesktopDataPaths.GetJobsRoot();
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "jobs.db");
        _connectionString = $"Data Source={dbPath}";
    }

    public SqliteConnection CreateConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // WAL permits readers during writes. A bounded busy timeout lets a
        // second UI operation wait for a short transaction instead of failing
        // immediately with "database is locked".
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        cmd.ExecuteNonQuery();

        return conn;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SqliteConnection.ClearAllPools();
    }
}
