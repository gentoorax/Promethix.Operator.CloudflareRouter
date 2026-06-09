using k8s;
using k8s.Autorest;
using k8s.Models;
using System.Globalization;
using System.Net;

namespace Promethix.CloudflareTunnelOperator.Routing.Integrations.Kubernetes;

public sealed class KubernetesIngressTargetValidator(IKubernetes kubernetes) : IIngressTargetValidator
{
    public async Task ValidateAsync(
        TunnelPublicHostnameCustomResource resource,
        TunnelIngressTargetSpec ingress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(ingress);

        var serviceNamespace = ingress.Service?.Namespace;
        var serviceName = ingress.Service?.Name;
        var servicePort = ingress.Service?.Port ?? 0;

        if (!string.IsNullOrWhiteSpace(serviceNamespace) && !string.IsNullOrWhiteSpace(serviceName))
        {
            V1Service service;
            try
            {
                service = await kubernetes.CoreV1.ReadNamespacedServiceAsync(
                    serviceName,
                    serviceNamespace,
                    pretty: null,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new InvalidOperationException(
                    $"Referenced ingress service {serviceNamespace}/{serviceName} does not exist.",
                    ex);
            }

            var portExists = service.Spec?.Ports?.Any(port =>
                port.Port == servicePort ||
                string.Equals(
                    port.TargetPort?.ToString(),
                    servicePort.ToString(CultureInfo.InvariantCulture),
                    StringComparison.Ordinal)) == true;

            if (!portExists)
            {
                throw new InvalidOperationException(
                    $"Referenced ingress service {serviceNamespace}/{serviceName} does not expose port {servicePort}.");
            }
        }

        var ingresses = await kubernetes.NetworkingV1.ListIngressForAllNamespacesAsync(
            allowWatchBookmarks: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var hostname = resource.Spec.Hostname.Trim();
        var className = ingress.ClassName.Trim();
        var matchExists = ingresses.Items.Any(item =>
            string.Equals(item.Spec?.IngressClassName, className, StringComparison.Ordinal)
            && item.Spec?.Rules?.Any(rule => string.Equals(rule.Host, hostname, StringComparison.OrdinalIgnoreCase)) == true);

        if (!matchExists)
        {
            throw new InvalidOperationException(
                $"No Kubernetes Ingress with class '{className}' publishes hostname '{hostname}'.");
        }
    }
}
