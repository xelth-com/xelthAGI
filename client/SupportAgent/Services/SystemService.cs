using System.Diagnostics;
using Microsoft.Win32;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace SupportAgent.Services;

/// <summary>
/// Handles direct OS operations (file system, process control)
/// Provides fast alternatives to UI automation for system tasks
/// </summary>
public class SystemService
{
    /// <summary>
    /// Lists files and directories in a given path
    /// </summary>
    public string ListDirectory(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return $"ERROR: Directory not found: {path}";
            }

            var items = new List<string>();

            // List directories first
            var dirs = Directory.GetDirectories(path);
            foreach (var dir in dirs)
            {
                var dirInfo = new DirectoryInfo(dir);
                items.Add($"[DIR]  {dirInfo.Name}");
            }

            // Then list files
            var files = Directory.GetFiles(path);
            foreach (var file in files)
            {
                var fileInfo = new FileInfo(file);
                var sizeKB = fileInfo.Length / 1024;
                items.Add($"[FILE] {fileInfo.Name} ({sizeKB} KB)");
            }

            if (items.Count == 0)
            {
                return $"Directory is empty: {path}";
            }

            return string.Join("\n", items);
        }
        catch (UnauthorizedAccessException)
        {
            return $"ERROR: Access denied to: {path}";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    /// <summary>
    /// Deletes a file or directory (recursive)
    /// </summary>
    public string DeletePath(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                return $"✅ Deleted file: {path}";
            }
            else if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
                return $"✅ Deleted directory: {path}";
            }
            else
            {
                return $"ERROR: Path not found: {path}";
            }
        }
        catch (UnauthorizedAccessException)
        {
            return $"ERROR: Access denied: {path}";
        }
        catch (IOException ex)
        {
            return $"ERROR: IO error - {ex.Message}";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    /// <summary>
    /// Reads text file content (truncated to maxChars)
    /// </summary>
    public string ReadFile(string path, int maxChars = 2000)
    {
        try
        {
            if (!File.Exists(path))
            {
                return $"ERROR: File not found: {path}";
            }

            var content = File.ReadAllText(path);

            if (content.Length > maxChars)
            {
                return content.Substring(0, maxChars) + $"\n... (truncated, total {content.Length} chars)";
            }

            return content;
        }
        catch (UnauthorizedAccessException)
        {
            return $"ERROR: Access denied: {path}";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    /// <summary>
    /// Runs a process with arguments and optional timeout
    /// </summary>
    public string RunProcess(string executablePath, string arguments = "")
    {
        try
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = false
            };

            var process = Process.Start(processStartInfo);

            if (process == null)
            {
                return $"ERROR: Failed to start process: {executablePath}";
            }

            // Give process 30 seconds to start responding (optional safety check)
            // This prevents hanging on processes that fail to initialize
            if (!process.WaitForInputIdle(30000))
            {
                // Process didn't become responsive, but that's ok for background processes
                // Just log it and continue
            }

            return $"✅ Started process: {executablePath} (PID: {process.Id})";
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return $"ERROR: Cannot execute - {ex.Message}";
        }
        catch (InvalidOperationException)
        {
            // WaitForInputIdle failed (expected for console apps)
            // Return success anyway since process started
            return $"✅ Started process (console/background): {executablePath}";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    /// <summary>
    /// Kills all processes matching the given name
    /// </summary>
    public string KillProcess(string processName)
    {
        try
        {
            // Remove .exe extension if provided
            processName = processName.Replace(".exe", "");

            var processes = Process.GetProcessesByName(processName);

            if (processes.Length == 0)
            {
                return $"No processes found with name: {processName}";
            }

            int killed = 0;
            var errors = new List<string>();

            foreach (var process in processes)
            {
                try
                {
                    process.Kill();
                    process.WaitForExit(2000); // Wait max 2 seconds
                    killed++;
                }
                catch (Exception ex)
                {
                    errors.Add($"PID {process.Id}: {ex.Message}");
                }
            }

            var result = $"✅ Killed {killed} process(es) named '{processName}'";
            if (errors.Count > 0)
            {
                result += $"\nErrors: {string.Join(", ", errors)}";
            }

            return result;
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    /// <summary>
    /// Creates a new directory
    /// </summary>
    public string CreateDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                return $"Directory already exists: {path}";
            }

            Directory.CreateDirectory(path);
            return $"✅ Created directory: {path}";
        }
        catch (UnauthorizedAccessException)
        {
            return $"ERROR: Access denied: {path}";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    /// <summary>
    /// Writes text to a file (overwrites if exists)
    /// </summary>
    public string WriteFile(string path, string content)
    {
        try
        {
            File.WriteAllText(path, content);
            return $"✅ Wrote {content.Length} characters to: {path}";
        }
        catch (UnauthorizedAccessException)
        {
            return $"ERROR: Access denied: {path}";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    /// <summary>
    /// Checks if a file or directory exists
    /// </summary>
    public string CheckExists(string path)
    {
        if (File.Exists(path))
        {
            var fileInfo = new FileInfo(path);
            return $"EXISTS: File - {fileInfo.Name} ({fileInfo.Length / 1024} KB)";
        }
        else if (Directory.Exists(path))
        {
            var dirInfo = new DirectoryInfo(path);
            var fileCount = dirInfo.GetFiles().Length;
            var dirCount = dirInfo.GetDirectories().Length;
            return $"EXISTS: Directory - {fileCount} files, {dirCount} subdirectories";
        }
        else
        {
            return $"NOT FOUND: {path}";
        }
    }

    // ==================== IT SUPPORT TOOLKIT ====================

    /// <summary>
    /// Gets environment variable value
    /// </summary>
    public string GetEnvVar(string varName)
    {
        try
        {
            var value = Environment.GetEnvironmentVariable(varName);
            if (value == null)
            {
                return $"NOT FOUND: Environment variable '{varName}' does not exist";
            }
            return $"{varName} = {value}";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    /// <summary>
    /// Reads Windows Registry value
    /// Supports: HKLM, HKCU, HKCR, HKU, HKCC
    /// </summary>
    public string RegistryRead(string root, string keyPath, string valueName)
    {
        try
        {
            RegistryKey? baseKey = root.ToUpper() switch
            {
                "HKLM" => Registry.LocalMachine,
                "HKCU" => Registry.CurrentUser,
                "HKCR" => Registry.ClassesRoot,
                "HKU" => Registry.Users,
                "HKCC" => Registry.CurrentConfig,
                _ => null
            };

            if (baseKey == null)
            {
                return $"ERROR: Invalid registry root '{root}'. Use: HKLM, HKCU, HKCR, HKU, or HKCC";
            }

            using var key = baseKey.OpenSubKey(keyPath);
            if (key == null)
            {
                return $"ERROR: Registry key not found: {root}\\{keyPath}";
            }

            var value = key.GetValue(valueName);
            if (value == null)
            {
                return $"ERROR: Value '{valueName}' not found in {root}\\{keyPath}";
            }

            return $"✅ {root}\\{keyPath}\\{valueName} = {value}";
        }
        catch (System.Security.SecurityException)
        {
            return $"ERROR: Access denied to registry key (may require Administrator)";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    /// <summary>
    /// Writes Windows Registry value
    /// WARNING: Requires Administrator for HKLM writes
    /// </summary>
    public string RegistryWrite(string root, string keyPath, string valueName, string value)
    {
        try
        {
            RegistryKey? baseKey = root.ToUpper() switch
            {
                "HKLM" => Registry.LocalMachine,
                "HKCU" => Registry.CurrentUser,
                "HKCR" => Registry.ClassesRoot,
                "HKU" => Registry.Users,
                "HKCC" => Registry.CurrentConfig,
                _ => null
            };

            if (baseKey == null)
            {
                return $"ERROR: Invalid registry root '{root}'. Use: HKLM, HKCU, HKCR, HKU, or HKCC";
            }

            using var key = baseKey.OpenSubKey(keyPath, writable: true);
            if (key == null)
            {
                return $"ERROR: Registry key not found or not writable: {root}\\{keyPath}";
            }

            key.SetValue(valueName, value);
            return $"✅ Set {root}\\{keyPath}\\{valueName} = {value}";
        }
        catch (System.Security.SecurityException)
        {
            return $"ERROR: Access denied (Administrator required for HKLM writes)";
        }
        catch (UnauthorizedAccessException)
        {
            return $"ERROR: Unauthorized access (key may be read-only or require elevation)";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    /// <summary>
    /// Pings a host to check network connectivity with retry logic
    /// </summary>
    public string NetworkPing(string host, int timeout = 2000, int retries = 3)
    {
        Exception? lastException = null;

        for (int attempt = 0; attempt < retries; attempt++)
        {
            try
            {
                using var ping = new Ping();
                var reply = ping.Send(host, timeout);

                if (reply.Status == IPStatus.Success)
                {
                    var retryInfo = attempt > 0 ? $" (succeeded on attempt {attempt + 1}/{retries})" : "";
                    return $"✅ Ping successful: {host} ({reply.Address}) - {reply.RoundtripTime}ms{retryInfo}";
                }
                else if (attempt < retries - 1)
                {
                    // Not successful, but not the last attempt - retry
                    Thread.Sleep(1000);
                    continue;
                }
                else
                {
                    // Last attempt failed
                    return $"❌ Ping failed after {retries} attempts: {host} - Status: {reply.Status}";
                }
            }
            catch (PingException ex)
            {
                lastException = ex;
                if (attempt < retries - 1)
                {
                    Thread.Sleep(1000);
                    continue;
                }
            }
            catch (Exception ex)
            {
                lastException = ex;
                if (attempt < retries - 1)
                {
                    Thread.Sleep(1000);
                    continue;
                }
            }
        }

        return $"ERROR: Ping failed after {retries} attempts - {lastException?.Message}";
    }

    /// <summary>
    /// Checks if a TCP port is open on a host with retry logic
    /// </summary>
    public string NetworkCheckPort(string host, int port, int timeout = 2000, int retries = 3)
    {
        Exception? lastException = null;

        for (int attempt = 0; attempt < retries; attempt++)
        {
            try
            {
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(host, port);

                if (connectTask.Wait(timeout))
                {
                    if (client.Connected)
                    {
                        var retryInfo = attempt > 0 ? $" (succeeded on attempt {attempt + 1}/{retries})" : "";
                        return $"✅ Port {port} is OPEN on {host}{retryInfo}";
                    }
                    else if (attempt < retries - 1)
                    {
                        // Not connected, but not the last attempt - retry
                        Thread.Sleep(1000);
                        continue;
                    }
                    else
                    {
                        return $"❌ Port {port} is CLOSED on {host} (after {retries} attempts)";
                    }
                }
                else if (attempt < retries - 1)
                {
                    // Timeout, but not the last attempt - retry
                    Thread.Sleep(1000);
                    continue;
                }
                else
                {
                    return $"❌ Port {port} on {host} - Connection timeout after {retries} attempts ({timeout}ms each)";
                }
            }
            catch (SocketException ex)
            {
                lastException = ex;
                if (attempt < retries - 1)
                {
                    Thread.Sleep(1000);
                    continue;
                }
            }
            catch (Exception ex)
            {
                lastException = ex;
                if (attempt < retries - 1)
                {
                    Thread.Sleep(1000);
                    continue;
                }
            }
        }

        return $"ERROR: Port check failed after {retries} attempts - {lastException?.Message}";
    }
}
