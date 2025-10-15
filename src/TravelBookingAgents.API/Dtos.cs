using System.Text.Json.Serialization;

namespace TravelBookingAgents.API;

public record AgentResponseDto(string? Result, List<MessageDto>? Messages);
public record MessageDto(string? AuthorName, List<MessageContentDto>? Contents);
public record MessageContentDto([property: JsonPropertyName("$type")] string? Type, string? Text);
