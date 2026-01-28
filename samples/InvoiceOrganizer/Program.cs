using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.AI;
using OpenAI;
using DocIngest.Core;
using DocIngest.Core.Middlewares;
using DocIngest.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;

var inputPath = Path.Combine(Directory.GetCurrentDirectory(), "input/invoices");
var outputPath = Path.Combine(Directory.GetCurrentDirectory(), "output");
var tempPath = Path.Combine(Directory.GetCurrentDirectory(), "temp");
Directory.CreateDirectory(inputPath);
Directory.CreateDirectory(outputPath);
Directory.CreateDirectory(tempPath);

var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddSingleton<IOcrService, TesseractOcrService>();
        services.AddSingleton<IDocumentGenerator, DefaultDocumentGenerator>();
        services.AddSingleton<IDeliveryService>(sp => new FolderDeliveryService(outputPath, sp.GetRequiredService<ILogger<FolderDeliveryService>>()));
        // AI categorization commented out for now
        // if (!string.IsNullOrEmpty(apiKey))
        // {
        //     services.AddOpenAIChatClient(options => options.ApiKey = apiKey);
        // }
    })
    .Build();

var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
var traversalLogger = loggerFactory.CreateLogger<DocumentTraversalMiddleware>();
var processingLogger = loggerFactory.CreateLogger<DocumentProcessingMiddleware>();
// var aiLogger = loggerFactory.CreateLogger<AiCategorizationMiddleware>();
var deliveryLogger = loggerFactory.CreateLogger<DeliveryMiddleware>();

var ocrService = host.Services.GetRequiredService<IOcrService>();
var generator = host.Services.GetRequiredService<IDocumentGenerator>();
var deliveryService = host.Services.GetRequiredService<IDeliveryService>();
var config = host.Services.GetRequiredService<IConfiguration>();
// var chatClient = host.Services.GetService<IChatClient>();

var builder = new PipelineBuilder();
builder.Use(new DocumentTraversalMiddleware(inputPath, traversalLogger));
builder.Use(new DocumentProcessingMiddleware(ocrService, config, processingLogger, generator));
// if (chatClient != null)
// {
//     builder.Use(new AiCategorizationMiddleware(chatClient, ocrService, aiLogger));
// }
builder.Use(new DateParsingMiddleware(loggerFactory.CreateLogger<DateParsingMiddleware>()));
builder.Use(new DeliveryMiddleware(deliveryService, deliveryLogger));

var pipeline = builder.Build();

var context = new PipelineContext();
context.Items["OrganizationCriteria"] = "month";
context.Items["OutputDirectory"] = tempPath;

await pipeline(context);

var docs = context.Items["Documents"] as List<Document>;
Console.WriteLine($"Found {docs?.Count ?? 0} documents");
if (docs != null)
{
    foreach (var doc in docs)
    {
        Console.WriteLine($"Document: {doc.Name}, Files: {doc.Files.Count}, Processed: {doc.ProcessedFiles.Count}");
    }
}

Console.WriteLine("Processing complete.");
