# CRDs

This document describes the Kubernetes resources understood by the operator.

The main resource is:

- `TunnelPublicHostname`

Examples are available in [../examples](../examples).

## TunnelPublicHostname

`TunnelPublicHostname` declares that a public hostname should be published through a managed Cloudflare Tunnel.

Required fields:

- `spec.className`
- `spec.hostname`
- `spec.tunnelRef.name`
- `spec.target`

Common fields:

```yaml
apiVersion: edge.promethix.net/v1alpha1
kind: TunnelPublicHostname
metadata:
  name: app-public
  namespace: demo
spec:
  className: public
  hostname: app.example.com
  enabled: true
  tunnelRef:
    name: public
```

## Ingress Target

Ingress mode is the preferred option for HTTP and HTTPS applications.

Use this when Traefik should handle routing, middleware, and TLS.

```yaml
spec:
  target:
    mode: ingress
    ingress:
      className: traefik-cloudflare-tunnel
      service:
        name: traefik-cloudflare-tunnel
        namespace: edge-system
        port: 443
        scheme: https
```

For HTTPS ingress targets, the operator sets Cloudflare `originRequest.originServerName` to `spec.hostname`.

Full example:

- [ingress-backed-app.yaml](../examples/ingress-backed-app.yaml)

## Direct Target

Direct mode publishes an HTTP or HTTPS origin without going through ingress.

Use this only when the workload does not fit the normal ingress path.

```yaml
spec:
  target:
    mode: direct
    direct:
      service:
        name: api
        namespace: demo
        port: 8443
        scheme: https
```

Full example:

- [direct-origin-app.yaml](../examples/direct-origin-app.yaml)

## Cloudflare Settings

Route-level Cloudflare settings live under `spec.cloudflare`.

```yaml
spec:
  cloudflare:
    proxied: true
```

## Security Policy Intent

Per-hostname security intent is optional and disabled by default.

When `operator.securityPolicies.enabled=true`, the operator reconciles managed Cloudflare rate-limit rules through the Cloudflare Rulesets API. WAF and Access policy reconciliation are not implemented yet.

```yaml
spec:
  cloudflare:
    security:
      rateLimit:
        enabled: true
        rules:
          - name: api-v1
            pathPrefix: /v1/
            requestsPerPeriod: 60
            periodSeconds: 60
            action: managed_challenge
```

The intended implementation boundary is:

- route reconciliation continues to use the Cloudflare Tunnel configuration API
- rate-limit reconciliation uses a separate Cloudflare Rulesets client
- security rules use operator ownership markers
- unmanaged Cloudflare rules are preserved
- `operator.applyChanges=false` plans security changes without applying them

Security policy reconciliation requires a Cloudflare zone id in addition to the account and tunnel ids.

Security reconciliation status is reported separately from route status using the `SecurityPolicyReady` condition:

- `True / Reconciled`: managed security rules match the declared intent
- `False / Planned`: changes were detected but `operator.applyChanges=false`
- `False / ReconcileFailed`: Cloudflare security policy reconciliation failed
- `True / NotRequested`: no security policy reconciliation was requested for the resource

Full example:

- [rate-limited-ingress-app.yaml](../examples/rate-limited-ingress-app.yaml)
