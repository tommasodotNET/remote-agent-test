using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;

namespace RemoteAgentTest.GroupChat;

public class RemoteChatCompletionAgent : ChatHistoryKernelAgent
{
    private readonly RemoteAgentHttpClient _client;

    public RemoteChatCompletionAgent(string agentName, RemoteAgentHttpClient client)
    {
        Name = agentName;

        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public override async IAsyncEnumerable<ChatMessageContent> InvokeAsync(ChatHistory history, KernelArguments? arguments = null, Kernel? kernel = null, CancellationToken cancellationToken = default)
    {
        var response = await _client.InvokeAsync(history, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            
            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<ChatMessageContent>(content);

            yield return result;
        }
        else
        {
            throw new Exception($"Failed to invoke agent: {response.ReasonPhrase}");
        }
        
    }

    public override async IAsyncEnumerable<StreamingChatMessageContent> InvokeStreamingAsync(ChatHistory history, KernelArguments? arguments = null, Kernel? kernel = null, CancellationToken cancellationToken = default)
    {
        var response = await _client.InvokeStreamingAsync(history, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(stream);

            while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(line))
                {
                    var result = JsonSerializer.Deserialize<StreamingChatMessageContent>(line);
                    if (result != null)
                    {
                        yield return result;
                    }
                }
            }
        }
        else
        {
            throw new Exception($"Failed to invoke streaming agent: {response.ReasonPhrase}");
        }
    }

    protected override Task<AgentChannel> RestoreChannelAsync(string channelState, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}

public class RemoteAgentHttpClient(HttpClient httpClient)
{
    public Task<HttpResponseMessage> InvokeAsync(ChatHistory history, CancellationToken cancellationToken = default)
    {
        return httpClient.PostAsync("/agent/invoke", new StringContent(JsonSerializer.Serialize(history), Encoding.UTF8, "application/json"), cancellationToken);
    }

    public Task<HttpResponseMessage> InvokeStreamingAsync(ChatHistory history, CancellationToken cancellationToken = default)
    {
        return httpClient.PostAsync("/agent/invoke-streaming", new StringContent(JsonSerializer.Serialize(history), Encoding.UTF8, "application/json"), cancellationToken);
    }
}