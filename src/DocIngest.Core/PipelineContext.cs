namespace DocIngest.Core;

/// <summary>
/// Represents the context for a pipeline execution, holding shared state and data.
/// </summary>
public class PipelineContext
{
    /// <summary>
    /// A dictionary for storing shared properties that can be updated by pipeline steps.
    /// </summary>
    public IDictionary<string, object> Items { get; } = new Dictionary<string, object>();
}