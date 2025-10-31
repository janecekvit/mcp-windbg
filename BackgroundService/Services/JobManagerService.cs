using System.Collections.Concurrent;
using Shared.Models;
using Microsoft.AspNetCore.SignalR;
using BackgroundService.Hubs;

namespace BackgroundService.Services;

public sealed class JobManagerService : IJobManagerService, IDisposable
{
    private readonly ILogger<JobManagerService> _logger;
    private readonly IHubContext<ProgressHub> _hubContext;
    private readonly ConcurrentDictionary<string, JobStatusInternal> _jobs = new();
    private readonly Timer _cleanupTimer;
    private bool _disposed;

    private class JobStatusInternal
    {
        public string JobId { get; init; } = string.Empty;
        public string? SessionId { get; set; }
        public JobOperationType Operation { get; init; }
        public JobState State { get; set; }
        public double Progress { get; set; }
        public string? Message { get; set; }
        public DateTime CreatedAt { get; init; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string? Result { get; set; }
        public string? Error { get; set; }

        public JobStatus ToJobStatus() => new(
            JobId, SessionId, Operation, State, Progress, Message,
            CreatedAt, StartedAt, CompletedAt, Result, Error);
    }

    public JobManagerService(
        ILogger<JobManagerService> logger,
        IHubContext<ProgressHub> hubContext)
    {
        _logger = logger;
        _hubContext = hubContext;

        // Cleanup old jobs every 10 minutes
        _cleanupTimer = new Timer(
            _ => CleanupOldJobs(TimeSpan.FromHours(1)),
            null,
            TimeSpan.FromMinutes(10),
            TimeSpan.FromMinutes(10));
    }

    public string CreateJob(JobOperationType operation, string? sessionId = null)
    {
        var jobId = Guid.NewGuid().ToString("N")[..12]; // 12 character job ID
        var job = new JobStatusInternal
        {
            JobId = jobId,
            SessionId = sessionId,
            Operation = operation,
            State = JobState.Queued,
            Progress = 0.0,
            CreatedAt = DateTime.UtcNow
        };

        _jobs[jobId] = job;
        _logger.LogInformation("Created job {JobId} for operation {Operation}", jobId, operation);

        return jobId;
    }

    public JobStatus GetJobStatus(string jobId)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
        {
            throw new ArgumentException($"Job {jobId} not found", nameof(jobId));
        }

        return job.ToJobStatus();
    }

    public async Task UpdateProgressAsync(string jobId, double progress, string? message = null)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
        {
            _logger.LogWarning("Attempted to update progress for non-existent job: {JobId}", jobId);
            return;
        }

        // Update job state
        if (job.State == JobState.Queued)
        {
            job.State = JobState.Running;
            job.StartedAt = DateTime.UtcNow;
        }

        job.Progress = Math.Clamp(progress, 0.0, 1.0);
        job.Message = message;

        _logger.LogDebug("Job {JobId} progress: {Progress:P0} - {Message}",
            jobId, progress, message ?? "(no message)");

        // Send SignalR notification
        var notification = new ProgressNotification(
            jobId,
            job.Progress,
            message,
            DateTime.UtcNow);

        await _hubContext.Clients.Group(jobId).SendAsync("Progress", notification);
    }

    public async Task CompleteJobAsync(string jobId, string result)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
        {
            _logger.LogWarning("Attempted to complete non-existent job: {JobId}", jobId);
            return;
        }

        job.State = JobState.Completed;
        job.Progress = 1.0;
        job.CompletedAt = DateTime.UtcNow;
        job.Result = result;

        var duration = job.CompletedAt.Value - job.CreatedAt;
        _logger.LogInformation("Job {JobId} completed successfully in {Duration:mm\\:ss}",
            jobId, duration);

        // Send SignalR notification
        var notification = new JobCompletedNotification(
            jobId,
            true,
            result,
            null,
            DateTime.UtcNow);

        await _hubContext.Clients.Group(jobId).SendAsync("Completed", notification);
    }

    public async Task FailJobAsync(string jobId, string error)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
        {
            _logger.LogWarning("Attempted to fail non-existent job: {JobId}", jobId);
            return;
        }

        job.State = JobState.Failed;
        job.CompletedAt = DateTime.UtcNow;
        job.Error = error;

        var duration = job.CompletedAt.Value - job.CreatedAt;
        _logger.LogError("Job {JobId} failed after {Duration:mm\\:ss}: {Error}",
            jobId, duration, error);

        // Send SignalR notification
        var notification = new JobCompletedNotification(
            jobId,
            false,
            null,
            error,
            DateTime.UtcNow);

        await _hubContext.Clients.Group(jobId).SendAsync("Completed", notification);
    }

    public async Task CancelJobAsync(string jobId)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
        {
            _logger.LogWarning("Attempted to cancel non-existent job: {JobId}", jobId);
            return;
        }

        job.State = JobState.Cancelled;
        job.CompletedAt = DateTime.UtcNow;
        job.Error = "Job was cancelled by user";

        _logger.LogInformation("Job {JobId} was cancelled", jobId);

        // Send SignalR notification
        var notification = new JobCompletedNotification(
            jobId,
            false,
            null,
            "Job was cancelled",
            DateTime.UtcNow);

        await _hubContext.Clients.Group(jobId).SendAsync("Completed", notification);
    }

    public IEnumerable<JobStatus> GetAllJobs(JobState? filterByState = null)
    {
        var jobs = _jobs.Values.Select(j => j.ToJobStatus());

        if (filterByState.HasValue)
        {
            jobs = jobs.Where(j => j.State == filterByState.Value);
        }

        return jobs.OrderByDescending(j => j.CreatedAt).ToList();
    }

    public void CleanupOldJobs(TimeSpan maxAge)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        var jobsToRemove = _jobs.Values
            .Where(j => j.State is JobState.Completed or JobState.Failed or JobState.Cancelled)
            .Where(j => j.CompletedAt < cutoff)
            .Select(j => j.JobId)
            .ToList();

        foreach (var jobId in jobsToRemove)
        {
            if (_jobs.TryRemove(jobId, out _))
            {
                _logger.LogDebug("Cleaned up old job: {JobId}", jobId);
            }
        }

        if (jobsToRemove.Count > 0)
        {
            _logger.LogInformation("Cleaned up {Count} old jobs", jobsToRemove.Count);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _cleanupTimer?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
