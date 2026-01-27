using System;

namespace DocIngest.Core.Middlewares;

/// <summary>
/// Middleware that logs before and after pipeline execution.
/// </summary>
public class LoggingMiddleware : IPipelineMiddleware
{
    /// <summary>
    /// Invokes the logging middleware.
    /// </summary>
    /// <param name="context">The pipeline context.</param>
    /// <param name="next">The next middleware.</param>
    /// <returns>A task.</returns>
    public async Task InvokeAsync(PipelineContext context, PipelineDelegate next)
    {
        Console.WriteLine("Before processing");
        await next(context);
        Console.WriteLine("After processing");
    }
}