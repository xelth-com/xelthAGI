using System.Diagnostics;

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
    /// Runs a process with arguments
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

            return $"✅ Started process: {executablePath} (PID: {process.Id})";
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return $"ERROR: Cannot execute - {ex.Message}";
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
}
