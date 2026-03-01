using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using PrinciPal.Common.Errors.Server;
using PrinciPal.Common.Results;
using PrinciPal.Domain.ValueObjects;
using PrinciPal.VsExtension.Abstractions;
using Task = System.Threading.Tasks.Task;

namespace PrinciPal.VsExtension.Adapters
{
    /// <summary>
    /// Publishes debug state to the MCP server over HTTP.
    /// </summary>
    public sealed class HttpDebugStatePublisher : IDebugStatePublisher
    {
        private readonly HttpClient _httpClient;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly string _serverUrl;
        private readonly string _sessionId;
        private readonly string _sessionQueryParams;

        public HttpDebugStatePublisher(int port, string sessionId, string sessionName, string solutionPath)
            : this(port, sessionId, sessionName, solutionPath, handler: null)
        {
        }

        internal HttpDebugStatePublisher(int port, string sessionId, string sessionName, string solutionPath, HttpMessageHandler? handler)
        {
            _sessionId = sessionId;
            _sessionQueryParams = $"name={Uri.EscapeDataString(sessionName)}&path={Uri.EscapeDataString(solutionPath)}";
            _serverUrl = $"http://localhost:{port}";
            _httpClient = handler != null
                ? new HttpClient(handler) { BaseAddress = new Uri(_serverUrl) }
                : new HttpClient { BaseAddress = new Uri(_serverUrl), Timeout = TimeSpan.FromSeconds(5) };
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };
        }

        public Task<Result> RegisterSessionAsync()
        {
            return SendAsync("Register", c =>
                c.PostAsync($"/api/sessions/{Uri.EscapeDataString(_sessionId)}?{_sessionQueryParams}", null));
        }

        public Task<Result> PushDebugStateAsync(DebugState state)
        {
            var json = JsonSerializer.Serialize(state, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            return SendAsync("Push", c =>
                c.PostAsync($"/api/sessions/{Uri.EscapeDataString(_sessionId)}/debug-state?{_sessionQueryParams}", content));
        }

        public Task<Result> ClearDebugStateAsync()
        {
            return SendAsync("Clear", c =>
                c.DeleteAsync($"/api/sessions/{Uri.EscapeDataString(_sessionId)}/debug-state"));
        }

        public Task<Result> DeregisterSessionAsync()
        {
            return SendAsync("Deregister", c =>
                c.DeleteAsync($"/api/sessions/{Uri.EscapeDataString(_sessionId)}"));
        }

        private async Task<Result> SendAsync(string action, Func<HttpClient, Task<HttpResponseMessage>> send)
        {
            try
            {
                var response = await Task.Run(() => send(_httpClient));
                Debug.WriteLine($"PrinciPal: {action} completed. Status: {response.StatusCode}");
                return Result.Success();
            }
            catch (HttpRequestException ex)
            {
                return Result.Failure(new ServerUnreachableError(_serverUrl, ex.Message));
            }
            catch (TaskCanceledException)
            {
                return Result.Failure(new RequestTimedOutError(action));
            }
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}
