var builder = DistributedApplication.CreateBuilder(args);

var openai = builder.AddConnectionString("openAiConnectionName");

var translatorAgent = builder.AddProject<Projects.RemoteAgentTest_Agent1>("translatoragent")
    .WithReference(openai);

var summaryAgent = builder.AddProject<Projects.RemoteAgentTest_Agent2>("summaryagent")
    .WithReference(openai);

var remoteChatCompletionAgent = builder.AddProject<Projects.RemoteAgentTest_GroupChat>("groupChat")
    .WithReference(openai)
    .WithReference(translatorAgent)
    .WithReference(summaryAgent);

builder.Build().Run();