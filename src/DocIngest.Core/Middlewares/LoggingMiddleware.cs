using Microsoft.Extensions.Logging;

namespace DocIngest.Core.Middlewares;

/// <summary>
/// Middleware that logs before and after pipeline execution.
/// </summary>
public class LoggingMiddleware : IPipelineMiddleware
{
    private readonly ILogger<LoggingMiddleware> _logger;

    public LoggingMiddleware(ILogger<LoggingMiddleware> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Invokes the logging middleware.
    /// </summary>
    /// <param name="context">The pipeline context.</param>
    /// <param name="next">The next middleware.</param>
    /// <returns>A task.</returns>
    public async Task InvokeAsync(PipelineContext context, PipelineDelegate next)
    {
        _logger.LogInformation("Before processing");
        await next(context);
        _logger.LogInformation("After processing");
    }
}