using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using System.ComponentModel;
using Microsoft.Agents.AI;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;

Console.WriteLine("🌍 Corporate Travel Booking Agent Demo");
Console.WriteLine("=".PadRight(50, '='));
Console.WriteLine();

IChatClient chatClient =
    new ChatClient(
            "gpt-4o-mini",
            new ApiKeyCredential(Environment.GetEnvironmentVariable("GITHUB_TOKEN")!),
            new OpenAIClientOptions { Endpoint = new Uri("https://models.github.ai/inference") })
        .AsIChatClient();

// Travel Research Agent
AIAgent travelResearch = new ChatClientAgent(
    chatClient,
    new ChatClientAgentOptions
    {
        Name = "TravelResearch",
        Instructions = @"You are a travel research specialist. Search for and compare flight, hotel, 
                        and car rental options. Present the best 2-3 options with price comparisons.",
        ChatOptions = new ChatOptions
        {
            Tools = [
                AIFunctionFactory.Create(SearchFlights),
                AIFunctionFactory.Create(SearchHotels)
            ],
        }
    });

// Policy Compliance Agent
AIAgent policyCompliance = new ChatClientAgent(
    chatClient,
    new ChatClientAgentOptions
    {
        Name = "PolicyCompliance",
        Instructions = @"You are a corporate travel policy officer. Validate requests against:
                        - Approved destinations (Seattle, SF, NYC, Austin, Chicago, Boston, Denver)
                        - 14-day advance booking requirement
                        - Budget limits ($800 flights, $300/night hotels)
                        Report violations and suggest alternatives."
    });

// Budget Approval Agent
AIAgent budgetApproval = new ChatClientAgent(
    chatClient,
    new ChatClientAgentOptions
    {
        Name = "BudgetApproval",
        Instructions = @"You are a budget analyst. Calculate total trip costs including flights, 
                        hotels, and estimated expenses. Determine if manager approval is needed 
                        (over $2,500). Engineering dept has $38,000 available budget."
    });

// Itinerary Optimizer Agent
AIAgent optimizer = new ChatClientAgent(
    chatClient,
    new ChatClientAgentOptions
    {
        Name = "ItineraryOptimizer",
        Instructions = @"You are an itinerary optimizer. Review travel options and suggest 
                        improvements to minimize costs and layovers while ensuring schedule requirements."
    });

// Booking Coordinator Agent
AIAgent bookingCoordinator = new ChatClientAgent(
    chatClient,
    new ChatClientAgentOptions
    {
        Name = "BookingCoordinator",
        Instructions = @"You are a booking coordinator. Once approved, finalize reservations,
                        generate confirmation numbers, and provide complete itinerary details.",
        ChatOptions = new ChatOptions
        {
            Tools = [
                AIFunctionFactory.Create(CreateBooking)
            ],
        }
    });

Console.WriteLine("Agents initialized:");
Console.WriteLine("  ✓ Travel Research");
Console.WriteLine("  ✓ Policy Compliance");
Console.WriteLine("  ✓ Budget Approval");
Console.WriteLine("  ✓ Itinerary Optimizer");
Console.WriteLine("  ✓ Booking Coordinator");
Console.WriteLine();

// Create a workflow with all 5 agents
Workflow workflow =
    AgentWorkflowBuilder
        .CreateGroupChatBuilderWith(agents =>
            new AgentWorkflowBuilder.RoundRobinGroupChatManager(agents)
            {
                MaximumIterationCount = 5
            })
        .AddParticipants(travelResearch, policyCompliance, budgetApproval, optimizer, bookingCoordinator)
        .Build();

AIAgent workflowAgent = await workflow.AsAgentAsync();

string travelRequest = """
    Book travel to Microsoft Build in Seattle, May 19-21, 2026.
    Employee: Engineering Department
    Need hotel near convention center and prefer direct flights.
    """;

Console.WriteLine("📝 Travel Request:");
Console.WriteLine(travelRequest);
Console.WriteLine();
Console.WriteLine("Processing through approval workflow...");
Console.WriteLine();

AgentRunResponse workflowResponse = await workflowAgent.RunAsync(travelRequest);

Console.WriteLine("✅ Workflow Complete!");
Console.WriteLine();
Console.WriteLine("Final Response:");
Console.WriteLine("=".PadRight(50, '='));
Console.WriteLine(workflowResponse.Text);

// Mock tool functions
[Description("Search for available flights")]
string SearchFlights(string origin, string destination, DateTime departureDate)
{
    return $"""
        Found 3 flight options from {origin} to {destination} on {departureDate:MMM d}:
        1. United UA1234 - Direct - $485 - Departs 8:30 AM
        2. Delta DL5678 - 1 stop - $395 - Departs 10:15 AM (2hr layover)
        3. Alaska AS9012 - Direct - $520 - Departs 2:45 PM
        """;
}

[Description("Search for available hotels")]
string SearchHotels(string destination, DateTime checkIn, DateTime checkOut)
{
    int nights = (checkOut - checkIn).Days;
    return $"""
        Found 3 hotel options in {destination} for {nights} nights:
        1. Hyatt Regency Seattle - $245/night - 0.3 miles from venue - ⭐⭐⭐⭐
        2. Marriott Downtown - $195/night - 0.8 miles from venue - ⭐⭐⭐⭐
        3. Hilton Seattle - $285/night - 0.2 miles from venue - ⭐⭐⭐⭐⭐
        """;
}

[Description("Create booking confirmation")]
string CreateBooking(string flightDetails, string hotelDetails, int numberOfNights)
{
    string confirmationNumber = $"CONF-{Guid.NewGuid().ToString().Substring(0, 8).ToUpper()}";
    return $"""
        ✅ BOOKING CONFIRMED
        Confirmation Number: {confirmationNumber}
        
        Flight: {flightDetails}
        Hotel: {hotelDetails} ({numberOfNights} nights)
        Status: All reservations confirmed
        """;
}