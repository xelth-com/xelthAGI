using SupportAgent.Models;
using SupportAgent.Services;
using System.Windows.Forms;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace SupportAgent;

class Program
{
    // Force window to foreground (Windows API)
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindow(IntPtr hWnd, bool bInvert);

    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_SHOWWINDOW = 0x0040;

    private const string DEFAULT_SERVER_URL = "https://xelth.com/AGI";
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

    [STAThread] // Required for Windows Forms
    static async Task<int> Main(string[] args)
    {
        Console.WriteLine("╔════════════════════════════════════════════╗");
        Console.WriteLine("║   Support Agent - C# + FlaUI Client       ║");
        Console.WriteLine("╚════════════════════════════════════════════╝\n");

        _clientId = GetOrCreateClientId();
        Console.WriteLine($"Client ID: {_clientId}");
        Console.WriteLine();

        var defaultServerUrl = LoadServerConfigFromFile() ?? DEFAULT_SERVER_URL;
        var serverUrl = GetArgument(args, "--server", defaultServerUrl);
        var targetApp = GetArgument(args, "--app", "");
        var task = GetArgument(args, "--task", "");
        var unsafeMode = HasFlag(args, "--unsafe") || HasFlag(args, "--auto-approve");

        if (string.IsNullOrEmpty(targetApp))
        {
            Console.WriteLine("Usage: SupportAgent --app <AppName> --task <Task> [--server <URL>] [--unsafe]");
            return 1;
        }

        if (string.IsNullOrEmpty(task))
        {
            Console.WriteLine("Error: --task parameter is required");
            return 1;
        }

        if (unsafeMode)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("⚠️  UNSAFE MODE ENABLED - Destructive actions will NOT require confirmation");
            Console.ResetColor();
        }

        using var automationService = new UIAutomationService();
        var serverService = new ServerCommunicationService(serverUrl, _clientId);

        Console.WriteLine($"Connecting to server: {serverUrl}");
        if (!await serverService.IsServerAvailable())
        {
            Console.WriteLine("❌ Server is not available!");
            return 1;
        }
        Console.WriteLine("✅ Server connected\n");

        Console.WriteLine($"Looking for window: {targetApp}");
        automationService.FindWindow(targetApp);

        if (automationService.CurrentWindow == null && targetApp.Contains("Notepad", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"⚠️  Window not found. Launching Notepad...");
            try
            {
                System.Diagnostics.Process.Start("notepad.exe");
                await Task.Delay(2000);
                automationService.FindWindow(targetApp);
            }
            catch { }
        }

        if (automationService.CurrentWindow == null)
        {
            Console.WriteLine($"❌ Window '{targetApp}' not found!");
            return 1;
        }
        Console.WriteLine($"✅ Found window: {automationService.CurrentWindow.Name}\n");

        Console.WriteLine($"Task: {task}");
        Console.WriteLine("Starting automation...\n");

        var maxSteps = 20;
        var stepCount = 0;
        int nextScreenshotQuality = 0;
        string previousTitle = "";
        int previousElementCount = 0;
        string previousContentHash = "";

        while (stepCount < maxSteps)
        {
            stepCount++;
            Console.WriteLine($"[Step {stepCount}]");

            try
            {
                if (automationService.CurrentWindow == null)
                {
                    Console.WriteLine("  ❌ Current window is no longer available");
                    break;
                }

                Console.WriteLine("  → Scanning UI state...");
                var uiState = automationService.GetWindowState(automationService.CurrentWindow);
                Console.WriteLine($"  → Found {uiState.Elements.Count} UI elements");

                previousTitle = uiState.WindowTitle;
                previousElementCount = uiState.Elements.Count;

                var textElements = uiState.Elements.Where(e =>
                    e.Type.Contains("Text", StringComparison.OrdinalIgnoreCase) ||
                    e.Type.Contains("Edit", StringComparison.OrdinalIgnoreCase) ||
                    e.Type.Contains("Document", StringComparison.OrdinalIgnoreCase));
                previousContentHash = string.Join("|", textElements.Select(e => e.Value ?? ""));

                // Always capture low-quality Shadow Screenshot for debugging/logging
                // This allows us to see what happened even if the agent didn't ask for vision
                uiState.DebugScreenshot = automationService.CaptureFullDesktop(30);

                if (nextScreenshotQuality > 0)
                {
                    Console.WriteLine($"  📷 Capturing AI Vision screenshot (Quality: {nextScreenshotQuality}%)...");
                    uiState.Screenshot = automationService.CaptureScreen(nextScreenshotQuality);
                    nextScreenshotQuality = 0;
                }

                Console.WriteLine("  → Asking server for next action...");
                var response = await serverService.GetNextCommand(uiState, task, _actionHistory);

                if (response != null && !string.IsNullOrEmpty(response.Reasoning))
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"\n  🧠 THOUGHT: {response.Reasoning}\n");
                    Console.ResetColor();
                }

                if (response == null || !response.Success)
                {
                    Console.WriteLine("  ❌ Server error or no response");
                    break;
                }

                if (response.TaskCompleted)
                {
                    Console.WriteLine("\n✅ Task completed successfully!");
                    return 0;
                }

                if (response.Command != null && response.Command.Action.ToLower() == "inspect_screen")
                {
                    int.TryParse(response.Command.Text, out int quality);
                    nextScreenshotQuality = quality > 0 ? quality : 50;
                    Console.WriteLine($"  👀 Server requested visual inspection (Quality: {nextScreenshotQuality}%)");
                    _actionHistory.Add($"SYSTEM: Requested screenshot at {nextScreenshotQuality}% quality");
                    continue;
                }

                // --- GUI INPUT DIALOG FOR HUMAN ASSISTANCE ---
                if (response.Command != null && response.Command.Action.ToLower() == "ask_user")
                {
                    Console.Beep();
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"\n  🤝 HUMAN ASSISTANCE REQUESTED:");
                    Console.WriteLine($"  {response.Command.Message}");
                    Console.ResetColor();

                    // Open GUI Dialog (Blocking)
                    string userInput = ShowInputDialog(
                        "AI Agent Needs Help",
                        response.Command.Message
                    );

                    // Log response
                    _actionHistory.Add($"USER_SAID: {userInput}");
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"  >>   ✅ User response recorded");
                    Console.ResetColor();

                    continue;
                }
                // --------------------------------------------------

                if (response.Command != null)
                {
                    var cmd = response.Command;
                    Console.WriteLine($"  → Executing: {cmd.Action} on {cmd.ElementId}");
                    if (!string.IsNullOrEmpty(cmd.Message))
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.WriteLine($"     💬 {cmd.Message}");
                        Console.ResetColor();
                    }

                    // Safety Rails
                    if (HighRiskActions.Contains(cmd.Action) && !unsafeMode)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"\n  ⚠️  WARNING: HIGH-RISK ACTION DETECTED!");
                        Console.WriteLine($"  Action: {cmd.Action}");
                        Console.WriteLine($"  Target: {cmd.Text}");
                        Console.ResetColor();

                        Console.Write("\n  Do you want to proceed? [Y/n]: ");
                        var confirmation = Console.ReadLine()?.Trim().ToLower() ?? "n";

                        if (confirmation != "y" && confirmation != "yes" && confirmation != "")
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine($"  ❌ Action DENIED by user");
                            Console.ResetColor();
                            _actionHistory.Add($"FAILED: User denied {cmd.Action} {cmd.Text} - Safety check");
                            continue;
                        }
                    }

                    // Measure execution time
                    var sw = Stopwatch.StartNew();
                    var success = await automationService.ExecuteCommand(automationService.CurrentWindow, cmd);
                    sw.Stop();

                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"  ⏱️  Execution time: {sw.ElapsedMilliseconds}ms");
                    Console.ResetColor();

                    await Task.Delay(300);

                    if (automationService.CurrentWindow == null)
                    {
                        Console.WriteLine("  ⚠️  Target window lost focus! Attempting to restore...");
                        automationService.FindWindow(targetApp);
                        if (automationService.CurrentWindow != null)
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine("  ✅ Focus restored to target window");
                            Console.ResetColor();
                        }
                        else
                        {
                            break;
                        }
                    }

                    var newState = automationService.GetWindowState(automationService.CurrentWindow);

                    bool titleChanged = newState.WindowTitle != previousTitle;
                    bool elementCountChanged = newState.Elements.Count != previousElementCount;

                    var newTextElements = newState.Elements.Where(e =>
                        e.Type.Contains("Text", StringComparison.OrdinalIgnoreCase) ||
                        e.Type.Contains("Edit", StringComparison.OrdinalIgnoreCase) ||
                        e.Type.Contains("Document", StringComparison.OrdinalIgnoreCase));
                    var newContentHash = string.Join("|", newTextElements.Select(e => e.Value ?? ""));
                    bool contentChanged = newContentHash != previousContentHash;

                    string stateDescription = "";

                    if (success)
                    {
                        var action = cmd.Action.ToLower();
                        if (action.StartsWith("os_") || action.StartsWith("net_") || action.StartsWith("reg_"))
                        {
                            var osResult = automationService.LastOsOperationResult ?? "";
                            var truncated = osResult.Length > 1000 ? osResult.Substring(0, 1000) + "..." : osResult;
                            _actionHistory.Add($"OS_RESULT: {truncated}");
                            Console.ForegroundColor = ConsoleColor.Cyan;
                            Console.WriteLine("  ✅ OS operation result logged to history");
                            Console.ResetColor();
                        }
                        else if (cmd.Action.ToLower() == "read_clipboard")
                        {
                            _actionHistory.Add($"CLIPBOARD_CONTENT: \"{automationService.LastClipboardContent}\"");
                            Console.ForegroundColor = ConsoleColor.Cyan;
                            Console.WriteLine("  ✅ Clipboard content logged to history");
                            Console.ResetColor();
                        }
                        else
                        {
                            if (titleChanged)
                            {
                                stateDescription = $"[Title Changed: {previousTitle}→{newState.WindowTitle}]";
                            }
                            else if (contentChanged)
                            {
                                var oldLen = previousContentHash.Length;
                                var newLen = newContentHash.Length;
                                stateDescription = $"[Content Modified: {oldLen}→{newLen} chars]";
                            }
                            else if (elementCountChanged)
                            {
                                stateDescription = $"[Elements Changed: {previousElementCount}→{newState.Elements.Count}]";
                            }
                            else
                            {
                                stateDescription = $"[State: NO CHANGE - {newState.WindowTitle}({newState.Elements.Count}) - Content: {newContentHash.Length} chars]";
                            }

                            _actionHistory.Add($"{cmd.Action} {cmd.ElementId} {cmd.Text} {stateDescription}");

                            if (titleChanged || contentChanged || elementCountChanged)
                            {
                                Console.ForegroundColor = ConsoleColor.Green;
                                Console.WriteLine($"  📝 Content changed: {stateDescription}");
                                Console.ResetColor();
                            }
                            else
                            {
                                Console.ForegroundColor = ConsoleColor.DarkYellow;
                                Console.WriteLine("  ⚠️  UI State unchanged - action may have failed!");
                                Console.ResetColor();
                            }
                        }
                        Console.WriteLine("  ✅ Command executed");
                    }
                    else
                    {
                        Console.WriteLine("  ⚠️  Command failed");
                        _actionHistory.Add($"FAILED: {cmd.Action} {cmd.ElementId} {stateDescription}");
                    }
                }

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
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("⚠️  Reached maximum steps limit");
            Console.ResetColor();
        }

        return 0;
    }

    // Aggressively force window to foreground (bypasses Windows restrictions)
    private static void ForceWindowToForeground(IntPtr hWnd)
    {
        // Get current foreground window and its thread
        IntPtr foregroundWindow = GetForegroundWindow();
        uint foregroundThreadId = GetWindowThreadProcessId(foregroundWindow, IntPtr.Zero);
        uint currentThreadId = GetCurrentThreadId();

        // Attach our thread to the foreground thread
        if (foregroundThreadId != currentThreadId)
        {
            AttachThreadInput(currentThreadId, foregroundThreadId, true);
            SetForegroundWindow(hWnd);
            AttachThreadInput(currentThreadId, foregroundThreadId, false);
        }
        else
        {
            SetForegroundWindow(hWnd);
        }

        // Make window topmost
        SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);

        // Flash window to draw attention
        FlashWindow(hWnd, true);
        Task.Delay(100).Wait();
        FlashWindow(hWnd, false);
    }

    // Helper method to show a GUI Input Dialog with Force Foreground
    private static string ShowInputDialog(string title, string prompt)
    {
        Form promptForm = new Form()
        {
            Width = 500,
            Height = 200,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            Text = title,
            StartPosition = FormStartPosition.CenterScreen,
            TopMost = true, // Force on top of other windows
            WindowState = FormWindowState.Normal, // Ensure not minimized
            MinimizeBox = false, // Disable minimize button
            MaximizeBox = false,
            ShowInTaskbar = true
        };

        Label textLabel = new Label()
        {
            Left = 20,
            Top = 20,
            Width = 440,
            Text = prompt,
            AutoSize = false,
            Height = 60
        };

        TextBox inputBox = new TextBox()
        {
            Left = 20,
            Top = 90,
            Width = 440
        };

        Button confirmation = new Button()
        {
            Text = "OK",
            Left = 360,
            Width = 100,
            Top = 120,
            DialogResult = DialogResult.OK
        };

        confirmation.Click += (sender, e) => { promptForm.Close(); };
        promptForm.Controls.Add(textLabel);
        promptForm.Controls.Add(inputBox);
        promptForm.Controls.Add(confirmation);
        promptForm.AcceptButton = confirmation;

        // Aggressively force focus when shown
        promptForm.Shown += (sender, e) =>
        {
            ForceWindowToForeground(promptForm.Handle);
            promptForm.Activate();
            promptForm.BringToFront();
            inputBox.Focus();
        };

        // Ensure focus on input box
        promptForm.ActiveControl = inputBox;

        // Show as dialog blocks execution until closed
        return promptForm.ShowDialog() == DialogResult.OK ? inputBox.Text : "";
    }

    private static string GetArgument(string[] args, string name, string defaultValue)
    {
        var index = Array.IndexOf(args, name);
        if (index >= 0 && index + 1 < args.Length) return args[index + 1];
        return defaultValue;
    }

    private static bool HasFlag(string[] args, string flag) => Array.IndexOf(args, flag) >= 0;

    private static string? LoadServerConfigFromFile()
    {
        try
        {
            if (File.Exists("server.txt"))
                return File.ReadAllLines("server.txt").FirstOrDefault(l => !l.StartsWith("#"));
        }
        catch { }
        return null;
    }

    private static string GetOrCreateClientId()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XelthAGI", "client-id.txt");
        try
        {
            if (File.Exists(path))
                return File.ReadAllText(path).Trim();

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var id = Guid.NewGuid().ToString("N");
            File.WriteAllText(path, id);
            return id;
        }
        catch
        {
            return "temp-" + Guid.NewGuid().ToString("N");
        }
    }
}
