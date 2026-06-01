namespace Promethix.CloudflareTunnelOperator.Routing.Domain;

public sealed record RouteConflict(string Hostname, string Reason);
