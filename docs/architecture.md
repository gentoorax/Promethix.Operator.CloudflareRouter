# Architecture

This document covers the project architecture and coding patterns used by the operator.

## Purpose

Promethix Cloudflare Tunnel Operator publishes Kubernetes-hosted services through an existing Cloudflare Tunnel.

The operator is intentionally narrow in scope:

- it manages Cloudflare Tunnel public hostname entries
- it does not replace your ingress controller
- it does not replace `external-dns` or `cert-manager`

For most HTTP and HTTPS workloads, the operator is designed to work alongside a dedicated Traefik ingress path.

## Design principles

These principles drive the current design:

- explicit intent over inference
- simple control loops over hidden automation
- clear ownership over shared infrastructure
- narrow scope over speculative platform-building

In practice, that means:

- tunnel publication intent lives in a dedicated CRD
- normal HTTP routing stays with `Ingress` and Traefik
- the operator only deletes what it owns
- new features should preserve clear boundaries between Kubernetes routing and Cloudflare publication

## Ownership and safety

The operator is designed for shared-tunnel scenarios. Ownership is part of the architecture, not an afterthought.

- only routes owned by this operator are eligible for deletion
- conflicting unmanaged hostnames are surfaced as conflicts, not overwritten
- dry-run operation is supported through `applyChanges=false`
- direct service targets must stay in the same namespace as the `TunnelPublicHostname` by default
- ingress service overrides are disabled by default

This is intentional. The operator should be safe to introduce into an existing tunnel without taking ownership of unrelated routes.

## Runtime shape

The operator follows a simple control-loop model:

```text
TunnelPublicHostname -> Kubernetes validation -> reconciliation plan -> Cloudflare Tunnel config update
```

Supporting pieces include:

- status updates back onto the CRD
- health endpoints
- watch-driven reconciliation
- finalizer-based cleanup

## Code layout

The code is split into a few focused areas:

- `Routing.Domain`
  - core route types and invariants
- `Routing.Application`
  - reconciliation planning and orchestration
- `Routing.Integrations.Kubernetes`
  - CRD loading, validation, status updates, ownership state
- `Routing.Integrations.Cloudflare`
  - Cloudflare Tunnel configuration reads and writes
- `Hosting`
  - DI, health endpoints, workers, configuration

## Layering

The solution follows a modular monolith style.

The intent is:

- keep one deployable service
- keep module boundaries clear
- avoid unnecessary abstraction between layers
- let dependencies flow inward toward domain and application code

For this operator, the current `Routing` module is split into:

- `Routing.Domain`
- `Routing.Application`
- `Routing.Integrations.Kubernetes`
- `Routing.Integrations.Cloudflare`
- `Hosting`

### `Routing.Domain`

This project should contain:

- core domain types
- invariants and validation that are true regardless of transport or platform
- value objects and route identity rules

It should not contain:

- Kubernetes client code
- Cloudflare API code
- hosting concerns

### `Routing.Application`

This project should contain:

- reconciliation orchestration
- planning and conflict handling
- application-facing interfaces
- use-case level logic that coordinates domain and integrations

It should depend on the domain model, but not on concrete infrastructure details.

### `Routing.Integrations.Kubernetes`

This project should contain:

- CRD models
- Kubernetes API access
- intent loading
- validation against cluster state
- status updates
- ownership persistence if Kubernetes-backed

It is the adapter between Kubernetes and the application layer.

### `Routing.Integrations.Cloudflare`

This project should contain:

- Cloudflare API models
- request and response mapping
- config merge logic specific to Cloudflare Tunnel
- write/read behavior for remotely managed tunnel configuration

It should not contain higher-level reconciliation policy beyond what is required to translate application decisions into Cloudflare payloads.

### `Hosting`

This project should contain:

- service startup
- dependency injection
- background workers
- health endpoints
- configuration binding
- logging and runtime wiring

This is the composition root. It should stay thin and should not accumulate business logic.

## Contributing notes

If you are changing behavior, keep these boundaries in mind:

- add business rules in `Domain` or `Application`
- keep Cloudflare-specific payload handling in the Cloudflare integration
- keep Kubernetes API concerns in the Kubernetes integration
- avoid pushing policy decisions into the hosting layer

When extending the operator:

- prefer explicit CRD intent over inference
- preserve safe behavior for shared tunnels
- avoid broadening scope unless the operator still has a clear ownership boundary

## Current limits

The operator is still under active development. Notable limits today include:

- no direct TCP or non-HTTP origin support yet
- no advanced per-route Cloudflare origin request settings yet
- live behavior should still be validated carefully before enabling writes in production
