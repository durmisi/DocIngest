using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using DocIngest.Core;
using DocIngest.Core.Services;
using DocIngest.Core.Middlewares;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.Fonts;
using System.IO;
using Xunit;
using Moq;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace DocIngest.Tests;

public class IntegrationTests : IDisposable
{
    private readonly string _tempInputDir;
    private readonly string _tempOutputDir;
    private readonly IServiceProvider _serviceProvider;

    public IntegrationTests()
    {
        // Create temp dirs for inputs and outputs
        _tempInputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _tempOutputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempInputDir);
        Directory.CreateDirectory(_tempOutputDir);

        // Setup DI with real components
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddConsole().SetMinimumLevel(LogLevel.Debug));
        // services.AddSingleton<IOcrService, TesseractOcrService>();
        services.AddSingleton<IDocumentGenerator, DefaultDocumentGenerator>();
        services.AddTransient<DocumentProcessingMiddleware>();
        services.AddTransient<DocumentTraversalMiddleware>();
        services.AddTransient<LoggingMiddleware>();
        services.AddTransient<AiCategorizationMiddleware>();
        services.AddTransient<DeliveryMiddleware>();
        services.AddSingleton<IDeliveryService>(sp => new FolderDeliveryService(Path.Combine(_tempOutputDir, "delivered"), sp.GetRequiredService<ILogger<FolderDeliveryService>>()));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { { "ImageProcessing:OutputFormat", "TXT" } })
            .Build());

        // Mock IChatClient for AI categorization
        var mockChatClient = new Mock<IChatClient>();
        mockChatClient.Setup(c => c.GetResponseAsync(It.IsAny<IList<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new[] { new ChatMessage(ChatRole.Assistant, "{\"Category\":\"invoice\",\"Tags\":[\"finance\",\"receipt\"],\"Insights\":[\"Total Amount: $100\",\"Date: 2023-01-01\"]}") }));
        services.AddSingleton(mockChatClient.Object);

        // Mock IOcrService for reliable OCR in tests
        var mockOcr = new Mock<IOcrService>();
        mockOcr.Setup(o => o.ExtractTextAsync(It.IsAny<byte[]>()))
            .ReturnsAsync("This is an invoice document with total amount $100.");
        services.AddSingleton<IOcrService>(mockOcr.Object);

        _serviceProvider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        // Cleanup temp dirs
        try { Directory.Delete(_tempInputDir, true); } catch { }
        try { Directory.Delete(_tempOutputDir, true); } catch { }
    }

    [Fact]
    public async Task ProcessDocumentPipelineExecutesSuccessfully()
    {
        // Arrange: Create sample input document (a subfolder with PNG image)
        var subDir = Path.Combine(_tempInputDir, "TestDoc");
        Directory.CreateDirectory(subDir);
        var inputFilePath = Path.Combine(subDir, "sample.png");
        CreateSampleImage(inputFilePath, "Test OCR Text");

        var context = new PipelineContext();
        context.Items["OutputDirectory"] = _tempOutputDir;

        // Build pipeline with real middlewares
        var builder = new PipelineBuilder();
        var loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();
        builder.Use(new DocumentTraversalMiddleware(_tempInputDir, loggerFactory.CreateLogger<DocumentTraversalMiddleware>()));
        builder.Use(new DocumentProcessingMiddleware(_serviceProvider.GetRequiredService<IOcrService>(), _serviceProvider.GetRequiredService<IConfiguration>(), loggerFactory.CreateLogger<DocumentProcessingMiddleware>(), _serviceProvider.GetRequiredService<IDocumentGenerator>()));
        builder.Use(_serviceProvider.GetRequiredService<AiCategorizationMiddleware>());
        builder.Use(_serviceProvider.GetRequiredService<DeliveryMiddleware>());
        builder.Use(_serviceProvider.GetRequiredService<LoggingMiddleware>());
        var pipeline = builder.Build();

        // Act: Execute pipeline
        await pipeline(context);

        // Assert: Verify outputs (e.g., check for delivered files in organized folder)
        var deliveredDir = Path.Combine(_tempOutputDir, "delivered");
        var outputFiles = Directory.GetFiles(deliveredDir, "*", SearchOption.AllDirectories);
        Assert.NotEmpty(outputFiles); // At least one output file should exist
        Assert.Contains(outputFiles, f => f.EndsWith(".pdf") || f.EndsWith(".txt"));

        // Assert AI categorization
        var documents = context.Items["Documents"] as List<Document>;
        Assert.NotNull(documents);
        Assert.Single(documents);
        var document = documents.First();
        Assert.NotEmpty(document.ProcessedFiles);
        var processedFile = document.ProcessedFiles.First();
        Assert.Equal("invoice", processedFile.Category);
        Assert.Contains("finance", processedFile.Tags);
        Assert.Contains("receipt", processedFile.Tags);
        Assert.Contains("Total Amount: $100", processedFile.Insights);
    }

    [Fact]
    public async Task ProcessMultiFileDocumentPipelineCombinesOutputs()
    {
        // Arrange: Create sample input document with multiple images
        var subDir = Path.Combine(_tempInputDir, "MultiPageDoc");
        Directory.CreateDirectory(subDir);
        CreateSampleImage(Path.Combine(subDir, "doc1.png"), "Page 1: Invoice Header");
        CreateSampleImage(Path.Combine(subDir, "doc2.png"), "Page 2: Invoice Details");
        CreateSampleImage(Path.Combine(subDir, "doc3.png"), "Page 3: Invoice Total");

        var context = new PipelineContext();
        context.Items["OutputDirectory"] = _tempOutputDir;

        // Build pipeline
        var builder = new PipelineBuilder();
        var loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();
        builder.Use(new DocumentTraversalMiddleware(_tempInputDir, loggerFactory.CreateLogger<DocumentTraversalMiddleware>()));
        builder.Use(new DocumentProcessingMiddleware(_serviceProvider.GetRequiredService<IOcrService>(), _serviceProvider.GetRequiredService<IConfiguration>(), loggerFactory.CreateLogger<DocumentProcessingMiddleware>(), _serviceProvider.GetRequiredService<IDocumentGenerator>()));
        builder.Use(_serviceProvider.GetRequiredService<AiCategorizationMiddleware>());
        builder.Use(_serviceProvider.GetRequiredService<DeliveryMiddleware>());
        builder.Use(_serviceProvider.GetRequiredService<LoggingMiddleware>());
        var pipeline = builder.Build();

        // Act
        await pipeline(context);

        // Assert
        var deliveredDir = Path.Combine(_tempOutputDir, "delivered");
        var outputFiles = Directory.GetFiles(deliveredDir, "*", SearchOption.AllDirectories);
        Assert.NotEmpty(outputFiles);
        // Should have combined PDF or TXT
        Assert.Contains(outputFiles, f => f.EndsWith(".pdf") || f.EndsWith(".txt"));

        // Check documents
        var documents = context.Items["Documents"] as List<Document>;
        Assert.NotNull(documents);
        Assert.Single(documents);
        var document = documents.First();
        Assert.Single(document.ProcessedFiles); // Combined into one document
    }

    [Theory]
    [InlineData("date")]
    [InlineData("year")]
    [InlineData("month")]
    [InlineData("name")]
    [InlineData("type")]
    public async Task DeliveryOrganizesByCriteria(string criteria)
    {
        // Arrange
        var subDir = Path.Combine(_tempInputDir, "OrgTestDoc");
        Directory.CreateDirectory(subDir);
        var inputFilePath = Path.Combine(subDir, "sample.png");
        CreateSampleImage(inputFilePath, "Test OCR Text");

        var context = new PipelineContext();
        context.Items["OutputDirectory"] = _tempOutputDir;
        context.Items["OrganizationCriteria"] = criteria;

        // Build pipeline
        var builder = new PipelineBuilder();
        var loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();
        builder.Use(new DocumentTraversalMiddleware(_tempInputDir, loggerFactory.CreateLogger<DocumentTraversalMiddleware>()));
        builder.Use(new DocumentProcessingMiddleware(_serviceProvider.GetRequiredService<IOcrService>(), _serviceProvider.GetRequiredService<IConfiguration>(), loggerFactory.CreateLogger<DocumentProcessingMiddleware>(), _serviceProvider.GetRequiredService<IDocumentGenerator>()));
        builder.Use(_serviceProvider.GetRequiredService<AiCategorizationMiddleware>());
        builder.Use(new DeliveryMiddleware(_serviceProvider.GetRequiredService<IDeliveryService>(), loggerFactory.CreateLogger<DeliveryMiddleware>()));
        builder.Use(_serviceProvider.GetRequiredService<LoggingMiddleware>());
        var pipeline = builder.Build();

        // Act
        await pipeline(context);

        // Assert folder structure
        var deliveredDir = Path.Combine(_tempOutputDir, "delivered");
        var subDirs = Directory.GetDirectories(deliveredDir);
        Assert.Single(subDirs);

        var orgDir = subDirs.First();
        var expectedKey = GetExpectedOrganizationKey(subDir, criteria);
        Assert.EndsWith(expectedKey, orgDir);

        if (criteria != "name")
        {
            var docDirs = Directory.GetDirectories(orgDir);
            Assert.Single(docDirs);
            Assert.EndsWith("OrgTestDoc", docDirs.First());
        }
    }

    private string GetExpectedOrganizationKey(string documentPath, string criteria)
    {
        var dirInfo = new DirectoryInfo(documentPath);
        return criteria.ToLowerInvariant() switch
        {
            "type" => "finance", // From mocked AI response
            "date" => dirInfo.CreationTime.ToString("yyyy-MM-dd"),
            "year" => dirInfo.CreationTime.ToString("yyyy"),
            "month" => dirInfo.CreationTime.ToString("yyyy-MM"),
            "name" => "OrgTestDoc",
            _ => dirInfo.CreationTime.ToString("yyyy-MM-dd")
        };
    }

    [Fact]
    public async Task HandlesInvalidAiResponseGracefully()
    {
        // Arrange: Mock invalid JSON response
        var mockChatClient = new Mock<IChatClient>();
        mockChatClient.Setup(c => c.GetResponseAsync(It.IsAny<IList<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new[] { new ChatMessage(ChatRole.Assistant, "invalid json") }));

        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddConsole().SetMinimumLevel(LogLevel.Debug));
        // services.AddSingleton<IOcrService, TesseractOcrService>();
        services.AddSingleton<IDocumentGenerator, DefaultDocumentGenerator>();
        services.AddTransient<DocumentProcessingMiddleware>();
        services.AddTransient<DocumentTraversalMiddleware>();
        services.AddTransient<LoggingMiddleware>();
        services.AddTransient<AiCategorizationMiddleware>();
        services.AddTransient<DeliveryMiddleware>();
        services.AddSingleton<IDeliveryService>(sp => new FolderDeliveryService(Path.Combine(_tempOutputDir, "delivered"), sp.GetRequiredService<ILogger<FolderDeliveryService>>()));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { { "ImageProcessing:OutputFormat", "TXT" } })
            .Build());
        services.AddSingleton(mockChatClient.Object);

        // Mock IOcrService
        var mockOcr2 = new Mock<IOcrService>();
        mockOcr2.Setup(o => o.ExtractTextAsync(It.IsAny<byte[]>()))
            .ReturnsAsync("This is an invoice document.");
        services.AddSingleton<IOcrService>(mockOcr2.Object);
        var provider = services.BuildServiceProvider();

        var subDir = Path.Combine(_tempInputDir, "ErrorTestDoc");
        Directory.CreateDirectory(subDir);
        var inputFilePath = Path.Combine(subDir, "sample.png");
        CreateSampleImage(inputFilePath, "Test OCR Text");

        var context = new PipelineContext();
        context.Items["OutputDirectory"] = _tempOutputDir;

        // Build pipeline
        var builder = new PipelineBuilder();
        var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
        builder.Use(new DocumentTraversalMiddleware(_tempInputDir, loggerFactory.CreateLogger<DocumentTraversalMiddleware>()));
        builder.Use(new DocumentProcessingMiddleware(provider.GetRequiredService<IOcrService>(), provider.GetRequiredService<IConfiguration>(), loggerFactory.CreateLogger<DocumentProcessingMiddleware>(), provider.GetRequiredService<IDocumentGenerator>()));
        builder.Use(provider.GetRequiredService<AiCategorizationMiddleware>());
        builder.Use(provider.GetRequiredService<DeliveryMiddleware>());
        builder.Use(provider.GetRequiredService<LoggingMiddleware>());
        var pipeline = builder.Build();

        // Act
        await pipeline(context);

        // Assert: Pipeline completes without throwing, but categorization fails
        var deliveredDir = Path.Combine(_tempOutputDir, "delivered");
        var outputFiles = Directory.GetFiles(deliveredDir, "*", SearchOption.AllDirectories);
        Assert.NotEmpty(outputFiles);

        var documents = context.Items["Documents"] as List<Document>;
        Assert.NotNull(documents);
        var document = documents.First();
        var processedFile = document.ProcessedFiles.First();
        // Category should be empty due to invalid JSON
        Assert.Equal(string.Empty, processedFile.Category);
    }

    // Commented out due to font issues in test environment
    // [Fact]
    // public async Task ProcessPdfDocumentExtractsTextAndCategorizes()
    // {
    //     // Arrange: Create a simple PDF
    //     var subDir = Path.Combine(_tempInputDir, "PdfDoc");
    //     Directory.CreateDirectory(subDir);
    //     var pdfPath = Path.Combine(subDir, "sample.pdf");
    //     CreateSamplePdf(pdfPath, "This is a sample invoice PDF content.");

    //     var context = new PipelineContext();
    //     context.Items["OutputDirectory"] = _tempOutputDir;

    //     // Build pipeline
    //     var builder = new PipelineBuilder();
    //     var loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();
    //     builder.Use(new DocumentTraversalMiddleware(_tempInputDir, loggerFactory.CreateLogger<DocumentTraversalMiddleware>()));
    //     builder.Use(new DocumentProcessingMiddleware(_serviceProvider.GetRequiredService<IOcrService>(), _serviceProvider.GetRequiredService<IConfiguration>(), loggerFactory.CreateLogger<DocumentProcessingMiddleware>(), _serviceProvider.GetRequiredService<IDocumentGenerator>()));
    //     builder.Use(_serviceProvider.GetRequiredService<AiCategorizationMiddleware>());
    //     builder.Use(_serviceProvider.GetRequiredService<DeliveryMiddleware>());
    //     builder.Use(_serviceProvider.GetRequiredService<LoggingMiddleware>());
    //     var pipeline = builder.Build();

    //     // Act
    //     await pipeline(context);

    //     // Assert
    //     var deliveredDir = Path.Combine(_tempOutputDir, "delivered");
    //     var outputFiles = Directory.GetFiles(deliveredDir, "*", SearchOption.AllDirectories);
    //     Assert.NotEmpty(outputFiles);

    //     var documents = context.Items["Documents"] as List<Document>;
    //     Assert.NotNull(documents);
    //     var document = documents.First();
    //     Assert.Single(document.ProcessedFiles);
    //     var processedFile = document.ProcessedFiles.First();
    //     Assert.Equal("invoice", processedFile.Category);
    // }

    private void CreateSampleImage(string path, string text)
    {
        using var image = new Image<Rgba32>(400, 200);
        image.Mutate(ctx =>
        {
            ctx.Fill(Color.White);
            ctx.DrawText(text, SystemFonts.CreateFont("Liberation Sans", 20), Color.Black, new PointF(10, 10));
        });
        image.SaveAsPng(path);
    }
}