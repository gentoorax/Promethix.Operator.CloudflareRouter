namespace Promethix.CloudflareTunnelOperator.Routing.Integrations.Kubernetes;

public sealed class KubernetesHostnameOwnershipValidator(
    IKubernetesNamespaceReader namespaceReader,
    Microsoft.Extensions.Options.IOptions<KubernetesOperatorOptions> options) : IHostnameOwnershipValidator
{
    public async Task ValidateAsync(
        TunnelPublicHostnameCustomResource resource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resource);

        if (!options.Value.EnforceNamespaceHostnamePolicy)
        {
            return;
        }

        var resourceNamespace = resource.Metadata.NamespaceProperty;
        if (string.IsNullOrWhiteSpace(resourceNamespace))
        {
            throw new InvalidOperationException("TunnelPublicHostname metadata.namespace is required.");
        }

        var namespaceResource = await namespaceReader.ReadAsync(resourceNamespace, cancellationToken).ConfigureAwait(false);
        var annotationName = options.Value.AllowedHostnameSuffixesAnnotation.Trim();
        var annotations = namespaceResource.Metadata?.Annotations;

        if (string.IsNullOrWhiteSpace(annotationName))
        {
            throw new InvalidOperationException("KubernetesOperator:AllowedHostnameSuffixesAnnotation is required when namespace hostname policy enforcement is enabled.");
        }

        if (annotations is null || !annotations.TryGetValue(annotationName, out var rawValue) || string.IsNullOrWhiteSpace(rawValue))
        {
            throw new InvalidOperationException(
                $"Namespace '{resourceNamespace}' is not allowed to claim hostnames because annotation '{annotationName}' is missing or empty.");
        }

        var hostname = resource.Spec.Hostname.Trim();
        var configuredSuffixes = rawValue
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        if (configuredSuffixes.Length == 0)
        {
            throw new InvalidOperationException(
                $"Namespace '{resourceNamespace}' annotation '{annotationName}' does not define any valid hostname suffixes.");
        }

        var normalizedAllowedSuffixes = configuredSuffixes
            .Select(NormalizeSuffix)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var allowed = normalizedAllowedSuffixes.Any(suffix => HostnameMatchesSuffix(hostname, suffix));
        if (!allowed)
        {
            throw new InvalidOperationException(
                $"Hostname '{hostname}' is not permitted for namespace '{resourceNamespace}'. Allowed suffixes: {string.Join(", ", configuredSuffixes)}.");
        }
    }

    private static string NormalizeSuffix(string value)
    {
        var normalized = value.Trim().TrimStart('.').ToUpperInvariant();
        return normalized;
    }

    private static bool HostnameMatchesSuffix(string hostname, string suffix)
    {
        var normalizedHostname = hostname.Trim().TrimEnd('.').ToUpperInvariant();
        return string.Equals(normalizedHostname, suffix, StringComparison.OrdinalIgnoreCase)
               || normalizedHostname.EndsWith($".{suffix}", StringComparison.OrdinalIgnoreCase);
    }
}
