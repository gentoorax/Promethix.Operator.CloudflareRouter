using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Promethix.CloudflareTunnelOperator.Routing.Application;
using Promethix.CloudflareTunnelOperator.Routing.Domain;
using System.Net.Http.Json;
using System.Text.Json;

namespace Promethix.CloudflareTunnelOperator.Routing.Integrations.Cloudflare;

public sealed class CloudflareTunnelRouteClient(
    HttpClient httpClient,
    IManagedRouteOwnershipStore ownershipStore,
    IOptions<CloudflareTunnelOptions> options,
    ILogger<CloudflareTunnelRouteClient> logger) : ICloudflareTunnelRouteClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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

    public async Task<IReadOnlyCollection<PublicHostnameRoute>> GetRoutesAsync(CancellationToken cancellationToken)
    {
        LogReadingRoutes(logger, options.Value.TunnelId, options.Value.AccountId, null);

        var configuration = await GetConfigurationAsync(
            options.Value.AccountId,
            options.Value.TunnelId,
            cancellationToken).ConfigureAwait(false);

        var ownershipByHostname = await ownershipStore.GetOwnershipAsync(cancellationToken).ConfigureAwait(false);

        return configuration.Ingress
            .Where(rule => !string.IsNullOrWhiteSpace(rule.Hostname))
            .Where(rule => rule.Service.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || rule.Service.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            .Select(rule => ToRoute(rule, ownershipByHostname))
            .ToArray();
    }

    public async Task ApplyAsync(RoutePlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        LogApplyingPlan(
            logger,
            plan.ToCreate.Count,
            plan.ToUpdate.Count,
            plan.ToDelete.Count,
            null);

        var ownershipByHostname = await ownershipStore.GetOwnershipAsync(cancellationToken).ConfigureAwait(false);
        var currentConfiguration = await GetConfigurationAsync(
            options.Value.AccountId,
            options.Value.TunnelId,
            cancellationToken).ConfigureAwait(false);

        var updatedConfiguration = CloudflareRouteConfigurationBuilder.Build(
            currentConfiguration,
            plan,
            ownershipByHostname,
            options.Value.OwnershipTag);

        await PutConfigurationAsync(
            options.Value.AccountId,
            options.Value.TunnelId,
            updatedConfiguration,
            cancellationToken).ConfigureAwait(false);

        var updatedOwnership = new Dictionary<string, string>(ownershipByHostname, StringComparer.OrdinalIgnoreCase);

        foreach (var route in plan.ToDelete)
        {
            updatedOwnership.Remove(route.Hostname);
        }

        foreach (var route in plan.ToCreate.Concat(plan.ToUpdate))
        {
            updatedOwnership[route.Hostname] = options.Value.OwnershipTag;
        }

        await ownershipStore.SaveOwnershipAsync(updatedOwnership, cancellationToken).ConfigureAwait(false);
    }

    private static PublicHostnameRoute ToRoute(TunnelIngressRule rule, IReadOnlyDictionary<string, string> ownershipByHostname)
    {
        var originService = new Uri(rule.Service, UriKind.Absolute);
        var protocol = string.Equals(originService.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            ? RouteProtocol.Https
            : RouteProtocol.Http;
        var ownershipTag = ownershipByHostname.TryGetValue(rule.Hostname!, out var value)
            ? value
            : "external";

        return PublicHostnameRoute.Create(rule.Hostname!, originService, protocol, ownershipTag);
    }

    private async Task<TunnelConfiguration> GetConfigurationAsync(string accountId, string tunnelId, CancellationToken cancellationToken)
    {
        var response = await httpClient.GetAsync(
            new Uri($"accounts/{accountId}/cfd_tunnel/{tunnelId}/configurations", UriKind.Relative),
            cancellationToken).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var envelope = await response.Content.ReadFromJsonAsync<CloudflareApiEnvelope<TunnelConfigurationResponse>>(
            JsonOptions,
            cancellationToken).ConfigureAwait(false);

        return envelope?.Result?.Config ?? new TunnelConfiguration();
    }

    private async Task PutConfigurationAsync(string accountId, string tunnelId, TunnelConfiguration configuration, CancellationToken cancellationToken)
    {
        var body = new
        {
            config = new
            {
                ingress = configuration.Ingress,
            },
        };

        var response = await httpClient.PutAsJsonAsync(
            new Uri($"accounts/{accountId}/cfd_tunnel/{tunnelId}/configurations", UriKind.Relative),
            body,
            JsonOptions,
            cancellationToken).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
    }
}
