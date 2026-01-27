using Tesseract;
using System.Net.Http;

namespace DocIngest.Core.Services;

public class TesseractOcrService : IOcrService
{
    private readonly string _tessDataPath;
    private readonly HttpClient _httpClient = new();

    public TesseractOcrService()
    {
        _tessDataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");
        Directory.CreateDirectory(_tessDataPath);
        EnsureTessData();
    }

    private void EnsureTessData()
    {
        var engPath = Path.Combine(_tessDataPath, "eng.traineddata");
        if (!File.Exists(engPath))
        {
            // Download eng.traineddata
            var url = "https://github.com/tesseract-ocr/tessdata/raw/main/eng.traineddata";
            var data = _httpClient.GetByteArrayAsync(url).Result;
            File.WriteAllBytes(engPath, data);
        }
    }

    public async Task<string> ExtractTextAsync(byte[] imageBytes)
    {
        using var engine = new TesseractEngine(_tessDataPath, "eng", EngineMode.Default);
        using var img = Pix.LoadFromMemory(imageBytes);
        using var page = engine.Process(img);
        return page.GetText();
    }
}