using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using System.Collections.Concurrent;
using TravelBookingAgents.API;

// (Workflow mode enum & parser defined at end of file inside internal static class to preserve top-level ordering)

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

// Streaming chat with optional approval checkpoint (approval=true triggers human gate after policy agent)
app.MapGet("/agent/chat/stream", async (HttpContext ctx, string prompt, IChatClient chatClient, string? mode, bool debug = false, bool approval = false) =>
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

    var workflowMode = WorkflowModeParser.Parse(mode);

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

        if (workflowMode == TravelWorkflowMode.GroupChat)
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

        // Build individual agents (used across modes)
        ChatClientAgent Make(string name, string instructions, ChatOptions? opts = null) => new(chatClient, new ChatClientAgentOptions
        {
            Name = name,
            Instructions = instructions,
            ChatOptions = opts
        });

        ChatClientAgent BuildResearchAgent() => Make("TravelResearch", agentDefinitions[0].Purpose, new ChatOptions
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
        ChatClientAgent BuildPolicyAgent() => Make("PolicyCompliance", agentDefinitions[1].Purpose);
        ChatClientAgent BuildBudgetAgent() => Make("BudgetApproval", agentDefinitions[2].Purpose);
        ChatClientAgent BuildOptimizerAgent() => Make("ItineraryOptimizer", agentDefinitions[3].Purpose);
        ChatClientAgent BuildBookingAgent() => Make("BookingCoordinator", agentDefinitions[4].Purpose, new ChatOptions
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

        // Mode: Concurrent (true parallel fan-out + aggregate)
        if (workflowMode == TravelWorkflowMode.Concurrent)
        {
            await Send(new { agent = "system", status = "working", message = "Executing concurrent agent fan-out..." });

            var researchAgent = BuildResearchAgent();
            var policyAgent = BuildPolicyAgent();
            var budgetAgent = BuildBudgetAgent();
            var optimizerAgent = BuildOptimizerAgent();
            var bookingAgent = BuildBookingAgent();

            var all = new List<ChatClientAgent> { researchAgent, policyAgent, budgetAgent, optimizerAgent, bookingAgent };
            var purposeLookup = agentDefinitions.ToDictionary(a => a.Name, a => a.Purpose);

            foreach (var a in all)
            {
                var agentName = a.Name ?? "(unnamed)";
                await Send(new { agent = agentName, status = "working", step = (int?)null, message = purposeLookup[agentName] });
            }

            var simulatedOutputs = new Dictionary<string, string>
            {
                ["TravelResearch"] = "Found sample flight & hotel options (non-stop + compliant lodging).",
                ["PolicyCompliance"] = "All selected options comply with advance purchase & cost thresholds.",
                ["BudgetApproval"] = "Projected spend within departmental budget (no escalation needed).",
                ["ItineraryOptimizer"] = "Chosen lowest total travel time with acceptable price delta.",
                ["BookingCoordinator"] = "Generated confirmation (simulated) and assembled itinerary summary."
            };

            async Task<(string Name, string Output, DateTime Started, DateTime Ended)> RunAgentAsync(ChatClientAgent agent)
            {
                var started = DateTime.UtcNow;
                var timeout = TimeSpan.FromSeconds(12);
                try
                {
                    var safeName = agent.Name ?? "(unnamed)";
                    var contextPrompt = $"User travel request:\n{prompt}\nYou are the {safeName} agent. {purposeLookup[safeName]} Provide a concise result.";
                    var runTask = agent.RunAsync(contextPrompt);
                    var completed = await Task.WhenAny(runTask, Task.Delay(timeout));
                    if (completed == runTask)
                    {
                        var result = await runTask;
                        return (safeName, result.Text ?? "(no output)", started, DateTime.UtcNow);
                    }
                    return (safeName, $"[timeout after {timeout.TotalSeconds}s] {simulatedOutputs[safeName]}", started, DateTime.UtcNow);
                }
                catch (Exception ex)
                {
                    var safeName = agent.Name ?? "(unnamed)";
                    return (safeName, $"[error] {ex.Message}. {simulatedOutputs[safeName]}", started, DateTime.UtcNow);
                }
            }

            var tasks = all.Select(RunAgentAsync).ToList();
            var completionOrder = new List<(string Name, string Output, double Elapsed)>();
            var stepCounter = 0;
            while (tasks.Any())
            {
                var finished = await Task.WhenAny(tasks);
                tasks.Remove(finished);
                var res = await finished;
                stepCounter++;
                completionOrder.Add((res.Name, res.Output, (res.Ended - res.Started).TotalSeconds));
                var truncated = res.Output.Length > 600 ? res.Output[..600] + "…" : res.Output;
                await Send(new { agent = res.Name, status = "step_complete", step = stepCounter, summary = truncated });
            }

            var final = new System.Text.StringBuilder();
            final.AppendLine($"User request: {prompt}");
            final.AppendLine("\nConcurrent agent results (in completion order):\n");
            foreach (var item in completionOrder)
            {
                final.AppendLine($"{item.Name} ({item.Elapsed:F1}s): {item.Output}\n");
            }
            await Send(new { agent = "system", status = "complete", response = new AgentResponseDto(final.ToString(), null) });
            return;
        }

        // Mode: Handoff (dynamic baton pass / conditional path + optional approval)
        if (workflowMode == TravelWorkflowMode.Handoff)
        {
            await Send(new { agent = "system", status = "working", message = "Executing handoff (dynamic path) orchestration..." });

            var hResearchAgent = BuildResearchAgent();
            var hPolicyAgent = BuildPolicyAgent();
            var hBudgetAgent = BuildBudgetAgent();
            var hOptimizerAgent = BuildOptimizerAgent();
            var hBookingAgent = BuildBookingAgent();

            var hSimulatedOutputs = new Dictionary<string, string>
            {
                ["TravelResearch"] = "Found sample flight & hotel options (non-stop + compliant lodging).",
                ["PolicyCompliance"] = "All selected options comply with advance purchase & cost thresholds.",
                ["BudgetApproval"] = "Projected spend within departmental budget (no escalation needed).",
                ["ItineraryOptimizer"] = "Optimized plan with minimal layover and balanced cost.",
                ["BookingCoordinator"] = "Generated confirmation (simulated) and assembled itinerary summary."
            };

            async Task<string> RunAgent(ChatClientAgent agent, string context)
            {
                var safeName = agent.Name ?? "(unnamed)";
                await Send(new { agent = safeName, status = "working", message = agentDefinitions.First(a => a.Name == safeName).Purpose });
                var timeout = TimeSpan.FromSeconds(12);
                try
                {
                    var runTask = agent.RunAsync(context);
                    var completed = await Task.WhenAny(runTask, Task.Delay(timeout));
                    if (completed == runTask)
                    {
                        var r = await runTask; return r.Text ?? "(no output)";
                    }
                    return $"[timeout after {timeout.TotalSeconds}s] {hSimulatedOutputs[safeName]}";
                }
                catch (Exception ex)
                {
                    return $"[error] {ex.Message}. {hSimulatedOutputs[safeName]}";
                }
            }

            var step = 0;
            var transcript = new System.Text.StringBuilder();

            // 1. Research
            step++;
            var researchCtx = $"User travel request:\n{prompt}\nResearch flights & hotels and present concise options.";
            var researchOut = await RunAgent(hResearchAgent, researchCtx);
            await Send(new { agent = hResearchAgent.Name, status = "step_complete", step, summary = (researchOut.Length > 600 ? researchOut[..600] + "…" : researchOut) });
            transcript.AppendLine($"Step {step} {hResearchAgent.Name}: {researchOut}\n");

            // 2. Policy (uses research output)
            step++;
            var policyCtx = $"User request: {prompt}\nPrevious output:\n{researchOut}\nCheck corporate policy and flag any violations.";
            var policyOut = await RunAgent(hPolicyAgent, policyCtx);
            await Send(new { agent = hPolicyAgent.Name, status = "step_complete", step, summary = (policyOut.Length > 600 ? policyOut[..600] + "…" : policyOut) });
            transcript.AppendLine($"Step {step} {hPolicyAgent.Name}: {policyOut}\n");

            // Optional approval gate right after policy check
            if (approval)
            {
                var workflowId = Guid.NewGuid().ToString();
                var state = WorkflowApprovalStore.Create(workflowId, new()
                {
                    Mode = workflowMode.ToString(),
                    Prompt = prompt,
                    Phase = "policy",
                    TranscriptSoFar = transcript.ToString()
                });
                await Send(new { agent = "system", status = "awaiting_input", workflowId, actions = new[] { "approve", "cancel" }, message = "Human approval required after policy evaluation before proceeding." });
                ApprovalDecision decision;
                try
                {
                    decision = await state.WaitForDecisionAsync(TimeSpan.FromMinutes(5));
                }
                catch (TimeoutException)
                {
                    decision = new ApprovalDecision("approve", "auto-timeout");
                }
                if (decision.Action.Equals("cancel", StringComparison.OrdinalIgnoreCase))
                {
                    await Send(new { agent = "system", status = "cancelled", message = "Workflow cancelled by user during policy approval." });
                    await Send(new { agent = "system", status = "complete", response = new AgentResponseDto(transcript + "\nCANCELLED.", null) });
                    return;
                }
                await Send(new { agent = "system", status = "resumed", workflowId, message = "Approval granted, continuing workflow." });
            }

            var policyIndicatesViolation = policyOut.Contains("violation", StringComparison.OrdinalIgnoreCase) || policyOut.Contains("non-compliant", StringComparison.OrdinalIgnoreCase);

            string? budgetOut = null;
            string? optimizerOut = null;

            if (!policyIndicatesViolation)
            {
                // 3a. Budget first then optimizer
                step++;
                var budgetCtx = $"User request: {prompt}\nResearch output:\n{researchOut}\nPolicy result:\n{policyOut}\nEstimate total trip cost succinctly.";
                budgetOut = await RunAgent(hBudgetAgent, budgetCtx);
                await Send(new { agent = hBudgetAgent.Name, status = "step_complete", step, summary = (budgetOut.Length > 600 ? budgetOut[..600] + "…" : budgetOut) });
                transcript.AppendLine($"Step {step} {hBudgetAgent.Name}: {budgetOut}\n");

                step++;
                var optCtx = $"User request: {prompt}\nResearch:\n{researchOut}\nPolicy:\n{policyOut}\nBudget:\n{budgetOut}\nOptimize itinerary (cost vs convenience).";
                optimizerOut = await RunAgent(hOptimizerAgent, optCtx);
                await Send(new { agent = hOptimizerAgent.Name, status = "step_complete", step, summary = (optimizerOut.Length > 600 ? optimizerOut[..600] + "…" : optimizerOut) });
                transcript.AppendLine($"Step {step} {hOptimizerAgent.Name}: {optimizerOut}\n");
            }
            else
            {
                // 3b. Violation path: optimizer first to adjust; skip budget if not needed or run after optimization
                step++;
                var optCtx = $"User request: {prompt}\nResearch:\n{researchOut}\nPolicy (violation detected):\n{policyOut}\nPropose compliant adjustments and improved options.";
                optimizerOut = await RunAgent(hOptimizerAgent, optCtx);
                await Send(new { agent = hOptimizerAgent.Name, status = "step_complete", step, summary = (optimizerOut.Length > 600 ? optimizerOut[..600] + "…" : optimizerOut) });
                transcript.AppendLine($"Step {step} {hOptimizerAgent.Name}: {optimizerOut}\n");

                step++;
                var budgetCtx = $"User request: {prompt}\nResearch:\n{researchOut}\nPolicy (violation detected):\n{policyOut}\nOptimizer adjustments:\n{optimizerOut}\nProvide updated cost estimate.";
                budgetOut = await RunAgent(hBudgetAgent, budgetCtx);
                await Send(new { agent = hBudgetAgent.Name, status = "step_complete", step, summary = (budgetOut.Length > 600 ? budgetOut[..600] + "…" : budgetOut) });
                transcript.AppendLine($"Step {step} {hBudgetAgent.Name}: {budgetOut}\n");
            }

            // 4. Booking
            step++;
            var bookingCtx = $"User request: {prompt}\nResearch:\n{researchOut}\nPolicy:\n{policyOut}\n" +
                              (optimizerOut is not null ? $"Optimizer:\n{optimizerOut}\n" : string.Empty) +
                              (budgetOut is not null ? $"Budget:\n{budgetOut}\n" : string.Empty) +
                              "Produce final confirmation and concise itinerary summary.";
            var bookingOut = await RunAgent(hBookingAgent, bookingCtx);
            await Send(new { agent = hBookingAgent.Name, status = "step_complete", step, summary = (bookingOut.Length > 600 ? bookingOut[..600] + "…" : bookingOut) });
            transcript.AppendLine($"Step {step} {hBookingAgent.Name}: {bookingOut}\n");

            await Send(new { agent = "system", status = "complete", response = new AgentResponseDto(transcript.ToString(), null) });
            return;
        }

        // Sequential orchestration path (default if not matched above) with optional approval after policy
        await Send(new { agent = "system", status = "working", message = "Executing sequential agent orchestration..." });

        var aggregated = new System.Text.StringBuilder();
        aggregated.AppendLine($"User request: {prompt}\n");
        var seqResearchAgent = BuildResearchAgent();
        var seqPolicyAgent = BuildPolicyAgent();
        var seqBudgetAgent = BuildBudgetAgent();
        var seqOptimizerAgent = BuildOptimizerAgent();
        var seqBookingAgent = BuildBookingAgent();

        var sequence = new List<ChatClientAgent> { seqResearchAgent, seqPolicyAgent, seqBudgetAgent, seqOptimizerAgent, seqBookingAgent };
        var seqPurposeLookup = agentDefinitions.ToDictionary(a => a.Name, a => a.Purpose);

        // Fallback simulated outputs for timeouts or errors
        var seqSimulatedOutputs = new Dictionary<string, string>
        {
            ["TravelResearch"] = "Found sample flight & hotel options (non-stop + compliant lodging).",
            ["PolicyCompliance"] = "All selected options comply with advance purchase & cost thresholds.",
            ["BudgetApproval"] = "Projected spend within departmental budget (no escalation needed).",
            ["ItineraryOptimizer"] = "Chosen lowest total travel time with acceptable price delta.",
            ["BookingCoordinator"] = "Generated confirmation (simulated) and assembled itinerary summary."
        };

        var stepNumber = 0;
        foreach (var agentInstance in sequence)
        {
            var safeSeqName = agentInstance.Name ?? "(unnamed)";
            var purpose = seqPurposeLookup[safeSeqName];
            stepNumber++;
            await Send(new { agent = safeSeqName, status = "working", step = stepNumber, message = purpose });
            var agentPrompt = $"User travel request:\n{prompt}\n\nContext so far:\n{aggregated}\nPlease perform your specialized role ({agentInstance.Name}) and respond succinctly.";
            string agentOutput;
            var startedAt = DateTime.UtcNow;
            if (debug) await Send(new { agent = safeSeqName, status = "working", message = $"[debug] starting at {startedAt:O}" });
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
                    agentOutput = $"[timeout after {timeout.TotalSeconds}s] {seqSimulatedOutputs[safeSeqName]}";
                }
            }
            catch (Exception exAgent)
            {
                agentOutput = $"[error] {exAgent.Message}. Using heuristic: {seqSimulatedOutputs[safeSeqName]}";
            }
            var endedAt = DateTime.UtcNow;
            if (debug) await Send(new { agent = safeSeqName, status = "working", message = $"[debug] finished at {endedAt:O} elapsed={(endedAt - startedAt).TotalSeconds:F1}s" });
            aggregated.AppendLine($"Step {stepNumber} - {safeSeqName}: {agentOutput}\n");

            // Emit a step completion summary event (status=step_complete) with truncated summary
            var truncated = agentOutput.Length > 600 ? agentOutput[..600] + "…" : agentOutput;
            await Send(new { agent = safeSeqName, status = "step_complete", step = stepNumber, summary = truncated });

            // Insert approval gate immediately after PolicyCompliance if requested
            if (approval && safeSeqName == "PolicyCompliance")
            {
                var workflowId = Guid.NewGuid().ToString();
                var state = WorkflowApprovalStore.Create(workflowId, new()
                {
                    Mode = workflowMode.ToString(),
                    Prompt = prompt,
                    Phase = "policy",
                    TranscriptSoFar = aggregated.ToString()
                });
                await Send(new { agent = "system", status = "awaiting_input", workflowId, actions = new[] { "approve", "cancel" }, message = "Human approval required after policy evaluation before continuing." });
                ApprovalDecision decision;
                try
                {
                    decision = await state.WaitForDecisionAsync(TimeSpan.FromMinutes(5));
                }
                catch (TimeoutException)
                {
                    decision = new ApprovalDecision("approve", "auto-timeout");
                }
                if (decision.Action.Equals("cancel", StringComparison.OrdinalIgnoreCase))
                {
                    await Send(new { agent = "system", status = "cancelled", message = "Workflow cancelled by user during policy approval." });
                    await Send(new { agent = "system", status = "complete", response = new AgentResponseDto(aggregated + "\nCANCELLED.", null) });
                    return;
                }
                await Send(new { agent = "system", status = "resumed", workflowId, message = "Approval granted, continuing workflow." });
            }
        }

        var finalResponse = aggregated.ToString();
        await Send(new { agent = "system", status = "complete", response = new AgentResponseDto(finalResponse, null) });
    }
    catch (Exception ex)
    {
        await Send(new { agent = "system", status = "error", message = ex.Message });
    }
});

app.MapPost("/agent/workflow/{id}/decision", (string id, ApprovalDecision decision) =>
{
    if (!WorkflowApprovalStore.TryResolve(id, decision))
    {
        return Results.NotFound(new { message = "Workflow not found or already decided." });
    }
    return Results.Ok(new { message = "Decision accepted." });
});

app.MapDefaultEndpoints();
app.Run();

// --- Approval infrastructure (kept outside namespace to avoid file-scoped ordering issues) ---
public record ApprovalDecision(string Action, string? Note);

public class ApprovalWorkflowState
{
    public string Id { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string Phase { get; set; } = string.Empty;
    public string TranscriptSoFar { get; set; } = string.Empty;
    private readonly TaskCompletionSource<ApprovalDecision> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void SetDecision(ApprovalDecision decision) => _tcs.TrySetResult(decision);
    public Task<ApprovalDecision> WaitForDecisionAsync(TimeSpan timeout) => _tcs.Task.WaitAsync(timeout);
}

public static class WorkflowApprovalStore
{
    private static readonly ConcurrentDictionary<string, ApprovalWorkflowState> _states = new();
    public static ApprovalWorkflowState Create(string id, ApprovalWorkflowState state)
    {
        state.Id = id;
        _states[id] = state;
        return state;
    }
    public static bool TryGet(string id, out ApprovalWorkflowState state) => _states.TryGetValue(id, out state!);
    public static bool TryResolve(string id, ApprovalDecision decision)
    {
        if (_states.TryRemove(id, out var state))
        {
            state.SetDecision(decision);
            return true;
        }
        return false;
    }
}

