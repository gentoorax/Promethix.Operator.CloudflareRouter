using FluentAssertions;
using Promethix.CloudflareTunnelOperator.Routing.Application;
using Promethix.CloudflareTunnelOperator.Routing.Domain;

namespace Promethix.CloudflareTunnelOperator.Routing.Tests;

public sealed class RoutePlannerTests
{
    private const string OwnershipTag = "promethix-cloudflare-tunnel-operator";

    [Fact]
    public void BuildPlanShouldCreateNewRoutesWhenHostnameIsMissing()
    {
        var desired = new[]
        {
            PublicHostnameRoute.Create("app.example.com", new Uri("https://app.default.svc.cluster.local:8443"), RouteProtocol.Https, OwnershipTag),
        };

        var plan = RoutePlanner.BuildPlan(desired, [], OwnershipTag);

        _ = plan.ToCreate.Should().ContainSingle();
        _ = plan.ToUpdate.Should().BeEmpty();
        _ = plan.ToDelete.Should().BeEmpty();
        _ = plan.Conflicts.Should().BeEmpty();
    }

    [Fact]
    public void BuildPlanShouldNotDeleteUnownedRoutes()
    {
        var actual = new[]
        {
            PublicHostnameRoute.Create("shared.example.com", new Uri("https://shared.default.svc.cluster.local:8443"), RouteProtocol.Https, "another-operator"),
        };

        var plan = RoutePlanner.BuildPlan([], actual, OwnershipTag);

        _ = plan.ToDelete.Should().BeEmpty();
        _ = plan.Conflicts.Should().BeEmpty();
    }

    [Fact]
    public void BuildPlanShouldRecordConflictWhenUnownedHostnameDiffers()
    {
        var desired = new[]
        {
            PublicHostnameRoute.Create("conflict.example.com", new Uri("https://desired.default.svc.cluster.local:8443"), RouteProtocol.Https, OwnershipTag),
        };
        var actual = new[]
        {
            PublicHostnameRoute.Create("conflict.example.com", new Uri("https://actual.default.svc.cluster.local:8443"), RouteProtocol.Https, "another-operator"),
        };

        var plan = RoutePlanner.BuildPlan(desired, actual, OwnershipTag);

        _ = plan.Conflicts.Should().ContainSingle().Which.Hostname.Should().Be("conflict.example.com");
        _ = plan.ToCreate.Should().BeEmpty();
        _ = plan.ToUpdate.Should().BeEmpty();
    }

    [Fact]
    public void BuildManagePlanShouldNotScheduleDeletionOfOtherOwnedRoutes()
    {
        var desired = PublicHostnameRoute.Create(
            "app.example.com",
            new Uri("https://app.default.svc.cluster.local:8443"),
            RouteProtocol.Https,
            OwnershipTag);
        var actual = new[]
        {
            PublicHostnameRoute.Create("app.example.com", new Uri("https://old.default.svc.cluster.local:8443"), RouteProtocol.Https, OwnershipTag),
            PublicHostnameRoute.Create("other.example.com", new Uri("https://other.default.svc.cluster.local:8443"), RouteProtocol.Https, OwnershipTag),
        };

        var plan = RoutePlanner.BuildManagePlan(desired, actual, OwnershipTag);

        _ = plan.ToUpdate.Should().ContainSingle().Which.Hostname.Should().Be("app.example.com");
        _ = plan.ToDelete.Should().BeEmpty();
    }

    [Fact]
    public void BuildPlanShouldUpdateOwnedRouteWhenOriginServerNameDiffers()
    {
        var desired = new[]
        {
            PublicHostnameRoute.Create(
                "whoami.promethix.net",
                new Uri("https://traefik-cloudflare-tunnel.traefik-cloudflare-tunnel.svc.cluster.local"),
                RouteProtocol.Https,
                OwnershipTag,
                "whoami.promethix.net"),
        };

        var actual = new[]
        {
            PublicHostnameRoute.Create(
                "whoami.promethix.net",
                new Uri("https://traefik-cloudflare-tunnel.traefik-cloudflare-tunnel.svc.cluster.local"),
                RouteProtocol.Https,
                OwnershipTag),
        };

        var plan = RoutePlanner.BuildPlan(desired, actual, OwnershipTag);

        _ = plan.ToUpdate.Should().ContainSingle();
        _ = plan.ToCreate.Should().BeEmpty();
        _ = plan.ToDelete.Should().BeEmpty();
        _ = plan.Conflicts.Should().BeEmpty();
    }

    [Fact]
    public void BuildManagePlanShouldUpdateOwnedRouteWhenOriginServerNameDiffers()
    {
        var desired = PublicHostnameRoute.Create(
            "whoami.promethix.net",
            new Uri("https://traefik-cloudflare-tunnel.traefik-cloudflare-tunnel.svc.cluster.local"),
            RouteProtocol.Https,
            OwnershipTag,
            "whoami.promethix.net");

        var actual = new[]
        {
            PublicHostnameRoute.Create(
                "whoami.promethix.net",
                new Uri("https://traefik-cloudflare-tunnel.traefik-cloudflare-tunnel.svc.cluster.local"),
                RouteProtocol.Https,
                OwnershipTag),
        };

        var plan = RoutePlanner.BuildManagePlan(desired, actual, OwnershipTag);

        _ = plan.ToUpdate.Should().ContainSingle();
        _ = plan.ToCreate.Should().BeEmpty();
        _ = plan.ToDelete.Should().BeEmpty();
        _ = plan.Conflicts.Should().BeEmpty();
    }

    [Fact]
    public void BuildCleanupPlanShouldDeleteOnlyOwnedMatchingHostname()
    {
        var actual = new[]
        {
            PublicHostnameRoute.Create("app.example.com", new Uri("https://app.default.svc.cluster.local:8443"), RouteProtocol.Https, OwnershipTag),
            PublicHostnameRoute.Create("shared.example.com", new Uri("https://shared.default.svc.cluster.local:8443"), RouteProtocol.Https, "another-operator"),
        };

        var plan = RoutePlanner.BuildCleanupPlan("app.example.com", actual, OwnershipTag);

        _ = plan.ToDelete.Should().ContainSingle().Which.Hostname.Should().Be("app.example.com");
        _ = plan.ToCreate.Should().BeEmpty();
        _ = plan.ToUpdate.Should().BeEmpty();
        _ = plan.Conflicts.Should().BeEmpty();
    }

    [Fact]
    public void BuildPlanShouldTolerateDuplicateActualHostnames()
    {
        var desired = new[]
        {
            PublicHostnameRoute.Create(
                "app.example.com",
                new Uri("https://new.default.svc.cluster.local:8443"),
                RouteProtocol.Https,
                OwnershipTag),
        };

        var actual = new[]
        {
            PublicHostnameRoute.Create(
                "app.example.com",
                new Uri("https://old.default.svc.cluster.local:8443"),
                RouteProtocol.Https,
                OwnershipTag),
            PublicHostnameRoute.Create(
                "app.example.com",
                new Uri("https://old.default.svc.cluster.local:8443"),
                RouteProtocol.Https,
                OwnershipTag),
        };

        var plan = RoutePlanner.BuildPlan(desired, actual, OwnershipTag);

        _ = plan.ToUpdate.Should().ContainSingle(route => route.Hostname == "app.example.com");
        _ = plan.Conflicts.Should().BeEmpty();
    }
}
