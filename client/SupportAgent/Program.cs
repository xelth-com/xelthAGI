using SupportAgent.Models;
using SupportAgent.Services;

namespace SupportAgent;

class Program
{
    private const string DEFAULT_SERVER_URL = "https://xelth.com/agi";
    private static readonly List<string> _actionHistory = new();
    private static string _clientId = "";

    // Safety Rails: High-risk actions that require user confirmation
    private static readonly HashSet<string> HighRiskActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "os_delete",
        "os_kill",
        "reg_write",
        "os_run",
        "write_clipboard"
    };

    static async Task<int> Main(string[] args)
    {
        Console.WriteLine("╔════════════════════════════════════════════╗");
        Console.WriteLine("║   Support Agent - C# + FlaUI Client       ║");
        Console.WriteLine("╚════════════════════════════════════════════╝\n");

        // Получаем или генерируем уникальный Client ID
        _clientId = GetOrCreateClientId();
        Console.WriteLine($"Client ID: {_clientId}");
        Console.WriteLine();

        // Параметры
        var defaultServerUrl = LoadServerConfigFromFile() ?? DEFAULT_SERVER_URL;
        var serverUrl = GetArgument(args, "--server", defaultServerUrl);
        var targetApp = GetArgument(args, "--app", "");
        var task = GetArgument(args, "--task", "");
        var unsafeMode = HasFlag(args, "--unsafe") || HasFlag(args, "--auto-approve");

        if (string.IsNullOrEmpty(targetApp))
        {
            Console.WriteLine("Usage: SupportAgent --app <AppName> --task <Task> [--server <URL>] [--unsafe]");
            Console.WriteLine("\nExamples:");
            Console.WriteLine("  SupportAgent --app InBodySuite --task \"Configure printer settings\"");
            Console.WriteLine("  SupportAgent --app notepad --task \"Type hello world\" --server http://my-server:5000");
            Console.WriteLine("  SupportAgent --app notepad --task \"Delete temp files\" --unsafe  (bypasses safety confirmations)");
            return 1;
        }

        if (string.IsNullOrEmpty(task))
        {
            Console.WriteLine("Error: --task parameter is required");
            return 1;
        }

        // Safety Rails notification
        if (unsafeMode)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("⚠️  UNSAFE MODE ENABLED - Destructive actions will NOT require confirmation");
            Console.ResetColor();
            Console.WriteLine();
        }

        // Инициализация сервисов
        using var automationService = new UIAutomationService();
        var serverService = new ServerCommunicationService(serverUrl, _clientId);

        // Проверка сервера
        Console.WriteLine($"Connecting to server: {serverUrl}");
        if (!await serverService.IsServerAvailable())
        {
            Console.WriteLine("❌ Server is not available!");
            Console.WriteLine("Please start the server and try again.");
            return 1;
        }
        Console.WriteLine("✅ Server connected\n");

        // Поиск окна приложения
        Console.WriteLine($"Looking for window: {targetApp}");
        var window = automationService.FindWindow(targetApp);

        // Если не нашли, пытаемся запустить Notepad автоматически
        if (window == null && targetApp.Contains("Notepad", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"⚠️  Window not found. Launching Notepad...");
            try
            {
                System.Diagnostics.Process.Start("notepad.exe");
                await Task.Delay(2000); // Ждем 2 секунды

                window = automationService.FindWindow(targetApp);
                if (window != null)
                {
                    Console.WriteLine($"✅ Notepad launched successfully!");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Failed to launch Notepad: {ex.Message}");
            }
        }

        if (window == null)
        {
            Console.WriteLine($"❌ Window '{targetApp}' not found!");
            Console.WriteLine("Please make sure the application is running.");
            return 1;
        }
        Console.WriteLine($"✅ Found window: {window.Name}\n");

        // Основной цикл автоматизации
        Console.WriteLine($"Task: {task}");
        Console.WriteLine("Starting automation...\n");

        var maxSteps = 50; // Максимум 50 шагов
        var stepCount = 0;
        int nextScreenshotQuality = 0; // 0 = no screenshot, >0 = capture with this quality

        // STATE TRACKING для self-healing
        string previousTitle = "";
        int previousElementCount = 0;
        string previousContentHash = ""; // Hash of text element Values for deep state detection

        while (stepCount < maxSteps)
        {
            stepCount++;
            Console.WriteLine($"[Step {stepCount}]");

            try
            {
                // 1. Получить текущее состояние UI
                Console.WriteLine("  → Scanning UI state...");
                var uiState = automationService.GetWindowState(window);
                Console.WriteLine($"  → Found {uiState.Elements.Count} UI elements");

                // Сохраняем состояние ДО выполнения команды для сравнения
                previousTitle = uiState.WindowTitle;
                previousElementCount = uiState.Elements.Count;

                // Capture content hash BEFORE action (deep state detection)
                var textElements = uiState.Elements.Where(e =>
                    e.Type.Contains("Text", StringComparison.OrdinalIgnoreCase) ||
                    e.Type.Contains("Edit", StringComparison.OrdinalIgnoreCase) ||
                    e.Type.Contains("Document", StringComparison.OrdinalIgnoreCase));
                previousContentHash = string.Join("|", textElements.Select(e => e.Value ?? ""));

                // ECONOMY MODE: Add screenshot only if requested
                if (nextScreenshotQuality > 0)
                {
                    Console.WriteLine($"  📷 Capturing screenshot (Quality: {nextScreenshotQuality}%)...");
                    uiState.Screenshot = automationService.CaptureScreen(nextScreenshotQuality);
                    nextScreenshotQuality = 0; // Reset flag
                }

                // 2. Отправить на сервер и получить команду
                Console.WriteLine("  → Asking server for next action...");
                var response = await serverService.GetNextCommand(uiState, task, _actionHistory);

                if (response == null)
                {
                    Console.WriteLine("  ❌ No response from server");
                    break;
                }

                if (!response.Success)
                {
                    Console.WriteLine($"  ❌ Server error: {response.Error}");
                    break;
                }

                // 3. Проверить завершение задачи
                if (response.TaskCompleted)
                {
                    Console.WriteLine("\n✅ Task completed successfully!");
                    if (!string.IsNullOrEmpty(response.Command?.Message))
                    {
                        Console.WriteLine($"   {response.Command.Message}");
                    }
                    return 0;
                }

                // 4. Handle special commands
                if (response.Command != null && response.Command.Action.ToLower() == "inspect_screen")
                {
                    // Server requested screenshot - parse quality and set flag
                    int.TryParse(response.Command.Text, out int quality);
                    nextScreenshotQuality = quality > 0 ? quality : 50; // Default to 50% if invalid

                    Console.WriteLine($"  👀 Server requested visual inspection (Quality: {nextScreenshotQuality}%)");
                    _actionHistory.Add($"SYSTEM: Requested screenshot at {nextScreenshotQuality}% quality");

                    // Continue to next iteration to capture and send screenshot
                    continue;
                }

                // Handle ask_user - request human assistance
                if (response.Command != null && response.Command.Action.ToLower() == "ask_user")
                {
                    // Alert user with beep
                    Console.Beep();

                    // Display message in yellow
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"\n  🤝 HUMAN ASSISTANCE REQUESTED:");
                    Console.WriteLine($"  {response.Command.Message}");
                    Console.ResetColor();

                    // Prompt for input
                    Console.Write("  >> ");
                    var userInput = Console.ReadLine() ?? "";

                    // Log to history
                    _actionHistory.Add($"USER_SAID: {userInput}");
                    Console.WriteLine($"  ✅ User response recorded\n");

                    // Continue to next iteration with user's response in history
                    continue;
                }

                // 5. Выполнить обычную команду
                if (response.Command != null)
                {
                    var cmd = response.Command;
                    Console.WriteLine($"  → Executing: {cmd.Action} on {cmd.ElementId}");

                    if (!string.IsNullOrEmpty(cmd.Message))
                    {
                        Console.WriteLine($"     💬 {cmd.Message}");
                    }

                    // Safety Rails: Confirm high-risk actions
                    if (HighRiskActions.Contains(cmd.Action) && !unsafeMode)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"\n  ⚠️  WARNING: HIGH-RISK ACTION DETECTED!");
                        Console.WriteLine($"  Action: {cmd.Action}");
                        Console.WriteLine($"  Target: {cmd.Text}");
                        if (!string.IsNullOrEmpty(cmd.ElementId))
                        {
                            Console.WriteLine($"  Parameter: {cmd.ElementId}");
                        }
                        Console.ResetColor();
                        Console.Write("\n  Do you want to proceed? [Y/n]: ");
                        var confirmation = Console.ReadLine()?.Trim().ToLower() ?? "n";

                        if (confirmation != "y" && confirmation != "yes" && confirmation != "")
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine($"  ❌ Action DENIED by user");
                            Console.ResetColor();
                            _actionHistory.Add($"FAILED: User denied {cmd.Action} {cmd.Text} - Safety check");
                            Console.WriteLine();
                            continue; // Skip to next iteration
                        }
                        Console.WriteLine($"  ✅ Confirmed by user");
                        Console.WriteLine();
                    }

                    var success = await automationService.ExecuteCommand(window, cmd);

                    // Проверяем состояние ПОСЛЕ выполнения для self-healing
                    await Task.Delay(300); // Даем время UI обновиться
                    var newState = automationService.GetWindowState(window);

                    // Deep state detection: check content changes
                    var newTextElements = newState.Elements.Where(e =>
                        e.Type.Contains("Text", StringComparison.OrdinalIgnoreCase) ||
                        e.Type.Contains("Edit", StringComparison.OrdinalIgnoreCase) ||
                        e.Type.Contains("Document", StringComparison.OrdinalIgnoreCase));
                    var newContentHash = string.Join("|", newTextElements.Select(e => e.Value ?? ""));

                    bool titleChanged = newState.WindowTitle != previousTitle;
                    bool countChanged = newState.Elements.Count != previousElementCount;
                    bool contentChanged = newContentHash != previousContentHash;

                    string stateChange = "";
                    if (titleChanged || countChanged || contentChanged)
                    {
                        if (contentChanged)
                        {
                            // Content changed - this is a success!
                            stateChange = $" [Content Modified: {previousContentHash.Length}→{newContentHash.Length} chars]";
                            Console.WriteLine($"  📝 Content changed:{stateChange}");
                        }
                        else
                        {
                            stateChange = $" [State: {previousTitle}({previousElementCount}) -> {newState.WindowTitle}({newState.Elements.Count})]";
                            Console.WriteLine($"  📊 UI State changed:{stateChange}");
                        }
                    }
                    else
                    {
                        stateChange = $" [State: NO CHANGE - {previousTitle}({previousElementCount}) - Content: {previousContentHash.Length} chars]";
                        Console.WriteLine($"  ⚠️  UI State unchanged - action may have failed!");
                    }

                    if (success)
                    {
                        // Special handling for read_clipboard - include content in history
                        if (cmd.Action.ToLower() == "read_clipboard")
                        {
                            var clipboardContent = automationService.LastClipboardContent ?? "";
                            // Truncate if too long to avoid bloating history
                            var truncatedContent = clipboardContent.Length > 1000
                                ? clipboardContent.Substring(0, 1000) + "... (truncated)"
                                : clipboardContent;
                            _actionHistory.Add($"CLIPBOARD_CONTENT: \"{truncatedContent}\"");
                            Console.WriteLine($"  ✅ Clipboard content logged to history");
                        }
                        // Special handling for OS commands - include result in history
                        else if (cmd.Action.ToLower().StartsWith("os_"))
                        {
                            var osResult = automationService.LastOsOperationResult ?? "";
                            // Truncate if too long to avoid bloating history
                            var truncatedResult = osResult.Length > 1000
                                ? osResult.Substring(0, 1000) + "... (truncated)"
                                : osResult;
                            _actionHistory.Add($"OS_RESULT: {truncatedResult}");
                            Console.WriteLine($"  ✅ OS operation result logged to history");
                        }
                        else
                        {
                            _actionHistory.Add($"{cmd.Action} {cmd.ElementId} {cmd.Text}{stateChange}");
                            Console.WriteLine("  ✅ Command executed");
                        }
                    }
                    else
                    {
                        Console.WriteLine("  ⚠️  Command failed");
                        _actionHistory.Add($"FAILED: {cmd.Action} {cmd.ElementId}{stateChange}");
                    }
                }

                // Небольшая пауза между шагами
                await Task.Delay(500);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ❌ Error: {ex.Message}");
                break;
            }

            Console.WriteLine();
        }

        if (stepCount >= maxSteps)
        {
            Console.WriteLine("⚠️  Reached maximum steps limit");
        }

        return 0;
    }

    private static string GetArgument(string[] args, string name, string defaultValue)
    {
        var index = Array.IndexOf(args, name);
        if (index >= 0 && index + 1 < args.Length)
        {
            return args[index + 1];
        }
        return defaultValue;
    }

    private static bool HasFlag(string[] args, string flag)
    {
        return Array.IndexOf(args, flag) >= 0;
    }

    private static string? LoadServerConfigFromFile()
    {
        var configPaths = new[]
        {
            "config/server.txt",
            "../config/server.txt",
            "server.txt"
        };

        foreach (var path in configPaths)
        {
            try
            {
                if (File.Exists(path))
                {
                    var lines = File.ReadAllLines(path);
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        // Пропускаем комментарии и пустые строки
                        if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith("#"))
                        {
                            return trimmed;
                        }
                    }
                }
            }
            catch
            {
                // Игнорируем ошибки чтения файла
            }
        }

        return null;
    }

    private static string GetOrCreateClientId()
    {
        try
        {
            // Путь к файлу с ID в AppData
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var clientIdDir = Path.Combine(appDataPath, "XelthAGI");
            var clientIdFile = Path.Combine(clientIdDir, "client-id.txt");

            // Создаем директорию если не существует
            Directory.CreateDirectory(clientIdDir);

            // Читаем существующий ID или создаем новый
            if (File.Exists(clientIdFile))
            {
                var existingId = File.ReadAllText(clientIdFile).Trim();
                if (!string.IsNullOrEmpty(existingId))
                {
                    return existingId;
                }
            }

            // Генерируем новый уникальный ID
            var newId = Guid.NewGuid().ToString("N"); // без дефисов, короче
            File.WriteAllText(clientIdFile, newId);
            return newId;
        }
        catch
        {
            // Если не можем сохранить, генерируем временный ID
            return Guid.NewGuid().ToString("N");
        }
    }
}
