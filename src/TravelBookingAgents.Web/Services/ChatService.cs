using System.Text.Json.Serialization;

namespace TravelBookingAgents.Web.Services;

public class ChatService(IHttpClientFactory httpClientFactory)
{
    public async Task<AgentResponse> SendMessageAsync(string prompt)
    {
        var client = httpClientFactory.CreateClient("api");
        var response = await client.GetFromJsonAsync<AgentResponse>($"/agent/chat?prompt={Uri.EscapeDataString(prompt)}");
        return response ?? new AgentResponse();
    }

    public async IAsyncEnumerable<AgentStatusUpdate> SendMessageStreamAsync(string prompt)
    {
        var client = httpClientFactory.CreateClient("api");
        using var response = await client.GetAsync($"/agent/chat/stream?prompt={Uri.EscapeDataString(prompt)}", HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            if (!string.IsNullOrWhiteSpace(line) && line.StartsWith("data: "))
            {
                var jsonData = line.Substring(6);
                var update = System.Text.Json.JsonSerializer.Deserialize<AgentStatusUpdate>(jsonData, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (update != null)
                {
                    yield return update;
                }
            }
        }
    }
}

public class AgentStatusUpdate
{
    [JsonPropertyName("agent")]
    public string? Agent { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("response")]
    public AgentResponse? Response { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

public class AgentResponse
{
    [JsonPropertyName("result")] public string? Result { get; set; }
    [JsonPropertyName("messages")] public List<Message>? Messages { get; set; }
}

public class Message
{
    [JsonPropertyName("authorName")] public string? AuthorName { get; set; }
    [JsonPropertyName("contents")] public List<MessageContent>? Contents { get; set; }
}

public class MessageContent
{
    [JsonPropertyName("$type")]
    public string? Type { get; set; }
    public string? Text { get; set; }
}
