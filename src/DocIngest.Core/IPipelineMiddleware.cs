namespace DocIngest.Core;

/// <summary>
/// Delegate for pipeline middleware steps.
/// </summary>
/// <param name="context">The pipeline context.</param>
/// <returns>A task representing the asynchronous operation.</returns>
public delegate Task PipelineDelegate(PipelineContext context);

/// <summary>
/// Interface for pipeline middleware components.
/// </summary>
public interface IPipelineMiddleware
{
    /// <summary>
    /// Invokes the middleware asynchronously.
    /// </summary>
    /// <param name="context">The pipeline context.</param>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task InvokeAsync(PipelineContext context, PipelineDelegate next);
}