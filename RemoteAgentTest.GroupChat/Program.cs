using Azure.AI.OpenAI;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.Chat;
using Microsoft.SemanticKernel.ChatCompletion;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using RemoteAgentTest.GroupChat;

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

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.AddServiceDefaults();
builder.Services.AddHttpClient<TranslatorAgentHttpClient>(client => { client.BaseAddress = new("https+http://translatoragent"); });
builder.Services.AddHttpClient<SumamryAgentHttpClient>(client => { client.BaseAddress = new("https+http://summaryagent"); });
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

app.MapGet("/remote-group-chat", async (Kernel kernel, TranslatorAgentHttpClient translatorAgentHttpClient, SumamryAgentHttpClient sumamryAgentHttpClient) =>
{
    // Use the clients as needed here
    var translatorAgent = new RemoteChatCompletionAgent("translatoragent", translatorAgentHttpClient);
    var summaryAgent = new RemoteChatCompletionAgent("summaryagent", sumamryAgentHttpClient);

    var terminateFunction = KernelFunctionFactory.CreateFromPrompt(
        $$$"""
        Determine if the text has been summarized. If so, respond with a single word: yes.

        History:

        {{$history}}
        """
    );

    var selectionFunction = KernelFunctionFactory.CreateFromPrompt(
        $$$"""
        Your job is to determine which participant takes the next turn in a conversation according to the action of the most recent participant.
        State only the name of the participant to take the next turn.

        Choose only from these participants:
        - {{{translatorAgent.Name}}}
        - {{{summaryAgent.Name}}}

        Always follow these steps when selecting the next participant:
        1) After user input, it is {{{translatorAgent.Name}}}'s turn.
        2) After {{{translatorAgent.Name}}} replies, it's {{{summaryAgent.Name}}}'s turn.
        
        History:
        {{$history}}
        """
    );

    var chat = new AgentGroupChat(translatorAgent, summaryAgent)
    {
        ExecutionSettings = new()
        {
            TerminationStrategy = new KernelFunctionTerminationStrategy(terminateFunction, kernel)
            {
                Agents = [summaryAgent],
                ResultParser = (result) => result.GetValue<string>()?.Contains("yes", StringComparison.OrdinalIgnoreCase) ?? false,
                HistoryVariableName = "history",
                MaximumIterations = 10
            },
            SelectionStrategy = new KernelFunctionSelectionStrategy(selectionFunction, kernel)
            {
                HistoryVariableName = "history"
            }
        }
    };

    var prompt = "COME I FORNITORI INFLUENZANO I TUOI COSTI Quando scegli un piano di assicurazione sanitaria, uno dei fattori più importanti da considerare è la rete di fornitori in convenzione disponibili con il piano. Northwind Standard offre un'ampia varietà di fornitori in convenzione, tra cui medici di base, specialisti, ospedali e farmacie. Questo ti permette di scegliere un fornitore comodo per te e la tua famiglia, contribuendo al contempo a mantenere bassi i tuoi costi. Se scegli un fornitore in convenzione con il tuo piano, pagherai generalmente copay e franchigie più basse rispetto a un fornitore fuori rete. Inoltre, molti servizi, come l'assistenza preventiva, possono essere coperti senza alcun costo aggiuntivo se ricevuti da un fornitore in convenzione. È importante notare, tuttavia, che Northwind Standard non copre i servizi di emergenza, l'assistenza per la salute mentale e l'abuso di sostanze, né i servizi fuori rete. Questo significa che potresti dover pagare di tasca tua per questi servizi se ricevuti da un fornitore fuori rete. Quando scegli un fornitore in convenzione, ci sono alcuni suggerimenti da tenere a mente. Verifica che il fornitore sia in convenzione con il tuo piano. Puoi confermarlo chiamando l'ufficio del fornitore e chiedendo se è in rete con Northwind Standard. Puoi anche utilizzare lo strumento di ricerca fornitori sul sito web di Northwind Health per verificare la copertura. Assicurati che il fornitore stia accettando nuovi pazienti. Alcuni fornitori potrebbero essere in convenzione ma non accettare nuovi pazienti. Considera la posizione del fornitore. Se il fornitore è troppo lontano, potrebbe essere difficile raggiungere gli appuntamenti. Valuta gli orari dell'ufficio del fornitore. Se lavori durante il giorno, potresti aver bisogno di trovare un fornitore con orari serali o nel fine settimana. Scegliere un fornitore in convenzione può aiutarti a risparmiare sui costi sanitari. Seguendo i suggerimenti sopra e facendo ricerche sulle opzioni disponibili, puoi trovare un fornitore conveniente, accessibile e in rete con il tuo piano Northwind Standard.";
    chat.AddChatMessage(new ChatMessageContent(AuthorRole.User, prompt));
    await foreach (var content in chat.InvokeAsync())
    {
        Console.WriteLine();
        Console.WriteLine($"# {content.Role} - {content.AuthorName ?? "*"}: '{content.Content}'");
        Console.WriteLine();
    }

    return Results.Ok();
})
.WithName("InvokeRemoteGroupChat");

app.MapDefaultEndpoints();

app.Run();

public class TranslatorAgentHttpClient : RemoteAgentHttpClient
{
    public TranslatorAgentHttpClient(HttpClient httpClient) : base(httpClient)
    {
    }
}

public class SumamryAgentHttpClient : RemoteAgentHttpClient
{
    public SumamryAgentHttpClient(HttpClient httpClient) : base(httpClient)
    {
    }
}