using System.Threading.Channels;

namespace Promethix.CloudflareTunnelOperator.Hosting.Reconciliation;

internal sealed class ReconciliationSignalQueue
{
    private readonly Channel<string> signals = Channel.CreateBounded<string>(
        new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest,
        });

    public void Request(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            reason = "unspecified";
        }

        this.signals.Writer.TryWrite(reason);
    }

    public async ValueTask<string> WaitAsync(TimeSpan fallbackInterval, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(fallbackInterval, TimeSpan.Zero);

        var readTask = this.signals.Reader.ReadAsync(cancellationToken).AsTask();
        var delayTask = Task.Delay(fallbackInterval, cancellationToken);
        var completedTask = await Task.WhenAny(readTask, delayTask).ConfigureAwait(false);

        if (completedTask == readTask)
        {
            return await readTask.ConfigureAwait(false);
        }

        await delayTask.ConfigureAwait(false);
        return "interval";
    }
}
