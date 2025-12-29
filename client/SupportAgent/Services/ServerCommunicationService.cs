using Newtonsoft.Json;
using SupportAgent.Models;
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
    }

    /// <summary>
    /// Отправляет состояние UI на сервер и получает команду
    /// </summary>
    public async Task<ServerResponse?> GetNextCommand(UIState state, string task, List<string> history)
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
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<ServerResponse>(responseJson);
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"Server communication error: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return null;
        }
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
