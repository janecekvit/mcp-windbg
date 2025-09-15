using BackgroundService.Services;
using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Models;

namespace BackgroundService.Controllers;

[ApiController]
[Route("api/tasks")]
[Produces("application/json")]
public class TasksController : ControllerBase
{
    private readonly ILogger<TasksController> _logger;
    private readonly IBackgroundTaskService _backgroundTaskService;

    public TasksController(ILogger<TasksController> logger, IBackgroundTaskService backgroundTaskService)
    {
        _logger = logger;
        _backgroundTaskService = backgroundTaskService;
    }

    /// <summary>
    /// Starts loading a dump file in the background
    /// </summary>
    /// <param name="request">The dump loading request</param>
    /// <returns>Task ID for monitoring progress</returns>
    [HttpPost("load-dump")]
    [ProducesResponseType<BackgroundTaskResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> StartLoadDump([FromBody] LoadDumpRequest request)
    {
        try
        {
            var taskId = await _backgroundTaskService.StartLoadDumpAsync(request.DumpFilePath);
            _logger.LogInformation("Started background dump loading task {TaskId} for: {DumpFile}", taskId, request.DumpFilePath);

            return Ok(new BackgroundTaskResponse(taskId, $"Started loading dump in background. Use task ID {taskId} to check progress."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting background dump loading for: {DumpFile}", request.DumpFilePath);
            return Problem(
                detail: $"Error starting background task: {ex.Message}",
                statusCode: Constants.Http.BadRequest,
                title: "Background Task Start Failed");
        }
    }

    /// <summary>
    /// Starts basic analysis in the background
    /// </summary>
    /// <param name="request">The basic analysis request</param>
    /// <returns>Task ID for monitoring progress</returns>
    [HttpPost("basic-analysis")]
    [ProducesResponseType<BackgroundTaskResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> StartBasicAnalysis([FromBody] BasicAnalysisRequest request)
    {
        try
        {
            var taskId = await _backgroundTaskService.StartBasicAnalysisAsync(request.SessionId);
            _logger.LogInformation("Started background basic analysis task {TaskId} for session: {SessionId}", taskId, request.SessionId);

            return Ok(new BackgroundTaskResponse(taskId, $"Started basic analysis in background. Use task ID {taskId} to check progress."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting background basic analysis for session: {SessionId}", request.SessionId);
            return Problem(
                detail: $"Error starting background task: {ex.Message}",
                statusCode: Constants.Http.BadRequest,
                title: "Background Task Start Failed");
        }
    }

    /// <summary>
    /// Starts predefined analysis in the background
    /// </summary>
    /// <param name="request">The predefined analysis request</param>
    /// <returns>Task ID for monitoring progress</returns>
    [HttpPost("predefined-analysis")]
    [ProducesResponseType<BackgroundTaskResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> StartPredefinedAnalysis([FromBody] PredefinedAnalysisRequest request)
    {
        try
        {
            var taskId = await _backgroundTaskService.StartPredefinedAnalysisAsync(request.SessionId, request.AnalysisType);
            _logger.LogInformation("Started background predefined analysis task {TaskId} for session {SessionId}: {AnalysisType}",
                taskId, request.SessionId, request.AnalysisType);

            return Ok(new BackgroundTaskResponse(taskId, $"Started {request.AnalysisType} analysis in background. Use task ID {taskId} to check progress."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting background predefined analysis for session {SessionId}: {AnalysisType}",
                request.SessionId, request.AnalysisType);
            return Problem(
                detail: $"Error starting background task: {ex.Message}",
                statusCode: Constants.Http.BadRequest,
                title: "Background Task Start Failed");
        }
    }

    /// <summary>
    /// Starts command execution in the background
    /// </summary>
    /// <param name="request">The command execution request</param>
    /// <returns>Task ID for monitoring progress</returns>
    [HttpPost("execute-command")]
    [ProducesResponseType<BackgroundTaskResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> StartExecuteCommand([FromBody] ExecuteCommandRequest request)
    {
        try
        {
            var taskId = await _backgroundTaskService.StartExecuteCommandAsync(request.SessionId, request.Command);
            _logger.LogInformation("Started background command execution task {TaskId} for session {SessionId}: {Command}",
                taskId, request.SessionId, request.Command);

            return Ok(new BackgroundTaskResponse(taskId, $"Started command execution in background. Use task ID {taskId} to check progress."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting background command execution for session {SessionId}: {Command}",
                request.SessionId, request.Command);
            return Problem(
                detail: $"Error starting background task: {ex.Message}",
                statusCode: Constants.Http.BadRequest,
                title: "Background Task Start Failed");
        }
    }

    /// <summary>
    /// Gets the status of a background task
    /// </summary>
    /// <param name="taskId">The task ID</param>
    /// <returns>Task status information</returns>
    [HttpGet("{taskId}")]
    [ProducesResponseType<BackgroundTaskInfo>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public IActionResult GetTaskStatus(string taskId)
    {
        try
        {
            var tasks = _backgroundTaskService.GetAllTasks();
            var task = tasks.FirstOrDefault(t => t.TaskId == taskId);

            if (task == null)
            {
                return Problem(
                    detail: $"Task {taskId} not found",
                    statusCode: Constants.Http.NotFound,
                    title: "Task Not Found");
            }

            return Ok(task);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting task status for: {TaskId}", taskId);
            return Problem(
                detail: $"Error getting task status: {ex.Message}",
                statusCode: Constants.Http.InternalServerError,
                title: "Task Status Retrieval Failed");
        }
    }

    /// <summary>
    /// Cancels a background task
    /// </summary>
    /// <param name="taskId">The task ID to cancel</param>
    /// <returns>Cancellation confirmation</returns>
    [HttpDelete("{taskId}")]
    [ProducesResponseType<BackgroundTaskResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CancelTask(string taskId)
    {
        try
        {
            await _backgroundTaskService.CancelTaskAsync(taskId);
            _logger.LogInformation("Cancelled background task: {TaskId}", taskId);

            return Ok(new BackgroundTaskResponse(taskId, $"Task {taskId} has been cancelled."));
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "Task not found for cancellation: {TaskId}", taskId);
            return Problem(
                detail: $"Task {taskId} not found",
                statusCode: Constants.Http.NotFound,
                title: "Task Not Found");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling task: {TaskId}", taskId);
            return Problem(
                detail: $"Error cancelling task: {ex.Message}",
                statusCode: Constants.Http.InternalServerError,
                title: "Task Cancellation Failed");
        }
    }

    /// <summary>
    /// Lists all background tasks
    /// </summary>
    /// <returns>List of all background tasks</returns>
    [HttpGet]
    [ProducesResponseType<BackgroundTaskListResponse>(StatusCodes.Status200OK)]
    public IActionResult GetAllTasks()
    {
        var tasks = _backgroundTaskService.GetAllTasks();
        return Ok(new BackgroundTaskListResponse(tasks));
    }
}