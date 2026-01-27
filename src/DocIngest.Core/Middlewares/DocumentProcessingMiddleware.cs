using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xceed.Document.NET;
using PdfSharp.Pdf;
using PdfSharp.Drawing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.IO;
using DocIngest.Core.Services;

namespace DocIngest.Core.Middlewares;

/// <summary>
/// Middleware that processes documents by handling images (combining pages, OCR) or passing through existing documents.
/// </summary>
public class DocumentProcessingMiddleware : IPipelineMiddleware
{
    private readonly IOcrService _ocrService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DocumentProcessingMiddleware> _logger;

    public DocumentProcessingMiddleware(IOcrService ocrService, IConfiguration configuration, ILogger<DocumentProcessingMiddleware> logger)
    {
        _ocrService = ocrService;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task InvokeAsync(PipelineContext context, PipelineDelegate next)
    {
        _logger.LogInformation("Starting document processing");

        var documents = context.Items["Documents"] as List<Document>;
        if (documents == null)
        {
            _logger.LogWarning("No documents found in context");
            await next(context);
            return;
        }

        var outputFormat = _configuration.GetValue<string>("ImageProcessing:OutputFormat") ?? "Word";
        var outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "output");
        Directory.CreateDirectory(outputDir);

        var processedDocuments = new List<string>();

        foreach (var document in documents)
        {
            var documentFiles = document.Files.Where(f => IsDocumentFile(f.Name)).ToList();
            if (documentFiles.Any())
            {
                // Add existing document files to processed list
                foreach (var docFile in documentFiles)
                {
                    processedDocuments.Add(docFile.Path);
                }
                _logger.LogInformation($"Added existing document files for {document.Name}");
                continue;
            }

            var imageFiles = document.Files
                .Where(f => IsImageFile(f.Name))
                .OrderBy(f => f.LastModified)
                .ToList();

            if (!imageFiles.Any())
            {
                _logger.LogInformation($"No processable files in document {document.Name}");
                continue;
            }

            _logger.LogInformation($"Processing document {document.Name} with {imageFiles.Count} images");

            // Load and combine images
            var combinedImage = await CombineImagesAsync(imageFiles);

            // OCR
            var text = await _ocrService.ExtractTextAsync(combinedImage);

            // Generate output
            var outputPath = await GenerateDocumentAsync(text, document.Name, outputFormat, outputDir);
            processedDocuments.Add(outputPath);
        }

        context.Items["ProcessedDocuments"] = processedDocuments;

        _logger.LogInformation("Document processing completed");
        await next(context);
    }

    private bool IsImageFile(string fileName)
    {
        var extensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".tiff" };
        return extensions.Any(ext => fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsDocumentFile(string fileName)
    {
        var extensions = new[] { ".docx", ".doc", ".pdf" };
        return extensions.Any(ext => fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<byte[]> CombineImagesAsync(List<FileInfo> imageFiles)
    {
        var images = new List<SixLabors.ImageSharp.Image<Rgba32>>();
        int totalHeight = 0;
        int maxWidth = 0;

        foreach (var file in imageFiles)
        {
            var image = await SixLabors.ImageSharp.Image.LoadAsync<Rgba32>(file.Path);
            images.Add(image);
            totalHeight += image.Height;
            maxWidth = Math.Max(maxWidth, image.Width);
        }

        var combined = new SixLabors.ImageSharp.Image<Rgba32>(maxWidth, totalHeight);
        int yOffset = 0;

        foreach (var image in images)
        {
            combined.Mutate(ctx => ctx.DrawImage(image, new Point(0, yOffset), 1f));
            yOffset += image.Height;
            image.Dispose();
        }

        using var ms = new MemoryStream();
        await combined.SaveAsPngAsync(ms);
        combined.Dispose();

        return ms.ToArray();
    }

    private async Task<string> GenerateDocumentAsync(string text, string documentName, string format, string outputDir)
    {
        var fileName = $"{documentName}.{format.ToLower()}";
        var outputPath = Path.Combine(outputDir, fileName);

        if (format.Equals("Word", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("Word format not yet implemented");
            // using var doc = Xceed.Document.NET.DocX.Create(outputPath);
            // doc.InsertParagraph(text);
            // doc.Save();
        }
        else if (format.Equals("PDF", StringComparison.OrdinalIgnoreCase))
        {
            var pdf = new PdfDocument();
            var page = pdf.AddPage();
            var gfx = XGraphics.FromPdfPage(page);
            var font = new XFont("Arial", 12);
            gfx.DrawString(text, font, XBrushes.Black, new XRect(10, 10, page.Width - 20, page.Height - 20), XStringFormats.TopLeft);
            pdf.Save(outputPath);
        }
        else
        {
            throw new NotSupportedException($"Output format {format} not supported");
        }

        return outputPath;
    }
}