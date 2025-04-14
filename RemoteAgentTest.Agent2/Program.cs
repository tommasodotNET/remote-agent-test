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

AppContext.SetSwitch("Microsoft.SemanticKernel.Experimental.GenAI.EnableOTelDiagnosticsSensitive", true);

builder.AddServiceDefaults();
builder.AddAzureOpenAIClient("openAiConnectionName");
builder.Services.AddOpenApi();
builder.Services.AddKernel().AddAzureOpenAIChatCompletion("gpt-4o");
builder.Services.AddSingleton<ChatCompletionAgent>(builder =>
{
    return new()
    {
        Name = "SummarizationAgent",
        Instructions = "Summarize user input",
        Kernel = builder.GetRequiredService<Kernel>()
    };
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/agent/details", (ChatCompletionAgent agent) =>
{
    var details = new 
    {
        Name = agent.Name,
        Instructions = agent.Instructions
    };
    return JsonSerializer.Serialize(details);
})
.WithName("GetAgentDetails");


app.MapPost("/agent/invoke", async (ChatCompletionAgent agent, HttpResponse response, ChatHistory history) =>
{
    response.Headers.Append("Content-Type", "application/json");

    var thread = new ChatHistoryAgentThread();

    await foreach (var chatResponse in agent.InvokeAsync(history, thread))
    {
        chatResponse.Message.AuthorName = agent.Name;
        
        return JsonSerializer.Serialize(chatResponse.Message);
    }

    return null;
})
.WithName("InvokeSummaryAgent");

app.MapPost("/agent/invoke-streaming", async (ChatCompletionAgent agent, HttpResponse response, ChatHistory history) =>
{
    response.Headers.Append("Content-Type", "application/jsonl");

    var thread = new ChatHistoryAgentThread();

    var chatResponse = agent.InvokeStreamingAsync(history, thread);
    await foreach (var delta in chatResponse)
    {
        var message = new StreamingChatMessageContent(AuthorRole.Assistant, delta.Message.Content)
        {
            AuthorName = agent.Name
        };

        await response.WriteAsync(JsonSerializer.Serialize(message));
        await response.Body.FlushAsync();
    }
})
.WithName("InvokeSummaryAgentStreaming");

app.MapDefaultEndpoints();

app.Run();