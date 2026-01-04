namespace SupportAgent.Models;

public static class AuthConfig
{
    // 500 characters placeholder (must match patcher.js)
    private const int TOKEN_SLOT_LENGTH = 500;

    // Read token from end of executable (appended by inject_token_slot.ps1)
    private static byte[] ReadTokenBytes()
    {
        try
        {
            // Get path to current executable
            var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (exePath == null || !System.IO.File.Exists(exePath))
            {
                return System.Text.Encoding.Unicode.GetBytes("DEV_TOKEN_UNPATCHED");
            }

            // Read last 2KB of file (token slot is at the very end)
            using var fs = new System.IO.FileStream(exePath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read);
            var tokenByteLength = TOKEN_SLOT_LENGTH * 2; // UTF-16LE = 2 bytes per char
            var offset = fs.Length - tokenByteLength;

            if (offset < 0)
            {
                return System.Text.Encoding.Unicode.GetBytes("DEV_TOKEN_UNPATCHED");
            }

            fs.Seek(offset, System.IO.SeekOrigin.Begin);
            var buffer = new byte[tokenByteLength];
            var bytesRead = fs.Read(buffer, 0, buffer.Length);

            if (bytesRead != tokenByteLength)
            {
                return System.Text.Encoding.Unicode.GetBytes("DEV_TOKEN_UNPATCHED");
            }

            return buffer;
        }
        catch
        {
            return System.Text.Encoding.Unicode.GetBytes("DEV_TOKEN_UNPATCHED");
        }
    }

    public static string TOKEN_SLOT
    {
        get
        {
            byte[] bytes = ReadTokenBytes();
            return System.Text.Encoding.Unicode.GetString(bytes);
        }
    }

    public static string GetToken()
    {
        string slot = TOKEN_SLOT;

        // If the slot still contains the default placeholder, we are in DEV mode
        if (slot.Contains("XELTH_TOKEN_SLOT_"))
        {
            return "DEV_TOKEN_UNPATCHED";
        }

        return slot.Trim();
    }
}
