using PrinciPal.Domain.ValueObjects;

namespace PrinciPal.Application.Abstractions;

public interface IDebugStateStore
{
    int MaxHistorySize { get; set; }
    int TotalCaptured { get; }
    void Update(DebugState state);
    void UpdateExpression(ExpressionResult result);
    DebugState? GetCurrentState();
    ExpressionResult? GetLastExpression();
    List<DebugStateSnapshot> GetHistory();
    DebugStateSnapshot? GetSnapshot(int index);
    void Clear();
    void ClearHistory();
}
