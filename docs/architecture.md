# Architecture Notes

## Intent

The operator is designed as a narrow, explicit control-loop service rather than a broad automation platform. The initial focus is safe reconciliation of HTTP/S public hostname routes for an existing Cloudflare Tunnel.

## Main Decisions

### Capability-first but pragmatic

The solution uses a single `Routing` capability with explicit `Domain`, `Application`, and `Integrations.Cloudflare` projects plus a `Hosting` bootstrap project. That keeps boundaries clear without inventing modules that do not exist yet.

### Explicit ownership

Managed routes carry an ownership marker. Reconciliation only proposes deletion for routes already owned by this operator instance. That creates room for shared-tunnel scenarios and safer rollout.

### Simple reconciliation flow

The control loop shape is:

```text
Kubernetes intent source -> application planner -> Cloudflare adapter -> health/logging
```

The current scaffold keeps Kubernetes and Cloudflare interactions behind explicit interfaces. That allows the first implementation to stay testable while leaving room for a later move to real CRDs, watches, finalizers, and status reporting.

### Production-minded defaults

The host includes:

- options validation on startup
- structured logging
- liveness and readiness endpoints
- container-oriented `8080` HTTP binding
- a background reconciliation worker

## Growth Path

Likely next steps:

1. Introduce a concrete CRD for route intent and corresponding status.
2. Replace the placeholder intent source with a watched Kubernetes source.
3. Implement the Cloudflare API client and remote diff/application logic.
4. Add conflict handling, status conditions, metrics, and richer tests.
