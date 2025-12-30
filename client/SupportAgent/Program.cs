using SupportAgent.Models;
using SupportAgent.Services;

namespace SupportAgent;

class Program
{
    private const string DEFAULT_SERVER_URL = "http://localhost:3232";
    private static readonly List<string> _actionHistory = new();

    static async Task<int> Main(string[] args)
    {
        Console.WriteLine("╔════════════════════════════════════════════╗");
        Console.WriteLine("║   Support Agent - C# + FlaUI Client       ║");
        Console.WriteLine("╚════════════════════════════════════════════╝\n");

        // Параметры
        var serverUrl = GetArgument(args, "--server", DEFAULT_SERVER_URL);
        var targetApp = GetArgument(args, "--app", "");
        var task = GetArgument(args, "--task", "");

        if (string.IsNullOrEmpty(targetApp))
        {
            Console.WriteLine("Usage: SupportAgent --app <AppName> --task <Task> [--server <URL>]");
            Console.WriteLine("\nExamples:");
            Console.WriteLine("  SupportAgent --app InBodySuite --task \"Configure printer settings\"");
            Console.WriteLine("  SupportAgent --app notepad --task \"Type hello world\" --server http://my-server:3232");
            return 1;
        }

        if (string.IsNullOrEmpty(task))
        {
            Console.WriteLine("Error: --task parameter is required");
            return 1;
        }

        // Инициализация сервисов
        using var automationService = new UIAutomationService();
        var serverService = new ServerCommunicationService(serverUrl);

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

                // 4. Выполнить команду
                if (response.Command != null)
                {
                    var cmd = response.Command;
                    Console.WriteLine($"  → Executing: {cmd.Action} on {cmd.ElementId}");

                    if (!string.IsNullOrEmpty(cmd.Message))
                    {
                        Console.WriteLine($"     💬 {cmd.Message}");
                    }

                    var success = await automationService.ExecuteCommand(window, cmd);
                    if (success)
                    {
                        _actionHistory.Add($"{cmd.Action} {cmd.ElementId} {cmd.Text}");
                        Console.WriteLine("  ✅ Command executed");
                    }
                    else
                    {
                        Console.WriteLine("  ⚠️  Command failed");
                        _actionHistory.Add($"FAILED: {cmd.Action} {cmd.ElementId}");
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
}
