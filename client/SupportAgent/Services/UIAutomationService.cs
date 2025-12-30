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

    private readonly UIA3Automation _automation;
    private Window? _currentWindow;
    private Dictionary<string, AutomationElement> _elementCache = new();
    private readonly HttpClient _httpClient;

    public UIAutomationService()
    {
        _automation = new UIA3Automation();
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) }; // Timeout for large files
    }

    /// <summary>
    /// Находит окно по имени процесса или заголовку
    /// </summary>
    public Window? FindWindow(string processNameOrTitle)
    {
        var desktop = _automation.GetDesktop();
        var windows = desktop.FindAllChildren(cf => cf.ByControlType(ControlType.Window));

        Window? fallbackMatch = null;

        foreach (var window in windows)
        {
            var title = window.Name ?? "";

            try
            {
                // Получаем имя процесса
                var processId = window.Properties.ProcessId.ValueOrDefault;
                var process = System.Diagnostics.Process.GetProcessById(processId);
                var processName = process.ProcessName;

                // 1. Точное совпадение по имени процесса (наивысший приоритет)
                if (processName.Equals(processNameOrTitle, StringComparison.OrdinalIgnoreCase))
                {
                    _currentWindow = window.AsWindow();
                    return _currentWindow;
                }

                // 2. Точное совпадение или начало заголовка
                if (title.Equals(processNameOrTitle, StringComparison.OrdinalIgnoreCase) ||
                    title.StartsWith(processNameOrTitle, StringComparison.OrdinalIgnoreCase))
                {
                    _currentWindow = window.AsWindow();
                    return _currentWindow;
                }

                // 3. Contains как fallback (низкий приоритет)
                if (fallbackMatch == null && title.Contains(processNameOrTitle, StringComparison.OrdinalIgnoreCase))
                {
                    // Исключаем окна командной строки и терминалов
                    if (!processName.Equals("cmd", StringComparison.OrdinalIgnoreCase) &&
                        !processName.Equals("powershell", StringComparison.OrdinalIgnoreCase) &&
                        !processName.Equals("WindowsTerminal", StringComparison.OrdinalIgnoreCase))
                    {
                        fallbackMatch = window.AsWindow();
                    }
                }
            }
            catch
            {
                // Процесс может быть недоступен
            }
        }

        _currentWindow = fallbackMatch;
        return _currentWindow;
    }

    /// <summary>
    /// Сканирует текущее состояние окна
    /// </summary>
    public UIState GetWindowState(Window window)
    {
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
            if (IsWindowInFocus(window))
            {
                return true; // Окно уже в фокусе
            }

            Console.WriteLine("  ⚠️  Target window lost focus! Attempting to restore...");

            var targetHandle = window.Properties.NativeWindowHandle.ValueOrDefault;
            bool success = SetForegroundWindow(targetHandle);

            if (success)
            {
                Thread.Sleep(200); // Даем время на переключение фокуса
                Console.WriteLine("  ✅ Focus restored to target window");
                return true;
            }
            else
            {
                Console.WriteLine("  ❌ Failed to restore window focus!");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Error ensuring window focus: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Captures screenshot with specified quality (1-100)
    /// </summary>
    public string CaptureScreen(int quality = 50)
    {
        try
        {
            var desktop = _automation.GetDesktop();
            // Prefer capturing specific window if available
            var target = _currentWindow ?? desktop;

            using var image = FlaUI.Core.Capturing.Capture.Element(target).Bitmap;

            // Setup JPEG encoder with quality
            var jpegEncoder = GetEncoder(ImageFormat.Jpeg);
            if (jpegEncoder == null)
            {
                Console.WriteLine("❌ JPEG encoder not found");
                return "";
            }

            var encoderParameters = new EncoderParameters(1);
            encoderParameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)quality);

            using var ms = new MemoryStream();
            image.Save(ms, jpegEncoder, encoderParameters);
            byte[] imageBytes = ms.ToArray();

            return Convert.ToBase64String(imageBytes);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Screenshot failed: {ex.Message}");
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
                    return ClickElement(window, command.ElementId);

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

    private bool ClickElement(Window window, string elementId)
    {
        var element = FindElementById(window, elementId);
        if (element == null)
        {
            Console.WriteLine($"Element not found: {elementId}");
            return false;
        }

        element.Click();
        return true;
    }

    private bool TypeText(Window window, string elementId, string text)
    {
        var element = FindElementById(window, elementId);
        if (element == null) return false;

        element.Focus();
        Thread.Sleep(100); // Даем время на фокус

        // МЕДЛЕННЫЙ ПОСИМВОЛЬНЫЙ ВВОД для надежности
        // Keyboard.Type() слишком быстрый и теряет символы
        foreach (char c in text)
        {
            Keyboard.Type(c.ToString());
            Thread.Sleep(20); // Задержка между символами (20ms = надежно)
        }

        Console.WriteLine($"  ✍️  Typed {text.Length} characters: \"{text}\"");
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

    public void Dispose()
    {
        _automation?.Dispose();
        _httpClient?.Dispose();
    }
}
