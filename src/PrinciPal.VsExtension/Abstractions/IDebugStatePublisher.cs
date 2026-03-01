using PrinciPal.Common.Results;
using PrinciPal.Domain.ValueObjects;
using System;
using System.Threading.Tasks;

namespace PrinciPal.VsExtension.Abstractions
{
    /// <summary>
    /// Publishes debug state to the MCP server over HTTP.
    /// </summary>
    public interface IDebugStatePublisher : IDisposable
    {
        Task<Result> RegisterSessionAsync();
        Task<Result> PushDebugStateAsync(DebugState state);
        Task<Result> ClearDebugStateAsync();
        Task<Result> DeregisterSessionAsync();
    }
}
