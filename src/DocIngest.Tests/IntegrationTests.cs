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
        services.AddSingleton<IOcrService, TesseractOcrService>();
        services.AddSingleton<IDocumentGenerator, DefaultDocumentGenerator>();
        services.AddTransient<DocumentProcessingMiddleware>();
        services.AddTransient<DocumentTraversalMiddleware>();
        services.AddTransient<LoggingMiddleware>();
        services.AddTransient<AiCategorizationMiddleware>();
        services.AddTransient<DeliveryMiddleware>();
        services.AddSingleton<IDeliveryService>(sp => new FolderDeliveryService(Path.Combine(_tempOutputDir, "delivered"), sp.GetRequiredService<ILogger<FolderDeliveryService>>()));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { { "ImageProcessing:OutputFormat", "PDF" } })
            .Build());
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
        builder.Use(_serviceProvider.GetRequiredService<DeliveryMiddleware>());
        builder.Use(_serviceProvider.GetRequiredService<LoggingMiddleware>());
        var pipeline = builder.Build();

        // Act: Execute pipeline
        await pipeline(context);

        // Assert: Verify outputs (e.g., check for generated PDF file in output dir)
        var outputFiles = Directory.GetFiles(_tempOutputDir);
        Assert.NotEmpty(outputFiles); // At least one output file should exist
        Assert.True(outputFiles.Any(f => f.EndsWith(".pdf") || f.EndsWith(".txt")), "Expected a PDF or TXT file to be generated");
    }

    private void CreateSampleImage(string path, string text)
    {
        using var image = new Image<Rgba32>(400, 200);
        image.Mutate(ctx =>
        {
            ctx.Fill(Color.White);
            ctx.DrawText(text, SystemFonts.CreateFont("Arial", 20), Color.Black, new PointF(10, 10));
        });
        image.SaveAsPng(path);
    }
}