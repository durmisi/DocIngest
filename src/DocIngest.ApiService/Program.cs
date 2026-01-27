using DocIngest.Core;
using DocIngest.Core.Middlewares;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire components.
builder.AddServiceDefaults();

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Example pipeline usage
app.MapGet("/process", async ([FromServices] ILogger<Program> logger) =>
{
    var pipelineBuilder = new PipelineBuilder();
    pipelineBuilder.Use(new LoggingMiddleware());
    string documentsFolder = "C:\\Temp\\Documents"; 
    pipelineBuilder.Use(new FolderTraversalMiddleware(documentsFolder, logger));
    pipelineBuilder.Use(async (context, next) =>
    {
        context.Items["Step1"] = "Value from Step1";
        await next(context);
    });
    pipelineBuilder.Use(async (context, next) =>
    {
        var value = context.Items["Step1"] as string;
        Console.WriteLine($"Processing: {value}");
        context.Items["Step2"] = "Updated in Step2";
        await next(context);
    });

    var pipeline = pipelineBuilder.Build();
    var context = new PipelineContext();
    await pipeline(context);

    return Results.Ok(context.Items);
});

app.Run();