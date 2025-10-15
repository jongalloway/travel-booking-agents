using TravelBookingAgents.API.Models;

namespace TravelBookingAgents.API.Services;

public class MockTravelDataService
{
    private static readonly TravelPolicy CompanyPolicy = new(
        ApprovedDestinations: new List<string>
        {
            "Seattle", "San Francisco", "New York", "Austin", "Chicago",
            "Boston", "Denver", "Portland", "Atlanta", "Dallas"
        },
        MinimumAdvanceBookingDays: 14,
        MaxFlightCostDomestic: 800,
        MaxFlightCostInternational: 2000,
        MaxHotelNightlyRate: 300,
        PreferredAirlines: new List<string> { "United", "Delta", "American" },
        PreferredHotelChains: new List<string> { "Marriott", "Hilton", "Hyatt" },
        ManagerApprovalThreshold: 2500
    );

    private static readonly Dictionary<string, BudgetInfo> Budgets = new()
    {
        ["Engineering"] = new BudgetInfo("Engineering", "PROJ-2026-001", 50000, 12000),
        ["Marketing"] = new BudgetInfo("Marketing", "PROJ-2026-002", 30000, 8000),
        ["Sales"] = new BudgetInfo("Sales", "PROJ-2026-003", 75000, 25000)
    };

    public TravelPolicy GetTravelPolicy() => CompanyPolicy;

    public BudgetInfo GetBudgetInfo(string department)
    {
        return Budgets.GetValueOrDefault(department)
            ?? new BudgetInfo(department, "UNKNOWN", 10000, 0);
    }

    public List<FlightOption> SearchFlights(string origin, string destination, DateTime departureDate)
    {
        var random = new Random(origin.GetHashCode() + destination.GetHashCode() + departureDate.Day);

        var airlines = new[] { "United", "Delta", "American", "Southwest", "Alaska" };
        var flights = new List<FlightOption>();

        for (int i = 0; i < 5; i++)
        {
            var airline = airlines[random.Next(airlines.Length)];
            var departureTime = departureDate.AddHours(6 + i * 3);
            var flightDuration = random.Next(120, 360);
            var layover = i % 2 == 0 ? 0 : random.Next(60, 180);
            var basePrice = random.Next(200, 800);

            flights.Add(new FlightOption(
                FlightNumber: $"{airline.Substring(0, 2).ToUpper()}{random.Next(100, 9999)}",
                Airline: airline,
                DepartureAirport: origin,
                ArrivalAirport: destination,
                DepartureTime: departureTime,
                ArrivalTime: departureTime.AddMinutes(flightDuration + layover),
                Price: basePrice,
                LayoverMinutes: layover,
                SeatClass: "Economy"
            ));
        }

        return flights.OrderBy(f => f.Price).ToList();
    }

    public List<HotelOption> SearchHotels(string destination, DateTime checkIn, DateTime checkOut)
    {
        var random = new Random(destination.GetHashCode() + checkIn.Day);
        var chains = new[] { "Marriott", "Hilton", "Hyatt", "Sheraton", "Westin" };
        var hotels = new List<HotelOption>();

        for (int i = 0; i < 5; i++)
        {
            var chain = chains[random.Next(chains.Length)];
            var stars = random.Next(3, 6);
            var basePrice = stars * 50 + random.Next(50, 150);

            hotels.Add(new HotelOption(
                Name: $"{chain} {destination} Downtown",
                Address: $"{random.Next(100, 999)} {destination} Street, {destination}",
                PricePerNight: basePrice,
                Stars: stars,
                ChainName: chain,
                HasBreakfast: random.Next(2) == 0,
                DistanceToVenueMiles: random.NextDouble() * 5
            ));
        }

        return hotels.OrderBy(h => h.PricePerNight).ToList();
    }

    public List<RentalCarOption> SearchRentalCars(string location, DateTime pickupDate, DateTime returnDate)
    {
        var companies = new[] { "Enterprise", "Hertz", "Budget", "Avis", "National" };
        var carTypes = new[] { "Economy", "Compact", "Midsize", "SUV" };
        var preferredCompanies = new[] { "Enterprise", "National" };

        return companies.Select(company => new RentalCarOption(
            Company: company,
            CarType: carTypes[new Random(company.GetHashCode()).Next(carTypes.Length)],
            DailyRate: new Random(company.GetHashCode()).Next(30, 80),
            IsPreferred: preferredCompanies.Contains(company)
        )).OrderBy(c => c.DailyRate).ToList();
    }

    public BookingConfirmation CreateBooking(FlightOption flight, HotelOption hotel, RentalCarOption? car, int nights)
    {
        var totalCost = flight.Price + (hotel.PricePerNight * nights);
        if (car != null)
        {
            totalCost += car.DailyRate * (nights + 1);
        }

        return new BookingConfirmation(
            ConfirmationNumber: $"CONF-{Guid.NewGuid().ToString().Substring(0, 8).ToUpper()}",
            Flight: flight,
            Hotel: hotel,
            RentalCar: car,
            TotalCost: totalCost,
            Status: "Confirmed"
        );
    }
}
