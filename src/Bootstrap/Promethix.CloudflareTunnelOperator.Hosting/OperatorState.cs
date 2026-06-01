namespace Promethix.CloudflareTunnelOperator.Hosting;

internal sealed class OperatorState
{
    public bool HasCompletedInitialReconciliation { get; private set; }

    public DateTimeOffset? LastCompletedAtUtc { get; private set; }

    public void MarkReconciliationCompleted(DateTimeOffset completedAtUtc)
    {
        this.HasCompletedInitialReconciliation = true;
        this.LastCompletedAtUtc = completedAtUtc;
    }
}
