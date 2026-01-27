namespace DocIngest.Core.Services;

public interface IOcrService
{
    Task<string> ExtractTextAsync(byte[] imageBytes);
}