# OpenSpec

This directory records proposed, accepted, and implemented changes. Each proposal is
the authoritative source for its own status; listing a change here does not approve
it. A change is not complete because its prose exists: its scenarios must be
implemented, tested, and linked to a released verification result.

Current changes:

- [layered-user-configuration](changes/layered-user-configuration/proposal.md) *(accepted)* — adds versioned global and repository configuration for daemon behavior and the MCP tool surface without making configuration a trust grant.
- [critical-platform-hardening](changes/critical-platform-hardening/proposal.md) *(proposed)* — establishes the mutation safety kernel, canonical operation spine, workspace provenance, agent/MCP contract, and the temporary transparent-daemon boundary.
- [shape-pre-release-product-spine](changes/shape-pre-release-product-spine/proposal.md) *(proposed)* — narrows the `0.1-preview` product surface, consolidates rules and recipes, and adds the discovery, impact, diagnostics, planning, and verification workflow that consumes the hardening guarantees.

The product-spine change does not weaken or duplicate the platform-hardening change.
It defines which capabilities may ship while the durable apply and isolated-evaluator
work remains incomplete.
