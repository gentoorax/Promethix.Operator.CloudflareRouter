using Microsoft.Extensions.Options;
using Promethix.CloudflareTunnelOperator.Routing.Domain;

namespace Promethix.CloudflareTunnelOperator.Routing.Integrations.Kubernetes;

public sealed class ManagedTunnelPublicHostnameValidator(
    IOptions<KubernetesOperatorOptions> options,
    IHostnameOwnershipValidator hostnameOwnershipValidator,
    IIngressTargetValidator ingressTargetValidator) : IManagedTunnelPublicHostnameValidator
{
    public bool IsManaged(TunnelPublicHostnameCustomResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return resource.Spec.Enabled
            && string.Equals(resource.Spec.ClassName, options.Value.ManagedClassName, StringComparison.Ordinal)
            && string.Equals(resource.Spec.TunnelRef.Name, options.Value.ManagedTunnelName, StringComparison.Ordinal);
    }

    public async Task ValidateAsync(TunnelPublicHostnameCustomResource resource, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resource);

        ValidateAllowedHostnameSuffixes(resource);
        await hostnameOwnershipValidator.ValidateAsync(resource, cancellationToken).ConfigureAwait(false);

        var target = resource.Spec.Target;
        if (target is null || string.IsNullOrWhiteSpace(target.Mode))
        {
            return;
        }

        if (string.Equals(target.Mode.Trim(), "ingress", StringComparison.OrdinalIgnoreCase))
        {
            await ValidateIngressAsync(resource, target, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (string.Equals(target.Mode.Trim(), "direct", StringComparison.OrdinalIgnoreCase))
        {
            ValidateDirect(resource, target, options.Value.AllowCrossNamespaceDirectTargets);
            return;
        }

        throw new InvalidOperationException($"Unsupported target mode '{target.Mode}'.");
    }

    private void ValidateAllowedHostnameSuffixes(TunnelPublicHostnameCustomResource resource)
    {
        var configuredSuffixes = options.Value.AllowedHostnameSuffixes
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        if (configuredSuffixes.Length == 0)
        {
            return;
        }

        var hostname = resource.Spec.Hostname.Trim().TrimEnd('.');
        var allowed = configuredSuffixes.Any(suffix => HostnameMatchesSuffix(hostname, suffix));

        if (!allowed)
        {
            throw new InvalidOperationException(
                $"Hostname '{resource.Spec.Hostname}' is not permitted by operator policy. Allowed suffixes: {string.Join(", ", configuredSuffixes)}.");
        }
    }

    private async Task ValidateIngressAsync(
        TunnelPublicHostnameCustomResource resource,
        TunnelTargetSpec target,
        CancellationToken cancellationToken)
    {
        var ingress = target.Ingress
            ?? throw new InvalidOperationException("spec.target.ingress is required when spec.target.mode=ingress.");

        if (string.IsNullOrWhiteSpace(ingress.ClassName))
        {
            throw new InvalidOperationException("spec.target.ingress.className is required when spec.target.mode=ingress.");
        }

        if (!string.Equals(ingress.ClassName, options.Value.ManagedIngressClassName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Ingress target class '{ingress.ClassName}' does not match managed ingress class '{options.Value.ManagedIngressClassName}'.");
        }

        if (ingress.Service is not null
            && !options.Value.AllowIngressServiceOverride
            && !MatchesConfiguredIngressTarget(ingress.Service, options.Value.IngressTargetUrl))
        {
            throw new InvalidOperationException("spec.target.ingress.service is not allowed by this operator. Use the configured ingress target or enable KubernetesOperator:AllowIngressServiceOverride.");
        }

        await ingressTargetValidator.ValidateAsync(resource, ingress, cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateDirect(
        TunnelPublicHostnameCustomResource resource,
        TunnelTargetSpec target,
        bool allowCrossNamespaceDirectTargets)
    {
        var direct = target.Direct
            ?? throw new InvalidOperationException("spec.target.direct is required when spec.target.mode=direct.");

        if (direct.Service is not null)
        {
            EnsureServiceTargetNamespaceAllowed(
                resource,
                direct.Service,
                "spec.target.direct.service",
                allowCrossNamespaceDirectTargets);
            return;
        }

        if (direct.Url is null)
        {
            throw new InvalidOperationException("spec.target.direct.url is required when spec.target.direct.service is not supplied.");
        }

        _ = ParseProtocol(direct.Protocol);
    }

    private static RouteProtocol ParseProtocol(string protocol)
    {
        return string.Equals(protocol.Trim(), "https", StringComparison.OrdinalIgnoreCase)
            ? RouteProtocol.Https
            : string.Equals(protocol.Trim(), "http", StringComparison.OrdinalIgnoreCase)
                ? RouteProtocol.Http
                : throw new InvalidOperationException($"Unsupported origin protocol '{protocol}'.");
    }

    private static void EnsureServiceTargetNamespaceAllowed(
        TunnelPublicHostnameCustomResource resource,
        TunnelIngressServiceTargetSpec service,
        string fieldPath,
        bool allowCrossNamespace)
    {
        if (allowCrossNamespace)
        {
            return;
        }

        var resourceNamespace = resource.Metadata.NamespaceProperty ?? string.Empty;

        if (!string.Equals(service.Namespace, resourceNamespace, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{fieldPath}.namespace must match the TunnelPublicHostname namespace unless cross-namespace direct targets are explicitly enabled.");
        }
    }

    private static bool HostnameMatchesSuffix(string hostname, string suffix)
    {
        var normalizedHostname = hostname.Trim().TrimEnd('.').ToUpperInvariant();
        var normalizedSuffix = suffix.Trim().TrimStart('.').ToUpperInvariant();

        return string.Equals(normalizedHostname, normalizedSuffix, StringComparison.OrdinalIgnoreCase)
               || normalizedHostname.EndsWith($".{normalizedSuffix}", StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesConfiguredIngressTarget(
        TunnelIngressServiceTargetSpec service,
        Uri configuredIngressTargetUrl)
    {
        var explicitTargetUrl = ServiceTargetUrlResolver.Resolve(service, "spec.target.ingress.service");

        return Uri.Compare(
                   explicitTargetUrl,
                   configuredIngressTargetUrl,
                   UriComponents.SchemeAndServer | UriComponents.Port,
                   UriFormat.SafeUnescaped,
                   StringComparison.OrdinalIgnoreCase)
               == 0;
    }
}
