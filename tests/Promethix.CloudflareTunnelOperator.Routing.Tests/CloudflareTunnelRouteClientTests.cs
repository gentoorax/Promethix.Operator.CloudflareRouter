using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Promethix.CloudflareTunnelOperator.Routing.Application;
using Promethix.CloudflareTunnelOperator.Routing.Integrations.Cloudflare;
using System.Net;
using System.Text;

namespace Promethix.CloudflareTunnelOperator.Routing.Tests;

public sealed class CloudflareTunnelRouteClientTests
{
    [Fact]
    public async Task GetRoutesShouldPruneOwnershipEntriesForMissingHostnames()
    {
        using var handler = new RecordingHttpMessageHandler(
            JsonResponse(
                """
                {
                  "success": true,
                  "result": {
                    "config": {
                      "ingress": [
                        {
                          "hostname": "active.example.com",
                          "service": "https://active.demo.svc.cluster.local:8443"
                        }
                      ]
                    }
                  }
                }
                """));
        using var httpClient = CreateHttpClient(handler);
        var ownershipStore = new RecordingOwnershipStore(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["active.example.com"] = "promethix-cloudflare-tunnel-operator",
                ["stale.example.com"] = "promethix-cloudflare-tunnel-operator",
            });
        var client = CreateClient(httpClient, ownershipStore);

        var routes = await client.GetRoutesAsync(CancellationToken.None);

        _ = routes.Should().ContainSingle(route => route.Hostname == "active.example.com");
        _ = ownershipStore.SavedOwnership.Should().NotBeNull();
        _ = ownershipStore.SavedOwnership!.Keys.Should().BeEquivalentTo(["active.example.com"]);
    }

    private static HttpClient CreateHttpClient(HttpMessageHandler handler)
    {
        return new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://api.cloudflare.com/client/v4/"),
        };
    }

    private static CloudflareTunnelRouteClient CreateClient(HttpClient httpClient, RecordingOwnershipStore ownershipStore)
    {
        return new CloudflareTunnelRouteClient(
            httpClient,
            ownershipStore,
            Options.Create(new CloudflareTunnelOptions
            {
                AccountId = "account-id",
                TunnelId = "tunnel-id",
                ApiToken = "token",
                OwnershipTag = "promethix-cloudflare-tunnel-operator",
            }),
            NullLogger<CloudflareTunnelRouteClient>.Instance);
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    private sealed class RecordingOwnershipStore(IReadOnlyDictionary<string, string> initialOwnership) : IManagedRouteOwnershipStore
    {
        public IReadOnlyDictionary<string, string>? SavedOwnership { get; private set; }

        public Task<IReadOnlyDictionary<string, string>> GetOwnershipAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(initialOwnership);
        }

        public Task SaveOwnershipAsync(IReadOnlyDictionary<string, string> ownershipByHostname, CancellationToken cancellationToken)
        {
            SavedOwnership = new Dictionary<string, string>(ownershipByHostname, StringComparer.OrdinalIgnoreCase);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingHttpMessageHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> responses = new(responses);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(this.responses.Count > 0
                ? this.responses.Dequeue()
                : new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
