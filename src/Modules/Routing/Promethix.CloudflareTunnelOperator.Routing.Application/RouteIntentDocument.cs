using Promethix.CloudflareTunnelOperator.Routing.Domain;

namespace Promethix.CloudflareTunnelOperator.Routing.Application;

public sealed record RouteIntentDocument(
    string Source,
    IReadOnlyCollection<PublicHostnameRoute> Routes);
