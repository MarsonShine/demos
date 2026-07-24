using VideoAnalysisDesktop.Infrastructure.Data;

namespace VideoAnalysisDesktop.Tests;

public sealed class JobRepositoryTests
{
    [Fact]
    public void TryCreateAndResumeJob_PersistsSnapshotAndEnforcesSingleActiveLease()
    {
        using var tmp = new TempDirectory();
        using var factory = new SqliteConnectionFactory(tmp.Path);
        var repository = new JobRepository(factory);
        var job = CreateJob("job-1", "Running");
        var firstAttempt = CreateAttempt("attempt-1", job.JobId, 1, isResume: false);

        Assert.True(repository.TryCreateRunningJob(job, firstAttempt, "[{\"index\":1}]"));
        Assert.False(repository.TryCreateRunningJob(
            CreateJob("job-2", "Running"),
            CreateAttempt("attempt-2", "job-2", 1, isResume: false),
            "[]"));

        var stored = Assert.IsType<JobRecord>(repository.GetJob(job.JobId));
        Assert.Equal(job.StartedAtUtc, stored.StartedAtUtc);
        Assert.Equal("[{\"index\":1}]", repository.GetInputSnapshot(job.JobId));

        repository.UpdateJobStatus(job.JobId, "Failed", "test failure", DateTime.UtcNow.ToString("O"));
        Assert.Equal(job.JobId, repository.GetLatestResumableJob()?.JobId);
        Assert.Equal(2, repository.GetNextAttemptSequence(job.JobId));

        var resumeAttempt = CreateAttempt("attempt-3", job.JobId, 2, isResume: true);
        Assert.True(repository.TryBeginResumeAttempt(job.JobId, resumeAttempt));
        Assert.Equal(job.JobId, repository.GetRunningJobId());

        repository.UpdateJobStatus(job.JobId, "Cancelled", endedAtUtc: DateTime.UtcNow.ToString("O"));
        Assert.Null(repository.GetRunningJobId());
    }

    [Fact]
    public void MarkRunningJobsInterrupted_ReleasesLeaseForRecovery()
    {
        using var tmp = new TempDirectory();
        using var factory = new SqliteConnectionFactory(tmp.Path);
        var repository = new JobRepository(factory);
        var job = CreateJob("job-1", "Running");
        Assert.True(repository.TryCreateRunningJob(job, CreateAttempt("attempt-1", job.JobId, 1, false), "[]"));

        repository.MarkRunningJobsInterrupted("app exited unexpectedly");

        Assert.Equal("Interrupted", repository.GetJob(job.JobId)?.Status);
        Assert.True(repository.TryCreateRunningJob(
            CreateJob("job-2", "Running"),
            CreateAttempt("attempt-2", "job-2", 1, false),
            "[]"));
    }

    [Fact]
    public void AttemptUpdates_PersistProcessAndLastEventDiagnostics()
    {
        using var tmp = new TempDirectory();
        using var factory = new SqliteConnectionFactory(tmp.Path);
        var repository = new JobRepository(factory);
        var job = CreateJob("job-1", "Running");
        var attempt = CreateAttempt("attempt-1", job.JobId, 1, false);
        Assert.True(repository.TryCreateRunningJob(job, attempt, "[]"));

        repository.UpdateAttemptProcess(attempt.AttemptId, 4321);
        repository.UpdateAttemptLastEvent(attempt.AttemptId, "{\"event\":\"stage_changed\"}");
        repository.UpdateAttemptResult(attempt.AttemptId, 0, "2026-01-01T00:00:00.0000000Z");

        var stored = Assert.IsType<AttemptRecord>(repository.GetAttempt(attempt.AttemptId));
        Assert.Equal(4321, stored.Pid);
        Assert.Equal("{\"event\":\"stage_changed\"}", stored.LastEvent);
        Assert.Equal(0, stored.ExitCode);
        Assert.Equal("2026-01-01T00:00:00.0000000Z", stored.EndedAtUtc);
    }

    private static JobRecord CreateJob(string jobId, string status) => new()
    {
        JobId = jobId,
        CreatedByUser = "test",
        InputRoot = @"C:\input",
        OutputRoot = @"C:\output",
        Status = status,
        PresetVersion = "1.0.0",
        PresetHash = "preset",
        InputSnapshotHash = "snapshot",
        EngineVersion = "engine",
        StartedAtUtc = DateTime.UtcNow.ToString("O"),
    };

    private static AttemptRecord CreateAttempt(string attemptId, string jobId, int sequence, bool isResume) => new()
    {
        AttemptId = attemptId,
        JobId = jobId,
        SequenceNumber = sequence,
        IsResume = isResume,
        StartedAtUtc = DateTime.UtcNow.ToString("O"),
    };
}
