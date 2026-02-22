// ============================================================
// FILE: PolarionMcpTools/DefaultMcpScopeEnforcer.cs
// ============================================================
// PURPOSE: Permissive scope enforcer for the stdio console
//          server (PolarionMcpServer). Because that server
//          is launched directly on the developer's workstation
//          with credentials already baked into appsettings,
//          no additional HTTP-level authorization layer exists.
//          All scopes are therefore granted implicitly.
//
// REGISTRATION (PolarionMcpServer/Program.cs):
//   builder.Services.AddSingleton<IMcpScopeEnforcer,
//       DefaultMcpScopeEnforcer>();
// ============================================================

namespace PolarionMcpTools;

/// <summary>
/// Always grants scope. Suitable for the stdio (local) server
/// where HTTP authentication does not apply.
/// </summary>
public sealed class DefaultMcpScopeEnforcer : IMcpScopeEnforcer
{
    /// <inheritdoc/>
    public string? CheckScope(string requiredScope)
    {
        // No HTTP session — allow everything.
        return null;
    }
}
