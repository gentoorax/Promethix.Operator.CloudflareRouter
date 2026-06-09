namespace Promethix.CloudflareTunnelOperator.Routing.Integrations.Kubernetes;

public interface IIngressTargetValidator
{
    Task ValidateAsync(
        TunnelPublicHostnameCustomResource resource,
        TunnelIngressTargetSpec ingress,
        CancellationToken cancellationToken);
}
