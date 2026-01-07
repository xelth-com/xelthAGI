using Newtonsoft.Json;

namespace SupportAgent.Models;

public class Command
{
    [JsonProperty("action")]
    public string Action { get; set; } = string.Empty;

    [JsonProperty("element_id")]
    public string ElementId { get; set; } = string.Empty;

    [JsonProperty("text")]
    public string Text { get; set; } = string.Empty;

    [JsonProperty("x")]
    public int X { get; set; }

    [JsonProperty("y")]
    public int Y { get; set; }

    [JsonProperty("delay_ms")]
    public int DelayMs { get; set; } = 100;

    [JsonProperty("message")]
    public string Message { get; set; } = string.Empty;

    // For 'download' action
    [JsonProperty("url")]
    public string Url { get; set; } = string.Empty;

    [JsonProperty("local_filename")]
    public string LocalFileName { get; set; } = string.Empty;
}

public class ServerRequest
{
    public string ClientId { get; set; } = string.Empty;
    public UIState State { get; set; } = new();
    public string Task { get; set; } = string.Empty;
    public List<string> History { get; set; } = new();
}

public class ServerResponse
{
    public Command? Command { get; set; }
    public bool Success { get; set; }
    public string Error { get; set; } = string.Empty;
    public bool TaskCompleted { get; set; }
    public string Reasoning { get; set; } = string.Empty;
    public string? CanonicalClientId { get; set; } // Server authority ID
}
