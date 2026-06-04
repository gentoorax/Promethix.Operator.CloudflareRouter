using FluentAssertions;
using Promethix.CloudflareTunnelOperator.Routing.Application;
using Promethix.CloudflareTunnelOperator.Routing.Domain;
using System.Globalization;

namespace Promethix.CloudflareTunnelOperator.Routing.Tests;

public sealed class RouteReconcilerTests
{
    [Fact]
    public async Task ReconcileAsyncShouldWrapFailuresAfterIntentLoad()
    {
        var intent = new RouteIntentDocument(
            "test",
            [
                new ManagedRouteIntent(
                    "app-promethix-net",
                    "demo",
                    1,
                    PublicHostnameRoute.Create(
                        "app.promethix.net",
                        new Uri("https://app.demo.svc.cluster.local:8443"),
                        RouteProtocol.Https,
                        "promethix-cloudflare-tunnel-operator")),
            ],
            []);

        var reconciler = new RouteReconciler(
            new StubIntentSource(intent),
            new ThrowingRouteClient(new InvalidOperationException("Cloudflare read failed.")),
            new StubClock(DateTimeOffset.Parse("2026-06-04T10:00:00Z", CultureInfo.InvariantCulture)));

        var act = () => reconciler.ReconcileAsync(
            new RoutingOperatorOptions
            {
                ApplyChanges = true,
                OwnershipTag = "promethix-cloudflare-tunnel-operator",
            },
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ReconciliationFailedException>();
        exception.Which.Intent.Should().Be(intent);
        exception.Which.InnerException.Should().BeOfType<InvalidOperationException>();
    }

    private sealed class StubIntentSource(RouteIntentDocument document) : IClusterRouteIntentSource
    {
        public Task<RouteIntentDocument> GetDesiredRoutesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(document);
        }
    }

    private sealed class ThrowingRouteClient(Exception exception) : ICloudflareTunnelRouteClient
    {
        public Task<IReadOnlyCollection<PublicHostnameRoute>> GetRoutesAsync(CancellationToken cancellationToken)
        {
            throw exception;
        }

        public Task ApplyAsync(RoutePlan plan, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class StubClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }
}
