using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using TravelBookingAgents.API;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddOpenApi();

builder.Services.AddSingleton<IChatClient>(_ =>
{
    var apiKey = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        Console.WriteLine("WARNING: GITHUB_TOKEN not set. LLM calls will fail.");
        apiKey = "invalid";
    }
    return new ChatClient(
        model: "gpt-4o-mini",
        credential: new ApiKeyCredential(apiKey!),
        options: new OpenAIClientOptions { Endpoint = new Uri("https://models.github.ai/inference") }
    ).AsIChatClient();
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/", () => Results.Text("Travel Booking Agents API"));

static AIAgent BuildWorkflowAgent(IChatClient chatClient)
{
    [Description("Search for available flights")] string SearchFlights(string origin, string destination, DateTime departureDate) =>
        $"Flights {origin}->{destination} {departureDate:MMM d}: UA123 $485 direct; DL456 $395 1-stop; AS789 $520 direct.";

    [Description("Search for available hotels")] string SearchHotels(string destination, DateTime checkIn, DateTime checkOut)
    {
        var nights = (checkOut - checkIn).Days;
        return $"Hotels {destination} {nights} nights: Hyatt $245; Marriott $195; Hilton $285.";
    }

    [Description("Create booking confirmation")] string CreateBooking(string flightDetails, string hotelDetails, int nights)
    {
        var code = $"CONF-{Guid.NewGuid().ToString()[..8].ToUpper()}";
        return $"BOOKING CONFIRMED {code}\nFlight: {flightDetails}\nHotel: {hotelDetails} ({nights} nights)";
    }

    var research = new ChatClientAgent(chatClient, new ChatClientAgentOptions
    {
        Name = "TravelResearch",
        Instructions = "Research flights & hotels; ALWAYS call tools; output top concise options.",
        ChatOptions = new ChatOptions { Tools = [AIFunctionFactory.Create(SearchFlights), AIFunctionFactory.Create(SearchHotels)] }
    });
    var policy = new ChatClientAgent(chatClient, new ChatClientAgentOptions
    {
        Name = "PolicyCompliance",
        Instructions = "Check policy: approved destination, >=14 day advance, flight <= $800, hotel <= $300. Suggest compliant alternatives."
    });
    var budget = new ChatClientAgent(chatClient, new ChatClientAgentOptions
    {
        Name = "BudgetApproval",
        Instructions = "Estimate total trip cost; if > $2500 require manager approval; assume Engineering budget adequate unless noted."
    });
    var optimizer = new ChatClientAgent(chatClient, new ChatClientAgentOptions
    {
        Name = "ItineraryOptimizer",
        Instructions = "Optimize for cost vs. convenience; minimize layovers; explain trade-offs; confirm if optimal."
    });
    var booking = new ChatClientAgent(chatClient, new ChatClientAgentOptions
    {
        Name = "BookingCoordinator",
        Instructions = "If approved, finalize and produce confirmation + full itinerary summary.",
        ChatOptions = new ChatOptions { Tools = [AIFunctionFactory.Create(CreateBooking)] }
    });

    var workflow = AgentWorkflowBuilder
        .CreateGroupChatBuilderWith(agents => new AgentWorkflowBuilder.RoundRobinGroupChatManager(agents)
        { MaximumIterationCount = 6 })
        .AddParticipants(research, policy, budget, optimizer, booking)
        .Build();

    return workflow.AsAgentAsync().GetAwaiter().GetResult();
}

app.MapGet("/agent/chat", async (string prompt, IChatClient chatClient) =>
{
    var agent = BuildWorkflowAgent(chatClient);
    var run = await agent.RunAsync(prompt);
    return Results.Json(new AgentResponseDto(run.Text, new List<MessageDto>()));
});

app.MapGet("/agent/chat/stream", async (HttpContext ctx, string prompt, IChatClient chatClient, bool sequential = true, bool debug = false) =>
{
    ctx.Response.Headers.Append("Content-Type", "text/event-stream");
    ctx.Response.Headers.Append("Cache-Control", "no-cache");

    async Task Send(object payload)
    {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await ctx.Response.WriteAsync($"data: {json}\n\n");
        await ctx.Response.Body.FlushAsync();
    }

    // Determine if we can call a real model or must run in offline simulation mode
    var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
    var offline = string.IsNullOrWhiteSpace(token) || token == "invalid";

    // Canonical list of agents & their purposes (mirrors construction in BuildWorkflowAgent)
    var agentDefinitions = new (string Name, string Purpose)[]
    {
        ("TravelResearch", "Research flights & hotels; ALWAYS call tools; output concise ranked options."),
        ("PolicyCompliance", "Validate corporate travel policy: advance purchase, destination approval, cost thresholds."),
        ("BudgetApproval", "Estimate total trip cost and flag if exceeding budget caps; note escalation needs."),
        ("ItineraryOptimizer", "Balance cost vs convenience; reduce layovers; produce optimal rationale."),
        ("BookingCoordinator", "If approvals achieved, finalize itinerary and produce booking confirmation.")
    };

    try
    {
        // Emit an initial kick-off message
        await Send(new { agent = "system", status = "working", message = "Initializing travel booking workflow..." });

        var aggregatedBuilder = new System.Text.StringBuilder();
        aggregatedBuilder.AppendLine($"User request: {prompt}\n");

        if (offline)
        {
            // Offline deterministic simulation so the demo works without a token
            foreach (var (name, purpose) in agentDefinitions)
            {
                await Send(new { agent = name, status = "working", message = $"{purpose}" });
                await Task.Delay(350); // brief stagger for UX feel
                var stepResult = name switch
                {
                    "TravelResearch" => "Found 2 flight options (non-stop vs 1-stop) and 3 hotels within policy.",
                    "PolicyCompliance" => "All options within policy: advance booking OK; costs under limits.",
                    "BudgetApproval" => "Projected total cost ~$1,950 (air $560 + hotel $780 + misc $610) < $2,500 cap.",
                    "ItineraryOptimizer" => "Selected non-stop morning outbound + afternoon return; mid-tier business hotel near venue.",
                    "BookingCoordinator" => "Generated confirmation code CONF-DEMO123 with full itinerary summary.",
                    _ => "Step complete."
                };
                aggregatedBuilder.AppendLine($"[{name}] {stepResult}");
                // Also emit a secondary progress note (still 'working') so UI can reflect evolving transcript if desired
                await Send(new { agent = name, status = "working", message = stepResult });
            }

            var finalText = aggregatedBuilder.ToString();
            await Send(new { agent = "system", status = "complete", response = new AgentResponseDto(finalText, null) });
            return; // done in offline mode
        }

        if (!sequential)
        {
            // Legacy workflow path (kept for comparison / future use)
            foreach (var (name, purpose) in agentDefinitions)
            {
                await Send(new { agent = name, status = "working", message = purpose });
            }
            var workflowAgent = BuildWorkflowAgent(chatClient);
            var runLegacy = await workflowAgent.RunAsync(prompt);
            await Send(new { agent = "system", status = "complete", response = new AgentResponseDto(runLegacy.Text, null) });
            return;
        }

        // New sequential orchestration for richer incremental updates
        await Send(new { agent = "system", status = "working", message = "Executing sequential agent orchestration..." });

        var aggregated = new System.Text.StringBuilder();
        aggregated.AppendLine($"User request: {prompt}\n");

        // Build individual agents (mirrors tool usage of research & booking)
        ChatClientAgent Make(string name, string instructions, ChatOptions? opts = null) => new(chatClient, new ChatClientAgentOptions
        {
            Name = name,
            Instructions = instructions,
            ChatOptions = opts
        });

        var researchAgent = Make("TravelResearch", agentDefinitions[0].Purpose, new ChatOptions
        {
            Tools =
            [
                AIFunctionFactory.Create((string origin, string destination, DateTime departureDate) =>
                    $"Flights {origin}->{destination} {departureDate:MMM d}: UA123 $485 direct; DL456 $395 1-stop; AS789 $520 direct.",
                    description: "Search for available flights"),
                AIFunctionFactory.Create((string destination, DateTime checkIn, DateTime checkOut) =>
                {
                    var nights = (checkOut - checkIn).Days;
                    return $"Hotels {destination} {nights} nights: Hyatt $245; Marriott $195; Hilton $285.";
                }, description: "Search for available hotels")
            ]
        });

        var policyAgent = Make("PolicyCompliance", agentDefinitions[1].Purpose);
        var budgetAgent = Make("BudgetApproval", agentDefinitions[2].Purpose);
        var optimizerAgent = Make("ItineraryOptimizer", agentDefinitions[3].Purpose);
        var bookingAgent = Make("BookingCoordinator", agentDefinitions[4].Purpose, new ChatOptions
        {
            Tools =
            [
                AIFunctionFactory.Create((string flightDetails, string hotelDetails, int nights) =>
                {
                    var code = $"CONF-{Guid.NewGuid().ToString()[..8].ToUpper()}";
                    return $"BOOKING CONFIRMED {code}\nFlight: {flightDetails}\nHotel: {hotelDetails} ({nights} nights)";
                }, description: "Create booking confirmation")
            ]
        });

        var sequence = new List<ChatClientAgent> { researchAgent, policyAgent, budgetAgent, optimizerAgent, bookingAgent };
        var purposeLookup = agentDefinitions.ToDictionary(a => a.Name, a => a.Purpose);

        // Fallback simulated outputs for timeouts or errors
        var simulatedOutputs = new Dictionary<string, string>
        {
            ["TravelResearch"] = "Found sample flight & hotel options (non-stop + compliant lodging).",
            ["PolicyCompliance"] = "All selected options comply with advance purchase & cost thresholds.",
            ["BudgetApproval"] = "Projected spend within departmental budget (no escalation needed).",
            ["ItineraryOptimizer"] = "Chosen lowest total travel time with acceptable price delta.",
            ["BookingCoordinator"] = "Generated confirmation (simulated) and assembled itinerary summary."
        };

        foreach (var agentInstance in sequence)
        {
            var purpose = purposeLookup[agentInstance.Name];
            await Send(new { agent = agentInstance.Name, status = "working", message = purpose });
            var agentPrompt = $"User travel request:\n{prompt}\n\nContext so far:\n{aggregated}\nPlease perform your specialized role ({agentInstance.Name}) and respond succinctly.";
            string agentOutput;
            var startedAt = DateTime.UtcNow;
            if (debug) await Send(new { agent = agentInstance.Name, status = "working", message = $"[debug] starting at {startedAt:O}" });
            try
            {
                var runTask = agentInstance.RunAsync(agentPrompt);
                var timeout = TimeSpan.FromSeconds(12);
                var completed = await Task.WhenAny(runTask, Task.Delay(timeout));
                if (completed == runTask)
                {
                    var runResult = await runTask; // already completed
                    agentOutput = runResult.Text ?? "(no output)";
                }
                else
                {
                    agentOutput = $"[timeout after {timeout.TotalSeconds}s] {simulatedOutputs[agentInstance.Name]}";
                }
            }
            catch (Exception exAgent)
            {
                agentOutput = $"[error] {exAgent.Message}. Using heuristic: {simulatedOutputs[agentInstance.Name]}";
            }
            var endedAt = DateTime.UtcNow;
            if (debug) await Send(new { agent = agentInstance.Name, status = "working", message = $"[debug] finished at {endedAt:O} elapsed={(endedAt-startedAt).TotalSeconds:F1}s" });
            aggregated.AppendLine($"[{agentInstance.Name}] {agentOutput}\n");

            // Stream the agent's result (still with status working so UI updates continuously)
            var truncated = agentOutput.Length > 800 ? agentOutput[..800] + "…" : agentOutput;
            await Send(new { agent = agentInstance.Name, status = "working", message = truncated });
        }

        var finalResponse = aggregated.ToString();
        await Send(new { agent = "system", status = "complete", response = new AgentResponseDto(finalResponse, null) });
    }
    catch (Exception ex)
    {
        await Send(new { agent = "system", status = "error", message = ex.Message });
    }
});

app.MapDefaultEndpoints();
app.Run();

