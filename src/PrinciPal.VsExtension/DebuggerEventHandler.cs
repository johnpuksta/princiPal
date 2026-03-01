using System;
using System.Diagnostics;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using PrinciPal.VsExtension.Abstractions;
using PrinciPal.VsExtension.Services;
using Task = System.Threading.Tasks.Task;

namespace PrinciPal.VsExtension
{
    /// <summary>
    /// Thin COM event shell — subscribes to VS debugger events and delegates to
    /// <see cref="DebugEventCoordinator"/> for all logic.
    /// </summary>
    public class DebuggerEventHandler : IDisposable
    {
        private readonly DTE2 _dte;
        private readonly AsyncPackage _package;
        private readonly DebugEventCoordinator _coordinator;
        private readonly IExtensionLogger _logger;

        // CRITICAL: Must be stored as a field to prevent COM RCW garbage collection
        private DebuggerEvents? _debuggerEvents;

        public DebuggerEventHandler(DTE2 dte, AsyncPackage package, DebugEventCoordinator coordinator, IExtensionLogger logger)
        {
            _dte = dte;
            _package = package;
            _coordinator = coordinator;
            _logger = logger;
        }

        public void Initialize()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            _debuggerEvents = _dte.Events.DebuggerEvents;
            _debuggerEvents.OnEnterBreakMode += OnEnterBreakMode;
            _debuggerEvents.OnEnterDesignMode += OnEnterDesignMode;
            _debuggerEvents.OnContextChanged += OnContextChanged;

            _ = RegisterAsync();
        }

        private async Task RegisterAsync()
        {
            var result = await _coordinator.RegisterAsync();
            result.Switch(
                onSuccess: () => { },
                onFailure: err => Debug.WriteLine($"PrinciPal: {err.Description}"));
        }

        private void OnEnterBreakMode(dbgEventReason reason, ref dbgExecutionAction executionAction)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Debug.WriteLine($"PrinciPal: Entered break mode. Reason: {reason}");

            if (!_coordinator.ShouldPushState())
            {
                Debug.WriteLine("PrinciPal: Skipping push — this instance is debugging another VS instance.");
                return;
            }

            _ = BuildAndPushAsync();
        }

        private void OnEnterDesignMode(dbgEventReason reason)
        {
            Debug.WriteLine($"PrinciPal: Entered design mode (debugging stopped). Reason: {reason}");
            _ = ClearAsync();
        }

        private void OnContextChanged(
            EnvDTE.Process newProcess,
            EnvDTE.Program newProgram,
            EnvDTE.Thread newThread,
            EnvDTE.StackFrame newStackFrame)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_coordinator.ShouldPushState() && _dte.Debugger.CurrentMode == dbgDebugMode.dbgBreakMode)
            {
                _ = BuildAndPushAsync();
            }
        }

        private async Task BuildAndPushAsync()
        {
            try
            {
                await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
                var state = _coordinator.BuildDebugState();

                var result = await _coordinator.PublishStateAsync(state);
                result.Switch(
                    onSuccess: () => { },
                    onFailure: err => Debug.WriteLine($"PrinciPal: {err.Description}"));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PrinciPal: Error pushing debug state: {ex.Message}");
            }
        }

        private async Task ClearAsync()
        {
            var result = await _coordinator.ClearStateAsync();
            result.Switch(
                onSuccess: () => { },
                onFailure: err => Debug.WriteLine($"PrinciPal: {err.Description}"));
        }

        public async Task DeregisterSessionAsync()
        {
            var result = await _coordinator.DeregisterAsync();
            result.Switch(
                onSuccess: () => Debug.WriteLine($"PrinciPal: Deregistered session."),
                onFailure: err => Debug.WriteLine($"PrinciPal: Failed to deregister session: {err.Description}"));
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
        }
    }
}
