namespace Promethix.CloudflareTunnelOperator.Routing.Domain;

public sealed record RoutePlan(
    IReadOnlyCollection<PublicHostnameRoute> ToCreate,
    IReadOnlyCollection<PublicHostnameRoute> ToUpdate,
    IReadOnlyCollection<PublicHostnameRoute> ToDelete,
    IReadOnlyCollection<RouteConflict> Conflicts)
{
    public bool HasChanges => this.ToCreate.Count > 0 || this.ToUpdate.Count > 0 || this.ToDelete.Count > 0;
}
