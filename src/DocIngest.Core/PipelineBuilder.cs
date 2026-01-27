namespace DocIngest.Core;

/// <summary>
/// Builder for constructing a pipeline of middleware components.
/// </summary>
public class PipelineBuilder
{
    private readonly List<Func<PipelineDelegate, PipelineDelegate>> _middlewares = new();

    /// <summary>
    /// Adds a middleware using a function delegate.
    /// </summary>
    /// <param name="middleware">The middleware function.</param>
    /// <returns>The pipeline builder for chaining.</returns>
    public PipelineBuilder Use(Func<PipelineContext, PipelineDelegate, Task> middleware)
    {
        _middlewares.Add(next => context => middleware(context, next));
        return this;
    }

    /// <summary>
    /// Adds a middleware implementing IPipelineMiddleware.
    /// </summary>
    /// <param name="middleware">The middleware instance.</param>
    /// <returns>The pipeline builder for chaining.</returns>
    public PipelineBuilder Use(IPipelineMiddleware middleware)
    {
        _middlewares.Add(next => context => middleware.InvokeAsync(context, next));
        return this;
    }

    /// <summary>
    /// Builds the pipeline delegate.
    /// </summary>
    /// <returns>The constructed pipeline delegate.</returns>
    public PipelineDelegate Build()
    {
        PipelineDelegate pipeline = _ => Task.CompletedTask; // Terminal
        foreach (var middleware in _middlewares.AsEnumerable().Reverse())
        {
            pipeline = middleware(pipeline);
        }
        return pipeline;
    }
}