# ADR-0079: Third-party adapters outside Core and Hosting

- Status: Accepted
- Date: 2026-09-29
- Related work: RT-020

## Decision

`RoslynTools.Adapters` owns third-party application integrations. It depends on
Core and Hosting; CLI, MCP, and DaemonHost compose it. Neither Core nor Hosting
may reference Adapters. Core owns the neutral contracts used by operations.

Move decision-file YAML persistence, diff rendering, and Scrutor registration
to this boundary in separately verified slices. Preserve the existing file,
output, error, registration, and host contracts. Core and Hosting then allow
only approved Microsoft/.NET direct application packages. Build-only Roslynator
and dependencies required transitively by Microsoft packages are permitted;
this is not a claim that the entire restore graph is Microsoft-only.

Evaluated package checks must include imported references; compiled-reference
and project-graph checks enforce code coupling and dependency direction.

## Consequences

Hosts share concrete adapters without making reusable application code depend
on a host or third-party implementation. Tests may reference Adapters directly
to exercise real serialization/rendering boundaries. The extraction does not
replace the third-party algorithms or introduce alternate wire formats.
