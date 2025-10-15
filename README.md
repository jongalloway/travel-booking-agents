# 🌍 Travel Booking Agents

An intelligent corporate travel booking and approval system powered by AI agents and .NET 9. This application demonstrates complex multi-agent orchestration with real business value, showcasing automated travel planning, policy compliance, budget management, itinerary optimization, and booking workflows.

## What This Sample Does

This sample is designed as a realistic showcase for multi‑agent coordination in a corporate scenario. It intentionally balances clarity (for conference/demo settings) with production-minded patterns (service composition, resilience, extensibility). At a glance it provides:

| Capability | What You See | Implementation Highlights |
|------------|--------------|---------------------------|
| Multi‑Agent Orchestration | 5 domain agents run in sequence (research → policy → budget → optimize → booking) | Agents built with Microsoft Agents Framework; sequential orchestration path for deterministic streaming |
| Real‑Time Streaming UI | Live “agent is working” updates + final consolidated transcript | Server-Sent Events (`/agent/chat/stream`) consumed by Blazor Server component |
| Tool / Function Calling | Agents call flight & hotel search + booking tools (mock) | Functions exposed with `AIFunctionFactory` wrapped into ChatOptions |
| Policy & Budget Logic | Automatic validation & cost assessment | Mock policy & budget data (extensible to real services) |
| Offline / Token Fallback | Works even without a valid `GITHUB_TOKEN` | Detects missing token → deterministic simulated outputs |
| Debug Diagnostics | Optional timing / timeout tracing | `debug=true` query emits per‑agent timing markers |
| Timeout Resilience | Long model calls won’t freeze UI | Per‑agent timeout + heuristic fallback output |
| Aspire Orchestration | Unified AppHost + service discovery | `TravelBookingAgents.AppHost` wires Web + API + defaults |
| Extensibility Surface | Easy to add agents, tools, real APIs | Clear separation of agents, mock data, and transport |

### Demo Storyline

1. User submits a natural language travel request.
2. Each agent contributes its specialization; progress is streamed live.
3. A final aggregated itinerary / rationale is returned (or a policy violation explanation).
4. Optional debug mode shows timing and fallback behavior.

### Two Execution Modes

| Mode | Trigger | Behavior |
|------|---------|----------|
| Online (LLM) | Valid `GITHUB_TOKEN` present | Actual model calls per agent (sequential or legacy group chat when `sequential=false`) |
| Offline (Simulated) | Missing / invalid token | Deterministic scripted outputs for reliable demos |

### Quick Links

* Primary streaming endpoint: `GET /agent/chat/stream?prompt=...&sequential=true`
* Legacy round‑robin workflow: `?sequential=false`
* Debug instrumentation: append `&debug=true`
* Console demo: run `TravelBookingAgents.Console` project

> TIP: For conference demos, the offline mode guarantees consistent timings while still showing realistic agent progression.

## Overview

This system replaces manual travel booking processes with an automated workflow featuring 5 specialized AI agents that collaborate to:

* Research travel options (flights, hotels, cars)
* Validate against corporate policies
* Check budgets and determine approval requirements
* Optimize itineraries for cost and convenience
* Finalize bookings with confirmation numbers

## Architecture

### Technology Stack

* **.NET 9** - Modern C# with latest features
* **Microsoft Agents Framework** - Multi-agent orchestration
* **Aspire** - Cloud-native orchestration and service discovery
* **Blazor Server** - Interactive web UI with real-time streaming
* **GitHub Models** - AI model integration (GPT-4o-mini)

### AI Agents

#### 1. **Travel Research Agent** 🔍

Searches for and compares travel options:

* Finds flights between origin and destination
* Searches hotels at destination  
* Locates rental car options
* Compares prices and presents top recommendations

**Tools:** `SearchFlights`, `SearchHotels`, `SearchRentalCars`

#### 2. **Policy Compliance Agent** ✅

Validates travel requests against company policies:

* Checks if destination is approved
* Verifies 14-day advance booking requirement
* Ensures flight/hotel costs within limits
* Validates preferred vendor usage

**Tools:** `GetTravelPolicy`, `ValidateDestinationAndTiming`

#### 3. **Budget Approval Agent** 💰

Manages travel spending and approvals:

* Retrieves department budget information
* Calculates total trip cost (flights + hotels + meals/incidentals)
* Determines if manager approval needed (>$2,500)
* Checks budget availability

**Tools:** `GetBudgetInfo`, `CheckBudgetAndApproval`

#### 4. **Itinerary Optimizer Agent** 📊

Optimizes travel plans for efficiency:

* Minimizes layover times
* Finds cost-saving opportunities
* Balances cost vs. convenience
* Ensures meeting schedule requirements

#### 5. **Booking Coordinator Agent** 📝

Finalizes reservations:

* Creates bookings for approved options
* Generates confirmation numbers
* Assembles complete itinerary
* Handles booking failures gracefully

**Tools:** `CreateBooking`

## Workflow

The agents work together in a round-robin group chat pattern:

```text
User Request
    ↓
Travel Research Agent → Finds 3 flight/hotel options
    ↓
Policy Compliance Agent → Validates against policies
    ↓
Budget Approval Agent → Checks budget, determines if approval needed
    ↓
Itinerary Optimizer Agent → Suggests optimizations
    ↓
Booking Coordinator Agent → Finalizes and confirms bookings
```

**Maximum iterations:** 5 (one per agent in round-robin)

## Sample Travel Policies

The system enforces these corporate policies:

* **Approved Destinations:** Seattle, San Francisco, New York, Austin, Chicago, Boston, Denver, Portland, Atlanta, Dallas
* **Advance Booking:** Minimum 14 days before travel
* **Flight Limits:** $800 domestic, $2,000 international
* **Hotel Limit:** $300 per night
* **Preferred Airlines:** United, Delta, American
* **Preferred Hotels:** Marriott, Hilton, Hyatt
* **Manager Approval:** Required for trips over $2,500

## Sample Departments & Budgets

| Department  | Project Code    | Available Budget | Already Spent |
|-------------|-----------------|------------------|---------------|
| Engineering | PROJ-2026-001   | $50,000          | $12,000       |
| Marketing   | PROJ-2026-002   | $30,000          | $8,000        |
| Sales       | PROJ-2026-003   | $75,000          | $25,000       |

## Getting Started

### Prerequisites

* [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
* [GitHub Token](https://github.com/settings/tokens) for GitHub Models access
* Visual Studio 2022 or VS Code

### Setup

1. **Clone the repository**

   ```bash
   git clone https://github.com/jongalloway/travel-booking-agents.git
   cd travel-booking-agents
   ```

2. **Set GitHub Token**

   ```bash
   # Windows (PowerShell)
   $env:GITHUB_TOKEN="your_github_token"
   
   # macOS/Linux
   export GITHUB_TOKEN="your_github_token"
   ```

3. **Run with Aspire**

   ```bash
   cd src/TravelBookingAgents.AppHost
   dotnet run
   ```

4. **Access the applications**
    * **Blazor Web UI:** <http://localhost:5xxx> (check Aspire dashboard)
    * **Aspire Dashboard:** <http://localhost:15xxx> (shown in console)

### Run Console App

For a command-line demo:

```bash
cd src/TravelBookingAgents.Console
dotnet run
```

## Usage Examples

### Example 1: Conference Travel

**Request:**

```text
Book travel to Microsoft Build in Seattle, May 19-21, 2026
```

**Workflow:**

1. ✈️ Research finds 3 flight options ($395-$520) and 3 hotel options ($195-$285/night)
2. ✅ Policy confirms Seattle is approved, dates meet 14-day requirement
3. 💰 Budget calculates ~$1,200 total, no manager approval needed
4. 📊 Optimizer suggests Delta flight with 2hr layover saves $90
5. 📝 Booking confirms reservation with confirmation number

### Example 2: Office Visit

**Request:**

```text
I need to visit the Austin office next month for 3 days
```

**Workflow:**

1. ✈️ Research presents flight/hotel options
2. ✅ Policy validates Austin is approved destination
3. 💰 Budget checks Engineering department has sufficient funds
4. 📊 Optimizer recommends optimal departure times
5. 📝 Booking finalizes 3-night reservation

### Example 3: Policy Violation

**Request:**

```text
Book last-minute trip to Miami tomorrow
```

**Expected Response:**

* ❌ Policy Agent flags:
    * Miami not in approved destinations
    * Less than 14-day advance booking requirement
* 💡 Suggests alternative approved destinations
* 🔒 Workflow stops without booking

## Project Structure

```text
src/
├── TravelBookingAgents.API/          # Backend API with agents
│   ├── Program.cs                     # Agent definitions & workflow
│   ├── Models/                        # Data models
│   │   └── TravelModels.cs           # Flight, Hotel, Policy, etc.
│   └── Services/
│       └── MockTravelDataService.cs  # Mock travel data & APIs
│
├── TravelBookingAgents.Web/          # Blazor web frontend
│   ├── Components/
│   │   └── Pages/
│   │       └── Chat.razor            # Main booking interface
│   └── Services/
│       └── ChatService.cs            # API communication
│
├── TravelBookingAgents.Console/      # Console demo app
│   └── Program.cs                    # Standalone workflow demo
│
├── TravelBookingAgents.AppHost/      # Aspire orchestration
│   └── AppHost.cs                    # Service configuration
│
└── TravelBookingAgents.ServiceDefaults/ # Shared configurations
    └── Extensions.cs
```

## Key Features

### 🎯 Multi-Agent Orchestration

Five specialized agents collaborate using round-robin group chat pattern with tool calling

### 📊 Real-Time Streaming

Blazor UI shows live updates as each agent processes the request

### 🔧 Function Calling

Agents use tools to search travel options, check policies, and create bookings

### 🏢 Business Logic

Realistic policies, budgets, and approval workflows mirror real corporate systems

### 🎨 Modern UI

Clean, responsive Blazor interface with agent status indicators

### ☁️ Cloud-Native

Aspire orchestration with service discovery, health checks, and telemetry

## Conference Demo Tips

### Quick Demo Flow (5 minutes)

1. Show Blazor UI with sample requests
2. Submit "Microsoft Build in Seattle" request
3. Watch agents work in sequence with live status
4. Show final booking confirmation
5. Explain business value and agent coordination

### Deep Dive (15 minutes)

1. Walk through each agent's role and tools
2. Show code for one agent (TravelResearch)
3. Explain workflow orchestration
4. Demo policy violation scenario
5. Show Console app for technical audience
6. Discuss extensibility (add agents, integrate real APIs)

### Key Talking Points

* ✨ **Real Business Value:** Automates manual travel booking process
* 🤖 **Complex Coordination:** 5 agents with different responsibilities
* 🔐 **Policy Enforcement:** Automated compliance checking
* 💼 **Human-in-Loop:** Budget approvals for high-cost trips
* 🚀 **Production Ready:** Error handling, state management
* 📈 **Extensible:** Easy to add agents or integrate real APIs

## Extending the System

### Add New Agents

```csharp
builder.AddAIAgent("ExpenseReporter", (sp, key) =>
{
    return new ChatClientAgent(
        sp.GetRequiredService<IChatClient>(),
        name: key,
        instructions: "Generate expense reports from bookings...",
        tools: [
            AIFunctionFactory.Create(GenerateExpenseReport)
        ]
    );
});
```

### Integrate Real APIs

Replace mock services in `MockTravelDataService.cs` with:

* **Flights:** Amadeus API, Skyscanner API
* **Hotels:** Booking.com API, Expedia API
* **Cars:** Turo API, Enterprise API

### Add Authentication

Integrate Azure AD B2C or Auth0 to:

* Track user department/budget
* Implement real manager approval workflows
* Audit booking history

## Troubleshooting

**Issue:** Agents not responding

* **Solution:** Check `GITHUB_TOKEN` environment variable is set

**Issue:** Build errors about missing packages

* **Solution:** Restore NuGet packages: `dotnet restore`

**Issue:** Aspire dashboard not accessible

* **Solution:** Check firewall settings, run as administrator

**Issue:** Model rate limits

* **Solution:** GitHub Models has rate limits; wait or use different account

## Contributing

Contributions welcome! Areas for improvement:

* Real travel API integrations
* Additional agents (Expense reporting, Trip modifications)
* Enhanced UI with approval workflows
* Database for booking history
* Email notifications

## License

MIT License - see [LICENSE](LICENSE)

## Acknowledgments

Built with:

* [Microsoft Agents Framework](https://github.com/microsoft/agents)
* [.NET Aspire](https://learn.microsoft.com/dotnet/aspire/)
* [GitHub Models](https://github.com/marketplace/models)
* [Blazor](https://dotnet.microsoft.com/apps/aspnet/web-apps/blazor)

## Contact

For questions or demo requests:

* **Repository:** <https://github.com/jongalloway/travel-booking-agents>
* **Issues:** <https://github.com/jongalloway/travel-booking-agents/issues>

---

**Ready to revolutionize corporate travel booking with AI agents!** ✈️🤖
