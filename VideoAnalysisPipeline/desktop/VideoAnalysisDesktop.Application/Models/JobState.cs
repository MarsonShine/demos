namespace VideoAnalysisDesktop.Application.Models;

/// <summary>
/// Fixed job states following the state machine:
/// Draft → Validating → Ready → Running → Succeeded
///                              ├→ Failed
///                              ├→ Cancelling → Cancelled
/// Failed / Cancelled → Validating → Running
/// Succeeded → only "New Job" allowed
/// </summary>
public enum JobState
{
    Draft,
    Validating,
    Ready,
    Running,
    Succeeded,
    Failed,
    Cancelling,
    Cancelled,
    Interrupted
}
