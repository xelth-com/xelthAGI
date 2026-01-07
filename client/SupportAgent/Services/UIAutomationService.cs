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
using System.Text;

namespace SupportAgent.Services;

public class UIAutomationService : IDisposable
{
    // Windows API
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    // Keyboard Layout API
    [DllImport("user32.dll")]
    private static extern IntPtr LoadKeyboardLayout(string pwszKLID, uint Flags);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    private const uint WM_INPUTLANGCHANGEREQUEST = 0x0050;
    private const string LANG_EN_US = "00000409";
    private const uint KLF_ACTIVATE = 1;

    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_SHOWWINDOW = 0x0040;

    // i18n Translation Map for Smart Localization
    // Maps English UI terms to their localized equivalents
    private static readonly Dictionary<string, Dictionary<string, string>> I18nMap = new()
    {
        ["File"] = new() { { "de", "Datei" }, { "fr", "Fichier" }, { "ru", "Файл" } },
        ["Edit"] = new() { { "de", "Bearbeiten" }, { "fr", "Édition" }, { "ru", "Правка" } },
        ["View"] = new() { { "de", "Ansicht" }, { "fr", "Affichage" }, { "ru", "Вид" } },
        ["Insert"] = new() { { "de", "Einfügen" }, { "fr", "Insertion" }, { "ru", "Вставка" } },
        ["Format"] = new() { { "de", "Format" }, { "fr", "Format" }, { "ru", "Формат" } },
        ["Tools"] = new() { { "de", "Extras" }, { "fr", "Outils" }, { "ru", "Сервис" } },
        ["Help"] = new() { { "de", "Hilfe" }, { "fr", "Aide" }, { "ru", "Справка" } },
        ["Save"] = new() { { "de", "Speichern" }, { "fr", "Enregistrer" }, { "ru", "Сохранить" } },
        ["Don't Save"] = new() { { "de", "Nicht speichern" }, { "fr", "Ne pas enregistrer" }, { "ru", "Не сохранять" } },
        ["Cancel"] = new() { { "de", "Abbrechen" }, { "fr", "Annuler" }, { "ru", "Отмена" } },
        ["Open"] = new() { { "de", "Öffnen" }, { "fr", "Ouvrir" }, { "ru", "Открыть" } },
        ["Close"] = new() { { "de", "Schließen" }, { "fr", "Fermer" }, { "ru", "Закрыть" } },
        ["New"] = new() { { "de", "Neu" }, { "fr", "Nouveau" }, { "ru", "Создать" } },
        ["Print"] = new() { { "de", "Drucken" }, { "fr", "Imprimer" }, { "ru", "Печать" } },
        ["Copy"] = new() { { "de", "Kopieren" }, { "fr", "Copier" }, { "ru", "Копировать" } },
        ["Paste"] = new() { { "de", "Einfügen" }, { "fr", "Coller" }, { "ru", "Вставить" } },
        ["Cut"] = new() { { "de", "Ausschneiden" }, { "fr", "Couper" }, { "ru", "Вырезать" } },
        ["Undo"] = new() { { "de", "Rückgängig" }, { "fr", "Annuler" }, { "ru", "Отменить" } },
        ["Redo"] = new() { { "de", "Wiederholen" }, { "fr", "Rétablir" }, { "ru", "Повторить" } },
        ["Find"] = new() { { "de", "Suchen" }, { "fr", "Rechercher" }, { "ru", "Найти" } },
        ["Replace"] = new() { { "de", "Ersetzen" }, { "fr", "Remplacer" }, { "ru", "Заменить" } }
    };

    private readonly UIA3Automation _automation;
    private Dictionary<string, AutomationElement> _elementCache = new();
    private readonly HttpClient _httpClient;
    private readonly SystemService _systemService;
    private readonly string _currentLanguage; // Detected OS UI language (e.g., "de", "fr", "ru")

    public Window? CurrentWindow { get; private set; }
    private Window? _lastInteractedWindow;
    public string? LastClipboardContent { get; private set; }
    public string? LastOsOperationResult { get; private set; }

    public UIAutomationService()
    {
        _automation = new UIA3Automation();
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) }; // Timeout for large files
        _systemService = new SystemService();

        // Auto-detect OS language for smart i18n element search
        _currentLanguage = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        Console.WriteLine($"  🌍 Detected OS Language: {_currentLanguage.ToUpper()}");
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

    public Window? FindWindow(string processNameOrTitle)
    {
        var desktop = _automation.GetDesktop();

        // FIRST: Try top-level windows (fast path)
        var windows = desktop.FindAllChildren(cf => cf.ByControlType(ControlType.Window));
        var result = SearchWindowsForMatch(windows, processNameOrTitle);
        if (result != null)
        {
            CurrentWindow = result;
            return CurrentWindow;
        }

        // SECOND: Try recursive search for child/modal dialogs (slower but catches more)
        Console.WriteLine($"  🔍 Top-level search failed, trying recursive search for child windows...");
        var allWindows = desktop.FindAllDescendants(cf => cf.ByControlType(ControlType.Window));
        result = SearchWindowsForMatch(allWindows, processNameOrTitle);
        CurrentWindow = result;
        return CurrentWindow;
    }

    private Window? SearchWindowsForMatch(AutomationElement[] windows, string processNameOrTitle)
    {
        Window? processPartialMatch = null;
        Window? titleFallbackMatch = null;

        foreach (var window in windows)
        {
            try
            {
                var title = window.Name ?? "";
                var processId = window.Properties.ProcessId.ValueOrDefault;
                var process = System.Diagnostics.Process.GetProcessById(processId);
                var processName = process.ProcessName;

                if (processName.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
                {
                    if (IsCalculator(processNameOrTitle, title))
                    {
                        return window.AsWindow();
                    }
                }

                if (processName.Equals(processNameOrTitle, StringComparison.OrdinalIgnoreCase))
                {
                    return window.AsWindow();
                }

                if (processPartialMatch == null && processName.StartsWith(processNameOrTitle, StringComparison.OrdinalIgnoreCase))
                    processPartialMatch = window.AsWindow();

                if (processPartialMatch == null && processNameOrTitle.Contains(processName, StringComparison.OrdinalIgnoreCase))
                    processPartialMatch = window.AsWindow();

                if (title.Equals(processNameOrTitle, StringComparison.OrdinalIgnoreCase) || title.StartsWith(processNameOrTitle, StringComparison.OrdinalIgnoreCase))
                {
                    if (processPartialMatch == null)
                    {
                        return window.AsWindow();
                    }
                }

                if (titleFallbackMatch == null && title.Contains(processNameOrTitle, StringComparison.OrdinalIgnoreCase))
                {
                    if (!IsTerminalProcess(processName))
                        titleFallbackMatch = window.AsWindow();
                }
            }
            catch { }
        }

        return processPartialMatch ?? titleFallbackMatch;
    }

    private bool IsCalculator(string search, string title)
    {
        var isSearch = search.Equals("calc", StringComparison.OrdinalIgnoreCase) ||
                       search.Equals("calculator", StringComparison.OrdinalIgnoreCase);
        var isWindow = title.Equals("Rechner", StringComparison.OrdinalIgnoreCase) ||
                       title.Equals("Calculator", StringComparison.OrdinalIgnoreCase) ||
                       title.Contains("Calculatrice", StringComparison.OrdinalIgnoreCase);
        return isSearch && isWindow;
    }

    private bool IsTerminalProcess(string name)
    {
        return name.Equals("cmd", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("powershell", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("WindowsTerminal", StringComparison.OrdinalIgnoreCase);
    }

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
                // CRITICAL: Activate window - restore if minimized, bring to foreground, and focus
                try
                {
                    EnsureWindowRestored(newWindow);
                    Thread.Sleep(100); // Wait for restore

                    newWindow.SetForeground();
                    Thread.Sleep(100); // Wait for foreground

                    newWindow.Focus();
                    Thread.Sleep(100); // Wait for focus

                    Console.WriteLine($"  ✅ Switched to window: {newWindow.Name}");
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ⚠️  Window activation partially failed: {ex.Message}");
                    // Still return true if we found the window, even if activation had issues
                    return true;
                }
            }

            if (DateTime.Now < deadline) Thread.Sleep(500);
        }

        Console.WriteLine($"  ❌ Window not found after 5 seconds: {titleOrProcess}");
        return false;
    }

    public UIState GetWindowState(Window window)
    {
        EnsureWindowRestored(window);
        _elementCache.Clear();

        var state = new UIState
        {
            WindowTitle = window.Name ?? "",
            ProcessName = window.Properties.ProcessId.ValueOrDefault.ToString(),
            Elements = new List<UIElement>()
        };

        // Scan main window
        ScanElements(window, state.Elements, maxDepth: 10);

        // CRITICAL: Also scan modal/popup windows (e.g., "Save changes?" dialogs)
        // These are separate top-level windows that won't be found as children
        try
        {
            var modalWindows = window.ModalWindows;
            if (modalWindows != null && modalWindows.Length > 0)
            {
                foreach (var modal in modalWindows)
                {
                    int beforeCount = state.Elements.Count;
                    Console.WriteLine($"  🪟 Scanning modal window: {modal.Name} (Type: {modal.ControlType})");

                    // DEBUG: Check children count
                    try
                    {
                        var children = modal.FindAllChildren();
                        Console.WriteLine($"  🔍 Modal has {children?.Length ?? 0} direct children");
                        if (children != null && children.Length > 0)
                        {
                            foreach (var child in children.Take(5)) // Show first 5
                            {
                                Console.WriteLine($"     - {child.ControlType}: '{child.Name}' (Enabled: {child.IsEnabled})");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  ⚠️ Could not enumerate children: {ex.Message}");
                    }

                    ScanElements(modal, state.Elements, maxDepth: 10);
                    int afterCount = state.Elements.Count;
                    Console.WriteLine($"  📊 Modal scan added {afterCount - beforeCount} elements (total: {afterCount})");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ⚠️ Modal scan failed: {ex.Message}");
        }

        return state;
    }

    private void EnsureWindowRestored(Window window)
    {
        try
        {
            if (window.Patterns.Window.IsSupported)
            {
                var windowPattern = window.Patterns.Window.Pattern;
                if (windowPattern.WindowVisualState.Value == WindowVisualState.Minimized)
                {
                    windowPattern.SetWindowVisualState(WindowVisualState.Normal);
                    Thread.Sleep(300);
                }
            }
        }
        catch { }
    }

    private bool EnsureWindowFocus(Window window)
    {
        try
        {
            var currentHandle = window.Properties.NativeWindowHandle.ValueOrDefault;

            if (_lastInteractedWindow != null)
            {
                try
                {
                    var oldHandle = _lastInteractedWindow.Properties.NativeWindowHandle.ValueOrDefault;
                    if (oldHandle != IntPtr.Zero && oldHandle != currentHandle)
                    {
                        SetWindowPos(oldHandle, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                    }
                }
                catch { }
            }

            _lastInteractedWindow = window;
            SetWindowPos(currentHandle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
            SetForegroundWindow(currentHandle);

            return true;
        }
        catch
        {
            return false;
        }
    }

    public string CaptureScreen(int quality = 50)
    {
        try
        {
            // ALWAYS capture full desktop for AI context.
            // Window-only captures often miss popups or render black/empty for some Win32 apps.
            var desktop = _automation.GetDesktop();
            return CaptureElement(desktop, quality);
        }
        catch
        {
            return "";
        }
    }

    public string CaptureFullDesktop(int quality = 30)
    {
        try
        {
            return CaptureElement(_automation.GetDesktop(), quality);
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// Captures the full desktop and saves it to a file (for coarse-to-fine vision)
    /// ASYNC: Uses fire-and-forget pattern to avoid blocking main thread on disk I/O
    /// </summary>
    public bool CaptureScreenToFile(string filePath)
    {
        try
        {
            var desktop = _automation.GetDesktop();
            using var image = FlaUI.Core.Capturing.Capture.Element(desktop).Bitmap;

            // CRITICAL: Save synchronously to ensure file exists before caller continues
            // Coarse-to-Fine vision depends on this file being ready immediately
            image.Save(filePath, ImageFormat.Png);
            Console.WriteLine($"  📷 Screenshot saved to: {filePath}");

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Screenshot capture failed: {ex.Message}");
            return false;
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
            return Convert.ToBase64String(ms.ToArray());
        }
        catch
        {
            return "";
        }
    }

    private ImageCodecInfo? GetEncoder(ImageFormat format)
    {
        return ImageCodecInfo.GetImageDecoders().FirstOrDefault(codec => codec.FormatID == format.Guid);
    }

    private void ScanElements(AutomationElement element, List<UIElement> elements, int maxDepth, int currentDepth = 0)
    {
        if (currentDepth >= maxDepth) return;

        try
        {
            var children = element.FindAllChildren();
            foreach (var child in children)
            {
                try
                {
                    // Safe property access - some elements throw exceptions when accessing properties
                    var elementId = child.Properties.AutomationId.ValueOrDefault ?? Guid.NewGuid().ToString();

                    string name = "";
                    try { name = child.Name ?? ""; } catch { name = ""; }

                    string type = "";
                    try { type = child.ControlType.ToString(); } catch { type = "Unknown"; }

                    bool isEnabled = true;
                    try { isEnabled = child.IsEnabled; } catch { }

                    var uiElement = new UIElement
                    {
                        Id = elementId,
                        Name = name,
                        Type = type,
                        Value = GetElementValue(child),
                        IsEnabled = isEnabled,
                        Bounds = new Models.Rectangle
                        {
                            X = (int)child.BoundingRectangle.X,
                            Y = (int)child.BoundingRectangle.Y,
                            Width = (int)child.BoundingRectangle.Width,
                            Height = (int)child.BoundingRectangle.Height
                        }
                    };

                    _elementCache[elementId] = child;

                    if (IsSignificantElement(uiElement))
                    {
                        elements.Add(uiElement);
                    }

                    ScanElements(child, elements, maxDepth, currentDepth + 1);
                }
                catch (Exception ex)
                {
                    // Skip elements that throw exceptions during property access
                    Console.WriteLine($"  ⚠️ Skipped element (property access error): {ex.Message}");
                }
            }
        }
        catch { }
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
        if (string.IsNullOrWhiteSpace(element.Name) && string.IsNullOrWhiteSpace(element.Value)) return false;
        if (element.Bounds.Width <= 0 || element.Bounds.Height <= 0) return false;
        return true;
    }

    public async Task<bool> ExecuteCommand(Window window, Command command)
    {
        try
        {
            await Task.Delay(command.DelayMs);
            var actionRequiresFocus = command.Action.ToLower() is "click" or "type" or "select" or "mouse_move";

            if (actionRequiresFocus)
            {
                EnsureWindowFocus(window);
            }

            switch (command.Action.ToLower())
            {
                case "click": return ClickElement(window, command.ElementId, command.X, command.Y);
                case "type": return TypeText(window, command.ElementId, command.Text);
                case "key": return PressKey(command.Text);
                case "select": return SelectItem(window, command.ElementId, command.Text);
                case "mouse_move": MoveMouse(command.X, command.Y); return true;
                case "wait": await Task.Delay(command.DelayMs); return true;
                case "download": return await DownloadFile(command.Url, command.LocalFileName);
                case "inspect_screen": return true;
                case "read_clipboard":
                    GetClipboardText();
                    Console.WriteLine($"  📋 Read clipboard");
                    return true;
                case "write_clipboard":
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
        var element = FindElementById(window, elementId);
        if (element == null)
        {
            if (x > 0 && y > 0)
            {
                try
                {
                    FlaUI.Core.Input.Mouse.Click(new System.Drawing.Point(x, y));
                    Console.WriteLine($"  ✅ Clicked coordinates ({x}, {y})");
                    return true;
                }
                catch { return false; }
            }
            return false;
        }

        element.Click();
        Console.WriteLine($"  ✅ Element clicked");
        return true;
    }

    /// <summary>
    /// Hybrid Strategy: Clipboard Injection (Fast) -> Fallback to Enforced Layout Typing (Reliable)
    /// </summary>
    private bool TypeText(Window window, string elementId, string text)
    {
        var element = FindElementById(window, elementId);
        if (element == null) return false;

        element.Focus();
        Thread.Sleep(100);

        // --- SMART TEXT MODE DETECTION ---
        // APPEND:text  → add to end (no clear)
        // PREPEND:text → add to beginning (Home, then type)
        // REPLACE:text → clear and replace (default)
        // text         → clear and replace (default)

        bool shouldClear = true;
        bool prependMode = false;
        string actualText = text;

        if (text.StartsWith("APPEND:", StringComparison.OrdinalIgnoreCase))
        {
            shouldClear = false;
            actualText = text.Substring(7); // Remove "APPEND:" prefix
            Console.WriteLine($"  📌 APPEND mode - adding to end");
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.END); // Jump to end
            Thread.Sleep(50);
        }
        else if (text.StartsWith("PREPEND:", StringComparison.OrdinalIgnoreCase))
        {
            shouldClear = false;
            prependMode = true;
            actualText = text.Substring(8); // Remove "PREPEND:" prefix
            Console.WriteLine($"  📌 PREPEND mode - adding to beginning");
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.HOME); // Jump to start
            Thread.Sleep(50);
        }
        else if (text.StartsWith("REPLACE:", StringComparison.OrdinalIgnoreCase))
        {
            actualText = text.Substring(8); // Remove "REPLACE:" prefix
            Console.WriteLine($"  📌 REPLACE mode - clearing and replacing");
        }
        else
        {
            // Default behavior: REPLACE
            Console.WriteLine($"  📌 Default REPLACE mode - clearing field");
        }

        // --- STRATEGY 1: CLIPBOARD INJECTION (Best for speed and layout independence) ---
        Console.WriteLine("  ⚡ Trying Clipboard Injection...");
        string? originalClipboard = null;
        try { originalClipboard = TextCopy.ClipboardService.GetText(); } catch { }

        try
        {
            TextCopy.ClipboardService.SetText(actualText);

            // Clear before paste if needed
            if (shouldClear)
            {
                Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
                Thread.Sleep(50);
            }

            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_V);
            Thread.Sleep(100);

            if (VerifyText(element, actualText))
            {
                Console.WriteLine("     ✅ Clipboard injection successful");
                // Restore clipboard (optional, polite behavior)
                if (originalClipboard != null) try { TextCopy.ClipboardService.SetText(originalClipboard); } catch { }
                return true;
            }
        }
        catch { }

        // --- STRATEGY 2: FORCE ENGLISH LAYOUT + TYPING (Fallback) ---
        Console.WriteLine("  ⚠️  Clipboard failed. Trying Force English Layout...");

        // Force layout switch on the target window
        var handle = window.Properties.NativeWindowHandle.ValueOrDefault;
        if (handle != IntPtr.Zero)
        {
            PostMessage(handle, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, LoadKeyboardLayout(LANG_EN_US, KLF_ACTIVATE));
            Thread.Sleep(50); // Wait for switch
        }

        // Clear field first if needed
        if (shouldClear)
        {
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
            Keyboard.Type(VirtualKeyShort.DELETE);
            Thread.Sleep(50);
        }

        // Type normally
        Keyboard.Type(actualText);
        Thread.Sleep(100);

        if (VerifyText(element, actualText))
        {
            Console.WriteLine("     ✅ Typed successfully (ENG Layout)");
            return true;
        }

        Console.WriteLine("     ❌ All typing methods failed");
        return false;
    }

    private bool VerifyText(AutomationElement element, string expected)
    {
        string val = GetElementValue(element);
        return val.Contains(expected, StringComparison.Ordinal);
    }

    private bool PressKey(string keyCommand)
    {
        try
        {
            switch (keyCommand.ToLower())
            {
                case "enter": Keyboard.Type(VirtualKeyShort.RETURN); break;
                case "esc": Keyboard.Type(VirtualKeyShort.ESCAPE); break;
                case "backspace": Keyboard.Type(VirtualKeyShort.BACK); break;
                case "delete": Keyboard.Type(VirtualKeyShort.DELETE); break;
                case "win": Keyboard.Type(VirtualKeyShort.LWIN); break;
                case "ctrl+a": Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A); break;
                case "ctrl+c": Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_C); break;
                case "ctrl+v": Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_V); break;
                default:
                    // Parse and execute keyboard commands using FlaUI (more reliable than SendKeys)
                    if (string.IsNullOrEmpty(keyCommand))
                    {
                        Console.WriteLine("  ⚠️  Empty key command");
                        return false;
                    }
                    Console.WriteLine($"  ⌨️  Processing keyboard input: {keyCommand}");
                    return ExecuteKeyboardSequence(keyCommand);
            }
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ PressKey failed: {ex.Message}");
            return false;
        }
    }

    private bool ExecuteKeyboardSequence(string sequence)
    {
        try
        {
            // Force English keyboard layout before typing
            var currentWindow = CurrentWindow;
            if (currentWindow != null)
            {
                var handle = currentWindow.Properties.NativeWindowHandle.ValueOrDefault;
                if (handle != IntPtr.Zero)
                {
                    PostMessage(handle, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, LoadKeyboardLayout(LANG_EN_US, KLF_ACTIVATE));
                    Thread.Sleep(50);
                }
            }

            int i = 0;
            while (i < sequence.Length)
            {
                // Handle Ctrl+ shortcuts (^a, ^c, ^v, etc.)
                if (sequence[i] == '^' && i + 1 < sequence.Length)
                {
                    char key = char.ToUpper(sequence[i + 1]);
                    VirtualKeyShort keyCode = (VirtualKeyShort)Enum.Parse(typeof(VirtualKeyShort), "KEY_" + key);
                    Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, keyCode);
                    Console.WriteLine($"    → Ctrl+{key}");
                    Thread.Sleep(50);
                    i += 2;
                    continue;
                }

                // Handle special keys in braces: {BACKSPACE}, {DELETE}, {ENTER}, etc.
                if (sequence[i] == '{')
                {
                    int endBrace = sequence.IndexOf('}', i);
                    if (endBrace > i)
                    {
                        string specialKey = sequence.Substring(i + 1, endBrace - i - 1).ToUpper();
                        switch (specialKey)
                        {
                            case "BACKSPACE":
                            case "BACK":
                                Keyboard.Type(VirtualKeyShort.BACK);
                                Console.WriteLine("    → Backspace");
                                break;
                            case "DELETE":
                            case "DEL":
                                Keyboard.Type(VirtualKeyShort.DELETE);
                                Console.WriteLine("    → Delete");
                                break;
                            case "ENTER":
                            case "RETURN":
                                Keyboard.Type(VirtualKeyShort.RETURN);
                                Console.WriteLine("    → Enter");
                                break;
                            case "ESC":
                            case "ESCAPE":
                                Keyboard.Type(VirtualKeyShort.ESCAPE);
                                Console.WriteLine("    → Escape");
                                break;
                            case "TAB":
                                Keyboard.Type(VirtualKeyShort.TAB);
                                Console.WriteLine("    → Tab");
                                break;
                            default:
                                Console.WriteLine($"    ⚠️  Unknown special key: {{{specialKey}}}");
                                break;
                        }
                        Thread.Sleep(50);
                        i = endBrace + 1;
                        continue;
                    }
                }

                // Regular text - type character
                Keyboard.Type(sequence[i].ToString());
                i++;
            }

            Thread.Sleep(100);
            Console.WriteLine("  ✅ Keyboard sequence completed");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ ExecuteKeyboardSequence failed: {ex.Message}");
            return false;
        }
    }

    private bool SelectItem(Window window, string elementId, string itemText)
    {
        var element = FindElementById(window, elementId);
        if (element != null && element.Patterns.SelectionItem.IsSupported)
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

    /// <summary>
    /// Resilient UI Search with Cascade Strategy (Localization-Resistant)
    /// Priority: AutomationID → Name (Exact) → Name (Contains) → Smart Menu Fallback
    /// </summary>
    private AutomationElement? FindElementById(Window window, string id)
    {
        // Check cache first
        if (_elementCache.TryGetValue(id, out var cachedElement))
        {
            Console.WriteLine($"  🔍 [Cache Hit] Element '{id}'");
            return cachedElement;
        }

        AutomationElement? element = null;
        string strategy = "";

        // Strategy 1: AutomationID (language-independent, most reliable)
        try
        {
            element = window.FindFirstDescendant(cf => cf.ByAutomationId(id));
            if (element != null)
            {
                strategy = "AutomationID";
                Console.WriteLine($"  ✅ Found element using {strategy}: '{id}'");
                return element;
            }
        }
        catch { }

        // Strategy 2: Name (Exact match, case-sensitive)
        try
        {
            element = window.FindFirstDescendant(cf => cf.ByName(id));
            if (element != null)
            {
                strategy = "Name (Exact)";
                Console.WriteLine($"  ✅ Found element '{element.Name}' using {strategy}");
                return element;
            }
        }
        catch { }

        // Strategy 3: Name (Contains, case-insensitive)
        // Note: FlaUI doesn't support "contains" in condition factory, so we use FindAll + LINQ
        try
        {
            var allElements = window.FindAllDescendants();
            element = allElements.FirstOrDefault(e =>
                !string.IsNullOrEmpty(e.Name) &&
                e.Name.Contains(id, StringComparison.OrdinalIgnoreCase));
            if (element != null)
            {
                strategy = "Name (Contains)";
                Console.WriteLine($"  ✅ Found element '{element.Name}' using {strategy}");
                return element;
            }
        }
        catch { }

        // Strategy 4: Smart i18n (Auto-detect OS language + English fallback)
        // Only search in detected language + English, much faster than trying all translations
        if (I18nMap.TryGetValue(id, out var translations))
        {
            // Target languages: English (default) + detected OS language
            var targetLangs = new[] { "en", _currentLanguage }.Distinct();

            foreach (var lang in targetLangs)
            {
                try
                {
                    // For English, use the original id; for others, get translation
                    string searchTerm = lang == "en" ? id : translations.GetValueOrDefault(lang, id);

                    element = window.FindFirstDescendant(cf => cf.ByName(searchTerm));
                    if (element != null)
                    {
                        strategy = $"Smart i18n ({lang.ToUpper()})";
                        Console.WriteLine($"  ✅ Found '{element.Name}' using {strategy} for '{id}'");
                        return element;
                    }
                }
                catch { }
            }
        }

        // Strategy 5: Smart Menu Fallback (index-based, works across all languages)
        // Common menu keywords and their typical positions
        var menuFallbacks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "File", 0 },     { "Datei", 0 },    { "Fichier", 0 },  { "Файл", 0 },
            { "Edit", 1 },     { "Bearbeiten", 1 }, { "Édition", 1 }, { "Правка", 1 },
            { "View", 2 },     { "Ansicht", 2 },  { "Affichage", 2 }, { "Вид", 2 },
            { "Insert", 3 },   { "Einfügen", 3 }, { "Insertion", 3 }, { "Вставка", 3 },
            { "Format", 4 },   { "Формат", 4 },
            { "Tools", 5 },    { "Extras", 5 },   { "Outils", 5 },   { "Сервис", 5 },
            { "Help", 6 },     { "Hilfe", 6 },    { "Aide", 6 },     { "Справка", 6 }
        };

        if (menuFallbacks.TryGetValue(id, out int menuIndex))
        {
            try
            {
                // Find MenuBar or Menu container
                var menuBar = window.FindFirstDescendant(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.MenuBar));
                if (menuBar != null)
                {
                    var menuItems = menuBar.FindAllChildren();
                    if (menuItems != null && menuIndex < menuItems.Length)
                    {
                        element = menuItems[menuIndex];
                        strategy = $"Smart Menu Fallback (Index {menuIndex})";
                        Console.WriteLine($"  ✅ Found menu item '{element.Name}' using {strategy} for query '{id}'");
                        return element;
                    }
                }
            }
            catch { }
        }

        // All strategies failed
        Console.WriteLine($"  ❌ Element '{id}' not found (tried all strategies)");
        return null;
    }

    private async Task<bool> DownloadFile(string url, string localFileName)
    {
        try
        {
            var downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            var fullPath = Path.Combine(downloadsPath, string.IsNullOrEmpty(localFileName) ? "downloaded_file" : localFileName);

            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            await using var fs = new FileStream(fullPath, FileMode.Create);
            await response.Content.CopyToAsync(fs);
            Console.WriteLine($"  ✅ Downloaded to {fullPath}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Download failed: {ex.Message}");
            return false;
        }
    }

    public string GetClipboardText()
    {
        try { LastClipboardContent = TextCopy.ClipboardService.GetText() ?? ""; return LastClipboardContent; }
        catch { return ""; }
    }

    public bool SetClipboardText(string text)
    {
        try { TextCopy.ClipboardService.SetText(text); return true; }
        catch { return false; }
    }

    public void Dispose()
    {
        // CRITICAL: Remove TOPMOST flag from current/last window on exit
        // This prevents windows from staying on top after agent shutdown
        try
        {
            // Try _lastInteractedWindow first (set by EnsureWindowFocus during click/type)
            // Then fallback to CurrentWindow (set by SwitchWindow/FindWindow)
            var windowToCleanup = _lastInteractedWindow ?? CurrentWindow;

            if (windowToCleanup == null)
            {
                Console.WriteLine("  ⚠️  No window to cleanup");
            }
            else if (windowToCleanup.Properties.IsOffscreen.ValueOrDefault)
            {
                Console.WriteLine("  ⚠️  Last window is offscreen, skipping cleanup");
            }
            else
            {
                var handle = windowToCleanup.Properties.NativeWindowHandle.ValueOrDefault;
                if (handle == IntPtr.Zero)
                {
                    Console.WriteLine("  ⚠️  Window handle is null, cannot cleanup");
                }
                else
                {
                    SetWindowPos(handle, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                    Console.WriteLine("  🧹 Cleaned up TOPMOST flag from last window");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ⚠️  Cleanup error: {ex.Message}");
        }

        _automation?.Dispose();
        _httpClient?.Dispose();
    }
}
