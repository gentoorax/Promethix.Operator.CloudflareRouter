using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Promethix.CloudflareTunnelOperator.Routing.Application;
using Promethix.CloudflareTunnelOperator.Routing.Domain;

namespace Promethix.CloudflareTunnelOperator.Routing.Integrations.Cloudflare;

public sealed class CloudflareTunnelRouteClient(
    IOptions<CloudflareTunnelOptions> options,
    ILogger<CloudflareTunnelRouteClient> logger) : ICloudflareTunnelRouteClient
{
    private static readonly Action<ILogger, string, string, Exception?> LogReadingRoutes =
        LoggerMessage.Define<string, string>(
            LogLevel.Information,
            new EventId(1000, nameof(GetRoutesAsync)),
            "Reading Cloudflare routes for tunnel {TunnelId} in account {AccountId}.");

    private static readonly Action<ILogger, int, int, int, Exception?> LogApplyingPlan =
        LoggerMessage.Define<int, int, int>(
            LogLevel.Information,
            new EventId(1001, nameof(ApplyAsync)),
            "Applying Cloudflare route plan: create {CreateCount}, update {UpdateCount}, delete {DeleteCount}.");

    public Task<IReadOnlyCollection<PublicHostnameRoute>> GetRoutesAsync(CancellationToken cancellationToken)
    {
        LogReadingRoutes(logger, options.Value.TunnelId, options.Value.AccountId, null);

        IReadOnlyCollection<PublicHostnameRoute> routes = Array.Empty<PublicHostnameRoute>();
        return Task.FromResult(routes);
    }

    public Task ApplyAsync(RoutePlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        LogApplyingPlan(
            logger,
            plan.ToCreate.Count,
            plan.ToUpdate.Count,
            plan.ToDelete.Count,
            null);

        return Task.CompletedTask;
    }
}
