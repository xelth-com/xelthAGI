using Newtonsoft.Json;
using SupportAgent.Models;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace SupportAgent.Services;

public class ServerCommunicationService
{
    private readonly HttpClient _httpClient;
    private readonly string _serverUrl;
    private readonly string _clientId;

    public ServerCommunicationService(string serverUrl, string clientId)
    {
        // Enable modern TLS protocols
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;

        _serverUrl = serverUrl.TrimEnd('/'); // Ensure no trailing slash
        _clientId = clientId;

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        // --- AUTHENTICATION INJECTION ---
        var token = AuthConfig.GetToken();
        if (token != "DEV_TOKEN_UNPATCHED")
        {
            // Ensure token is ASCII-safe before adding to header
            var asciiToken = Encoding.ASCII.GetString(Encoding.ASCII.GetBytes(token));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", asciiToken);
        }

        // Ensure Client ID is ASCII-safe
        var asciiClientId = Encoding.ASCII.GetString(Encoding.ASCII.GetBytes(_clientId));
        _httpClient.DefaultRequestHeaders.Add("X-Client-ID", asciiClientId);
    }

    public async Task<ServerResponse?> GetNextCommand(UIState state, string task, List<string> history, int maxRetries = 3)
    {
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                var request = new ServerRequest
                {
                    ClientId = _clientId,
                    State = state,
                    Task = task,
                    History = history
                };

                var json = JsonConvert.SerializeObject(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // UPPERCASE ENDPOINT: /DECIDE
                var response = await _httpClient.PostAsync($"{_serverUrl}/DECIDE", content);

                if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                {
                    Console.WriteLine("\n  [X] AUTH ERROR: Server rejected the token.");
                    return null;
                }

                response.EnsureSuccessStatusCode();
                var responseJson = await response.Content.ReadAsStringAsync();

                return JsonConvert.DeserializeObject<ServerResponse>(responseJson);
            }
            catch (Exception ex)
            {
                if (attempt < maxRetries - 1)
                {
                    await Task.Delay(1000 * (attempt + 1));
                    continue;
                }
                Console.WriteLine($"  [X] Error getting command: {ex.Message}");
                return null;
            }
        }
        return null;
    }

    public async Task<bool> IsServerAvailable()
    {
        try
        {
            // UPPERCASE ENDPOINT: /HEALTH
            // Logging added to debug connection issues
            Console.WriteLine($"  [?] Connecting to: {_serverUrl}/HEALTH");

            var response = await _httpClient.GetAsync($"{_serverUrl}/HEALTH");

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"  [X] HTTP Error: {response.StatusCode}");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [X] Connection Exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"     Inner: {ex.InnerException.Message}");
            }
            return false;
        }
    }
}
