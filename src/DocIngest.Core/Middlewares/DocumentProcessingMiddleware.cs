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
using System.Text.RegularExpressions;

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
        var outputDir = (context.Items["OutputDirectory"] as string) ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "output");
        Directory.CreateDirectory(outputDir);

        foreach (var document in documents)
        {
            if (!document.Files.Any())
            {
                _logger.LogInformation($"No files in document {document.Name}");
                continue;
            }

            // Process images
            var imageFiles = document.Files.Where(f => IsImageFile(f.Name)).OrderBy(f => GetPageNumber(f.Name)).ToList();
            var imageGroups = imageFiles.GroupBy(f => GetGroupKey(f.Name));
            foreach (var group in imageGroups)
            {
                var sortedGroup = group.OrderBy(f => GetPageNumber(f.Name)).ToList();
                var combinedImage = await CombineImagesAsync(sortedGroup);
                var ocrText = await _ocrService.ExtractTextAsync(combinedImage);
                var groupName = Path.GetFileNameWithoutExtension(group.Key);
                var outputPath = await _documentGenerator.GenerateDocumentAsync(ocrText, document.Name + "_" + groupName, outputFormat, outputDir);
                document.ProcessedFiles.Add(new ProcessedFile { Path = outputPath });
                _logger.LogInformation($"Processed image group {groupName} for {document.Name}, generated {outputPath}");
            }

            // Process PDFs
            var pdfFiles = document.Files.Where(f => IsPdfFile(f.Name)).ToList();
            var pdfGroups = pdfFiles.GroupBy(f => GetGroupKey(f.Name));
            foreach (var group in pdfGroups)
            {
                var texts = new List<string>();
                foreach (var pdf in group)
                {
                    var pdfText = ExtractTextFromPdf(pdf.Path);
                    texts.Add(pdfText);
                }
                var combinedText = string.Join("\n\n--- Page Break ---\n\n", texts);
                var groupName = Path.GetFileNameWithoutExtension(group.Key);
                var outputPath = await _documentGenerator.GenerateDocumentAsync(combinedText, document.Name + "_" + groupName, outputFormat, outputDir);
                document.ProcessedFiles.Add(new ProcessedFile { Path = outputPath });
                _logger.LogInformation($"Processed PDF group {groupName} for {document.Name}, generated {outputPath}");
            }

            // Add other files (not images or PDFs)
            var otherFiles = document.Files.Where(f => !IsImageFile(f.Name) && !IsPdfFile(f.Name)).ToList();
            foreach (var file in otherFiles)
            {
                document.ProcessedFiles.Add(new ProcessedFile { Path = file.Path });
                _logger.LogInformation($"Added file {file.Name} for {document.Name} to processed documents");
            }
        }

        context.Items["Documents"] = documents;

        _logger.LogInformation("Document processing completed");
        await next(context);
    }

    private bool IsImageFile(string fileName)
    {
        var extensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".tiff" };
        return extensions.Any(ext => fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
    }

   
    private bool IsPdfFile(string fileName) => fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

    
    private int GetPageNumber(string fileName)
    {
        var match = Regex.Match(fileName, @"\d+");
        return match.Success ? int.Parse(match.Value) : 0;
    }

    private string GetGroupKey(string fileName)
    {
        var match = Regex.Match(fileName, @"^(.*?)(\d+)(.*)$");
        if (match.Success)
        {
            return match.Groups[1].Value + match.Groups[3].Value;
        }
        return fileName;
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