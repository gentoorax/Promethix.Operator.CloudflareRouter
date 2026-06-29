using FluentAssertions;
using Microsoft.Extensions.Options;
using Promethix.CloudflareTunnelOperator.Routing.Integrations.Kubernetes;

namespace Promethix.CloudflareTunnelOperator.Routing.Tests;

public sealed class ManagedTunnelPublicHostnameValidatorTests
{
    [Fact]
    public async Task ValidateAsyncAllowsHostnameWithinOperatorSuffixes()
    {
        var validator = CreateValidator(options =>
        {
            options.AllowedHostnameSuffixes = "apps.example.com, internal.example.com";
        });

        var resource = CreateIngressResource("whoami.apps.example.com");

        var act = () => validator.ValidateAsync(resource, CancellationToken.None);

        _ = await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ValidateAsyncRejectsHostnameOutsideOperatorSuffixes()
    {
        var validator = CreateValidator(options =>
        {
            options.AllowedHostnameSuffixes = "apps.example.com";
        });

        var resource = CreateIngressResource("whoami.other.example.net");

        var act = () => validator.ValidateAsync(resource, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        _ = exception.Which.Message.Should().Be(
            "Hostname 'whoami.other.example.net' is not permitted by operator policy. Allowed suffixes: apps.example.com.");
    }

    private static ManagedTunnelPublicHostnameValidator CreateValidator(Action<KubernetesOperatorOptions>? configure = null)
    {
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

        configure?.Invoke(options);

        return new ManagedTunnelPublicHostnameValidator(
            Options.Create(options),
            new AcceptingHostnameOwnershipValidator(),
            new AcceptingIngressTargetValidator());
    }

    private static TunnelPublicHostnameCustomResource CreateIngressResource(string hostname)
    {
        return new TunnelPublicHostnameCustomResource
        {
            Metadata = new k8s.Models.V1ObjectMeta
            {
                Name = "whoami-public",
                NamespaceProperty = "tenant-a",
            },
            Spec = new TunnelPublicHostnameSpec
            {
                ClassName = "public",
                Hostname = hostname,
                TunnelRef = new TunnelReferenceSpec
                {
                    Name = "delta-public",
                },
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
    }

    private sealed class AcceptingHostnameOwnershipValidator : IHostnameOwnershipValidator
    {
        public Task ValidateAsync(TunnelPublicHostnameCustomResource resource, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
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
