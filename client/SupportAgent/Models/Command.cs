namespace SupportAgent.Models;

public class Command
{
    public string Action { get; set; } = string.Empty;
    public string ElementId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int DelayMs { get; set; } = 100;
    public string Message { get; set; } = string.Empty; // Для вывода пользователю
}

public class ServerRequest
{
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
}
