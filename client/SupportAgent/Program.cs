using SupportAgent.Models;
using SupportAgent.Services;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Text;
using FlaUI.Core;
using WinFormsLabel = System.Windows.Forms.Label;
using WinFormsButton = System.Windows.Forms.Button;
using WinFormsTextBox = System.Windows.Forms.TextBox;

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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    private static string GetWindowTitleSafe(FlaUI.Core.AutomationElements.Window window)
    {
        try
        {
            // Try FlaUI's Name property first
            return window.Name;
        }
        catch (Exception)
        {
            // Fallback: Get window title via Windows API
            try
            {
                var handle = window.Properties.NativeWindowHandle.ValueOrDefault;
                if (handle != IntPtr.Zero)
                {
                    var sb = new StringBuilder(256);
                    GetWindowText(handle, sb, sb.Capacity);
                    var title = sb.ToString();
                    if (!string.IsNullOrEmpty(title))
                        return title;
                }
            }
            catch { }
            return "[Unknown Window]";
        }
    }

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
      try {
        // FORCE UTF-8 ENCODING for correct Unicode handling (Cyrillic, Umlauts, etc.)
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

        Console.WriteLine("╔════════════════════════════════════════════╗");
        Console.WriteLine("║   Support Agent - C# + FlaUI Client       ║");
        Console.WriteLine("╚════════════════════════════════════════════╝\n");

        string currentToken = AuthConfig.GetToken();

        // --- DEV MODE CHECK ---
        if (currentToken == "DEV_TOKEN_MISSING")
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("❌ DEV MODE ERROR: 'dev_token.txt' not found!");
            Console.WriteLine("   Please generate a local token:");
            Console.WriteLine("   node ../../server/scripts/mint_zero.js");
            Console.ResetColor();
            return 1;
        }

        if (currentToken.Length > 50 && !currentToken.Contains("XELTH_TOKEN_SLOT"))
        {
            // Подсказка, что мы работаем под ID 0
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("🔧 FAST DEV MODE ACTIVE (ID: 00000000)");
            Console.WriteLine("   Using local token from dev_token.txt");
            Console.ResetColor();
            Console.WriteLine();
        }
        // -----------------------

        _clientId = GetOrCreateClientId();
        Console.WriteLine($"Client ID: {_clientId}");
        Console.WriteLine();

        var defaultServerUrl = LoadServerConfigFromFile() ?? DEFAULT_SERVER_URL;
        var serverUrl = GetArgument(args, "--server", defaultServerUrl);
        var targetApp = GetArgument(args, "--app", "");
        var task = GetArgument(args, "--task", "");
        var unsafeMode = HasFlag(args, "--unsafe") || HasFlag(args, "--auto-approve");

        // targetApp is now optional. If empty, we attach to active window.

        // --- INTERACTIVE MODE ---
        if (string.IsNullOrEmpty(task))
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n[Interactive Mode]");
            Console.ResetColor();

            Console.WriteLine($"Server: {serverUrl}");

            // REPLACED: Console input with GUI Dialog
            task = ShowUnifiedDialog(
                "XelthAGI Agent - Start",
                "Hello! I am ready to help.",
                "Please enter the task you want me to perform:",
                DialogMode.InputOnly
            );

            if (string.IsNullOrEmpty(task))
            {
                Console.WriteLine("No task provided. Exiting.");
                return 0;
            }

            // Log the task to console for history
            Console.WriteLine($"Task entered: {task}");

            // Ask for Auto-Approve in interactive mode (Simple fallback or assume safe defaults)
            if (!unsafeMode)
            {
                // Optional: We can assume safe mode or strictly ask.
                // For simplified UX, let's default to Safe mode unless flagged.
            }
        }
        // ------------------------

        if (unsafeMode)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("⚠️  UNSAFE MODE ENABLED - Destructive actions will NOT require confirmation");
            Console.ResetColor();
        }

        using var automationService = new UIAutomationService();
        var serverService = new ServerCommunicationService(serverUrl, _clientId);

        // Initialize OCR Service for text recognition
        var ocrService = new OcrService();
        Console.WriteLine($"OCR Support: {(ocrService.IsSupported ? "✅ Available" : "❌ Not Available")}");

        Console.WriteLine($"Connecting to server: {serverUrl}");
        if (!await serverService.IsServerAvailable())
        {
            Console.WriteLine("❌ Server is not available!");
            return 1;
        }
        Console.WriteLine("✅ Server connected\n");

        if (!string.IsNullOrEmpty(targetApp))
        {
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
        }
        else
        {
            Console.WriteLine("ℹ️  No app specified. Attaching to active window...");
            automationService.AttachToActiveWindow();
            if (automationService.CurrentWindow == null)
            {
                Console.WriteLine("⚠️  Could not attach to active window. Will retry in loop.");
            }
        }

        if (automationService.CurrentWindow != null)
        {
            string windowName = GetWindowTitleSafe(automationService.CurrentWindow);
            Console.WriteLine($"✅ Target window: {windowName}\n");
        }

        Console.WriteLine($"Task: {task}");
        Console.WriteLine("Starting automation...\n");

        var maxSteps = 20;
        var stepCount = 0;
        int nextScreenshotQuality = 0;
        string previousTitle = "";
        int previousElementCount = 0;
        string previousContentHash = "";
        string lastKnownWindowTitle = ""; // Track last window to prevent focus loss

        while (stepCount < maxSteps)
        {
            stepCount++;
            Console.WriteLine($"[Step {stepCount}]");

            try
            {
                // If we don't have a window yet (e.g. no --app specified), try to find active one
                if (automationService.CurrentWindow == null)
                {
                    automationService.AttachToActiveWindow();
                    if (automationService.CurrentWindow == null)
                    {
                        Console.WriteLine("  ⏳ Waiting for active window...");
                        await Task.Delay(1000);
                        continue;
                    }
                }

                Console.WriteLine("  → Scanning UI state...");
                var uiState = automationService.GetWindowState(automationService.CurrentWindow);
                Console.WriteLine($"  → Found {uiState.Elements.Count} UI elements");

                // Remember current window title
                previousTitle = uiState.WindowTitle;
                lastKnownWindowTitle = uiState.WindowTitle;
                previousElementCount = uiState.Elements.Count;

                var textElements = uiState.Elements.Where(e =>
                    e.Type.Contains("Text", StringComparison.OrdinalIgnoreCase) ||
                    e.Type.Contains("Edit", StringComparison.OrdinalIgnoreCase) ||
                    e.Type.Contains("Document", StringComparison.OrdinalIgnoreCase));
                previousContentHash = string.Join("|", textElements.Select(e => e.Value ?? ""));

                // Always capture low-quality Shadow Screenshot for debugging/logging
                // Quality reduced to 20% to save bandwidth
                uiState.DebugScreenshot = automationService.CaptureFullDesktop(20);

                if (nextScreenshotQuality > 0)
                {
                    Console.WriteLine($"  📷 Capturing AI Vision screenshot (Quality: {nextScreenshotQuality}%)...");
                    uiState.Screenshot = automationService.CaptureScreen(nextScreenshotQuality);

                    // --- NEW: AUTO OCR ON INSPECTION ---
                    // Whenever AI requests vision, we also give it text recognition ability
                    if (!string.IsNullOrEmpty(uiState.Screenshot) && ocrService.IsSupported)
                    {
                        Console.WriteLine("  🧠 Running OCR Analysis...");
                        try
                        {
                            byte[] imageBytes = Convert.FromBase64String(uiState.Screenshot);
                            using (var ms = new MemoryStream(imageBytes))
                            using (var bmp = new Bitmap(ms))
                            {
                                var ocrResult = await ocrService.GetTextFromScreen(bmp);
                                _actionHistory.Add($"SYSTEM: {ocrResult}");
                                Console.WriteLine("  ✅ OCR Text added to context");
                            }
                        }
                        catch (Exception ocrEx)
                        {
                            Console.WriteLine($"  ⚠️ OCR Failed: {ocrEx.Message}");
                        }
                    }

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

                    // CONNECTION LOSS - Ask user if they want to shut down
                    if (ShowConnectionLostDialog())
                    {
                        Console.WriteLine("  🛑 User requested shutdown");
                        return 0;
                    }
                    break;
                }

                if (response.TaskCompleted)
                {
                    Console.WriteLine("\n✅ Task completed successfully!");

                    // REPLACED: Use unified dialog instead of disappearing notification
                    // Dialog will auto-close after 10 seconds
                    string completionMsg = response.Command?.Message ?? "Task completed successfully.";
                    ShowUnifiedDialog(
                        "Task Completed",
                        "The agent has finished work.",
                        $"RESULT:\n{completionMsg}",
                        DialogMode.MessageOnly
                    );

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
                    Console.WriteLine($"  {response.Command.Text}");
                    Console.ResetColor();

                    // Open GUI Dialog (Blocking) -> UPDATED CALL
                    string userInput = ShowUnifiedDialog(
                        "AI Agent Needs Help",
                        "The AI Agent needs your input:",
                        response.Command.Text,
                        DialogMode.FullInteractive
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

                    // --- REMOTE SHUTDOWN HANDLER ---
                    if (cmd.Action.ToLower() == "shutdown")
                    {
                        Console.Beep();
                        Console.ForegroundColor = ConsoleColor.Magenta;
                        Console.WriteLine($"\n🛑 REMOTE SHUTDOWN RECEIVED");
                        Console.WriteLine($"   Reason: {cmd.Message}");
                        Console.ResetColor();
                        return 0; // Graceful exit
                    }
                    // -------------------------------

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

                        // GUI Confirmation Dialog
                        DialogResult result = ShowSafetyDialog(
                            "⚠️ Security Alert: High-Risk Action",
                            $"The agent wants to execute a high-risk command:\n\nACTION: {cmd.Action.ToUpper()}\nTARGET: {cmd.Text}\n\nDo you want to allow this?"
                        );

                        if (result == DialogResult.Yes)
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"  ✅ Action APPROVED by user");
                            Console.ResetColor();
                            // Proceed to execution
                        }
                        else if (result == DialogResult.Cancel) // "Don't Know" button
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine($"  ❓ Action SUSPENDED (User needs explanation)");
                            Console.ResetColor();
                            _actionHistory.Add($"SUSPENDED: User clicked 'Don't Know' for {cmd.Action}. REQUIRED: Use 'ask_user' to explain WHY this is safe/necessary, or switch to a safer method.");
                            continue;
                        }
                        else // "No" button
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"  ❌ Action DENIED by user");
                            Console.ResetColor();
                            _actionHistory.Add($"FAILED: User explicitly denied {cmd.Action} {cmd.Text}. STOP trying this action. Try a different approach.");
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

                    // Auto-focus after launching new application
                    if (cmd.Action.ToLower() == "os_run" && success && string.IsNullOrEmpty(targetApp))
                    {
                        await Task.Delay(1500); // Wait for app to start
                        Console.WriteLine("  🔄 Auto-switching to newly launched window...");

                        // Try to find window by process name (e.g., "calc" -> "Rechner")
                        var appName = cmd.Text.Replace(".exe", "");
                        var foundWindow = automationService.FindWindow(appName);

                        if (foundWindow == null)
                        {
                            // Fallback: Try to get active window
                            foundWindow = automationService.AttachToActiveWindow();
                        }

                        if (foundWindow != null)
                        {
                            string newWindowName = GetWindowTitleSafe(foundWindow);
                            Console.ForegroundColor = ConsoleColor.Cyan;
                            Console.WriteLine($"  ✅ Switched to: {newWindowName}");
                            Console.ResetColor();
                        }
                    }

                    if (automationService.CurrentWindow == null)
                    {
                        Console.WriteLine("  ⚠️  Target window lost focus! Attempting to restore...");
                        if (!string.IsNullOrEmpty(targetApp))
                        {
                            automationService.FindWindow(targetApp);
                        }
                        else if (!string.IsNullOrEmpty(lastKnownWindowTitle))
                        {
                            // Try to find last known window instead of random active window
                            Console.WriteLine($"  🔍 Looking for last window: {lastKnownWindowTitle}");
                            automationService.FindWindow(lastKnownWindowTitle);

                            if (automationService.CurrentWindow == null)
                            {
                                // Last resort: attach to active window
                                automationService.AttachToActiveWindow();
                            }
                        }
                        else
                        {
                            automationService.AttachToActiveWindow();
                        }

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

            // TIMEOUT - Ask user if they want to shut down
            if (ShowTimeoutDialog())
            {
                Console.WriteLine("  🛑 User requested shutdown");
                return 0;
            }
        }

        return 0;
      }
      catch (Exception ex)
      {
          Console.ForegroundColor = ConsoleColor.Red;
          Console.WriteLine($"\n❌ FATAL CRASH: {ex}");
          Console.ResetColor();
          return -1;
      }
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

    // ENUM for Dialog Modes
    private enum DialogMode
    {
        FullInteractive, // Buttons + Text (for ask_user)
        InputOnly,       // Only text input (for startup)
        MessageOnly      // Only message and OK button (for completion)
    }

    // Helper method to show a GUI Input Dialog with Force Foreground
    // Helper method to show a 3-Button Safety Dialog
    private static DialogResult ShowSafetyDialog(string title, string prompt)
    {
        Form promptForm = new Form()
        {
            Width = 500,
            Height = 220,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            Text = title,
            StartPosition = FormStartPosition.CenterScreen,
            TopMost = true,
            WindowState = FormWindowState.Normal,
            MinimizeBox = false,
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
            Height = 100
        };

        Button yesButton = new Button()
        {
            Text = "Yes",
            Left = 160,
            Width = 100,
            Top = 140,
            DialogResult = DialogResult.Yes
        };

        Button noButton = new Button()
        {
            Text = "No",
            Left = 270,
            Width = 100,
            Top = 140,
            DialogResult = DialogResult.No
        };

        Button dontKnowButton = new Button()
        {
            Text = "Don't Know",
            Left = 380,
            Width = 100,
            Top = 140,
            DialogResult = DialogResult.Cancel
        };

        promptForm.Controls.Add(textLabel);
        promptForm.Controls.Add(yesButton);
        promptForm.Controls.Add(noButton);
        promptForm.Controls.Add(dontKnowButton);

        // Aggressively force focus when shown
        promptForm.Shown += (sender, e) =>
        {
            ForceWindowToForeground(promptForm.Handle);
            promptForm.Activate();
            promptForm.BringToFront();
        };

        // Show and wait
        ForceWindowToForeground(promptForm.Handle);
        return promptForm.ShowDialog();
    }

    // Unified Dialog supporting 3 modes: FullInteractive, InputOnly, MessageOnly
    private static string ShowUnifiedDialog(string title, string headerText, string contentText, DialogMode mode)
    {
        string resultValue = "";

        Form promptForm = new Form()
        {
            Width = 600,
            Height = 500,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            Text = title,
            StartPosition = FormStartPosition.CenterScreen,
            TopMost = true,
            ControlBox = mode != DialogMode.InputOnly // Hide close button only if input strictly required
        };

        // Header
        Label headerLabel = new Label()
        {
            Left = 20, Top = 15, Width = 540, Height = 25,
            Text = headerText,
            Font = new Font("Segoe UI", 11, FontStyle.Bold)
        };

        // Scrollable Text Area (Prompt or Content)
        TextBox messageBox = new TextBox()
        {
            Left = 20,
            Top = 50,
            Width = 540,
            Height = 160,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.White,
            Text = contentText,
            Font = new Font("Segoe UI", 10)
        };

        // --- Quick Actions Section (Only for FullInteractive) ---
        GroupBox groupActions = new GroupBox()
        {
            Left = 20, Top = 220, Width = 540, Height = 80,
            Text = "Quick Reply",
            Visible = (mode == DialogMode.FullInteractive)
        };

        Button btnYes = new Button() { Text = "✅ Yes / Allow", Left = 20, Top = 25, Width = 150, Height = 40, BackColor = Color.LightGreen };
        Button btnNo = new Button() { Text = "❌ No / Deny", Left = 190, Top = 25, Width = 150, Height = 40, BackColor = Color.LightCoral };
        Button btnDunno = new Button() { Text = "❓ Don't Know", Left = 360, Top = 25, Width = 150, Height = 40 };

        btnYes.Click += (s, e) => { resultValue = "Yes"; promptForm.DialogResult = DialogResult.OK; promptForm.Close(); };
        btnNo.Click += (s, e) => { resultValue = "No"; promptForm.DialogResult = DialogResult.OK; promptForm.Close(); };
        btnDunno.Click += (s, e) => { resultValue = "Don't Know"; promptForm.DialogResult = DialogResult.OK; promptForm.Close(); };

        groupActions.Controls.Add(btnYes);
        groupActions.Controls.Add(btnNo);
        groupActions.Controls.Add(btnDunno);

        // --- Custom Input / Main Input Section ---
        int inputTop = (mode == DialogMode.FullInteractive) ? 310 : 230;

        GroupBox groupInput = new GroupBox()
        {
            Left = 20, Top = inputTop, Width = 540, Height = 100,
            Text = (mode == DialogMode.InputOnly) ? "Enter your command/task here:" : "Or type specific data/response:",
            Visible = (mode != DialogMode.MessageOnly)
        };

        TextBox inputBox = new TextBox()
        {
            Left = 20, Top = 35, Width = 380, Height = 40,
            Font = new Font("Segoe UI", 11)
        };

        Button btnSend = new Button()
        {
            Text = (mode == DialogMode.InputOnly) ? "Start Task" : "Send Text",
            Left = 410, Top = 33, Width = 110, Height = 34,
            BackColor = Color.LightBlue
        };

        btnSend.Click += (s, e) => {
            resultValue = inputBox.Text;
            if (string.IsNullOrWhiteSpace(resultValue)) return;
            promptForm.DialogResult = DialogResult.OK;
            promptForm.Close();
        };

        inputBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) btnSend.PerformClick(); };

        groupInput.Controls.Add(inputBox);
        groupInput.Controls.Add(btnSend);

        // --- Close/Exit Button for MessageOnly mode ---
        Button btnClose = new Button()
        {
            Text = "Close / Exit",
            Left = 200, Top = 400, Width = 140, Height = 40,
            Visible = (mode == DialogMode.MessageOnly)
        };
        btnClose.Click += (s, e) => { promptForm.Close(); };

        promptForm.Controls.Add(headerLabel);
        promptForm.Controls.Add(messageBox);
        promptForm.Controls.Add(groupActions);
        promptForm.Controls.Add(groupInput);
        promptForm.Controls.Add(btnClose);

        // Resize form based on mode
        if (mode == DialogMode.MessageOnly) promptForm.Height = 350;
        if (mode == DialogMode.InputOnly) promptForm.Height = 400;

        // Auto-focus logic
        promptForm.Shown += (sender, e) =>
        {
            ForceWindowToForeground(promptForm.Handle);
            promptForm.Activate();
            if (mode != DialogMode.MessageOnly) inputBox.Focus();

            // Auto-close after 10 seconds for MessageOnly mode
            if (mode == DialogMode.MessageOnly)
            {
                var autoCloseTimer = new System.Windows.Forms.Timer();
                autoCloseTimer.Interval = 10000; // 10 seconds
                autoCloseTimer.Tick += (s, args) =>
                {
                    autoCloseTimer.Stop();
                    promptForm.Close();
                };
                autoCloseTimer.Start();
            }
        };

        // Sound alert
        if (mode != DialogMode.InputOnly) // Don't beep on startup
            System.Media.SystemSounds.Exclamation.Play();

        promptForm.ShowDialog();
        return resultValue;
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


    // CONNECTION LOST DIALOG (Blocking)
    private static bool ShowConnectionLostDialog()
    {
        Form dialogForm = new Form()
        {
            Width = 500,
            Height = 200,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            Text = "Connection Lost",
            StartPosition = FormStartPosition.CenterScreen,
            TopMost = true,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = true,
            ControlBox = false
        };

        Label messageLabel = new Label()
        {
            Left = 20,
            Top = 20,
            Width = 440,
            Height = 80,
            Text = "I've lost connection to the server or encountered an error.\n\nWould you like to shut me down?",
            Font = new Font("Segoe UI", 10)
        };

        Button shutdownButton = new Button()
        {
            Text = "Shutdown",
            Left = 150,
            Width = 100,
            Top = 120,
            DialogResult = DialogResult.Yes,
            BackColor = Color.LightCoral
        };

        Button retryButton = new Button()
        {
            Text = "Retry",
            Left = 260,
            Width = 100,
            Top = 120,
            DialogResult = DialogResult.No
        };

        dialogForm.Controls.Add(messageLabel);
        dialogForm.Controls.Add(shutdownButton);
        dialogForm.Controls.Add(retryButton);

        ForceWindowToForeground(dialogForm.Handle);
        DialogResult result = dialogForm.ShowDialog();

        return result == DialogResult.Yes; // true = shutdown
    }

    // TIMEOUT DIALOG (Blocking)
    private static bool ShowTimeoutDialog()
    {
        Form dialogForm = new Form()
        {
            Width = 500,
            Height = 200,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            Text = "Maximum Steps Reached",
            StartPosition = FormStartPosition.CenterScreen,
            TopMost = true,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = true,
            ControlBox = false
        };

        Label messageLabel = new Label()
        {
            Left = 20,
            Top = 20,
            Width = 440,
            Height = 80,
            Text = "I've reached the maximum number of steps.\n\nThe task may not be complete. Shut down?",
            Font = new Font("Segoe UI", 10)
        };

        Button shutdownButton = new Button()
        {
            Text = "Shutdown",
            Left = 150,
            Width = 100,
            Top = 120,
            DialogResult = DialogResult.Yes,
            BackColor = Color.LightCoral
        };

        Button continueButton = new Button()
        {
            Text = "Continue Anyway",
            Left = 260,
            Width = 120,
            Top = 120,
            DialogResult = DialogResult.No
        };

        dialogForm.Controls.Add(messageLabel);
        dialogForm.Controls.Add(shutdownButton);
        dialogForm.Controls.Add(continueButton);

        ForceWindowToForeground(dialogForm.Handle);
        DialogResult result = dialogForm.ShowDialog();

        return result == DialogResult.Yes; // true = shutdown
    }
}
