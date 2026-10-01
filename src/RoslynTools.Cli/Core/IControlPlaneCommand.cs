namespace RoslynTools.Core;

/// <summary>
/// Marks a user-authoritative local control-plane command whose explicit CLI verb is
/// itself the apply decision. These root-only commands may write user-scoped local state,
/// but do not use repository-source <c>--dry-run</c>/<c>--apply</c> flags.
/// They must remain unavailable to daemon and MCP request surfaces unless separately
/// admitted by an application-port policy.
/// </summary>
internal interface IControlPlaneCommand : IRtCommand;
