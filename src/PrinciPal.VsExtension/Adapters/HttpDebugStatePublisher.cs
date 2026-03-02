using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
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
        private static readonly Random _random = new Random();
        private static readonly object _jitterLock = new object();

        private readonly HttpClient _httpClient;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly string _serverUrl;
        private readonly string _sessionId;
        private readonly string _sessionQueryParams;
        private readonly int _retryBaseDelayMs;
        private readonly int _heartbeatIntervalMs;
        private readonly Timer _heartbeatTimer;
        private readonly object _heartbeatLock = new object();
        private volatile bool _heartbeatStopped;

        public HttpDebugStatePublisher(int port, string sessionId, string sessionName, string solutionPath)
            : this(port, sessionId, sessionName, solutionPath, handler: null)
        {
        }

        internal HttpDebugStatePublisher(int port, string sessionId, string sessionName, string solutionPath, HttpMessageHandler? handler, int retryBaseDelayMs = 500, int heartbeatIntervalMs = 30_000)
        {
            _sessionId = sessionId;
            _sessionQueryParams = $"name={Uri.EscapeDataString(sessionName)}&path={Uri.EscapeDataString(solutionPath)}";
            _serverUrl = $"http://localhost:{port}";
            _retryBaseDelayMs = retryBaseDelayMs;
            _httpClient = handler != null
                ? new HttpClient(handler) { BaseAddress = new Uri(_serverUrl) }
                : new HttpClient { BaseAddress = new Uri(_serverUrl), Timeout = TimeSpan.FromSeconds(5) };
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };
            _heartbeatIntervalMs = heartbeatIntervalMs;
            _heartbeatTimer = new Timer(_ =>
            {
                lock (_heartbeatLock)
                {
                    if (_heartbeatStopped) return;
                    _ = RegisterSessionAsync();
                }
            });
        }

        public void StartHeartbeat()
        {
            if (_heartbeatIntervalMs <= 0)
                return;
            _heartbeatStopped = false;
            _heartbeatTimer.Change(_heartbeatIntervalMs, _heartbeatIntervalMs);
        }

        public void StopHeartbeat()
        {
            lock (_heartbeatLock)
            {
                _heartbeatStopped = true;
                _heartbeatTimer.Change(Timeout.Infinite, Timeout.Infinite);
            }
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
                c.DeleteAsync($"/api/sessions/{Uri.EscapeDataString(_sessionId)}"), maxAttempts: 1);
        }

        private async Task<Result> SendAsync(string action, Func<HttpClient, Task<HttpResponseMessage>> send, int maxAttempts = 3)
        {
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    var response = await Task.Run(() => send(_httpClient)).ConfigureAwait(false);
                    Debug.WriteLine($"PrinciPal: {action} completed. Status: {response.StatusCode}");
                    return Result.Success();
                }
                catch (HttpRequestException ex)
                {
                    if (attempt + 1 < maxAttempts)
                    {
                        Debug.WriteLine($"PrinciPal: {action} failed (attempt {attempt + 1}/{maxAttempts}), retrying: {ex.Message}");
                        await Task.Delay(ComputeDelay(attempt)).ConfigureAwait(false);
                        continue;
                    }
                    return Result.Failure(new ServerUnreachableError(_serverUrl, ex.Message));
                }
                catch (TaskCanceledException)
                {
                    if (attempt + 1 < maxAttempts)
                    {
                        Debug.WriteLine($"PrinciPal: {action} timed out (attempt {attempt + 1}/{maxAttempts}), retrying");
                        await Task.Delay(ComputeDelay(attempt)).ConfigureAwait(false);
                        continue;
                    }
                    return Result.Failure(new RequestTimedOutError(action));
                }
            }

            // Unreachable, but the compiler needs it
            return Result.Failure(new ServerUnreachableError(_serverUrl, "Max retries exhausted"));
        }

        private int ComputeDelay(int attempt)
        {
            int jitter;
            lock (_jitterLock)
            {
                jitter = _random.Next(0, _retryBaseDelayMs + 1);
            }
            return _retryBaseDelayMs * (1 << attempt) + jitter;
        }

        public void Dispose()
        {
            StopHeartbeat();
            _heartbeatTimer.Dispose();
            _httpClient.Dispose();
        }
    }
}
