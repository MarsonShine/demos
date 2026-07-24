using VideoAnalysisDesktop.Application.Models;
using VideoAnalysisDesktop.Application.Services;

namespace VideoAnalysisDesktop.Tests;

public class JobStateMachineTests
{
    [Fact]
    public void DraftToValidating_IsAllowed()
    {
        Assert.True(JobStateMachine.CanTransition(JobState.Draft, JobState.Validating));
    }

    [Fact]
    public void RunningToSucceeded_IsAllowed()
    {
        Assert.True(JobStateMachine.CanTransition(JobState.Running, JobState.Succeeded));
    }

    [Fact]
    public void RunningToFailed_IsAllowed()
    {
        Assert.True(JobStateMachine.CanTransition(JobState.Running, JobState.Failed));
    }

    [Fact]
    public void RunningToCancelling_IsAllowed()
    {
        Assert.True(JobStateMachine.CanTransition(JobState.Running, JobState.Cancelling));
    }

    [Fact]
    public void FailedToValidating_IsAllowed()
    {
        Assert.True(JobStateMachine.CanTransition(JobState.Failed, JobState.Validating));
    }

    [Fact]
    public void CancelledToValidating_IsAllowed()
    {
        Assert.True(JobStateMachine.CanTransition(JobState.Cancelled, JobState.Validating));
    }

    [Fact]
    public void SucceededToAnything_IsDisallowed()
    {
        Assert.False(JobStateMachine.CanTransition(JobState.Succeeded, JobState.Validating));
        Assert.False(JobStateMachine.CanTransition(JobState.Succeeded, JobState.Running));
        Assert.False(JobStateMachine.CanTransition(JobState.Succeeded, JobState.Failed));
    }

    [Fact]
    public void DraftToRunning_IsDisallowed()
    {
        Assert.False(JobStateMachine.CanTransition(JobState.Draft, JobState.Running));
    }

    [Fact]
    public void CanResume_OnlyForFailedCancelledInterrupted()
    {
        Assert.True(JobStateMachine.CanResume(JobState.Failed));
        Assert.True(JobStateMachine.CanResume(JobState.Cancelled));
        Assert.True(JobStateMachine.CanResume(JobState.Interrupted));
        Assert.False(JobStateMachine.CanResume(JobState.Draft));
        Assert.False(JobStateMachine.CanResume(JobState.Running));
        Assert.False(JobStateMachine.CanResume(JobState.Succeeded));
    }
}
