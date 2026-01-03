namespace SupportAgent.Models;

public class UIState
{
    public string WindowTitle { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public List<UIElement> Elements { get; set; } = new();
    public string Screenshot { get; set; } = string.Empty; // Base64 (For AI Vision)
    public string DebugScreenshot { get; set; } = string.Empty; // Base64 (For Human Logs/Debugging)
}

public class UIElement
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public Rectangle Bounds { get; set; } = new();
}

public class Rectangle
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}
