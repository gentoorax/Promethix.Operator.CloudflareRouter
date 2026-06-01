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

        var plan = RoutePlanner.BuildPlan(desired, Array.Empty<PublicHostnameRoute>(), OwnershipTag);

        plan.ToCreate.Should().ContainSingle();
        plan.ToUpdate.Should().BeEmpty();
        plan.ToDelete.Should().BeEmpty();
        plan.Conflicts.Should().BeEmpty();
    }

    [Fact]
    public void BuildPlanShouldNotDeleteUnownedRoutes()
    {
        var actual = new[]
        {
            PublicHostnameRoute.Create("shared.example.com", new Uri("https://shared.default.svc.cluster.local:8443"), RouteProtocol.Https, "another-operator"),
        };

        var plan = RoutePlanner.BuildPlan(Array.Empty<PublicHostnameRoute>(), actual, OwnershipTag);

        plan.ToDelete.Should().BeEmpty();
        plan.Conflicts.Should().BeEmpty();
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

        plan.Conflicts.Should().ContainSingle().Which.Hostname.Should().Be("conflict.example.com");
        plan.ToCreate.Should().BeEmpty();
        plan.ToUpdate.Should().BeEmpty();
    }
}
