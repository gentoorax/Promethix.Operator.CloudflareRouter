using Promethix.CloudflareTunnelOperator.Routing.Domain;

namespace Promethix.CloudflareTunnelOperator.Routing.Integrations.Cloudflare;

internal static class CloudflareRouteConfigurationBuilder
{
    public static TunnelConfiguration Build(
        TunnelConfiguration currentConfiguration,
        RoutePlan plan,
        IReadOnlyDictionary<string, string> ownershipByHostname,
        string ownershipTag)
    {
        ArgumentNullException.ThrowIfNull(currentConfiguration);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(ownershipByHostname);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownershipTag);

        var deleteHostnames = plan.ToDelete.Select(route => route.Hostname).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var updateRoutes = plan.ToUpdate.ToDictionary(route => route.Hostname, StringComparer.OrdinalIgnoreCase);
        var createRoutes = plan.ToCreate.ToDictionary(route => route.Hostname, StringComparer.OrdinalIgnoreCase);
        var managedHostnames = ownershipByHostname
            .Where(pair => string.Equals(pair.Value, ownershipTag, StringComparison.Ordinal))
            .Select(pair => pair.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var ingress = new List<TunnelIngressRule>();
        var fallbackRules = new List<TunnelIngressRule>();

        foreach (var existingRule in currentConfiguration.Ingress)
        {
            if (string.IsNullOrWhiteSpace(existingRule.Hostname))
            {
                fallbackRules.Add(existingRule);
                continue;
            }

            if (!managedHostnames.Contains(existingRule.Hostname))
            {
                ingress.Add(existingRule);
                continue;
            }

            if (deleteHostnames.Contains(existingRule.Hostname))
            {
                continue;
            }

            if (updateRoutes.TryGetValue(existingRule.Hostname, out var updateRoute))
            {
                ingress.Add(ToIngressRule(updateRoute));
                updateRoutes.Remove(existingRule.Hostname);
                continue;
            }

            ingress.Add(existingRule);
        }

        foreach (var route in updateRoutes.Values)
        {
            ingress.Add(ToIngressRule(route));
        }

        foreach (var route in createRoutes.Values)
        {
            ingress.Add(ToIngressRule(route));
        }

        if (fallbackRules.Count == 0)
        {
            fallbackRules.Add(new TunnelIngressRule { Service = "http_status:404" });
        }

        foreach (var fallbackRule in fallbackRules)
        {
            ingress.Add(fallbackRule);
        }

        return new TunnelConfiguration
        {
            Ingress = ingress,
        };
    }

    private static TunnelIngressRule ToIngressRule(PublicHostnameRoute route)
    {
        return new TunnelIngressRule
        {
            Hostname = route.Hostname,
            Service = route.OriginService.ToString(),
        };
    }
}
