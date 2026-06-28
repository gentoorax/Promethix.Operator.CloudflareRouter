namespace Promethix.CloudflareTunnelOperator.Routing.Integrations.Kubernetes;

public interface IManagedTunnelPublicHostnameValidator
{
    bool IsManaged(TunnelPublicHostnameCustomResource resource);

    Task ValidateAsync(TunnelPublicHostnameCustomResource resource, CancellationToken cancellationToken);
}
