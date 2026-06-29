using FluentAssertions;
using Promethix.CloudflareTunnelOperator.Routing.Domain;
using Promethix.CloudflareTunnelOperator.Routing.Integrations.Cloudflare;

namespace Promethix.CloudflareTunnelOperator.Routing.Tests;

public sealed class CloudflareRouteConfigurationBuilderTests
{
    private const string OwnershipTag = "promethix-cloudflare-tunnel-operator";

    [Fact]
    public void BuildShouldPreserveUnmanagedRulesAndFallbackWhileReplacingManagedOnes()
    {
        var currentConfiguration = new TunnelConfiguration
        {
            Ingress =
            [
                new TunnelIngressRule
                {
                    Hostname = "manual.example.com",
                    Service = "https://manual.default.svc.cluster.local:8443",
                },
                new TunnelIngressRule
                {
                    Hostname = "managed.example.com",
                    Service = "https://old.default.svc.cluster.local:8443",
                },
                new TunnelIngressRule
                {
                    Service = "http_status:404",
                },
            ],
        };

        var updatedManagedRoute = PublicHostnameRoute.Create(
            "managed.example.com",
            new Uri("https://new.default.svc.cluster.local:8443"),
            RouteProtocol.Https,
            OwnershipTag);

        var createdManagedRoute = PublicHostnameRoute.Create(
            "created.example.com",
            new Uri("http://created.default.svc.cluster.local:8080"),
            RouteProtocol.Http,
            OwnershipTag);

        var plan = new RoutePlan(
            ToCreate: [createdManagedRoute],
            ToUpdate: [updatedManagedRoute],
            ToDelete: [],
            Conflicts: []);

        var ownership = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["managed.example.com"] = OwnershipTag,
        };

        var rebuilt = CloudflareRouteConfigurationBuilder.Build(currentConfiguration, plan, ownership, OwnershipTag);

        _ = rebuilt.Ingress.Should().Contain(rule => rule.Hostname == "manual.example.com" && rule.Service == "https://manual.default.svc.cluster.local:8443");
        _ = rebuilt.Ingress.Should().Contain(rule => rule.Hostname == "managed.example.com" && rule.Service == "https://new.default.svc.cluster.local:8443");
        _ = rebuilt.Ingress.Should().Contain(rule => rule.Hostname == "created.example.com" && rule.Service == "http://created.default.svc.cluster.local:8080");
        _ = rebuilt.Ingress.Should().ContainSingle(rule => string.IsNullOrWhiteSpace(rule.Hostname) && rule.Service == "http_status:404");
    }

    [Fact]
    public void BuildShouldEmitOriginServerNameForHttpsManagedIngressRoutes()
    {
        var currentConfiguration = new TunnelConfiguration
        {
            Ingress =
            [
                new TunnelIngressRule
                {
                    Service = "http_status:404",
                },
            ],
        };

        var createdManagedRoute = PublicHostnameRoute.Create(
            "whoami.example.com",
            new Uri("https://traefik-cloudflare-tunnel.traefik-cloudflare-tunnel.svc.cluster.local:443"),
            RouteProtocol.Https,
            OwnershipTag,
            "whoami.example.com");

        var plan = new RoutePlan(
            ToCreate: [createdManagedRoute],
            ToUpdate: [],
            ToDelete: [],
            Conflicts: []);

        var rebuilt = CloudflareRouteConfigurationBuilder.Build(
            currentConfiguration,
            plan,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            OwnershipTag);

        var route = rebuilt.Ingress.Single(rule => rule.Hostname == "whoami.example.com");
        _ = route.Service.Should().Be("https://traefik-cloudflare-tunnel.traefik-cloudflare-tunnel.svc.cluster.local");
        Assert.NotNull(route.OriginRequest);
        _ = route.OriginRequest.OriginServerName.Should().Be("whoami.example.com");
    }

    [Fact]
    public void BuildShouldNotEmitOriginServerNameForHttpRoutes()
    {
        var currentConfiguration = new TunnelConfiguration
        {
            Ingress =
            [
                new TunnelIngressRule
                {
                    Service = "http_status:404",
                },
            ],
        };

        var createdManagedRoute = PublicHostnameRoute.Create(
            "whoami.example.com",
            new Uri("http://traefik-cloudflare-tunnel.traefik-cloudflare-tunnel.svc.cluster.local:80"),
            RouteProtocol.Http,
            OwnershipTag);

        var plan = new RoutePlan(
            ToCreate: [createdManagedRoute],
            ToUpdate: [],
            ToDelete: [],
            Conflicts: []);

        var rebuilt = CloudflareRouteConfigurationBuilder.Build(
            currentConfiguration,
            plan,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            OwnershipTag);

        var route = rebuilt.Ingress.Single(rule => rule.Hostname == "whoami.example.com");
        _ = route.Service.Should().Be("http://traefik-cloudflare-tunnel.traefik-cloudflare-tunnel.svc.cluster.local");
        _ = route.OriginRequest.Should().BeNull();
    }

    [Fact]
    public void BuildShouldNotDuplicatePlannedCreateWhenCurrentConfigurationAlreadyContainsHostname()
    {
        var currentConfiguration = new TunnelConfiguration
        {
            Ingress =
            [
                new TunnelIngressRule
                {
                    Hostname = "whoami.example.com",
                    Service = "http://stale.default.svc.cluster.local:8080",
                },
                new TunnelIngressRule
                {
                    Service = "http_status:404",
                },
            ],
        };

        var createdManagedRoute = PublicHostnameRoute.Create(
            "whoami.example.com",
            new Uri("https://traefik-cloudflare-tunnel.traefik-cloudflare-tunnel.svc.cluster.local:443"),
            RouteProtocol.Https,
            OwnershipTag,
            "whoami.example.com");

        var plan = new RoutePlan(
            ToCreate: [createdManagedRoute],
            ToUpdate: [],
            ToDelete: [],
            Conflicts: []);

        var rebuilt = CloudflareRouteConfigurationBuilder.Build(
            currentConfiguration,
            plan,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            OwnershipTag);

        _ = rebuilt.Ingress.Count(rule => rule.Hostname == "whoami.example.com").Should().Be(1);

        var route = rebuilt.Ingress.Single(rule => rule.Hostname == "whoami.example.com");
        _ = route.Service.Should().Be("https://traefik-cloudflare-tunnel.traefik-cloudflare-tunnel.svc.cluster.local");
        Assert.NotNull(route.OriginRequest);
        _ = route.OriginRequest.OriginServerName.Should().Be("whoami.example.com");
    }
}
