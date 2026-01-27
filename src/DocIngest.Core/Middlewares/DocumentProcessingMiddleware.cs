using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.IO;
using DocIngest.Core.Services;
using Xceed.Words.NET;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;

namespace DocIngest.Core.Middlewares;

/// <summary>
/// Middleware that processes documents by handling images (combining pages, OCR) or passing through existing documents.
/// </summary>
public class DocumentProcessingMiddleware : IPipelineMiddleware
{
    private readonly IOcrService _ocrService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DocumentProcessingMiddleware> _logger;
    private readonly IDocumentGenerator _documentGenerator;

    public DocumentProcessingMiddleware(IOcrService ocrService, IConfiguration configuration, ILogger<DocumentProcessingMiddleware> logger, IDocumentGenerator documentGenerator)
    {
        _ocrService = ocrService;
        _configuration = configuration;
        _logger = logger;
        _documentGenerator = documentGenerator;
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
        var outputDir = context.Items.ContainsKey("OutputDirectory") ? context.Items["OutputDirectory"] as string : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "output");
        Directory.CreateDirectory(outputDir);

        var processedDocuments = new List<string>();

        foreach (var document in documents)
        {
            var documentFiles = document.Files.Where(f => IsDocumentFile(f.Name)).ToList();
            if (documentFiles.Any())
            {
                // Extract text from existing document files
                var contentBuilder = new System.Text.StringBuilder();
                foreach (var docFile in documentFiles)
                {
                    var extractedText = await ExtractTextFromDocumentAsync(docFile.Path);
                    contentBuilder.AppendLine(extractedText);
                    document.ProcessedDocuments.Add(docFile.Path);
                }
                document.Content = contentBuilder.ToString();
                _logger.LogInformation($"Extracted content from existing document files for {document.Name}");
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

            // Set content on document
            document.Content = text;

            // Generate output
            var outputPath = await _documentGenerator.GenerateDocumentAsync(text, document.Name, outputFormat, outputDir);
            processedDocuments.Add(outputPath);
            document.ProcessedDocuments.Add(outputPath);
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

    private async Task<string> ExtractTextFromDocumentAsync(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".docx" => ExtractTextFromDocx(filePath),
            ".doc" => ExtractTextFromDocx(filePath), // Assuming DocX can handle .doc, but may need conversion
            ".pdf" => ExtractTextFromPdf(filePath),
            _ => string.Empty
        };
    }

    private string ExtractTextFromDocx(string filePath)
    {
        using var doc = DocX.Load(filePath);
        return doc.Text;
    }

    private string ExtractTextFromPdf(string filePath)
    {
        using var pdfReader = new PdfReader(filePath);
        using var pdfDoc = new PdfDocument(pdfReader);
        var strategy = new SimpleTextExtractionStrategy();
        var extractedText = string.Empty;
        for (int i = 1; i <= pdfDoc.GetNumberOfPages(); i++)
        {
            var page = pdfDoc.GetPage(i);
            extractedText += PdfTextExtractor.GetTextFromPage(page, strategy);
        }
        return extractedText;
    }
}