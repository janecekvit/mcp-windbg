using Shared.Models;

namespace BackgroundService.Services;

public interface IJobManagerService
{
    /// <summary>
    /// Creates a new job and returns its ID
    /// </summary>
    string CreateJob(JobOperationType operation, string? sessionId = null);

    /// <summary>
    /// Gets the current status of a job
    /// </summary>
    JobStatus GetJobStatus(string jobId);

    /// <summary>
    /// Updates the progress of a running job
    /// </summary>
    Task UpdateProgressAsync(string jobId, double progress, string? message = null);

    /// <summary>
    /// Updates the phase of a running job
    /// </summary>
    Task UpdatePhaseAsync(string jobId, JobPhase phase, string? message = null);

    /// <summary>
    /// Marks a job as completed successfully
    /// </summary>
    Task CompleteJobAsync(string jobId, string result);

    /// <summary>
    /// Marks a job as failed
    /// </summary>
    Task FailJobAsync(string jobId, string error);

    /// <summary>
    /// Marks a job as cancelled
    /// </summary>
    Task CancelJobAsync(string jobId);

    /// <summary>
    /// Gets all jobs (optionally filtered by state)
    /// </summary>
    IEnumerable<JobStatus> GetAllJobs(JobState? filterByState = null);

    /// <summary>
    /// Cleans up completed/failed jobs older than the specified age
    /// </summary>
    void CleanupOldJobs(TimeSpan maxAge);
}
