namespace DocIngest.Core.Services;

/// <summary>
/// Interface for generating documents from text in various formats.
/// </summary>
public interface IDocumentGenerator
{
    /// <summary>
    /// Generates a document from the provided text and saves it to the specified output directory.
    /// </summary>
    /// <param name="text">The text content to include in the document.</param>
    /// <param name="documentName">The name of the document (used for file naming).</param>
    /// <param name="format">The output format (e.g., "Word", "PDF").</param>
    /// <param name="outputDir">The directory where the document will be saved.</param>
    /// <returns>The full path to the generated document file.</returns>
    Task<string> GenerateDocumentAsync(string text, string documentName, string format, string outputDir);
}