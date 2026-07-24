using VideoAnalysisDesktop.Application.Models;

namespace VideoAnalysisDesktop.Application.Services;

/// <summary>
/// Enforces valid job state transitions:
/// Draft → Validating → Ready → Running → Succeeded
///                              ├→ Failed
///                              ├→ Cancelling → Cancelled
/// Failed / Cancelled → Validating → Running
/// Succeeded → only "New Job"
/// </summary>
public sealed class JobStateMachine
{
    private static readonly Dictionary<JobState, HashSet<JobState>> ValidTransitions = new()
    {
        [JobState.Draft] = [JobState.Validating],
        [JobState.Validating] = [JobState.Ready, JobState.Failed],
        [JobState.Ready] = [JobState.Running, JobState.Failed],
        [JobState.Running] = [JobState.Succeeded, JobState.Failed, JobState.Cancelling],
        [JobState.Cancelling] = [JobState.Cancelled],
        [JobState.Cancelled] = [JobState.Validating],
        [JobState.Failed] = [JobState.Validating],
        [JobState.Interrupted] = [JobState.Validating],
        [JobState.Succeeded] = [], // Only "New Job" allowed
    };

    public static bool CanTransition(JobState from, JobState to)
    {
        return ValidTransitions.TryGetValue(from, out var allowed) && allowed.Contains(to);
    }

    public static bool CanStartNewJob(JobState current)
    {
        return current != JobState.Running && current != JobState.Cancelling;
    }

    public static bool CanResume(JobState current)
    {
        return current is JobState.Failed or JobState.Cancelled or JobState.Interrupted;
    }

    public static bool IsTerminal(JobState state)
    {
        return state is JobState.Succeeded or JobState.Cancelled;
    }
}
