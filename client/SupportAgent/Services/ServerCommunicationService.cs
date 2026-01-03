using Newtonsoft.Json;
using SupportAgent.Models;
using System.Net.Http.Headers; // Required for AuthenticationHeaderValue
using System.Text;

namespace SupportAgent.Services;

public class ServerCommunicationService
{
    private readonly HttpClient _httpClient;
    private readonly string _serverUrl;
    private readonly string _clientId;

    public ServerCommunicationService(string serverUrl, string clientId)
    {
        _serverUrl = serverUrl;
        _clientId = clientId;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        // --- AUTHENTICATION INJECTION ---
        // Read the embedded token from the binary itself
        var token = AuthConfig.GetToken();

        // If patched, send as Bearer token
        if (token != "DEV_TOKEN_UNPATCHED")
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        // Always send ClientID as a header for logging
        _httpClient.DefaultRequestHeaders.Add("X-Client-ID", _clientId);
    }

    /// <summary>
    /// Отправляет состояние UI на сервер и получает команду (с retry logic)
    /// </summary>
    public async Task<ServerResponse?> GetNextCommand(UIState state, string task, List<string> history, int maxRetries = 3)
    {
        Exception? lastException = null;

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

                var response = await _httpClient.PostAsync($"{_serverUrl}/decide", content);

                // AUTH ERROR HANDLING
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                    response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    Console.WriteLine("\n AUTH ERROR: Server rejected the embedded token.");
                    Console.WriteLine("   Please download a fresh copy of the agent from the dashboard.\n");
                    return null;
                }

                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync();

                // Success! Log if this was a retry
                if (attempt > 0)
                {
                    Console.WriteLine($"  ✅ Server request succeeded on attempt {attempt + 1}/{maxRetries}");
                }

                return JsonConvert.DeserializeObject<ServerResponse>(responseJson);
            }
            catch (HttpRequestException ex)
            {
                lastException = ex;
                if (attempt < maxRetries - 1)
                {
                    // Exponential backoff: 1s, 2s, 4s
                    int delayMs = (int)Math.Pow(2, attempt) * 1000;
                    Console.WriteLine($"  ⚠️  Server request failed (attempt {attempt + 1}/{maxRetries}), retrying in {delayMs/1000}s...");
                    await Task.Delay(delayMs);
                    continue;
                }
                Console.WriteLine($"Server communication error after {maxRetries} attempts: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                lastException = ex;
                if (attempt < maxRetries - 1)
                {
                    // Exponential backoff: 1s, 2s, 4s
                    int delayMs = (int)Math.Pow(2, attempt) * 1000;
                    Console.WriteLine($"  ⚠️  Request error (attempt {attempt + 1}/{maxRetries}), retrying in {delayMs/1000}s...");
                    await Task.Delay(delayMs);
                    continue;
                }
                Console.WriteLine($"Error after {maxRetries} attempts: {ex.Message}");
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Проверяет доступность сервера
    /// </summary>
    public async Task<bool> IsServerAvailable()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_serverUrl}/health");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
