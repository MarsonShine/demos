using Microsoft.Data.Sqlite;

namespace VideoAnalysisDesktop.Infrastructure.Data;

/// <summary>
/// Persistent job ledger. All state mutations go through this repository so
/// that a resume creates a new attempt on the original job and only one job can
/// own the machine-wide processing lease at a time.
/// </summary>
public sealed class JobRepository
{
    private static readonly string[] ResumableStates = ["Failed", "Cancelled", "Interrupted"];
    private static readonly string[] ActiveStates = ["Running", "Cancelling"];

    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly object _schemaLock = new();
    private bool _schemaInitialized;

    public JobRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    private void EnsureSchema()
    {
        lock (_schemaLock)
        {
            if (_schemaInitialized)
                return;

            using var conn = _connectionFactory.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Jobs (
                    JobId TEXT PRIMARY KEY,
                    CreatedByUser TEXT NOT NULL,
                    InputRoot TEXT NOT NULL,
                    OutputRoot TEXT NOT NULL,
                    Status TEXT NOT NULL DEFAULT 'Draft',
                    PresetVersion TEXT NOT NULL,
                    PresetHash TEXT NOT NULL,
                    InputSnapshotHash TEXT NOT NULL,
                    EngineVersion TEXT,
                    StartedAtUtc TEXT,
                    EndedAtUtc TEXT,
                    ErrorSummary TEXT,
                    ResultPath TEXT
                );

                CREATE TABLE IF NOT EXISTS Attempts (
                    AttemptId TEXT PRIMARY KEY,
                    JobId TEXT NOT NULL,
                    SequenceNumber INTEGER NOT NULL,
                    IsResume INTEGER NOT NULL DEFAULT 0,
                    Pid INTEGER,
                    EventFilePath TEXT,
                    StdoutFilePath TEXT,
                    StderrFilePath TEXT,
                    ExitCode INTEGER,
                    StartedAtUtc TEXT,
                    EndedAtUtc TEXT,
                    LastEvent TEXT,
                    FOREIGN KEY (JobId) REFERENCES Jobs(JobId)
                );

                CREATE TABLE IF NOT EXISTS InputSnapshots (
                    JobId TEXT PRIMARY KEY,
                    SnapshotJson TEXT NOT NULL,
                    FOREIGN KEY (JobId) REFERENCES Jobs(JobId)
                );

                CREATE TABLE IF NOT EXISTS AppState (
                    Key TEXT PRIMARY KEY,
                    Value TEXT NOT NULL
                );

                -- One row is the atomic machine-wide job lease. A unique
                -- primary key makes concurrent starts fail deterministically.
                CREATE TABLE IF NOT EXISTS ActiveJobLease (
                    LeaseKey INTEGER PRIMARY KEY CHECK (LeaseKey = 1),
                    JobId TEXT NOT NULL UNIQUE,
                    AcquiredAtUtc TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS IX_Jobs_Status_StartedAtUtc ON Jobs(Status, StartedAtUtc DESC);
                CREATE UNIQUE INDEX IF NOT EXISTS IX_Attempts_Job_Sequence ON Attempts(JobId, SequenceNumber);
            ";
            cmd.ExecuteNonQuery();
            _schemaInitialized = true;
        }
    }

    public void CreateJob(JobRecord job)
    {
        EnsureSchema();
        using var conn = _connectionFactory.CreateConnection();
        InsertJob(conn, transaction: null, job);
    }

    /// <summary>
    /// Atomically creates the initial running job, snapshot, first attempt and
    /// active lease. False means another active job already owns the lease.
    /// </summary>
    public bool TryCreateRunningJob(JobRecord job, AttemptRecord attempt, string snapshotJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotJson);
        EnsureSchema();

        using var conn = _connectionFactory.CreateConnection();
        using var transaction = conn.BeginTransaction();
        try
        {
            AcquireLease(conn, transaction, job.JobId);
            InsertJob(conn, transaction, job);
            SaveInputSnapshot(conn, transaction, job.JobId, snapshotJson);
            InsertAttempt(conn, transaction, attempt);
            transaction.Commit();
            return true;
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            transaction.Rollback();
            return false;
        }
    }

    /// <summary>
    /// Atomically acquires the lease for an existing failed/cancelled/interrupted
    /// job and creates its next attempt.
    /// </summary>
    public bool TryBeginResumeAttempt(string jobId, AttemptRecord attempt)
    {
        EnsureSchema();
        using var conn = _connectionFactory.CreateConnection();
        using var transaction = conn.BeginTransaction();
        try
        {
            using var statusCommand = conn.CreateCommand();
            statusCommand.Transaction = transaction;
            statusCommand.CommandText = "SELECT Status FROM Jobs WHERE JobId = @JobId";
            statusCommand.Parameters.AddWithValue("@JobId", jobId);
            var status = statusCommand.ExecuteScalar()?.ToString();
            if (status is null || !ResumableStates.Contains(status, StringComparer.Ordinal))
            {
                transaction.Rollback();
                return false;
            }

            AcquireLease(conn, transaction, jobId);

            using var update = conn.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = @"
                UPDATE Jobs
                SET Status = 'Running', EndedAtUtc = NULL, ErrorSummary = NULL, ResultPath = NULL
                WHERE JobId = @JobId";
            update.Parameters.AddWithValue("@JobId", jobId);
            update.ExecuteNonQuery();

            InsertAttempt(conn, transaction, attempt);
            transaction.Commit();
            return true;
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            transaction.Rollback();
            return false;
        }
    }

    public void MarkJobRunning(string jobId)
    {
        EnsureSchema();
        using var conn = _connectionFactory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE Jobs
            SET Status = 'Running', EndedAtUtc = NULL, ErrorSummary = NULL, ResultPath = NULL
            WHERE JobId = @JobId";
        cmd.Parameters.AddWithValue("@JobId", jobId);
        cmd.ExecuteNonQuery();
    }

    public void UpdateJobStatus(
        string jobId,
        string status,
        string? errorSummary = null,
        string? endedAtUtc = null,
        string? resultPath = null)
    {
        EnsureSchema();
        using var conn = _connectionFactory.CreateConnection();
        using var transaction = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "UPDATE Jobs SET Status = @Status";
        if (errorSummary is not null)
            cmd.CommandText += ", ErrorSummary = @ErrorSummary";
        if (endedAtUtc is not null)
            cmd.CommandText += ", EndedAtUtc = @EndedAtUtc";
        if (resultPath is not null)
            cmd.CommandText += ", ResultPath = @ResultPath";
        cmd.CommandText += " WHERE JobId = @JobId";

        cmd.Parameters.AddWithValue("@JobId", jobId);
        cmd.Parameters.AddWithValue("@Status", status);
        if (errorSummary is not null)
            cmd.Parameters.AddWithValue("@ErrorSummary", errorSummary);
        if (endedAtUtc is not null)
            cmd.Parameters.AddWithValue("@EndedAtUtc", endedAtUtc);
        if (resultPath is not null)
            cmd.Parameters.AddWithValue("@ResultPath", resultPath);
        cmd.ExecuteNonQuery();

        if (IsTerminalOrResumable(status))
            ReleaseLease(conn, transaction, jobId);

        transaction.Commit();
    }

    /// <summary>
    /// Call only after the UI has established that no live application instance
    /// owns the named process mutex. This recovers jobs after a host crash.
    /// </summary>
    public void MarkRunningJobsInterrupted(string errorSummary)
    {
        EnsureSchema();
        using var conn = _connectionFactory.CreateConnection();
        using var transaction = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"
            UPDATE Jobs
            SET Status = 'Interrupted', EndedAtUtc = @EndedAtUtc, ErrorSummary = @ErrorSummary
            WHERE Status IN ('Running', 'Cancelling')";
        cmd.Parameters.AddWithValue("@EndedAtUtc", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@ErrorSummary", errorSummary);
        cmd.ExecuteNonQuery();

        using var lease = conn.CreateCommand();
        lease.Transaction = transaction;
        lease.CommandText = "DELETE FROM ActiveJobLease";
        lease.ExecuteNonQuery();
        transaction.Commit();
    }

    public JobRecord? GetJob(string jobId)
    {
        EnsureSchema();
        using var conn = _connectionFactory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Jobs WHERE JobId = @JobId";
        cmd.Parameters.AddWithValue("@JobId", jobId);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadJob(reader) : null;
    }

    public AttemptRecord? GetAttempt(string attemptId)
    {
        EnsureSchema();
        using var conn = _connectionFactory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT AttemptId, JobId, SequenceNumber, IsResume, Pid,
                   EventFilePath, StdoutFilePath, StderrFilePath, ExitCode,
                   StartedAtUtc, EndedAtUtc, LastEvent
            FROM Attempts
            WHERE AttemptId = @AttemptId";
        cmd.Parameters.AddWithValue("@AttemptId", attemptId);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadAttempt(reader) : null;
    }

    public JobRecord? GetLatestResumableJob()
    {
        EnsureSchema();
        using var conn = _connectionFactory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT * FROM Jobs
            WHERE Status IN ('Failed', 'Cancelled', 'Interrupted')
            ORDER BY COALESCE(EndedAtUtc, StartedAtUtc) DESC
            LIMIT 1";
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadJob(reader) : null;
    }

    public List<JobRecord> GetAllJobs()
    {
        EnsureSchema();
        var jobs = new List<JobRecord>();
        using var conn = _connectionFactory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Jobs ORDER BY StartedAtUtc DESC";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            jobs.Add(ReadJob(reader));
        return jobs;
    }

    public int GetNextAttemptSequence(string jobId)
    {
        EnsureSchema();
        using var conn = _connectionFactory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(SequenceNumber), 0) + 1 FROM Attempts WHERE JobId = @JobId";
        cmd.Parameters.AddWithValue("@JobId", jobId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public void CreateAttempt(AttemptRecord attempt)
    {
        EnsureSchema();
        using var conn = _connectionFactory.CreateConnection();
        InsertAttempt(conn, transaction: null, attempt);
    }

    public void UpdateAttemptProcess(string attemptId, int? processId)
    {
        EnsureSchema();
        using var conn = _connectionFactory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Attempts SET Pid = @Pid WHERE AttemptId = @AttemptId";
        cmd.Parameters.AddWithValue("@AttemptId", attemptId);
        cmd.Parameters.AddWithValue("@Pid", processId ?? (object)DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void UpdateAttemptLastEvent(string attemptId, string eventJson)
    {
        EnsureSchema();
        using var conn = _connectionFactory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Attempts SET LastEvent = @LastEvent WHERE AttemptId = @AttemptId";
        cmd.Parameters.AddWithValue("@AttemptId", attemptId);
        cmd.Parameters.AddWithValue("@LastEvent", eventJson);
        cmd.ExecuteNonQuery();
    }

    public void UpdateAttemptResult(string attemptId, int exitCode, string? endedAtUtc = null)
    {
        EnsureSchema();
        using var conn = _connectionFactory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Attempts SET ExitCode = @ExitCode, EndedAtUtc = @EndedAtUtc WHERE AttemptId = @AttemptId";
        cmd.Parameters.AddWithValue("@AttemptId", attemptId);
        cmd.Parameters.AddWithValue("@ExitCode", exitCode);
        cmd.Parameters.AddWithValue("@EndedAtUtc", endedAtUtc ?? DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public void SaveInputSnapshot(string jobId, string snapshotJson)
    {
        EnsureSchema();
        using var conn = _connectionFactory.CreateConnection();
        SaveInputSnapshot(conn, transaction: null, jobId, snapshotJson);
    }

    public string? GetInputSnapshot(string jobId)
    {
        EnsureSchema();
        using var conn = _connectionFactory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT SnapshotJson FROM InputSnapshots WHERE JobId = @JobId";
        cmd.Parameters.AddWithValue("@JobId", jobId);
        return cmd.ExecuteScalar()?.ToString();
    }

    public void SetAppState(string key, string value)
    {
        EnsureSchema();
        using var conn = _connectionFactory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO AppState (Key, Value) VALUES (@Key, @Value)";
        cmd.Parameters.AddWithValue("@Key", key);
        cmd.Parameters.AddWithValue("@Value", value);
        cmd.ExecuteNonQuery();
    }

    public string? GetAppState(string key)
    {
        EnsureSchema();
        using var conn = _connectionFactory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Value FROM AppState WHERE Key = @Key";
        cmd.Parameters.AddWithValue("@Key", key);
        return cmd.ExecuteScalar()?.ToString();
    }

    public string? GetRunningJobId()
    {
        EnsureSchema();
        using var conn = _connectionFactory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT JobId FROM Jobs WHERE Status IN ('Running', 'Cancelling') ORDER BY StartedAtUtc DESC LIMIT 1";
        return cmd.ExecuteScalar()?.ToString();
    }

    private static void InsertJob(SqliteConnection conn, SqliteTransaction? transaction, JobRecord job)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"
            INSERT INTO Jobs (
                JobId, CreatedByUser, InputRoot, OutputRoot, Status,
                PresetVersion, PresetHash, InputSnapshotHash, EngineVersion,
                StartedAtUtc, EndedAtUtc, ErrorSummary, ResultPath)
            VALUES (
                @JobId, @CreatedByUser, @InputRoot, @OutputRoot, @Status,
                @PresetVersion, @PresetHash, @InputSnapshotHash, @EngineVersion,
                @StartedAtUtc, @EndedAtUtc, @ErrorSummary, @ResultPath)";
        cmd.Parameters.AddWithValue("@JobId", job.JobId);
        cmd.Parameters.AddWithValue("@CreatedByUser", job.CreatedByUser);
        cmd.Parameters.AddWithValue("@InputRoot", job.InputRoot);
        cmd.Parameters.AddWithValue("@OutputRoot", job.OutputRoot);
        cmd.Parameters.AddWithValue("@Status", job.Status);
        cmd.Parameters.AddWithValue("@PresetVersion", job.PresetVersion);
        cmd.Parameters.AddWithValue("@PresetHash", job.PresetHash);
        cmd.Parameters.AddWithValue("@InputSnapshotHash", job.InputSnapshotHash);
        cmd.Parameters.AddWithValue("@EngineVersion", job.EngineVersion ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@StartedAtUtc", job.StartedAtUtc ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@EndedAtUtc", job.EndedAtUtc ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@ErrorSummary", job.ErrorSummary ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@ResultPath", job.ResultPath ?? (object)DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static void InsertAttempt(SqliteConnection conn, SqliteTransaction? transaction, AttemptRecord attempt)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"
            INSERT INTO Attempts (
                AttemptId, JobId, SequenceNumber, IsResume, Pid,
                EventFilePath, StdoutFilePath, StderrFilePath, StartedAtUtc,
                EndedAtUtc, LastEvent)
            VALUES (
                @AttemptId, @JobId, @SequenceNumber, @IsResume, @Pid,
                @EventFilePath, @StdoutFilePath, @StderrFilePath, @StartedAtUtc,
                @EndedAtUtc, @LastEvent)";
        cmd.Parameters.AddWithValue("@AttemptId", attempt.AttemptId);
        cmd.Parameters.AddWithValue("@JobId", attempt.JobId);
        cmd.Parameters.AddWithValue("@SequenceNumber", attempt.SequenceNumber);
        cmd.Parameters.AddWithValue("@IsResume", attempt.IsResume ? 1 : 0);
        cmd.Parameters.AddWithValue("@Pid", attempt.Pid ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@EventFilePath", attempt.EventFilePath ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@StdoutFilePath", attempt.StdoutFilePath ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@StderrFilePath", attempt.StderrFilePath ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@StartedAtUtc", attempt.StartedAtUtc ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@EndedAtUtc", attempt.EndedAtUtc ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@LastEvent", attempt.LastEvent ?? (object)DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static void SaveInputSnapshot(SqliteConnection conn, SqliteTransaction? transaction, string jobId, string snapshotJson)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"
            INSERT OR REPLACE INTO InputSnapshots (JobId, SnapshotJson)
            VALUES (@JobId, @SnapshotJson)";
        cmd.Parameters.AddWithValue("@JobId", jobId);
        cmd.Parameters.AddWithValue("@SnapshotJson", snapshotJson);
        cmd.ExecuteNonQuery();
    }

    private static void AcquireLease(SqliteConnection conn, SqliteTransaction transaction, string jobId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"
            INSERT INTO ActiveJobLease (LeaseKey, JobId, AcquiredAtUtc)
            VALUES (1, @JobId, @AcquiredAtUtc)";
        cmd.Parameters.AddWithValue("@JobId", jobId);
        cmd.Parameters.AddWithValue("@AcquiredAtUtc", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    private static void ReleaseLease(SqliteConnection conn, SqliteTransaction transaction, string jobId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "DELETE FROM ActiveJobLease WHERE JobId = @JobId";
        cmd.Parameters.AddWithValue("@JobId", jobId);
        cmd.ExecuteNonQuery();
    }

    private static bool IsTerminalOrResumable(string status)
        => status is "Succeeded" or "Failed" or "Cancelled" or "Interrupted";

    private static JobRecord ReadJob(SqliteDataReader reader)
        => new()
        {
            JobId = reader.GetString(0),
            CreatedByUser = reader.GetString(1),
            InputRoot = reader.GetString(2),
            OutputRoot = reader.GetString(3),
            Status = reader.GetString(4),
            PresetVersion = reader.GetString(5),
            PresetHash = reader.GetString(6),
            InputSnapshotHash = reader.GetString(7),
            EngineVersion = reader.IsDBNull(8) ? null : reader.GetString(8),
            StartedAtUtc = reader.IsDBNull(9) ? null : reader.GetString(9),
            EndedAtUtc = reader.IsDBNull(10) ? null : reader.GetString(10),
            ErrorSummary = reader.IsDBNull(11) ? null : reader.GetString(11),
            ResultPath = reader.IsDBNull(12) ? null : reader.GetString(12),
        };

    private static AttemptRecord ReadAttempt(SqliteDataReader reader)
        => new()
        {
            AttemptId = reader.GetString(0),
            JobId = reader.GetString(1),
            SequenceNumber = reader.GetInt32(2),
            IsResume = reader.GetInt32(3) != 0,
            Pid = reader.IsDBNull(4) ? null : reader.GetInt32(4),
            EventFilePath = reader.IsDBNull(5) ? null : reader.GetString(5),
            StdoutFilePath = reader.IsDBNull(6) ? null : reader.GetString(6),
            StderrFilePath = reader.IsDBNull(7) ? null : reader.GetString(7),
            ExitCode = reader.IsDBNull(8) ? null : reader.GetInt32(8),
            StartedAtUtc = reader.IsDBNull(9) ? null : reader.GetString(9),
            EndedAtUtc = reader.IsDBNull(10) ? null : reader.GetString(10),
            LastEvent = reader.IsDBNull(11) ? null : reader.GetString(11),
        };
}

public sealed class JobRecord
{
    public string JobId { get; set; } = "";
    public string CreatedByUser { get; set; } = "";
    public string InputRoot { get; set; } = "";
    public string OutputRoot { get; set; } = "";
    public string Status { get; set; } = "Draft";
    public string PresetVersion { get; set; } = "";
    public string PresetHash { get; set; } = "";
    public string InputSnapshotHash { get; set; } = "";
    public string? EngineVersion { get; set; }
    public string? StartedAtUtc { get; set; }
    public string? EndedAtUtc { get; set; }
    public string? ErrorSummary { get; set; }
    public string? ResultPath { get; set; }
}

public sealed class AttemptRecord
{
    public string AttemptId { get; set; } = "";
    public string JobId { get; set; } = "";
    public int SequenceNumber { get; set; }
    public bool IsResume { get; set; }
    public int? Pid { get; set; }
    public string? EventFilePath { get; set; }
    public string? StdoutFilePath { get; set; }
    public string? StderrFilePath { get; set; }
    public int? ExitCode { get; set; }
    public string? StartedAtUtc { get; set; }
    public string? EndedAtUtc { get; set; }
    public string? LastEvent { get; set; }
}
