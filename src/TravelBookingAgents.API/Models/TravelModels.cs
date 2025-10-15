namespace TravelBookingAgents.API.Models;

public record FlightOption(
    string FlightNumber,
    string Airline,
    string DepartureAirport,
    string ArrivalAirport,
    DateTime DepartureTime,
    DateTime ArrivalTime,
    decimal Price,
    int LayoverMinutes,
    string SeatClass);

public record HotelOption(
    string Name,
    string Address,
    decimal PricePerNight,
    int Stars,
    string ChainName,
    bool HasBreakfast,
    double DistanceToVenueMiles);

public record RentalCarOption(
    string Company,
    string CarType,
    decimal DailyRate,
    bool IsPreferred);

public record TravelPolicy(
    List<string> ApprovedDestinations,
    int MinimumAdvanceBookingDays,
    decimal MaxFlightCostDomestic,
    decimal MaxFlightCostInternational,
    decimal MaxHotelNightlyRate,
    List<string> PreferredAirlines,
    List<string> PreferredHotelChains,
    decimal ManagerApprovalThreshold);

public record BudgetInfo(
    string Department,
    string ProjectCode,
    decimal AvailableBudget,
    decimal AlreadySpent);

public record TravelRequest(
    string Destination,
    DateTime StartDate,
    DateTime EndDate,
    string Purpose,
    string? PreferredAirline = null,
    string? PreferredHotelChain = null,
    bool AisleSeat = false);

public record BookingConfirmation(
    string ConfirmationNumber,
    FlightOption Flight,
    HotelOption Hotel,
    RentalCarOption? RentalCar,
    decimal TotalCost,
    string Status);
