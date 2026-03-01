using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;

namespace PrinciPal.VsExtension
{
    public sealed class ServerProcessManager : IDisposable
    {
        private readonly Action<string> _log;
        private readonly object _lock = new object();
        private Process? _process;
        private int _port;
        private int _restartCount;
        private bool _disposed;

        private const int MaxRestarts = 5;
        private const string ServerExeRelativePath = "Server\\PrinciPal.Server.exe";
        private const string DevMarkerRelativePath = "Server\\.devproject";

        public int Port
        {
            get { lock (_lock) { return _port; } }
        }

        public ServerProcessManager(Action<string> log)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public void Start(int port)
        {
            lock (_lock)
            {
                if (_disposed) return;
                _port = port;
                _restartCount = 0;
                StartProcess();
            }
        }

        private void StartProcess()
        {
            var startInfo = ResolveStartInfo();
            if (startInfo == null) return;

            _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            _process.OutputDataReceived += (s, e) => { if (e.Data != null) _log(e.Data); };
            _process.ErrorDataReceived += (s, e) => { if (e.Data != null) _log($"[stderr] {e.Data}"); };
            _process.Exited += OnProcessExited;

            try
            {
                _process.Start();
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
                _log($"MCP server started (PID {_process.Id}) on http://localhost:{_port}/");
            }
            catch (Exception ex)
            {
                _log($"Failed to start MCP server: {ex.Message}");
            }
        }

        private ProcessStartInfo? ResolveStartInfo()
        {
            var extensionDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var exePath = Path.Combine(extensionDir, ServerExeRelativePath);
            var devMarkerPath = Path.Combine(extensionDir, DevMarkerRelativePath);
            var args = $"--port {_port}";

            // Release: bundled self-contained exe
            if (File.Exists(exePath))
            {
                _log($"Using bundled server: {exePath}");
                return new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
            }

            // Debug: dev marker contains the .csproj path, use dotnet run
            if (File.Exists(devMarkerPath))
            {
                var projectPath = File.ReadAllText(devMarkerPath).Trim();
                if (File.Exists(projectPath))
                {
                    _log($"Dev mode: using dotnet run --project {projectPath}");
                    return new ProcessStartInfo
                    {
                        FileName = "dotnet",
                        Arguments = $"run --project \"{projectPath}\" --no-build -- {args}",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                    };
                }
            }

            _log($"Server not found. Checked:\n  - {exePath}\n  - {devMarkerPath}");
            return null;
        }

        private void OnProcessExited(object sender, EventArgs e)
        {
            lock (_lock)
            {
                if (_disposed) return;

                var exitCode = -1;
                try { exitCode = _process?.ExitCode ?? -1; } catch { }

                if (exitCode == 0)
                {
                    _log("MCP server exited normally.");
                    return;
                }

                _restartCount++;
                if (_restartCount > MaxRestarts)
                {
                    _log($"MCP server crashed {_restartCount} times. Giving up.");
                    return;
                }

                _log($"MCP server crashed (exit code {exitCode}). Restarting (attempt {_restartCount}/{MaxRestarts})...");
                StartProcess();
            }
        }

        /// <summary>
        /// Checks if a princiPal MCP server is already running on the given port
        /// by hitting the /api/health endpoint. Distinguishes our server from
        /// unrelated services that happen to be on the same port.
        /// </summary>
        public static bool IsPrinciPalServerRunning(int port)
        {
            try
            {
                var request = WebRequest.CreateHttp($"http://localhost:{port}/api/health");
                request.Method = "GET";
                request.Timeout = 2000;
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    return response.StatusCode == HttpStatusCode.OK;
                }
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;

                if (_process != null && !_process.HasExited)
                {
                    // Only kill the server if no other sessions are using it.
                    // The idle-shutdown watchdog will clean it up if all sessions leave.
                    var hasOtherSessions = false;
                    try
                    {
                        var request = WebRequest.CreateHttp($"http://localhost:{_port}/api/sessions");
                        request.Method = "GET";
                        request.Timeout = 2000;
                        using (var response = (HttpWebResponse)request.GetResponse())
                        using (var reader = new System.IO.StreamReader(response.GetResponseStream()))
                        {
                            var body = reader.ReadToEnd();
                            // If there are remaining sessions (array not empty), don't kill
                            hasOtherSessions = body.Contains("sessionId") || body.Contains("SessionId");
                        }
                    }
                    catch { /* can't reach server, safe to kill */ }

                    if (!hasOtherSessions)
                    {
                        try
                        {
                            _process.Kill();
                            _process.WaitForExit(5000);
                            _log("MCP server stopped.");
                        }
                        catch (Exception ex)
                        {
                            _log($"Error stopping MCP server: {ex.Message}");
                        }
                    }
                    else
                    {
                        _log("Other sessions still active — leaving MCP server running.");
                    }
                }

                _process?.Dispose();
                _process = null;
            }
        }
    }
}
