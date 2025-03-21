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


app.MapPost("/agent/invoke", async (Kernel kernel, ChatHistory history) =>
{
    ChatCompletionAgent summaryAgent =
    new()
    {
        Name = "SummarizationAgent",
        Instructions = "Summarize user input",
        Kernel = kernel
    };

    // Generate the agent response(s)
    await foreach (var response in summaryAgent.InvokeAsync(history))
    {
        response.AuthorName = summaryAgent.Name;
        
        return JsonSerializer.Serialize(response);
    }

    return null;
})
.WithName("InvokeSummaryAgent");

app.MapPost("/agent/invoke-streaming", async (Kernel kernel, HttpResponse response, ChatHistory history) =>
{
    ChatCompletionAgent summaryAgent =
    new()
    {
        Name = "SummarizationAgent",
        Instructions = "Summarize user input",
        Kernel = kernel
    };

    response.Headers.Append("Content-Type", "application/jsonl");

    var chatResponse = summaryAgent.InvokeStreamingAsync(history);
    await foreach (var delta in chatResponse)
    {
        var message = new StreamingChatMessageContent(AuthorRole.Assistant, delta.Content)
        {
            AuthorName = summaryAgent.Name
        };

        await response.WriteAsync(JsonSerializer.Serialize(message));
        await response.Body.FlushAsync();
    }
})
.WithName("InvokeSummaryAgentStreaming");

app.MapDefaultEndpoints();

app.Run();