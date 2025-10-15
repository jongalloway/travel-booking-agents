using Aspire.Hosting.GitHub;

var builder = DistributedApplication.CreateBuilder(args);

var model = GitHubModel.OpenAI.OpenAIGPT4oMini;

var chatDeployment = builder.AddGitHubModel("chat", model);

var api =
    builder.AddProject<Projects.TravelBookingAgents_API>("api")
        .WithIconName("BrainSparkle")
        .WithEnvironment("MODEL_NAME", model.Id)
        .WithReference(chatDeployment);

var web =
    builder.AddProject<Projects.TravelBookingAgents_Web>("web")
        .WithIconName("Globe")
        .WithReference(api);

// Expose the sample Chat UI during dev time
if (builder.ExecutionContext.IsRunMode)
{
    api.WithUrl("/index.html", "ChatUI (Legacy)");
    web.WithUrl("/", "ChatUI");
}

builder.Build().Run();