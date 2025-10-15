namespace TravelBookingAgents.API;

internal enum TravelWorkflowMode { GroupChat, Sequential, Concurrent, Handoff }

internal static class WorkflowModeParser
{
    public static TravelWorkflowMode Parse(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode)) return TravelWorkflowMode.GroupChat;
        return mode.ToLowerInvariant() switch
        {
            "sequential" => TravelWorkflowMode.Sequential,
            "concurrent" => TravelWorkflowMode.Concurrent,
            "handoff" => TravelWorkflowMode.Handoff,
            _ => TravelWorkflowMode.GroupChat
        };
    }
}
