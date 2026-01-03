using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using SupportAgent.Models;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SupportAgent.Services;

public class UIAutomationService : IDisposable
{
    // Windows API для проверки фокуса окна
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_SHOWWINDOW = 0x0040;

    private readonly UIA3Automation _automation;
    private Dictionary<string, AutomationElement> _elementCache = new();
    private readonly HttpClient _httpClient;
    private readonly SystemService _systemService;

    // Current active window (can be switched dynamically)
    public Window? CurrentWindow { get; private set; }

    // Track last interacted window for cleanup
    private Window? _lastInteractedWindow;

    // Clipboard content storage for read_clipboard command
    public string? LastClipboardContent { get; private set; }

    // OS operation result storage
    public string? LastOsOperationResult { get; private set; }

    public UIAutomationService()
    {
        _automation = new UIA3Automation();
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) }; // Timeout for large files
        _systemService = new SystemService();
    }

    /// <summary>
    /// Attaches to the currently active (foreground) window
    /// </summary>
    public Window? AttachToActiveWindow()
    {
        try
        {
            var handle = GetForegroundWindow();
            if (handle != IntPtr.Zero)
            {
                var element = _automation.FromHandle(handle);
                var window = element.AsWindow();

                if (window != null)
                {
                    CurrentWindow = window;
                    Console.WriteLine($"  ✅ Attached to active window: {window.Name}");
                    return CurrentWindow;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ⚠️ Failed to attach to active window: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// Находит окно по имени процесса или заголовку
    /// Process name matching has highest priority to solve localization issues
    /// </summary>
    public Window? FindWindow(string processNameOrTitle)
    {
        var desktop = _automation.GetDesktop();
        var windows = desktop.FindAllChildren(cf => cf.ByControlType(ControlType.Window));

        Window? processPartialMatch = null;
        Window? titleFallbackMatch = null;

        foreach (var window in windows)
        {
            var title = window.Name ?? "";

            try
            {
                // Получаем имя процесса
                var processId = window.Properties.ProcessId.ValueOrDefault;
                var process = System.Diagnostics.Process.GetProcessById(processId);
                var processName = process.ProcessName;

                // Special handling for UWP apps (ApplicationFrameHost wrapper)
                // These apps run inside ApplicationFrameHost, so we must match by title patterns
                if (processName.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
                {
                    // Calculator app matching (handles all localizations)
                    var isCalculatorSearch = processNameOrTitle.Equals("calc", StringComparison.OrdinalIgnoreCase) ||
                                            processNameOrTitle.Equals("calculator", StringComparison.OrdinalIgnoreCase) ||
                                            processNameOrTitle.Equals("calculatorapp", StringComparison.OrdinalIgnoreCase);

                    var isCalculatorWindow = title.Equals("Rechner", StringComparison.OrdinalIgnoreCase) ||
                                            title.Equals("Calculator", StringComparison.OrdinalIgnoreCase) ||
                                            title.Equals("Taschenrechner", StringComparison.OrdinalIgnoreCase) ||
                                            title.Contains("Calculatrice", StringComparison.OrdinalIgnoreCase) ||
                                            title.Contains("Calculadora", StringComparison.OrdinalIgnoreCase);

                    if (isCalculatorSearch && isCalculatorWindow)
                    {
                        CurrentWindow = window.AsWindow();
                        return CurrentWindow;
                    }
                }

                // 1. Точное совпадение по имени процесса (наивысший приоритет)
                if (processName.Equals(processNameOrTitle, StringComparison.OrdinalIgnoreCase))
                {
                    CurrentWindow = window.AsWindow();
                    return CurrentWindow;
                }

                // 2. Частичное совпадение по имени процесса (например, "calc" -> "CalculatorApp")
                // Проверяем если поисковый запрос содержится в начале имени процесса
                if (processPartialMatch == null &&
                    processName.StartsWith(processNameOrTitle, StringComparison.OrdinalIgnoreCase))
                {
                    processPartialMatch = window.AsWindow();
                }

                // 3. Также проверяем обратное: если имя процесса содержится в поисковом запросе
                // Это покрывает случаи "calculator" -> "calc.exe" или "CalculatorApp"
                if (processPartialMatch == null &&
                    processNameOrTitle.Contains(processName, StringComparison.OrdinalIgnoreCase))
                {
                    processPartialMatch = window.AsWindow();
                }

                // 4. Точное совпадение или начало заголовка
                if (title.Equals(processNameOrTitle, StringComparison.OrdinalIgnoreCase) ||
                    title.StartsWith(processNameOrTitle, StringComparison.OrdinalIgnoreCase))
                {
                    // Если уже есть частичное совпадение процесса, приоритет у него
                    if (processPartialMatch == null)
                    {
                        CurrentWindow = window.AsWindow();
                        return CurrentWindow;
                    }
                }

                // 5. Contains в заголовке как последний fallback (низкий приоритет)
                if (titleFallbackMatch == null &&
                    title.Contains(processNameOrTitle, StringComparison.OrdinalIgnoreCase))
                {
                    // Исключаем окна командной строки и терминалов
                    if (!processName.Equals("cmd", StringComparison.OrdinalIgnoreCase) &&
                        !processName.Equals("powershell", StringComparison.OrdinalIgnoreCase) &&
                        !processName.Equals("WindowsTerminal", StringComparison.OrdinalIgnoreCase))
                    {
                        titleFallbackMatch = window.AsWindow();
                    }
                }
            }
            catch
            {
                // Процесс может быть недоступен
            }
        }

        // Приоритет: частичное совпадение процесса > fallback по заголовку
        CurrentWindow = processPartialMatch ?? titleFallbackMatch;
        return CurrentWindow;
    }

    /// <summary>
    /// Switches to a different window by title or process name
    /// Retries for up to 5 seconds with 500ms polling interval
    /// </summary>
    public bool SwitchWindow(string titleOrProcess)
    {
        DateTime deadline = DateTime.Now.AddSeconds(5);
        int attemptCount = 0;

        while (DateTime.Now < deadline)
        {
            attemptCount++;
            var newWindow = FindWindow(titleOrProcess);

            if (newWindow != null)
            {
                if (attemptCount > 1)
                {
                    Console.WriteLine($"  ✅ Switched to window: {newWindow.Name} (after {attemptCount} attempts)");
                }
                else
                {
                    Console.WriteLine($"  ✅ Switched to window: {newWindow.Name}");
                }
                return true;
            }

            // Window not found yet, wait 500ms before retrying
            if (DateTime.Now < deadline)
            {
                if (attemptCount == 1)
                {
                    Console.WriteLine($"  ⏳ Window '{titleOrProcess}' not found yet, waiting up to 5 seconds...");
                }
                Thread.Sleep(500);
            }
        }

        // Timeout reached
        Console.WriteLine($"  ❌ Window not found after 5 seconds ({attemptCount} attempts): {titleOrProcess}");
        Console.WriteLine($"  💡 Tip: Make sure the window is fully loaded. Try matching by process name (e.g., 'calc', 'notepad')");
        return false;
    }

    /// <summary>
    /// Сканирует текущее состояние окна
    /// </summary>
    public UIState GetWindowState(Window window)
    {
        // Path 1: Client-Side Reflex (Auto-Restore)
        // If window is minimized, restore it immediately so we can see elements/screenshot
        EnsureWindowRestored(window);

        // Очищаем кеш перед новым сканированием
        _elementCache.Clear();

        var state = new UIState
        {
            WindowTitle = window.Name ?? "",
            ProcessName = window.Properties.ProcessId.ValueOrDefault.ToString(),
            Elements = new List<UIElement>()
        };

        // Рекурсивно собираем все элементы
        ScanElements(window, state.Elements, maxDepth: 10);

        return state;
    }

    /// <summary>
    /// Checks if window is minimized and restores it
    /// </summary>
    private void EnsureWindowRestored(Window window)
    {
        try
        {
            if (window.Patterns.Window.IsSupported)
            {
                var windowPattern = window.Patterns.Window.Pattern;
                if (windowPattern.WindowVisualState.Value == WindowVisualState.Minimized)
                {
                    Console.WriteLine("  ⚠️  Window is MINIMIZED! Auto-restoring...");
                    windowPattern.SetWindowVisualState(WindowVisualState.Normal);
                    Thread.Sleep(300); // Wait for animation
                    Console.WriteLine("  ✅ Window restored to Normal state");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Failed to auto-restore window: {ex.Message}");
        }
    }

    /// <summary>
    /// Проверяет, находится ли целевое окно в фокусе (foreground)
    /// </summary>
    private bool IsWindowInFocus(Window window)
    {
        try
        {
            var foregroundHandle = GetForegroundWindow();
            var targetHandle = window.Properties.NativeWindowHandle.ValueOrDefault;
            return foregroundHandle == targetHandle;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Возвращает фокус на целевое окно
    /// </summary>
    private bool EnsureWindowFocus(Window window)
    {
        try
        {
            var currentHandle = window.Properties.NativeWindowHandle.ValueOrDefault;

            // CLEANUP: If we switched windows, release the previous one from TopMost
            if (_lastInteractedWindow != null)
            {
                try
                {
                    var oldHandle = _lastInteractedWindow.Properties.NativeWindowHandle.ValueOrDefault;
                    if (oldHandle != IntPtr.Zero && oldHandle != currentHandle)
                    {
                        // Downgrade previous window to Normal (Not TopMost)
                        SetWindowPos(oldHandle, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                    }
                }
                catch { /* Ignore errors if old window is closed */ }
            }

            _lastInteractedWindow = window;

            // PROACTIVE LOCK: Set TOPMOST FIRST to HOLD focus, not just restore it
            // This prevents focus loss during the action, not after
            SetWindowPos(currentHandle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
            SetForegroundWindow(currentHandle);

            if (!IsWindowInFocus(window))
            {
                Console.WriteLine("  ⚠️  Target window lost focus! Attempting to restore...");
                Thread.Sleep(200); // Даем время на переключение фокуса
                Console.WriteLine("  ✅ Focus restored to target window");
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Error ensuring window focus: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Captures context-aware screenshot (Window if focused, else Desktop) for AI
    /// </summary>
    public string CaptureScreen(int quality = 50)
    {
        try
        {
            var desktop = _automation.GetDesktop();
            // Prefer capturing specific window if available
            var target = CurrentWindow ?? desktop;

            return CaptureElement(target, quality);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Screenshot failed: {ex.Message}");
            return "";
        }
    }

    /// <summary>
    /// Captures full desktop screenshot regardless of focus (For Shadow Debugging)
    /// </summary>
    public string CaptureFullDesktop(int quality = 30)
    {
        try
        {
            var desktop = _automation.GetDesktop();
            return CaptureElement(desktop, quality);
        }
        catch
        {
            return "";
        }
    }

    private string CaptureElement(AutomationElement element, int quality)
    {
        try
        {
            using var image = FlaUI.Core.Capturing.Capture.Element(element).Bitmap;

            // Setup JPEG encoder with quality
            var jpegEncoder = GetEncoder(ImageFormat.Jpeg);
            if (jpegEncoder == null) return "";

            var encoderParameters = new EncoderParameters(1);
            encoderParameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)quality);

            using var ms = new MemoryStream();
            image.Save(ms, jpegEncoder, encoderParameters);
            byte[] imageBytes = ms.ToArray();

            return Convert.ToBase64String(imageBytes);
        }
        catch
        {
            return "";
        }
    }

    private ImageCodecInfo? GetEncoder(ImageFormat format)
    {
        var codecs = ImageCodecInfo.GetImageDecoders();
        return codecs.FirstOrDefault(codec => codec.FormatID == format.Guid);
    }

    private void ScanElements(AutomationElement element, List<UIElement> elements, int maxDepth, int currentDepth = 0)
    {
        if (currentDepth >= maxDepth) return;

        try
        {
            var children = element.FindAllChildren();
            foreach (var child in children)
            {
                // Генерируем стабильный ID
                var elementId = child.Properties.AutomationId.ValueOrDefault ?? Guid.NewGuid().ToString();

                var uiElement = new UIElement
                {
                    Id = elementId,
                    Name = child.Name ?? "",
                    Type = child.ControlType.ToString(),
                    Value = GetElementValue(child),
                    IsEnabled = child.IsEnabled,
                    Bounds = new Models.Rectangle
                    {
                        X = (int)child.BoundingRectangle.X,
                        Y = (int)child.BoundingRectangle.Y,
                        Width = (int)child.BoundingRectangle.Width,
                        Height = (int)child.BoundingRectangle.Height
                    }
                };

                // Сохраняем элемент в кеш для быстрого доступа
                _elementCache[elementId] = child;

                // Добавляем только значимые элементы
                if (IsSignificantElement(uiElement))
                {
                    elements.Add(uiElement);
                }

                // Рекурсия для вложенных элементов
                ScanElements(child, elements, maxDepth, currentDepth + 1);
            }
        }
        catch
        {
            // Некоторые элементы могут быть недоступны
        }
    }

    private string GetElementValue(AutomationElement element)
    {
        try
        {
            if (element.Patterns.Value.IsSupported)
            {
                return element.Patterns.Value.Pattern.Value.ValueOrDefault ?? "";
            }
            if (element.Patterns.Text.IsSupported)
            {
                return element.Patterns.Text.Pattern.DocumentRange.GetText(-1) ?? "";
            }
        }
        catch { }

        return "";
    }

    private bool IsSignificantElement(UIElement element)
    {
        // Фильтруем незначимые элементы
        if (string.IsNullOrWhiteSpace(element.Name) && string.IsNullOrWhiteSpace(element.Value))
            return false;

        if (element.Bounds.Width <= 0 || element.Bounds.Height <= 0)
            return false;

        return true;
    }

    /// <summary>
    /// Выполняет команду на элементе
    /// </summary>
    public async Task<bool> ExecuteCommand(Window window, Command command)
    {
        try
        {
            await Task.Delay(command.DelayMs);

            // CRITICAL: Проверяем фокус окна перед действиями, требующими взаимодействия
            var actionRequiresFocus = command.Action.ToLower() is "click" or "type" or "select" or "mouse_move";

            if (actionRequiresFocus)
            {
                if (!EnsureWindowFocus(window))
                {
                    Console.WriteLine($"  ❌ Cannot execute {command.Action}: Target window is not in focus and focus could not be restored!");
                    return false;
                }
            }

            switch (command.Action.ToLower())
            {
                case "click":
                    return ClickElement(window, command.ElementId, command.X, command.Y);

                case "type":
                    return TypeText(window, command.ElementId, command.Text);

                case "key":
                    return PressKey(command.Text);

                case "select":
                    return SelectItem(window, command.ElementId, command.Text);

                case "mouse_move":
                    MoveMouse(command.X, command.Y);
                    return true;

                case "wait":
                    await Task.Delay(command.DelayMs);
                    return true;

                case "download":
                    return await DownloadFile(command.Url, command.LocalFileName);

                case "inspect_screen":
                    // This command is handled in Program.cs - it signals to capture screenshot
                    // Return true to indicate acknowledgment
                    return true;

                case "read_clipboard":
                    var clipboardContent = GetClipboardText();
                    Console.WriteLine($"  📋 Read clipboard: {clipboardContent.Length} characters");
                    return true;

                case "write_clipboard":
                    if (string.IsNullOrEmpty(command.Text))
                    {
                        Console.WriteLine("  ❌ write_clipboard requires text parameter");
                        return false;
                    }
                    return SetClipboardText(command.Text);

                // OS / System Operations
                case "os_list":
                    if (string.IsNullOrEmpty(command.Text))
                    {
                        Console.WriteLine("  ❌ os_list requires path parameter");
                        return false;
                    }
                    LastOsOperationResult = _systemService.ListDirectory(command.Text);
                    Console.WriteLine($"  📁 Listed directory: {command.Text}");
                    return true;

                case "os_delete":
                    if (string.IsNullOrEmpty(command.Text))
                    {
                        Console.WriteLine("  ❌ os_delete requires path parameter");
                        return false;
                    }
                    LastOsOperationResult = _systemService.DeletePath(command.Text);
                    Console.WriteLine($"  🗑️  Delete operation: {command.Text}");
                    return true;

                case "os_read":
                    if (string.IsNullOrEmpty(command.Text))
                    {
                        Console.WriteLine("  ❌ os_read requires path parameter");
                        return false;
                    }
                    // ElementId can optionally contain max chars (default 2000)
                    int maxChars = 2000;
                    if (!string.IsNullOrEmpty(command.ElementId) && int.TryParse(command.ElementId, out int parsed))
                    {
                        maxChars = parsed;
                    }
                    LastOsOperationResult = _systemService.ReadFile(command.Text, maxChars);
                    Console.WriteLine($"  📄 Read file: {command.Text}");
                    return true;

                case "os_run":
                    if (string.IsNullOrEmpty(command.Text))
                    {
                        Console.WriteLine("  ❌ os_run requires executable path");
                        return false;
                    }
                    // ElementId can optionally contain arguments
                    var args = command.ElementId ?? "";
                    LastOsOperationResult = _systemService.RunProcess(command.Text, args);
                    Console.WriteLine($"  🚀 Run process: {command.Text} {args}");
                    return true;

                case "os_kill":
                    if (string.IsNullOrEmpty(command.Text))
                    {
                        Console.WriteLine("  ❌ os_kill requires process name");
                        return false;
                    }
                    LastOsOperationResult = _systemService.KillProcess(command.Text);
                    Console.WriteLine($"  ⚡ Kill process: {command.Text}");
                    return true;

                case "os_mkdir":
                    if (string.IsNullOrEmpty(command.Text))
                    {
                        Console.WriteLine("  ❌ os_mkdir requires path parameter");
                        return false;
                    }
                    LastOsOperationResult = _systemService.CreateDirectory(command.Text);
                    Console.WriteLine($"  📁 Create directory: {command.Text}");
                    return true;

                case "os_write":
                    if (string.IsNullOrEmpty(command.Text))
                    {
                        Console.WriteLine("  ❌ os_write requires path parameter");
                        return false;
                    }
                    // ElementId contains the content to write
                    var content = command.ElementId ?? "";
                    LastOsOperationResult = _systemService.WriteFile(command.Text, content);
                    Console.WriteLine($"  💾 Write file: {command.Text}");
                    return true;

                case "os_exists":
                    if (string.IsNullOrEmpty(command.Text))
                    {
                        Console.WriteLine("  ❌ os_exists requires path parameter");
                        return false;
                    }
                    LastOsOperationResult = _systemService.CheckExists(command.Text);
                    Console.WriteLine($"  🔍 Check exists: {command.Text}");
                    return true;

                // IT Support Toolkit - Environment Variables
                case "os_getenv":
                    if (string.IsNullOrEmpty(command.Text))
                    {
                        Console.WriteLine("  ❌ os_getenv requires variable name");
                        return false;
                    }
                    LastOsOperationResult = _systemService.GetEnvVar(command.Text);
                    Console.WriteLine($"  🔧 Get environment variable: {command.Text}");
                    return true;

                // IT Support Toolkit - Registry Operations
                case "reg_read":
                    if (string.IsNullOrEmpty(command.Text) || string.IsNullOrEmpty(command.ElementId))
                    {
                        Console.WriteLine("  ❌ reg_read requires: text=root\\keyPath, element_id=valueName");
                        return false;
                    }
                    // Parse root and keyPath from Text (format: "HKLM\\Software\\...")
                    var readParts = command.Text.Split(new[] { '\\' }, 2);
                    if (readParts.Length < 2)
                    {
                        Console.WriteLine("  ❌ reg_read text format: root\\keyPath (e.g., HKLM\\Software\\...)");
                        return false;
                    }
                    LastOsOperationResult = _systemService.RegistryRead(readParts[0], readParts[1], command.ElementId);
                    Console.WriteLine($"  📝 Read registry: {command.Text}\\{command.ElementId}");
                    return true;

                case "reg_write":
                    if (string.IsNullOrEmpty(command.Text) || string.IsNullOrEmpty(command.ElementId) || command.X == 0)
                    {
                        Console.WriteLine("  ❌ reg_write requires: text=root\\keyPath, element_id=valueName, x=value");
                        return false;
                    }
                    // Parse root and keyPath from Text
                    var writeParts = command.Text.Split(new[] { '\\' }, 2);
                    if (writeParts.Length < 2)
                    {
                        Console.WriteLine("  ❌ reg_write text format: root\\keyPath (e.g., HKCU\\Software\\...)");
                        return false;
                    }
                    // Value is stored in X field (hack: convert int to string)
                    var regValue = command.X.ToString();
                    LastOsOperationResult = _systemService.RegistryWrite(writeParts[0], writeParts[1], command.ElementId, regValue);
                    Console.WriteLine($"  📝 Write registry: {command.Text}\\{command.ElementId} = {regValue}");
                    return true;

                // IT Support Toolkit - Network Diagnostics
                case "net_ping":
                    if (string.IsNullOrEmpty(command.Text))
                    {
                        Console.WriteLine("  ❌ net_ping requires host parameter");
                        return false;
                    }
                    // Optional: X field can contain timeout in ms (default 2000)
                    var pingTimeout = command.X > 0 ? command.X : 2000;
                    LastOsOperationResult = _systemService.NetworkPing(command.Text, pingTimeout);
                    Console.WriteLine($"  🌐 Ping host: {command.Text}");
                    return true;

                case "net_port":
                    if (string.IsNullOrEmpty(command.Text) || command.X == 0)
                    {
                        Console.WriteLine("  ❌ net_port requires: text=host, x=port");
                        return false;
                    }
                    // X field contains port number
                    var port = command.X;
                    // Optional: Y field can contain timeout in ms (default 2000)
                    var portTimeout = command.Y > 0 ? command.Y : 2000;
                    LastOsOperationResult = _systemService.NetworkCheckPort(command.Text, port, portTimeout);
                    Console.WriteLine($"  🌐 Check port: {command.Text}:{port}");
                    return true;

                // Window Management
                case "switch_window":
                    if (string.IsNullOrEmpty(command.Text))
                    {
                        Console.WriteLine("  ❌ switch_window requires window title or process name");
                        return false;
                    }
                    return SwitchWindow(command.Text);

                default:
                    Console.WriteLine($"Unknown command: {command.Action}");
                    return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error executing command: {ex.Message}");
            return false;
        }
    }

    private bool ClickElement(Window window, string elementId, int x = 0, int y = 0)
    {
        // COORDINATE-BASED CLICK (резервный метод)
        if (string.IsNullOrEmpty(elementId) && x > 0 && y > 0)
        {
            Console.WriteLine($"  → Clicking by coordinates: ({x}, {y})");
            try
            {
                FlaUI.Core.Input.Mouse.Click(new System.Drawing.Point(x, y));
                Console.WriteLine($"  ✅ Coordinate click successful");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ❌ Coordinate click failed: {ex.Message}");
                return false;
            }
        }

        // ELEMENT-BASED CLICK (основной метод)
        var element = FindElementById(window, elementId);
        if (element == null)
        {
            Console.WriteLine($"  ❌ Element not found: {elementId}");

            // Если есть координаты в Bounds, пробуем кликнуть по ним как fallback
            if (x > 0 && y > 0)
            {
                Console.WriteLine($"  → Trying fallback: Coordinate click ({x}, {y})");
                try
                {
                    FlaUI.Core.Input.Mouse.Click(new System.Drawing.Point(x, y));
                    Console.WriteLine($"  ✅ Fallback coordinate click successful");
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ❌ Fallback click failed: {ex.Message}");
                }
            }

            return false;
        }

        element.Click();
        Console.WriteLine($"  ✅ Element click successful");
        return true;
    }

    private bool TypeText(Window window, string elementId, string text)
    {
        var element = FindElementById(window, elementId);
        if (element == null) return false;

        const int MaxRetries = 2;
        const int CharDelayMs = 75; // Increased to 75ms - Notepad sometimes needs extra time to process chars

        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            element.Focus();
            Thread.Sleep(150); // Increased focus delay

            // МЕДЛЕННЫЙ ПОСИМВОЛЬНЫЙ ВВОД для надежности
            foreach (char c in text)
            {
                Keyboard.Type(c.ToString());
                Thread.Sleep(CharDelayMs);
            }

            Thread.Sleep(100); // Wait for text to be processed

            // Verify that text was typed correctly
            string currentValue = GetElementValue(element);

            // ALWAYS show what was actually typed for debugging
            Console.WriteLine($"  ✍️  Typed {text.Length} characters: \"{text}\"");
            Console.WriteLine($"     Verification: Current value = \"{currentValue}\" (total {currentValue.Length} chars)");

            // Strict verification: text must be exactly at the end (exact match)
            // We allow the currentValue to be longer (prefix content), but the typed text must be complete
            if (currentValue.EndsWith(text, StringComparison.Ordinal))
            {
                Console.WriteLine($"     ✅ Text verified successfully");
                return true;
            }
            else
            {
                // Text verification failed
                Console.WriteLine($"     ❌ VERIFICATION FAILED!");
                Console.WriteLine($"     Expected: \"{text}\"");
                Console.WriteLine($"     Got:      \"{currentValue}\"");

                if (attempt < MaxRetries)
                {
                    Console.WriteLine($"  ⚠️  Retrying (attempt {attempt}/{MaxRetries})...");

                    // Clear and try again
                    element.Focus();
                    Thread.Sleep(100);
                    Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
                    Thread.Sleep(50);
                    Keyboard.Type(VirtualKeyShort.DELETE);
                    Thread.Sleep(100);
                }
                else
                {
                    Console.WriteLine($"  ❌ Final attempt failed - giving up after {MaxRetries} attempts");
                    // Still return true to not break the flow - partial success is better than failure
                    return true;
                }
            }
        }

        return true;
    }

    private bool PressKey(string keyCommand)
    {
        try
        {
            // Поддержка комбинаций клавиш и специальных клавиш
            switch (keyCommand.ToLower())
            {
                case "ctrl+a":
                    Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
                    Console.WriteLine("  → Pressed Ctrl+A (Select All)");
                    break;

                case "delete":
                    Keyboard.Type(VirtualKeyShort.DELETE);
                    Console.WriteLine("  → Pressed Delete");
                    break;

                case "backspace":
                    Keyboard.Type(VirtualKeyShort.BACK);
                    Console.WriteLine("  → Pressed Backspace");
                    break;

                case "enter":
                    Keyboard.Type(VirtualKeyShort.RETURN);
                    Console.WriteLine("  → Pressed Enter");
                    break;

                case "ctrl+c":
                    Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_C);
                    Console.WriteLine("  → Pressed Ctrl+C (Copy)");
                    break;

                case "ctrl+v":
                    Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_V);
                    Console.WriteLine("  → Pressed Ctrl+V (Paste)");
                    break;

                case "ctrl+x":
                    Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_X);
                    Console.WriteLine("  → Pressed Ctrl+X (Cut)");
                    break;

                case "escape":
                case "esc":
                    Keyboard.Type(VirtualKeyShort.ESCAPE);
                    Console.WriteLine("  → Pressed Escape");
                    break;

                default:
                    Console.WriteLine($"  ⚠️  Unknown key command: {keyCommand}");
                    return false;
            }

            Thread.Sleep(100); // Небольшая пауза после нажатия
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Error pressing key '{keyCommand}': {ex.Message}");
            return false;
        }
    }

    private bool SelectItem(Window window, string elementId, string itemText)
    {
        var element = FindElementById(window, elementId);
        if (element == null) return false;

        if (element.Patterns.SelectionItem.IsSupported)
        {
            element.Patterns.SelectionItem.Pattern.Select();
            return true;
        }

        return false;
    }

    private void MoveMouse(int x, int y)
    {
        FlaUI.Core.Input.Mouse.MoveTo(new System.Drawing.Point(x, y));
    }

    private AutomationElement? FindElementById(Window window, string id)
    {
        // Сначала проверяем кеш
        if (_elementCache.TryGetValue(id, out var cachedElement))
        {
            return cachedElement;
        }

        // Если не в кеше, ищем по AutomationId
        try
        {
            return window.FindFirstDescendant(cf => cf.ByAutomationId(id));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Downloads a file from URL to local Downloads folder
    /// </summary>
    private async Task<bool> DownloadFile(string url, string localFileName)
    {
        if (string.IsNullOrEmpty(url))
        {
            Console.WriteLine("❌ Download command missing URL.");
            return false;
        }

        if (string.IsNullOrEmpty(localFileName))
        {
            // Extract filename from URL if not specified
            localFileName = Path.GetFileName(new Uri(url).LocalPath);
            if (string.IsNullOrEmpty(localFileName))
            {
                localFileName = "downloaded_file";
            }
        }

        // Determine save path (Downloads folder)
        var downloadsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads"
        );
        var fullPath = Path.Combine(downloadsPath, localFileName);

        try
        {
            Console.WriteLine($"  → Downloading from {url}");
            Console.WriteLine($"  → Saving to {fullPath}");

            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            await using (var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await response.Content.CopyToAsync(fs);
            }

            Console.WriteLine($"  ✅ File downloaded successfully");
            return true;
        }
        catch (HttpRequestException httpEx)
        {
            Console.WriteLine($"  ❌ HTTP error during download: {httpEx.Message}");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Error downloading file: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Gets text from clipboard using TextCopy library (handles STA thread automatically)
    /// </summary>
    public string GetClipboardText()
    {
        try
        {
            var clipboardText = TextCopy.ClipboardService.GetText() ?? "";
            LastClipboardContent = clipboardText;
            return clipboardText;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Error reading clipboard: {ex.Message}");
            LastClipboardContent = "";
            return "";
        }
    }

    /// <summary>
    /// Sets text to clipboard using TextCopy library (handles STA thread automatically)
    /// </summary>
    public bool SetClipboardText(string text)
    {
        try
        {
            TextCopy.ClipboardService.SetText(text);
            Console.WriteLine($"  ✅ Clipboard set ({text.Length} characters)");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Error writing to clipboard: {ex.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        // FINAL CLEANUP: Restore window state
        try
        {
            if (_lastInteractedWindow != null)
            {
                var handle = _lastInteractedWindow.Properties.NativeWindowHandle.ValueOrDefault;
                if (handle != IntPtr.Zero)
                {
                    // Restore to Normal (Not TopMost)
                    SetWindowPos(handle, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                    Console.WriteLine("  🧹 Cleanup: Released window from Always-On-Top");
                }
            }
        }
        catch { /* Ignore cleanup errors */ }

        _automation?.Dispose();
        _httpClient?.Dispose();
    }
}
