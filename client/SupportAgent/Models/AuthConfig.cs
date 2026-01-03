namespace SupportAgent.Models;

public static class AuthConfig
{
    private const string TOKEN_SLOT_RESOURCE = "TokenSlot";

    // Read raw bytes - this should survive bundling
    private static byte[] ReadResourceBytes()
    {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames().FirstOrDefault(n => n.Contains("TokenSlot"));

        if (resourceName == null) return System.Text.Encoding.Unicode.GetBytes("DEV_TOKEN_UNPATCHED");

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null) return System.Text.Encoding.Unicode.GetBytes("DEV_TOKEN_UNPATCHED");

        using var ms = new System.IO.MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    public static string TOKEN_SLOT
    {
        get
        {
            byte[] bytes = ReadResourceBytes();
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
