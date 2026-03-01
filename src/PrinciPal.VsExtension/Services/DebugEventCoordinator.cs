using System.Diagnostics;
using System.Threading.Tasks;
using PrinciPal.Common.Results;
using PrinciPal.Domain.ValueObjects;
using PrinciPal.VsExtension.Abstractions;
using Task = System.Threading.Tasks.Task;

namespace PrinciPal.VsExtension.Services
{
    /// <summary>
    /// Testable orchestration layer that coordinates debugger reads and server publishes.
    /// Depends only on interfaces — no COM, HTTP, or VS SDK types.
    /// </summary>
    public sealed class DebugEventCoordinator
    {
        private readonly IDebuggerReader _reader;
        private readonly IDebugStatePublisher _publisher;
        private readonly IExtensionLogger _logger;

        public DebugEventCoordinator(IDebuggerReader reader, IDebugStatePublisher publisher, IExtensionLogger logger)
        {
            _reader = reader;
            _publisher = publisher;
            _logger = logger;
        }

        /// <summary>
        /// Returns true if we should push state (i.e. we are NOT debugging another VS instance).
        /// Must be called on the UI thread.
        /// </summary>
        public bool ShouldPushState()
        {
            return !_reader.IsDebuggingVisualStudio();
        }

        /// <summary>
        /// Reads all debug state sections from the debugger. Must be called on the UI thread.
        /// Returns a partially-populated DebugState if some sections fail.
        /// </summary>
        public DebugState BuildDebugState()
        {
            var state = new DebugState
            {
                IsInBreakMode = _reader.IsInBreakMode
            };

            if (!state.IsInBreakMode) return state;

            _reader.ReadCurrentLocation().Switch(
                onSuccess: loc => state.CurrentLocation = loc,
                onFailure: err => _logger.Log(err.Description));

            _reader.ReadLocals().Switch(
                onSuccess: locals => state.Locals = locals,
                onFailure: err => _logger.Log(err.Description));

            _reader.ReadCallStack().Switch(
                onSuccess: stack => state.CallStack = stack,
                onFailure: err => _logger.Log(err.Description));

            _reader.ReadBreakpoints().Switch(
                onSuccess: bps => state.Breakpoints = bps,
                onFailure: err => _logger.Log(err.Description));

            return state;
        }

        public Task<Result> PublishStateAsync(DebugState state)
        {
            return _publisher.PushDebugStateAsync(state);
        }

        public Task<Result> ClearStateAsync()
        {
            return _publisher.ClearDebugStateAsync();
        }

        public Task<Result> RegisterAsync()
        {
            return _publisher.RegisterSessionAsync();
        }

        public Task<Result> DeregisterAsync()
        {
            return _publisher.DeregisterSessionAsync();
        }
    }
}
