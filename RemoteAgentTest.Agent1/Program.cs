using System.Text.Json;
using Azure.AI.OpenAI;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

var otelExporterEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
var otelExporterHeaders = builder.Configuration["OTEL_EXPORTER_OTLP_HEADERS"];

AppContext.SetSwitch("Microsoft.SemanticKernel.Experimental.GenAI.EnableOTelDiagnosticsSensitive", true);

var loggerFactory = LoggerFactory.Create(builder =>
{
    // Add OpenTelemetry as a logging provider
    builder.AddOpenTelemetry(options =>
    {
        options.AddOtlpExporter(exporter => {exporter.Endpoint = new Uri(otelExporterEndpoint); exporter.Headers = otelExporterHeaders; exporter.Protocol = OtlpExportProtocol.Grpc;});
        // Format log messages. This defaults to false.
        options.IncludeFormattedMessage = true;
    });

    builder.AddTraceSource("Microsoft.SemanticKernel");
    builder.SetMinimumLevel(LogLevel.Information);
});

using var traceProvider = Sdk.CreateTracerProviderBuilder()
    .AddSource("Microsoft.SemanticKernel*")
    .AddOtlpExporter(exporter => {exporter.Endpoint = new Uri(otelExporterEndpoint); exporter.Headers = otelExporterHeaders; exporter.Protocol = OtlpExportProtocol.Grpc;})
    .Build();

using var meterProvider = Sdk.CreateMeterProviderBuilder()
    .AddMeter("Microsoft.SemanticKernel*")
    .AddOtlpExporter(exporter => {exporter.Endpoint = new Uri(otelExporterEndpoint); exporter.Headers = otelExporterHeaders; exporter.Protocol = OtlpExportProtocol.Grpc;})
    .Build();

builder.Services.AddOpenApi();
builder.AddServiceDefaults();
builder.AddAzureOpenAIClient("openAiConnectionName");
builder.Services.AddSingleton(builder => {
    var kernelBuilder = Kernel.CreateBuilder();

    kernelBuilder.AddAzureOpenAIChatCompletion("gpt-4o", builder.GetService<AzureOpenAIClient>());
    
    return kernelBuilder.Build();
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();


app.MapPost("/agent/invoke", async (Kernel kernel, HttpResponse response, ChatHistory history) =>
{
    ChatCompletionAgent translatorAgent =
    new()
    {
        Name = "TranslatorAgent",
        Instructions = "Translate user input in english",
        Kernel = kernel
    };

    response.Headers.Append("Content-Type", "application/json");

    // Generate the agent response(s)
    await foreach (var chatResponse in translatorAgent.InvokeAsync(history))
    {
        chatResponse.AuthorName = translatorAgent.Name;
        
        return JsonSerializer.Serialize(chatResponse);
    }

    return null;
})
.WithName("InvokeTranslatorAgent");

app.MapPost("/agent/invoke-streaming", async (Kernel kernel, HttpResponse response, ChatHistory history) =>
{
    ChatCompletionAgent translatorAgent =
    new()
    {
        Name = "TranslatorAgent",
        Instructions = "Translate user input in english",
        Kernel = kernel
    };

    response.Headers.Append("Content-Type", "application/jsonl");

    var chatResponse = translatorAgent.InvokeStreamingAsync(history);
    await foreach (var delta in chatResponse)
    {
        var message = new StreamingChatMessageContent(AuthorRole.Assistant, delta.Content)
        {
            AuthorName = translatorAgent.Name
        };
        
        await response.WriteAsync(JsonSerializer.Serialize(message));
        await response.Body.FlushAsync();
    }
})
.WithName("InvokeTranslatorAgentStreaming");

app.MapDefaultEndpoints();

app.Run();