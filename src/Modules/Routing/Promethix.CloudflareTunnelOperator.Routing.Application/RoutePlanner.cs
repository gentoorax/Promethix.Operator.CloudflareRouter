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

        var desiredByHostname = desiredRoutes
            .GroupBy(route => route.Hostname, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var normalizedActualRoutes = actualRoutes
            .GroupBy(route => route.Hostname, StringComparer.OrdinalIgnoreCase)
            .Select(group => SelectActualRoute(group, ownershipTag))
            .ToArray();
        var actualByHostname = normalizedActualRoutes.ToDictionary(route => route.Hostname, StringComparer.OrdinalIgnoreCase);

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

        foreach (var actual in normalizedActualRoutes)
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

    public static RoutePlan BuildManagePlan(
        PublicHostnameRoute desiredRoute,
        IReadOnlyCollection<PublicHostnameRoute> actualRoutes,
        string ownershipTag)
    {
        ArgumentNullException.ThrowIfNull(desiredRoute);
        ArgumentNullException.ThrowIfNull(actualRoutes);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownershipTag);

        var actual = actualRoutes
            .Where(route => string.Equals(route.Hostname, desiredRoute.Hostname, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(route => string.Equals(route.OwnershipTag, ownershipTag, StringComparison.Ordinal))
            .FirstOrDefault();

        return actual is null
            ? new RoutePlan([desiredRoute], [], [], [])
            : !string.Equals(actual.OwnershipTag, ownershipTag, StringComparison.Ordinal)
                ? RouteMatches(desiredRoute, actual)
                ? new RoutePlan([], [], [], [])
                : new RoutePlan([], [], [], [new RouteConflict(desiredRoute.Hostname, "Hostname exists but is not owned by this operator.")])
                : RouteMatches(desiredRoute, actual)
                    ? new RoutePlan([], [], [], [])
                    : new RoutePlan([], [desiredRoute], [], []);
    }

    public static RoutePlan BuildCleanupPlan(
        string hostname,
        IReadOnlyCollection<PublicHostnameRoute> actualRoutes,
        string ownershipTag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostname);
        ArgumentNullException.ThrowIfNull(actualRoutes);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownershipTag);

        var actual = actualRoutes
            .Where(route => string.Equals(route.Hostname, hostname, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(route => string.Equals(route.OwnershipTag, ownershipTag, StringComparison.Ordinal))
            .FirstOrDefault();

        return actual is null || !string.Equals(actual.OwnershipTag, ownershipTag, StringComparison.Ordinal)
            ? new RoutePlan([], [], [], [])
            : new RoutePlan([], [], [actual], []);
    }

    private static bool RouteMatches(PublicHostnameRoute left, PublicHostnameRoute right)
    {
        return string.Equals(left.Hostname, right.Hostname, StringComparison.OrdinalIgnoreCase)
            && left.Protocol == right.Protocol
            && left.OriginService == right.OriginService
            && string.Equals(left.OriginServerName, right.OriginServerName, StringComparison.OrdinalIgnoreCase);
    }

    private static PublicHostnameRoute SelectActualRoute(
        IGrouping<string, PublicHostnameRoute> group,
        string ownershipTag)
    {
        return group
            .OrderByDescending(route => string.Equals(route.OwnershipTag, ownershipTag, StringComparison.Ordinal))
            .First();
    }
}
