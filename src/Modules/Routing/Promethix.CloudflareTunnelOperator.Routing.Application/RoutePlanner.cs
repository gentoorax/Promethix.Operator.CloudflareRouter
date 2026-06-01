using Promethix.CloudflareTunnelOperator.Routing.Domain;

namespace Promethix.CloudflareTunnelOperator.Routing.Application;

public static class RoutePlanner
{
    public static RoutePlan BuildPlan(
        IReadOnlyCollection<PublicHostnameRoute> desiredRoutes,
        IReadOnlyCollection<PublicHostnameRoute> actualRoutes,
        string ownershipTag)
    {
        ArgumentNullException.ThrowIfNull(desiredRoutes);
        ArgumentNullException.ThrowIfNull(actualRoutes);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownershipTag);

        var desiredByHostname = desiredRoutes.ToDictionary(route => route.Hostname, StringComparer.OrdinalIgnoreCase);
        var actualByHostname = actualRoutes.ToDictionary(route => route.Hostname, StringComparer.OrdinalIgnoreCase);

        var toCreate = new List<PublicHostnameRoute>();
        var toUpdate = new List<PublicHostnameRoute>();
        var toDelete = new List<PublicHostnameRoute>();
        var conflicts = new List<RouteConflict>();

        foreach (var desired in desiredRoutes)
        {
            if (!actualByHostname.TryGetValue(desired.Hostname, out var actual))
            {
                toCreate.Add(desired);
                continue;
            }

            if (!string.Equals(actual.OwnershipTag, ownershipTag, StringComparison.Ordinal))
            {
                if (!RouteMatches(desired, actual))
                {
                    conflicts.Add(new RouteConflict(desired.Hostname, "Hostname exists but is not owned by this operator."));
                }

                continue;
            }

            if (!RouteMatches(desired, actual))
            {
                toUpdate.Add(desired);
            }
        }

        foreach (var actual in actualRoutes)
        {
            if (desiredByHostname.ContainsKey(actual.Hostname))
            {
                continue;
            }

            if (string.Equals(actual.OwnershipTag, ownershipTag, StringComparison.Ordinal))
            {
                toDelete.Add(actual);
            }
        }

        return new RoutePlan(toCreate, toUpdate, toDelete, conflicts);
    }

    private static bool RouteMatches(PublicHostnameRoute left, PublicHostnameRoute right)
    {
        return string.Equals(left.Hostname, right.Hostname, StringComparison.OrdinalIgnoreCase)
            && left.Protocol == right.Protocol
            && left.OriginService == right.OriginService;
    }
}
