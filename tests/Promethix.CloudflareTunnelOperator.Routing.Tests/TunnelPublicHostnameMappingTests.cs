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
                Hostname = "app.example.com",
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

        _ = resource.Spec.Enabled.Should().BeTrue();
        _ = resource.Spec.Cloudflare.Proxied.Should().BeTrue();
        _ = resource.Spec.TunnelRef.Name.Should().Be("delta-public");
        _ = resource.Spec.Target.Mode.Should().Be("ingress");
        Assert.NotNull(resource.Spec.Target.Ingress);
        _ = resource.Spec.Target.Ingress.ClassName.Should().Be("traefik-cloudflare-tunnel");
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

        _ = result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task IngressTargetCanOverrideDefaultService()
    {
        var client = CreateClient(options =>
        {
            options.AllowIngressServiceOverride = true;
            options.IngressServiceOverrideMode = nameof(IngressServiceOverrideMode.Any);
        });

        var resource = new TunnelPublicHostnameCustomResource
        {
            Metadata = new k8s.Models.V1ObjectMeta
            {
                Name = "whoami-public",
                NamespaceProperty = "tenant-a",
            },
            Spec = new TunnelPublicHostnameSpec
            {
                ClassName = "public",
                Hostname = "whoami.apps.example.com",
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

        _ = invalidIntent.Should().BeNull();
        _ = managedIntent.Should().NotBeNull();
        _ = managedIntent.Route.OriginService.Should().Be(new Uri("https://traefik-cloudflare-tunnel.edge-system.svc.cluster.local:443"));
        _ = managedIntent.Route.OriginServerName.Should().Be("whoami.apps.example.com");
    }

    [Fact]
    public async Task IngressServiceOverrideIsRejectedByDefault()
    {
        var client = CreateClient();
        var resource = CreateIngressOverrideResource();

        var (managedIntent, invalidIntent) = await client.TryBuildIntentAsync(resource, CancellationToken.None);

        _ = managedIntent.Should().BeNull();
        _ = invalidIntent.Should().NotBeNull();
        _ = invalidIntent.Reason.Should().Be("spec.target.ingress.service is not allowed by this operator. Allowed modes are Disabled, ConfiguredTargetOnly, or Any.");
    }

    [Fact]
    public async Task IngressServiceMatchingConfiguredTargetIsAllowedWhenModeIsConfiguredTargetOnly()
    {
        var client = CreateClient(options =>
        {
            options.IngressTargetUrl = new Uri("https://traefik-cloudflare-tunnel.edge-system.svc.cluster.local:443");
            options.IngressServiceOverrideMode = nameof(IngressServiceOverrideMode.ConfiguredTargetOnly);
        });

        var resource = CreateIngressOverrideResource();
        var (managedIntent, invalidIntent) = await client.TryBuildIntentAsync(resource, CancellationToken.None);

        _ = invalidIntent.Should().BeNull();
        _ = managedIntent.Should().NotBeNull();
        Assert.NotNull(managedIntent);
        _ = managedIntent.Route.OriginService.Should().Be(new Uri("https://traefik-cloudflare-tunnel.edge-system.svc.cluster.local:443"));
    }

    [Fact]
    public async Task ArbitraryIngressServiceOverrideIsRejectedWhenModeIsConfiguredTargetOnly()
    {
        var client = CreateClient(options =>
        {
            options.IngressTargetUrl = new Uri("https://traefik-cloudflare-tunnel.edge-system.svc.cluster.local:443");
            options.IngressServiceOverrideMode = nameof(IngressServiceOverrideMode.ConfiguredTargetOnly);
        });

        var resource = CreateIngressOverrideResource();
        resource.Spec.Target!.Ingress!.Service!.Name = "other-ingress";

        var (managedIntent, invalidIntent) = await client.TryBuildIntentAsync(resource, CancellationToken.None);

        _ = managedIntent.Should().BeNull();
        _ = invalidIntent.Should().NotBeNull();
        _ = invalidIntent.Reason.Should().Be("spec.target.ingress.service is not allowed by this operator. Allowed modes are Disabled, ConfiguredTargetOnly, or Any.");
    }

    [Fact]
    public async Task HostnameClaimIsRejectedWhenNamespacePolicyDeniesIt()
    {
        var client = CreateClient(
            hostnameOwnershipValidator: new RejectingHostnameOwnershipValidator(
                "Hostname 'whoami.apps.example.com' is not permitted for namespace 'tenant-a'. Allowed suffixes: apps.example.com."));

        var resource = CreateIngressManagedResource();

        var (managedIntent, invalidIntent) = await client.TryBuildIntentAsync(resource, CancellationToken.None);

        _ = managedIntent.Should().BeNull();
        _ = invalidIntent.Should().NotBeNull();
        _ = invalidIntent.Reason.Should().Be("Hostname 'whoami.apps.example.com' is not permitted for namespace 'tenant-a'. Allowed suffixes: apps.example.com.");
    }

    [Fact]
    public async Task HostnameClaimIsRejectedWhenOperatorSuffixPolicyDeniesIt()
    {
        var client = CreateClient(options =>
        {
            options.AllowedHostnameSuffixes = "internal.example.com";
        });

        var resource = CreateIngressManagedResource();

        var (managedIntent, invalidIntent) = await client.TryBuildIntentAsync(resource, CancellationToken.None);

        _ = managedIntent.Should().BeNull();
        _ = invalidIntent.Should().NotBeNull();
        _ = invalidIntent.Reason.Should().Be(
            "Hostname 'whoami.apps.example.com' is not permitted by operator policy. Allowed suffixes: internal.example.com.");
    }

    [Fact]
    public async Task DirectTargetCanUseServiceReference()
    {
        var client = CreateClient();
        var resource = new TunnelPublicHostnameCustomResource
        {
            Metadata = new k8s.Models.V1ObjectMeta
            {
                Name = "api-direct",
                NamespaceProperty = "tenant-a",
            },
            Spec = new TunnelPublicHostnameSpec
            {
                ClassName = "public",
                Hostname = "api.apps.example.com",
                TunnelRef = new TunnelReferenceSpec { Name = "delta-public" },
                Target = new TunnelTargetSpec
                {
                    Mode = "direct",
                    Direct = new TunnelDirectTargetSpec
                    {
                        Service = new TunnelIngressServiceTargetSpec
                        {
                            Name = "api",
                            Namespace = "tenant-a",
                            Port = 8443,
                            Scheme = "https",
                        },
                    },
                },
            },
        };

        var (managedIntent, invalidIntent) = await client.TryBuildIntentAsync(resource, CancellationToken.None);

        _ = invalidIntent.Should().BeNull();
        _ = managedIntent.Should().NotBeNull();
        Assert.NotNull(managedIntent);
        _ = managedIntent.Route.OriginService.Should().Be(new Uri("https://api.tenant-a.svc.cluster.local:8443"));
    }

    [Fact]
    public async Task DirectServiceTargetCannotCrossNamespaceByDefault()
    {
        var client = CreateClient();
        var resource = new TunnelPublicHostnameCustomResource
        {
            Metadata = new k8s.Models.V1ObjectMeta
            {
                Name = "api-direct",
                NamespaceProperty = "tenant-a",
            },
            Spec = new TunnelPublicHostnameSpec
            {
                ClassName = "public",
                Hostname = "api.apps.example.com",
                TunnelRef = new TunnelReferenceSpec { Name = "delta-public" },
                Target = new TunnelTargetSpec
                {
                    Mode = "direct",
                    Direct = new TunnelDirectTargetSpec
                    {
                        Service = new TunnelIngressServiceTargetSpec
                        {
                            Name = "api",
                            Namespace = "other",
                            Port = 8443,
                            Scheme = "https",
                        },
                    },
                },
            },
        };

        var (managedIntent, invalidIntent) = await client.TryBuildIntentAsync(resource, CancellationToken.None);

        _ = managedIntent.Should().BeNull();
        _ = invalidIntent.Should().NotBeNull();
        _ = invalidIntent.Reason.Should().Be("spec.target.direct.service.namespace must match the TunnelPublicHostname namespace unless cross-namespace direct targets are explicitly enabled.");
    }

    [Fact]
    public async Task DirectServiceTargetCanCrossNamespaceWhenExplicitlyAllowed()
    {
        var client = CreateClient(options => options.AllowCrossNamespaceDirectTargets = true);
        var resource = new TunnelPublicHostnameCustomResource
        {
            Metadata = new k8s.Models.V1ObjectMeta
            {
                Name = "api-direct",
                NamespaceProperty = "tenant-a",
            },
            Spec = new TunnelPublicHostnameSpec
            {
                ClassName = "public",
                Hostname = "api.apps.example.com",
                TunnelRef = new TunnelReferenceSpec { Name = "delta-public" },
                Target = new TunnelTargetSpec
                {
                    Mode = "direct",
                    Direct = new TunnelDirectTargetSpec
                    {
                        Service = new TunnelIngressServiceTargetSpec
                        {
                            Name = "api",
                            Namespace = "other",
                            Port = 8443,
                            Scheme = "https",
                        },
                    },
                },
            },
        };

        var (managedIntent, invalidIntent) = await client.TryBuildIntentAsync(resource, CancellationToken.None);

        _ = invalidIntent.Should().BeNull();
        _ = managedIntent.Should().NotBeNull();
        Assert.NotNull(managedIntent);
        _ = managedIntent.Route.OriginService.Should().Be(new Uri("https://api.other.svc.cluster.local:8443"));
    }

    [Fact]
    public async Task DirectTargetKeepsUrlProtocolCompatibility()
    {
        var client = CreateClient();
        var resource = new TunnelPublicHostnameCustomResource
        {
            Metadata = new k8s.Models.V1ObjectMeta
            {
                Name = "api-direct",
                NamespaceProperty = "tenant-a",
            },
            Spec = new TunnelPublicHostnameSpec
            {
                ClassName = "public",
                Hostname = "api.apps.example.com",
                TunnelRef = new TunnelReferenceSpec { Name = "delta-public" },
                Target = new TunnelTargetSpec
                {
                    Mode = "direct",
                    Direct = new TunnelDirectTargetSpec
                    {
                        Protocol = "https",
                        Url = new Uri("https://api.tenant-a.svc.cluster.local:8443"),
                    },
                },
            },
        };

        var (managedIntent, invalidIntent) = await client.TryBuildIntentAsync(resource, CancellationToken.None);

        _ = invalidIntent.Should().BeNull();
        _ = managedIntent.Should().NotBeNull();
        Assert.NotNull(managedIntent);
        _ = managedIntent.Route.OriginService.Should().Be(new Uri("https://api.tenant-a.svc.cluster.local:8443"));
    }

    [Fact]
    public async Task RateLimitSecurityPolicyCanBeMappedFromPathPrefix()
    {
        var client = CreateClient(securityPoliciesEnabled: true);
        var resource = new TunnelPublicHostnameCustomResource
        {
            Metadata = new k8s.Models.V1ObjectMeta
            {
                Name = "api-public",
                NamespaceProperty = "demo",
            },
            Spec = new TunnelPublicHostnameSpec
            {
                ClassName = "public",
                Hostname = "api.delta.promethix.net",
                TunnelRef = new TunnelReferenceSpec { Name = "delta-public" },
                Target = new TunnelTargetSpec
                {
                    Mode = "direct",
                    Direct = new TunnelDirectTargetSpec
                    {
                        Service = new TunnelIngressServiceTargetSpec
                        {
                            Name = "api",
                            Namespace = "demo",
                            Port = 8080,
                            Scheme = "http",
                        },
                    },
                },
                Cloudflare = new CloudflareRouteSpec
                {
                    Security = new CloudflareSecuritySpec
                    {
                        RateLimit = new CloudflareRateLimitSpec
                        {
                            Enabled = true,
                            Rules =
                            [
                                new CloudflareRateLimitRuleSpec
                                {
                                    Name = "api-v1",
                                    PathPrefix = "/v1/",
                                    RequestsPerPeriod = 60,
                                    PeriodSeconds = 60,
                                    Action = "managed_challenge",
                                },
                            ],
                        },
                    },
                },
            },
        };

        var (managedIntent, invalidIntent) = await client.TryBuildIntentAsync(resource, CancellationToken.None);

        _ = invalidIntent.Should().BeNull();
        _ = managedIntent.Should().NotBeNull();
        Assert.NotNull(managedIntent);
        _ = managedIntent.SecurityPolicy.Should().NotBeNull();
        var securityPolicy = managedIntent.SecurityPolicy;
        Assert.NotNull(securityPolicy);
        _ = securityPolicy.Hostname.Should().Be("api.delta.promethix.net");
        _ = securityPolicy.RateLimitRules.Should().ContainSingle();

        var rule = securityPolicy.RateLimitRules.Single();
        _ = rule.Name.Should().Be("api-v1");
        _ = rule.Expression.Should().Be("(http.host eq \"api.delta.promethix.net\" and starts_with(http.request.uri.path, \"/v1/\"))");
        _ = rule.RequestsPerPeriod.Should().Be(60);
        _ = rule.PeriodSeconds.Should().Be(60);
        _ = rule.Action.Should().Be("managed_challenge");
    }

    [Fact]
    public async Task EnabledRateLimitRequiresRules()
    {
        var client = CreateClient(securityPoliciesEnabled: true);
        var resource = new TunnelPublicHostnameCustomResource
        {
            Metadata = new k8s.Models.V1ObjectMeta
            {
                Name = "api-public",
                NamespaceProperty = "demo",
            },
            Spec = new TunnelPublicHostnameSpec
            {
                ClassName = "public",
                Hostname = "api.delta.promethix.net",
                TunnelRef = new TunnelReferenceSpec { Name = "delta-public" },
                Target = new TunnelTargetSpec
                {
                    Mode = "direct",
                    Direct = new TunnelDirectTargetSpec
                    {
                        Service = new TunnelIngressServiceTargetSpec
                        {
                            Name = "api",
                            Namespace = "demo",
                            Port = 8080,
                            Scheme = "http",
                        },
                    },
                },
                Cloudflare = new CloudflareRouteSpec
                {
                    Security = new CloudflareSecuritySpec
                    {
                        RateLimit = new CloudflareRateLimitSpec
                        {
                            Enabled = true,
                        },
                    },
                },
            },
        };

        var (managedIntent, invalidIntent) = await client.TryBuildIntentAsync(resource, CancellationToken.None);

        _ = managedIntent.Should().BeNull();
        _ = invalidIntent.Should().NotBeNull();
        Assert.NotNull(invalidIntent);
        _ = invalidIntent.Reason.Should().Be("spec.cloudflare.security.rateLimit.rules must contain at least one rule when rate limiting is enabled.");
    }

    [Fact]
    public async Task EnterpriseOnlyLogRateLimitActionIsRejectedByDefault()
    {
        var client = CreateClient(securityPoliciesEnabled: true);
        var resource = CreateRateLimitedResource("log");

        var (managedIntent, invalidIntent) = await client.TryBuildIntentAsync(resource, CancellationToken.None);

        _ = managedIntent.Should().BeNull();
        _ = invalidIntent.Should().NotBeNull();
        Assert.NotNull(invalidIntent);
        _ = invalidIntent.Reason.Should().Be(
            "spec.cloudflare.security.rateLimit.rules[].action must be one of block, challenge, or managed_challenge. The log action is Enterprise-only and must be explicitly enabled by the operator administrator.");
    }

    [Fact]
    public async Task EnterpriseOnlyLogRateLimitActionCanBeEnabledExplicitly()
    {
        var client = CreateClient(
            securityPoliciesEnabled: true,
            configureRouting: options => options.AllowEnterpriseOnlyRateLimitActions = true);
        var resource = CreateRateLimitedResource("log");

        var (managedIntent, invalidIntent) = await client.TryBuildIntentAsync(resource, CancellationToken.None);

        _ = invalidIntent.Should().BeNull();
        _ = managedIntent.Should().NotBeNull();
        Assert.NotNull(managedIntent);
        Assert.NotNull(managedIntent.SecurityPolicy);
        _ = managedIntent.SecurityPolicy.RateLimitRules.Should().ContainSingle();
        _ = managedIntent.SecurityPolicy.RateLimitRules.Single().Action.Should().Be("log");
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

    private sealed class AcceptingHostnameOwnershipValidator : IHostnameOwnershipValidator
    {
        public Task ValidateAsync(TunnelPublicHostnameCustomResource resource, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class RejectingHostnameOwnershipValidator(string reason) : IHostnameOwnershipValidator
    {
        public Task ValidateAsync(TunnelPublicHostnameCustomResource resource, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException(reason);
        }
    }

    private static TunnelPublicHostnameCustomResource CreateIngressOverrideResource()
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
                Hostname = "whoami.apps.example.com",
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
    }

    private static TunnelPublicHostnameCustomResource CreateIngressManagedResource()
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
                Hostname = "whoami.apps.example.com",
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
    }

    private static TunnelPublicHostnameCustomResource CreateRateLimitedResource(string action)
    {
        return new TunnelPublicHostnameCustomResource
        {
            Metadata = new k8s.Models.V1ObjectMeta
            {
                Name = "api-public",
                NamespaceProperty = "demo",
            },
            Spec = new TunnelPublicHostnameSpec
            {
                ClassName = "public",
                Hostname = "api.delta.promethix.net",
                TunnelRef = new TunnelReferenceSpec { Name = "delta-public" },
                Target = new TunnelTargetSpec
                {
                    Mode = "direct",
                    Direct = new TunnelDirectTargetSpec
                    {
                        Service = new TunnelIngressServiceTargetSpec
                        {
                            Name = "api",
                            Namespace = "demo",
                            Port = 8080,
                            Scheme = "http",
                        },
                    },
                },
                Cloudflare = new CloudflareRouteSpec
                {
                    Security = new CloudflareSecuritySpec
                    {
                        RateLimit = new CloudflareRateLimitSpec
                        {
                            Enabled = true,
                            Rules =
                            [
                                new CloudflareRateLimitRuleSpec
                                {
                                    Name = "api-v1",
                                    PathPrefix = "/v1/",
                                    RequestsPerPeriod = 60,
                                    PeriodSeconds = 60,
                                    Action = action,
                                },
                            ],
                        },
                    },
                },
            },
        };
    }

    private static KubernetesTunnelPublicHostnameClient CreateClient(
        Action<KubernetesOperatorOptions>? configure = null,
        bool securityPoliciesEnabled = false,
        Action<RoutingOperatorOptions>? configureRouting = null,
        IHostnameOwnershipValidator? hostnameOwnershipValidator = null)
    {
        var kubernetesOptions = new KubernetesOperatorOptions
        {
            ManagedClassName = "public",
            ManagedTunnelName = "delta-public",
            ManagedIngressClassName = "traefik-cloudflare-tunnel",
            IngressTargetUrl = new Uri("https://default.edge-system.svc.cluster.local"),
            ManagedFinalizerName = "edge.promethix.net/tunnelpublichostname-protection",
            OwnershipConfigMapNamespace = "edge-system",
            OwnershipConfigMapName = "promethix-cloudflare-tunnel-operator-ownership",
        };
        configure?.Invoke(kubernetesOptions);

        var routingOperatorOptions = new RoutingOperatorOptions
        {
            OwnershipTag = "promethix-cloudflare-tunnel-operator",
            SecurityPoliciesEnabled = securityPoliciesEnabled,
        };
        configureRouting?.Invoke(routingOperatorOptions);

        return new KubernetesTunnelPublicHostnameClient(
            kubernetes: null!,
            Options.Create(kubernetesOptions),
            Options.Create(routingOperatorOptions),
            new ManagedTunnelPublicHostnameValidator(
                Options.Create(kubernetesOptions),
                hostnameOwnershipValidator ?? new AcceptingHostnameOwnershipValidator(),
                new AcceptingIngressTargetValidator()),
            NullLogger<KubernetesTunnelPublicHostnameClient>.Instance);
    }
}
