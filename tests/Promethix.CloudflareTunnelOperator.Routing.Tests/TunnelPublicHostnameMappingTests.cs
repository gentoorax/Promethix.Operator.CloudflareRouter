using FluentAssertions;
using k8s;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Promethix.CloudflareTunnelOperator.Hosting.Options;
using Promethix.CloudflareTunnelOperator.Routing.Application;
using Promethix.CloudflareTunnelOperator.Routing.Integrations.Kubernetes;

namespace Promethix.CloudflareTunnelOperator.Routing.Tests;

public sealed class TunnelPublicHostnameMappingTests
{
    [Fact]
    public void SpecShouldDefaultToIngressFriendlyShape()
    {
        var resource = new TunnelPublicHostnameCustomResource
        {
            Spec = new TunnelPublicHostnameSpec
            {
                ClassName = "public",
                Hostname = "app.promethix.net",
                TunnelRef = new TunnelReferenceSpec { Name = "delta-public" },
                Target = new TunnelTargetSpec
                {
                    Mode = "ingress",
                    Ingress = new TunnelIngressTargetSpec
                    {
                        ClassName = "traefik-cloudflare-tunnel",
                    },
                },
            },
        };

        resource.Spec.Enabled.Should().BeTrue();
        resource.Spec.Cloudflare.Proxied.Should().BeTrue();
        resource.Spec.TunnelRef.Name.Should().Be("delta-public");
        resource.Spec.Target.Mode.Should().Be("ingress");
        resource.Spec.Target.Ingress!.ClassName.Should().Be("traefik-cloudflare-tunnel");
    }

    [Fact]
    public void KubernetesOptionsShouldRequireIngressTargetSettings()
    {
        var validator = new KubernetesOperatorOptionsValidator();
        var options = new KubernetesOperatorOptions
        {
            ManagedClassName = "public",
            ManagedTunnelName = "delta-public",
            ManagedIngressClassName = "traefik-cloudflare-tunnel",
            IngressTargetUrl = new Uri("https://traefik-cloudflare-tunnel.edge-system.svc.cluster.local"),
            ManagedFinalizerName = "edge.promethix.net/tunnelpublichostname-protection",
            OwnershipConfigMapNamespace = "edge-system",
            OwnershipConfigMapName = "promethix-cloudflare-tunnel-operator-ownership",
        };

        var result = validator.Validate(Options.DefaultName, options);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task IngressTargetCanOverrideDefaultService()
    {
        var client = new KubernetesTunnelPublicHostnameClient(
            kubernetes: null!,
            Options.Create(new KubernetesOperatorOptions
            {
                ManagedClassName = "public",
                ManagedTunnelName = "delta-public",
                ManagedIngressClassName = "traefik-cloudflare-tunnel",
                IngressTargetUrl = new Uri("https://default.edge-system.svc.cluster.local"),
                ManagedFinalizerName = "edge.promethix.net/tunnelpublichostname-protection",
                OwnershipConfigMapNamespace = "edge-system",
                OwnershipConfigMapName = "promethix-cloudflare-tunnel-operator-ownership",
            }),
            Options.Create(new RoutingOperatorOptions
            {
                OwnershipTag = "promethix-cloudflare-tunnel-operator",
            }),
            new AcceptingIngressTargetValidator(),
            NullLogger<KubernetesTunnelPublicHostnameClient>.Instance);

        var resource = new TunnelPublicHostnameCustomResource
        {
            Metadata = new k8s.Models.V1ObjectMeta
            {
                Name = "whoami-public",
                NamespaceProperty = "demo",
            },
            Spec = new TunnelPublicHostnameSpec
            {
                ClassName = "public",
                Hostname = "whoami.delta.promethix.net",
                TunnelRef = new TunnelReferenceSpec { Name = "delta-public" },
                Target = new TunnelTargetSpec
                {
                    Mode = "ingress",
                    Ingress = new TunnelIngressTargetSpec
                    {
                        ClassName = "traefik-cloudflare-tunnel",
                        Service = new TunnelIngressServiceTargetSpec
                        {
                            Name = "traefik-cloudflare-tunnel",
                            Namespace = "edge-system",
                            Port = 443,
                            Scheme = "https",
                        },
                    },
                },
            },
        };

        var (managedIntent, invalidIntent) = await client.TryBuildIntentAsync(resource, CancellationToken.None);

        invalidIntent.Should().BeNull();
        managedIntent.Should().NotBeNull();
        managedIntent!.Route.OriginService.Should().Be(new Uri("https://traefik-cloudflare-tunnel.edge-system.svc.cluster.local:443"));
    }

    private sealed class AcceptingIngressTargetValidator : IIngressTargetValidator
    {
        public Task ValidateAsync(
            TunnelPublicHostnameCustomResource resource,
            TunnelIngressTargetSpec ingress,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
