using k8s;
using k8s.Autorest;
using Promethix.CloudflareTunnelOperator.Routing.Integrations.Kubernetes;

namespace Promethix.CloudflareTunnelOperator.Hosting.Reconciliation;

internal sealed class RouteIntentWatchService(
    IKubernetes kubernetes,
    ReconciliationSignalQueue signalQueue,
    ILogger<RouteIntentWatchService> logger) : BackgroundService
{
    private static readonly TimeSpan WatchReconnectDelay = TimeSpan.FromSeconds(5);
    private const int WatchTimeoutSeconds = 300;

    private static readonly Action<ILogger, Exception> LogWatchError =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(2100, "RouteIntentWatchFailed"),
            "TunnelPublicHostname watch failed.");

    private static readonly Action<ILogger, string, string, string, Exception?> LogWatchEvent =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Information,
            new EventId(2101, "RouteIntentWatchEvent"),
            "Observed TunnelPublicHostname watch event {WatchEventType} for {Namespace}/{Name}.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await WatchAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogWatchError(logger, ex);
                signalQueue.Request("watch-error");
                await Task.Delay(WatchReconnectDelay, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private async Task WatchAsync(CancellationToken cancellationToken)
    {
        var responseTask = kubernetes.CustomObjects.ListClusterCustomObjectWithHttpMessagesAsync<TunnelPublicHostnameCustomResourceList>(
            group: TunnelPublicHostnameCustomResource.Group,
            version: TunnelPublicHostnameCustomResource.Version,
            plural: TunnelPublicHostnameCustomResource.PluralName,
            watch: true,
            allowWatchBookmarks: true,
            timeoutSeconds: WatchTimeoutSeconds,
            cancellationToken: cancellationToken);

#pragma warning disable CS0618
        await foreach (var (eventType, resource) in responseTask.WatchAsync<TunnelPublicHostnameCustomResource, TunnelPublicHostnameCustomResourceList>(
                           onError: static _ => { },
                           cancellationToken: cancellationToken).ConfigureAwait(false))
#pragma warning restore CS0618
        {
            var @namespace = resource.Metadata.NamespaceProperty ?? string.Empty;
            var name = resource.Metadata.Name ?? string.Empty;

            LogWatchEvent(logger, eventType.ToString(), @namespace, name, null);
            signalQueue.Request($"watch:{eventType}");
        }
    }
}
