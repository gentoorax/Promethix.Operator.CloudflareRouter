namespace Promethix.CloudflareTunnelOperator.Routing.Integrations.Kubernetes;

internal static class ServiceTargetUrlResolver
{
    public static Uri Resolve(TunnelIngressServiceTargetSpec service, string fieldPath)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldPath);

        if (string.IsNullOrWhiteSpace(service.Name))
        {
            throw new InvalidOperationException($"{fieldPath}.name is required when {fieldPath} is supplied.");
        }

        if (string.IsNullOrWhiteSpace(service.Namespace))
        {
            throw new InvalidOperationException($"{fieldPath}.namespace is required when {fieldPath} is supplied.");
        }

        if (service.Port <= 0)
        {
            throw new InvalidOperationException($"{fieldPath}.port must be greater than zero when {fieldPath} is supplied.");
        }

        var scheme = NormalizeScheme(service.Scheme, fieldPath);
        return new Uri($"{scheme}://{service.Name}.{service.Namespace}.svc.cluster.local:{service.Port}");
    }

    private static string NormalizeScheme(string? scheme, string fieldPath)
    {
        var normalizedScheme = scheme?.Trim();

        return normalizedScheme?.ToUpperInvariant() switch
        {
            "HTTP" => Uri.UriSchemeHttp,
            "HTTPS" => Uri.UriSchemeHttps,
            _ => throw new InvalidOperationException($"{fieldPath}.scheme must be http or https when {fieldPath} is supplied."),
        };
    }
}
