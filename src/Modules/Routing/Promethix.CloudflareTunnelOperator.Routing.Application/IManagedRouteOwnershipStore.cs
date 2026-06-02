namespace Promethix.CloudflareTunnelOperator.Routing.Application;

public interface IManagedRouteOwnershipStore
{
    Task<IReadOnlyDictionary<string, string>> GetOwnershipAsync(CancellationToken cancellationToken);

    Task SaveOwnershipAsync(IReadOnlyDictionary<string, string> ownershipByHostname, CancellationToken cancellationToken);
}
