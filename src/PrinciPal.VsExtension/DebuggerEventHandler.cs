using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using PrinciPal.Domain.ValueObjects;
using Task = System.Threading.Tasks.Task;

namespace PrinciPal.VsExtension
{
    /// <summary>
    /// Subscribes to VS debugger events and pushes debug state to the MCP server.
    /// </summary>
    public class DebuggerEventHandler : IDisposable
    {
        private readonly DTE2 _dte;
        private readonly AsyncPackage _package;
        private readonly HttpClient _httpClient;
        private readonly JsonSerializerOptions _jsonOptions;

        // CRITICAL: Must be stored as a field to prevent COM RCW garbage collection
        private DebuggerEvents? _debuggerEvents;

        private readonly string _serverUrl;
        private readonly string _sessionId;
        private readonly string _sessionQueryParams;

        public DebuggerEventHandler(DTE2 dte, AsyncPackage package, int port, string sessionId, string sessionName, string solutionPath)
        {
            _dte = dte;
            _package = package;
            _sessionId = sessionId;
            _sessionQueryParams = $"name={Uri.EscapeDataString(sessionName)}&path={Uri.EscapeDataString(solutionPath)}";
            _serverUrl = $"http://localhost:{port}";
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(_serverUrl),
                Timeout = TimeSpan.FromSeconds(5)
            };
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };
        }

        public void Initialize()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // Store in field to prevent GC of the COM RCW
            _debuggerEvents = _dte.Events.DebuggerEvents;
            _debuggerEvents.OnEnterBreakMode += OnEnterBreakMode;
            _debuggerEvents.OnEnterDesignMode += OnEnterDesignMode;
            _debuggerEvents.OnContextChanged += OnContextChanged;

            // Register session immediately so the server knows this IDE is alive
            _ = RegisterSessionAsync();
        }

        private async Task RegisterSessionAsync()
        {
            try
            {
                await Task.Run(async () =>
                {
                    try
                    {
                        var response = await _httpClient.PostAsync(
                            $"/api/sessions/{Uri.EscapeDataString(_sessionId)}?{_sessionQueryParams}",
                            null);
                        Debug.WriteLine($"PrinciPal: Registered session '{_sessionId}'. Status: {response.StatusCode}");
                    }
                    catch (HttpRequestException ex)
                    {
                        Debug.WriteLine($"PrinciPal: MCP server not reachable at {_serverUrl}. {ex.Message}");
                    }
                    catch (TaskCanceledException)
                    {
                        Debug.WriteLine("PrinciPal: Register request timed out.");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PrinciPal: Error registering session: {ex.Message}");
            }
        }

        private void OnEnterBreakMode(dbgEventReason reason, ref dbgExecutionAction executionAction)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Debug.WriteLine($"PrinciPal: Entered break mode. Reason: {reason}");

            if (IsDebuggingVisualStudio())
            {
                Debug.WriteLine("PrinciPal: Skipping push — this instance is debugging another VS instance.");
                return;
            }

            _ = PushDebugStateAsync();
        }

        private void OnEnterDesignMode(dbgEventReason reason)
        {
            Debug.WriteLine($"PrinciPal: Entered design mode (debugging stopped). Reason: {reason}");
            _ = ClearDebugStateAsync();
        }

        private async Task ClearDebugStateAsync()
        {
            try
            {
                await Task.Run(async () =>
                {
                    try
                    {
                        var response = await _httpClient.DeleteAsync($"/api/sessions/{Uri.EscapeDataString(_sessionId)}/debug-state");
                        Debug.WriteLine($"PrinciPal: Cleared debug state. Status: {response.StatusCode}");
                    }
                    catch (HttpRequestException ex)
                    {
                        Debug.WriteLine($"PrinciPal: MCP server not reachable at {_serverUrl}. {ex.Message}");
                    }
                    catch (TaskCanceledException)
                    {
                        Debug.WriteLine("PrinciPal: Clear request timed out.");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PrinciPal: Error clearing debug state: {ex.Message}");
            }
        }

        private void OnContextChanged(
            EnvDTE.Process newProcess,
            EnvDTE.Program newProgram,
            EnvDTE.Thread newThread,
            EnvDTE.StackFrame newStackFrame)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // Only push if we're in break mode (context changes happen during stepping)
            if (_dte.Debugger.CurrentMode == dbgDebugMode.dbgBreakMode && !IsDebuggingVisualStudio())
            {
                _ = PushDebugStateAsync();
            }
        }

        /// <summary>
        /// Returns true if this VS instance is debugging another Visual Studio process (devenv.exe).
        /// When developing the extension, the outer VS debugs the experimental instance — we skip
        /// pushing from the outer VS so only the experimental instance's state is captured.
        /// </summary>
        private bool IsDebuggingVisualStudio()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var processes = _dte.Debugger.DebuggedProcesses;
                if (processes == null) return false;

                for (int i = 1; i <= processes.Count; i++)
                {
                    var name = processes.Item(i).Name;
                    if (name.EndsWith("devenv.exe", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PrinciPal: Error checking debugged processes: {ex.Message}");
            }
            return false;
        }

        private async Task PushDebugStateAsync()
        {
            try
            {
                DebugState state;

                // Must read debugger state on the UI thread
                await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
                state = ReadDebugState();

                // Post to MCP server on a background thread
                await Task.Run(async () =>
                {
                    var json = JsonSerializer.Serialize(state, _jsonOptions);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    try
                    {
                        var response = await _httpClient.PostAsync($"/api/sessions/{Uri.EscapeDataString(_sessionId)}/debug-state?{_sessionQueryParams}", content);
                        Debug.WriteLine($"PrinciPal: Pushed debug state. Status: {response.StatusCode}");
                    }
                    catch (HttpRequestException ex)
                    {
                        Debug.WriteLine($"PrinciPal: MCP server not reachable at {_serverUrl}. {ex.Message}");
                    }
                    catch (TaskCanceledException)
                    {
                        Debug.WriteLine("PrinciPal: Push timed out.");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PrinciPal: Error pushing debug state: {ex.Message}");
            }
        }

        private DebugState ReadDebugState()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var debugger = _dte.Debugger;
            var state = new DebugState
            {
                IsInBreakMode = debugger.CurrentMode == dbgDebugMode.dbgBreakMode
            };

            if (!state.IsInBreakMode) return state;

            // Current location
            try
            {
                var currentFrame = debugger.CurrentStackFrame;
                if (currentFrame != null)
                {
                    state.CurrentLocation = new SourceLocation
                    {
                        FunctionName = currentFrame.FunctionName
                    };

                    // Get file/line from active document
                    var doc = _dte.ActiveDocument;
                    if (doc != null)
                    {
                        state.CurrentLocation.FilePath = doc.FullName;
                        state.CurrentLocation.ProjectName = doc.ProjectItem?.ContainingProject?.Name;

                        var selection = doc.Selection as TextSelection;
                        if (selection != null)
                        {
                            state.CurrentLocation.Line = selection.CurrentLine;
                            state.CurrentLocation.Column = selection.CurrentColumn;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PrinciPal: Error reading location: {ex.Message}");
            }

            // Local variables
            try
            {
                var currentFrame = debugger.CurrentStackFrame;
                if (currentFrame != null)
                {
                    state.Locals = ReadExpressions(currentFrame.Locals, maxDepth: 2);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PrinciPal: Error reading locals: {ex.Message}");
            }

            // Call stack
            try
            {
                var thread = debugger.CurrentThread;
                if (thread != null)
                {
                    var frames = thread.StackFrames;
                    for (int i = 1; i <= Math.Min(frames.Count, 20); i++) // Cap at 20 frames
                    {
                        var frame = frames.Item(i);
                        state.CallStack.Add(new StackFrameInfo
                        {
                            Index = i,
                            FunctionName = frame.FunctionName,
                            Language = frame.Language,
                            Module = frame.Module
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PrinciPal: Error reading call stack: {ex.Message}");
            }

            // Breakpoints
            try
            {
                var breakpoints = debugger.Breakpoints;
                for (int i = 1; i <= breakpoints.Count; i++)
                {
                    var bp = breakpoints.Item(i);
                    state.Breakpoints.Add(new BreakpointInfo
                    {
                        FilePath = bp.File,
                        Line = bp.FileLine,
                        Column = bp.FileColumn,
                        FunctionName = bp.FunctionName,
                        Enabled = bp.Enabled,
                        Condition = bp.Condition
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PrinciPal: Error reading breakpoints: {ex.Message}");
            }

            return state;
        }

        private List<LocalVariable> ReadExpressions(Expressions expressions, int maxDepth, int currentDepth = 0)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var variables = new List<LocalVariable>();
            if (expressions == null || currentDepth > maxDepth) return variables;

            for (int i = 1; i <= expressions.Count; i++)
            {
                try
                {
                    var expr = expressions.Item(i);
                    var variable = new LocalVariable
                    {
                        Name = expr.Name,
                        Value = expr.Value,
                        Type = expr.Type,
                        IsValidValue = expr.IsValidValue
                    };

                    // Recurse into members for complex types
                    if (expr.DataMembers != null && expr.DataMembers.Count > 0 && currentDepth < maxDepth)
                    {
                        variable.Members = ReadExpressions(expr.DataMembers, maxDepth, currentDepth + 1);
                    }

                    variables.Add(variable);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"PrinciPal: Error reading expression at index {i}: {ex.Message}");
                }
            }

            return variables;
        }

        /// <summary>
        /// Deregisters this session from the MCP server. Best-effort, catches all exceptions.
        /// </summary>
        public async Task DeregisterSessionAsync()
        {
            try
            {
                await Task.Run(async () =>
                {
                    try
                    {
                        var response = await _httpClient.DeleteAsync($"/api/sessions/{Uri.EscapeDataString(_sessionId)}");
                        Debug.WriteLine($"PrinciPal: Deregistered session '{_sessionId}'. Status: {response.StatusCode}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"PrinciPal: Failed to deregister session '{_sessionId}': {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PrinciPal: Error deregistering session: {ex.Message}");
            }
        }

        public void Dispose()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_debuggerEvents != null)
            {
                try
                {
                    _debuggerEvents.OnEnterBreakMode -= OnEnterBreakMode;
                    _debuggerEvents.OnEnterDesignMode -= OnEnterDesignMode;
                    _debuggerEvents.OnContextChanged -= OnContextChanged;
                }
                catch { /* COM cleanup may fail during shutdown */ }
                _debuggerEvents = null;
            }
            _httpClient.Dispose();
        }
    }
}
