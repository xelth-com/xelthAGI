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
      // Declare automation service OUTSIDE try block to ensure Dispose is always called
      UIAutomationService? automationService = null;

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

            // REPLACED: Use unified ShowAgentNotification
            task = ShowAgentNotification(
                $"🤖 Agent ID: {_clientId} - Ready",
                "I am ready to work!\n\nPlease enter your task below or wait 2 minutes to exit.",
                timeoutSeconds: 120
            );

            if (string.IsNullOrEmpty(task) || task == "TIMEOUT" || task == "SHUTDOWN")
            {
                Console.WriteLine("⏱️ Timeout or shutdown: No task provided. Exiting.");
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

        automationService = new UIAutomationService();
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

        // --- PRE-TASK NOTIFICATION ---
        string windowInfo = automationService.CurrentWindow != null
            ? $"🪟 Target: {GetWindowTitleSafe(automationService.CurrentWindow)}"
            : "🪟 Target: Will attach to active window";

        string preTaskResponse = ShowAgentNotification(
            $"🤖 Agent ID: {_clientId} - Ready to Start",
            $"I am about to execute the following task:\n\n📋 TASK:\n{task}\n\n{windowInfo}\n\n⏱️ Auto-start in 10 seconds...",
            timeoutSeconds: 10
        );

        if (preTaskResponse == "SHUTDOWN")
        {
            Console.WriteLine("🛑 Shutdown requested by user.");
            return 0;
        }

        Console.WriteLine("⏱️ Auto-starting after timeout...");
        // --- END PRE-TASK NOTIFICATION ---

        Console.WriteLine($"Task: {task}");
        Console.WriteLine("Starting automation...\n");

        var maxSteps = 20;
        var stepCount = 0;
        int nextScreenshotQuality = 0;
        string previousTitle = "";
        int previousElementCount = 0;
        string previousContentHash = "";
        string lastKnownWindowTitle = ""; // Track last window to prevent focus loss

        // Coarse-to-Fine Vision State
        string? currentOriginalScreenPath = null; // Path to the original high-res screenshot
        double currentScaleFactor = 1.0; // Scale factor used for the low-res overview

        // Cleanup old vision temp files on startup
        VisionHelper.CleanupOldFiles(olderThanMinutes: 30);

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

                // CRITICAL: Let UI fully render before screenshot (fixes shadow screenshot race condition)
                // Even though we delay after command execution, Windows UI rendering is async
                if (stepCount > 1) // Skip on first iteration
                {
                    // Aggressively increased to 500ms to ensure "What You See Is What You Got"
                    await Task.Delay(500);
                }

                Console.WriteLine($"  → Scanning UI state... (T={DateTime.Now:HH:mm:ss.fff})");
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
                    Console.WriteLine($"  📷 Capturing AI Vision screenshot (Coarse-to-Fine mode)...");

                    // STEP 1: Capture full high-resolution screenshot to file
                    currentOriginalScreenPath = VisionHelper.GetTempPath($"screen_original_{stepCount}.png");
                    bool captured = automationService.CaptureScreenToFile(currentOriginalScreenPath);

                    if (captured && File.Exists(currentOriginalScreenPath))
                    {
                        // STEP 2: Create low-res overview for token efficiency
                        string lowResPath = VisionHelper.GetTempPath($"screen_lowres_{stepCount}.jpg");
                        currentScaleFactor = VisionHelper.CreateLowResOverview(
                            currentOriginalScreenPath,
                            lowResPath,
                            targetLongSide: 1280); // 1280px for balance between detail and tokens

                        // STEP 3: Convert low-res to Base64 for transmission
                        uiState.Screenshot = VisionHelper.ImageToBase64(lowResPath);

                        Console.WriteLine($"  ✅ Vision prepared: Overview sent (scale: {currentScaleFactor:F4})");

                        // --- AUTO OCR ON INSPECTION ---
                        // Run OCR on the low-res version for text recognition
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
                    }
                    else
                    {
                        Console.WriteLine("  ❌ Vision capture failed - falling back to standard capture");
                        uiState.Screenshot = automationService.CaptureScreen(nextScreenshotQuality);
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

                    // CONNECTION LOSS - Ask user using unified notification
                    string connectionLostResponse = ShowAgentNotification(
                        $"❌ Agent ID: {_clientId} - Connection Lost",
                        "I've lost connection to the server or encountered an error.\n\nWould you like to shut me down?",
                        timeoutSeconds: 60
                    );

                    if (connectionLostResponse == "SHUTDOWN" || connectionLostResponse == "Yes")
                    {
                        Console.WriteLine("  🛑 User requested shutdown");
                        return 0;
                    }

                    // User chose No (retry) or timeout - retry
                    Console.WriteLine("  🔄 Retrying connection...");
                    await Task.Delay(2000);
                    continue; // Retry the loop
                }

                if (response.TaskCompleted)
                {
                    Console.WriteLine("\n✅ Task completed successfully!");

                    // Show completion notification
                    string completionMsg = response.Command?.Message ?? "Task completed successfully.";
                    string completionResponse = ShowAgentNotification(
                        $"✅ Agent ID: {_clientId} - Work finished!",
                        $"RESULT:\n{completionMsg}\n\n⏱️ Auto-closing in 10 seconds...",
                        timeoutSeconds: 10
                    );

                    if (completionResponse == "SHUTDOWN")
                    {
                        Console.WriteLine("🛑 Shutdown requested by user.");
                        return 0;
                    }

                    Console.WriteLine("⏱️ No continuation - auto-closing after timeout.");
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

                // --- ZOOM IN HANDLER (Coarse-to-Fine Vision) ---
                if (response.Command != null && response.Command.Action.ToLower() == "zoom_in")
                {
                    Console.WriteLine($"  🔍 Server requested ZOOM IN on specific area");

                    // Validate that we have an original screenshot to zoom into
                    if (string.IsNullOrEmpty(currentOriginalScreenPath) || !File.Exists(currentOriginalScreenPath))
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("  ❌ ZOOM FAILED: No original screenshot available!");
                        Console.ResetColor();
                        _actionHistory.Add("SYSTEM: Zoom request FAILED - no original screenshot. You must call 'inspect_screen' first.");
                        continue;
                    }

                    try
                    {
                        // Parse coordinates from command (X, Y, ElementId as W, Text as H)
                        // Expected: X=left, Y=top, ElementId=width (as string), Text=height (as string)
                        int llmX = response.Command.X;
                        int llmY = response.Command.Y;

                        // Parse width and height from ElementId and Text fields
                        int llmW = 0, llmH = 0;
                        if (!int.TryParse(response.Command.ElementId, out llmW)) llmW = 400; // Default width
                        if (!int.TryParse(response.Command.Text, out llmH)) llmH = 300; // Default height

                        Console.WriteLine($"  🔍 Zoom coordinates: X={llmX}, Y={llmY}, W={llmW}, H={llmH}");

                        // Create high-res crop using VisionHelper
                        string cropPath = VisionHelper.GetTempPath($"screen_crop_{stepCount}.jpg");
                        VisionHelper.CreateHighResCrop(
                            currentOriginalScreenPath,
                            cropPath,
                            llmX, llmY, llmW, llmH,
                            currentScaleFactor);

                        // Send the crop in the next iteration by setting uiState.Screenshot
                        // We'll add it to history and force a re-check
                        string cropBase64 = VisionHelper.ImageToBase64(cropPath);

                        if (!string.IsNullOrEmpty(cropBase64))
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine("  ✅ High-res crop created - sending to LLM");
                            Console.ResetColor();

                            // Create a new UI state with the zoomed screenshot
                            var zoomState = new UIState
                            {
                                WindowTitle = uiState.WindowTitle,
                                ProcessName = uiState.ProcessName,
                                Elements = uiState.Elements, // Keep same elements
                                Screenshot = cropBase64, // Replace with zoom
                                DebugScreenshot = "" // No need for debug screenshot on zoom
                            };

                            _actionHistory.Add($"SYSTEM: High-resolution zoom provided for area [{llmX},{llmY}] {llmW}x{llmH}");

                            // Send zoom immediately to LLM
                            Console.WriteLine("  → Asking server to analyze zoomed area...");
                            var zoomResponse = await serverService.GetNextCommand(zoomState, task, _actionHistory);

                            if (zoomResponse != null && !string.IsNullOrEmpty(zoomResponse.Reasoning))
                            {
                                Console.ForegroundColor = ConsoleColor.Cyan;
                                Console.WriteLine($"\n  🧠 THOUGHT (after zoom): {zoomResponse.Reasoning}\n");
                                Console.ResetColor();
                            }

                            // Replace the current response with zoom response for execution
                            response = zoomResponse;
                            // Fall through to normal command execution below
                        }
                        else
                        {
                            Console.WriteLine("  ❌ Failed to create crop");
                            _actionHistory.Add("SYSTEM: Zoom failed - could not create crop");
                            continue; // Skip this iteration if crop failed
                        }
                    }
                    catch (Exception zoomEx)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"  ❌ Zoom error: {zoomEx.Message}");
                        Console.ResetColor();
                        _actionHistory.Add($"SYSTEM: Zoom failed - {zoomEx.Message}");
                        continue; // Skip this iteration if zoom errored
                    }

                    // If we got here successfully, response has been replaced with zoomResponse
                    // Fall through to normal command execution
                }

                // --- GUI INPUT DIALOG FOR HUMAN ASSISTANCE ---
                if (response != null && response.Command != null && response.Command.Action.ToLower() == "ask_user")
                {
                    Console.Beep();
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"\n  🤝 HUMAN ASSISTANCE REQUESTED:");
                    Console.WriteLine($"  {response.Command.Text}");
                    Console.ResetColor();

                    // Open unified notification for user input
                    string userInput = ShowAgentNotification(
                        $"🤖 Agent ID: {_clientId} - Needs Help",
                        response.Command.Text,
                        timeoutSeconds: 300 // 5 minutes for user to respond
                    );

                    // Handle shutdown
                    if (userInput == "SHUTDOWN")
                    {
                        Console.WriteLine("🛑 Shutdown requested during ask_user.");
                        return 0;
                    }

                    // Log response (skip if timeout)
                    if (userInput != "TIMEOUT")
                    {
                        _actionHistory.Add($"USER_SAID: {userInput}");
                    }
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"  >>   ✅ User response recorded");
                    Console.ResetColor();

                    continue;
                }
                // --------------------------------------------------

                if (response != null && response.Command != null)
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

                    Console.WriteLine($"  → Executing: {cmd.Action} on {cmd.ElementId} (T={DateTime.Now:HH:mm:ss.fff})");
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

                        // GUI Confirmation Dialog - Using unified notification
                        string userResponse = ShowAgentNotification(
                            $"⚠️ Agent ID: {_clientId} - Permission Required",
                            $"The agent wants to execute a high-risk command:\n\nACTION: {cmd.Action.ToUpper()}\nTARGET: {cmd.Text}\n\nDo you want to allow this?",
                            timeoutSeconds: 60 // 1 minute to decide
                        );

                        // Handle shutdown
                        if (userResponse == "SHUTDOWN")
                        {
                            Console.WriteLine("🛑 Shutdown requested during safety check.");
                            return 0;
                        }

                        // Parse response: Yes/No/DontKnow
                        DialogResult result = userResponse.Equals("Yes", StringComparison.OrdinalIgnoreCase) ? DialogResult.Yes
                            : userResponse.Equals("No", StringComparison.OrdinalIgnoreCase) ? DialogResult.No
                            : DialogResult.Cancel;

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

                    // Smart delay based on command type (UI needs time to settle)
                    // INCREASED DELAYS: To ensure screenshots capture the RESULT of the action, not the process.
                    int settleDelay = cmd.Action.ToLower() switch
                    {
                        "type" => 2000,     // 2.0s: Typing/Clipboard often triggers validation or UI reflows
                        "key" => 2000,      // 2.0s: Complex key sequences need time
                        "click" => 1000,    // 1.0s: Allow for button animations and dialog appearance
                        "switch_window" => 1000, // 1.0s: Window focus switching and DWM composition
                        "select" => 1000,   // 1.0s: Dropdown animations
                        "os_run" => 2000,   // 2.0s: App launch time
                        _ => 500            // 0.5s: Default safety buffer
                    };
                    await Task.Delay(settleDelay);

                    // Auto-focus after launching new application
                    if (cmd.Action.ToLower() == "os_run" && success && string.IsNullOrEmpty(targetApp))
                    {
                        await Task.Delay(1500); // Wait for app to start
                        Console.WriteLine("  🔄 Auto-switching to newly launched window...");

                        // Try to find and ACTIVATE window by process name (e.g., "calc" -> "Rechner")
                        var appName = cmd.Text.Replace(".exe", "");
                        bool switched = automationService.SwitchWindow(appName);

                        if (!switched)
                        {
                            // Fallback: Try to get active window
                            var foundWindow = automationService.AttachToActiveWindow();
                            if (foundWindow != null)
                            {
                                string newWindowName = GetWindowTitleSafe(foundWindow);
                                Console.ForegroundColor = ConsoleColor.Cyan;
                                Console.WriteLine($"  ✅ Attached to active window: {newWindowName}");
                                Console.ResetColor();
                            }
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

            // TIMEOUT - Ask user using unified notification
            string timeoutResponse = ShowAgentNotification(
                $"⚠️ Agent ID: {_clientId} - Maximum Steps Reached",
                "I've reached the maximum number of steps.\n\nThe task may not be complete.\n\nShut down or continue?",
                timeoutSeconds: 30
            );

            if (timeoutResponse == "SHUTDOWN" || timeoutResponse == "No")
            {
                Console.WriteLine("  🛑 User requested shutdown");
                return 0;
            }

            // User chose Yes (continue) or timeout - keep going
            Console.WriteLine("  ▶️ Continuing automation...");
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
      finally
      {
          // CRITICAL: Always cleanup TOPMOST flags on exit
          // This prevents windows from staying on top after agent shutdown/crash
          if (automationService != null)
          {
              Console.WriteLine("  🧹 Cleaning up resources...");
              automationService.Dispose();
          }
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
    // ShowSafetyDialog REMOVED - replaced with ShowUnifiedDialog everywhere

    // Unified Dialog supporting 3 modes: FullInteractive, InputOnly, MessageOnly
    private static string ShowUnifiedDialog(string title, string headerText, string contentText, DialogMode mode, int timeoutSeconds = 0)
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
            ShowInTaskbar = true, // Ensure visible in taskbar
            ControlBox = mode != DialogMode.InputOnly
        };

        // Aggressive Keep-On-Top Timer
        var keepTopTimer = new System.Windows.Forms.Timer();
        keepTopTimer.Interval = 2000; // Every 2 seconds
        keepTopTimer.Tick += (s, e) => {
            if (!promptForm.IsDisposed && promptForm.Visible) {
                SetWindowPos(promptForm.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                promptForm.Activate();
            } else {
                keepTopTimer.Stop();
            }
        };
        keepTopTimer.Start();

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
            Text = "OK / Continue",
            Left = 220, Top = 240, Width = 140, Height = 40,
            Visible = (mode == DialogMode.MessageOnly),
            BackColor = Color.LightGreen
        };
        btnClose.Click += (s, e) => { promptForm.Close(); };

        promptForm.Controls.Add(headerLabel);
        promptForm.Controls.Add(messageBox);
        promptForm.Controls.Add(groupActions);
        promptForm.Controls.Add(groupInput);
        promptForm.Controls.Add(btnClose);

        // Resize form based on mode
        if (mode == DialogMode.MessageOnly) promptForm.Height = 320;
        if (mode == DialogMode.InputOnly) promptForm.Height = 400;

        // Auto-focus logic and timeout handling
        promptForm.Shown += (sender, e) =>
        {
            // AGGRESSIVE: Force window to absolute foreground
            ForceWindowToForeground(promptForm.Handle);
            Thread.Sleep(50);
            ForceWindowToForeground(promptForm.Handle); // Call twice for reliability
            promptForm.Activate();
            promptForm.BringToFront();

            // Ensure TopMost is really applied
            SetWindowPos(promptForm.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);

            if (mode != DialogMode.MessageOnly) inputBox.Focus();

            // Auto-close timer if timeout specified
            if (timeoutSeconds > 0)
            {
                var autoCloseTimer = new System.Windows.Forms.Timer();
                autoCloseTimer.Interval = timeoutSeconds * 1000;
                autoCloseTimer.Tick += (s, args) =>
                {
                    autoCloseTimer.Stop();
                    resultValue = "TIMEOUT"; // Return special value to differentiate from user cancel
                    promptForm.DialogResult = DialogResult.OK;
                    promptForm.Close();
                };
                autoCloseTimer.Start();

                // Show countdown in corner (visual feedback)
                var countdownLabel = new WinFormsLabel()
                {
                    Left = 480, Top = 10, Width = 80, Height = 30,
                    TextAlign = ContentAlignment.MiddleRight,
                    Font = new Font("Segoe UI", 11, FontStyle.Bold),
                    ForeColor = Color.OrangeRed,
                    Text = $"⏱️ {timeoutSeconds}s"
                };
                promptForm.Controls.Add(countdownLabel);

                var countdownTimer = new System.Windows.Forms.Timer();
                int remainingSeconds = timeoutSeconds;
                countdownTimer.Interval = 1000; // 1 second
                countdownTimer.Tick += (s, args) =>
                {
                    remainingSeconds--;
                    countdownLabel.Text = $"⏱️ {remainingSeconds}s";
                    if (remainingSeconds <= 5) countdownLabel.ForeColor = Color.Red;
                    if (remainingSeconds <= 0) countdownTimer.Stop();
                };
                countdownTimer.Start();
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


    // AGENT NOTIFICATION - Single unified design for greeting/completion
    // Returns: user input text, "SHUTDOWN" if shutdown clicked, "TIMEOUT" if auto-closed, "Yes"/"No"/"Don't Know" for quick replies
    private static string ShowAgentNotification(string headerText, string message, int timeoutSeconds = 10)
    {
        string resultValue = "TIMEOUT";

        Form notifForm = new Form()
        {
            Width = 600,
            Height = 470,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            Text = "XelthAGI Agent",
            StartPosition = FormStartPosition.CenterScreen,
            TopMost = true,
            ShowInTaskbar = true,
            MaximizeBox = false,
            MinimizeBox = false
        };

        // ULTRA-AGGRESSIVE Keep-On-Top Timer - beats everything
        var keepTopTimer = new System.Windows.Forms.Timer();
        keepTopTimer.Interval = 500; // Every 0.5 seconds (very aggressive)
        keepTopTimer.Tick += (s, e) => {
            if (!notifForm.IsDisposed && notifForm.Visible) {
                SetWindowPos(notifForm.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                notifForm.BringToFront();
                notifForm.Activate();
                notifForm.Focus();
            } else {
                keepTopTimer.Stop();
            }
        };
        keepTopTimer.Start();

        // Header (shorter width to not overlap countdown)
        WinFormsLabel headerLabel = new WinFormsLabel()
        {
            Left = 20, Top = 20, Width = 440, Height = 30,
            Text = headerText,
            Font = new Font("Segoe UI", 12, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };

        // Countdown Label (top right, opposite header) - MUST be added AFTER header to be on top
        WinFormsLabel countdownLabel = new WinFormsLabel()
        {
            Left = 470, Top = 20, Width = 90, Height = 30,
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("Segoe UI", 12, FontStyle.Bold),
            ForeColor = Color.OrangeRed,
            Text = $"⏱️ {timeoutSeconds}s"
        };

        // Message Box
        WinFormsTextBox messageBox = new WinFormsTextBox()
        {
            Left = 20, Top = 60, Width = 540, Height = 140,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.White,
            Text = message,
            Font = new Font("Segoe UI", 10)
        };

        // 4 Quick Reply Buttons (all in one line)
        WinFormsButton btnYes = new WinFormsButton()
        {
            Text = "✅ Yes / Allow",
            Left = 20, Top = 220, Width = 130, Height = 40,
            BackColor = Color.LightGreen
        };
        btnYes.Click += (s, e) => { resultValue = "Yes"; notifForm.Close(); };

        WinFormsButton btnNo = new WinFormsButton()
        {
            Text = "❌ No / Deny",
            Left = 160, Top = 220, Width = 130, Height = 40,
            BackColor = Color.LightCoral
        };
        btnNo.Click += (s, e) => { resultValue = "No"; notifForm.Close(); };

        WinFormsButton btnDontKnow = new WinFormsButton()
        {
            Text = "❓ Don't Know",
            Left = 300, Top = 220, Width = 130, Height = 40,
            BackColor = Color.LightYellow
        };
        btnDontKnow.Click += (s, e) => { resultValue = "Don't Know"; notifForm.Close(); };

        WinFormsButton btnShutdown = new WinFormsButton()
        {
            Text = "🛑 Shutdown",
            Left = 440, Top = 220, Width = 120, Height = 40,
            BackColor = Color.Salmon,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        btnShutdown.Click += (s, e) => { resultValue = "SHUTDOWN"; notifForm.Close(); };

        // Text Input Section (3 lines) - moved up since buttons are now in one line
        GroupBox groupInput = new GroupBox()
        {
            Left = 20, Top = 270, Width = 540, Height = 140,
            Text = "Or type specific data/response:",
            Font = new Font("Segoe UI", 9)
        };

        WinFormsTextBox inputBox = new WinFormsTextBox()
        {
            Left = 15, Top = 30, Width = 400, Height = 80,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Segoe UI", 10)
        };

        WinFormsButton btnSend = new WinFormsButton()
        {
            Text = "Send Text",
            Left = 420, Top = 30, Width = 100, Height = 80,
            BackColor = Color.LightBlue
        };
        btnSend.Click += (s, e) => {
            if (!string.IsNullOrWhiteSpace(inputBox.Text)) {
                resultValue = inputBox.Text;
                notifForm.Close();
            }
        };

        groupInput.Controls.Add(inputBox);
        groupInput.Controls.Add(btnSend);

        notifForm.Controls.Add(headerLabel);
        notifForm.Controls.Add(messageBox);
        notifForm.Controls.Add(btnYes);
        notifForm.Controls.Add(btnNo);
        notifForm.Controls.Add(btnDontKnow);
        notifForm.Controls.Add(btnShutdown);
        notifForm.Controls.Add(groupInput);
        notifForm.Controls.Add(countdownLabel); // Add last so it's on top

        // Auto-close timer
        var autoCloseTimer = new System.Windows.Forms.Timer();
        autoCloseTimer.Interval = timeoutSeconds * 1000;
        autoCloseTimer.Tick += (s, args) =>
        {
            autoCloseTimer.Stop();
            notifForm.Close();
        };
        autoCloseTimer.Start();

        // Countdown timer
        var countdownTimer = new System.Windows.Forms.Timer();
        int remainingSeconds = timeoutSeconds;
        countdownTimer.Interval = 1000;
        countdownTimer.Tick += (s, args) =>
        {
            remainingSeconds--;
            countdownLabel.Text = $"⏱️ {remainingSeconds}s";
            if (remainingSeconds <= 5) countdownLabel.ForeColor = Color.Red;
            if (remainingSeconds <= 0) countdownTimer.Stop();
        };
        countdownTimer.Start();

        notifForm.Shown += (sender, e) =>
        {
            ForceWindowToForeground(notifForm.Handle);
            Thread.Sleep(50);
            ForceWindowToForeground(notifForm.Handle);
            SetWindowPos(notifForm.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
            inputBox.Focus();
        };

        System.Media.SystemSounds.Exclamation.Play();
        notifForm.ShowDialog();

        return resultValue;
    }

    // CONNECTION LOST DIALOG - REMOVED, now uses ShowAgentNotification (unified design)

    // TIMEOUT DIALOG - REMOVED, now uses ShowAgentNotification (unified design)
}
