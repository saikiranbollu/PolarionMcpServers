// ============================================================
// FILE: PolarionMcpTools/IMcpScopeEnforcer.cs
// ============================================================
// PURPOSE: Injectable service that MCP write-tools call to
//          verify the caller holds the required scope before
//          mutating Polarion data.
//
//          Two concrete implementations:
//          • DefaultMcpScopeEnforcer  (PolarionMcpTools)
//              – always grants access; used by the stdio
//                console server where no HTTP auth exists.
//          • HttpMcpScopeEnforcer  (PolarionRemoteMcpServer)
//              – reads claims from IHttpContextAccessor and
//                enforces scope constraints.
// ============================================================

namespace PolarionMcpTools;

/// <summary>
/// Verifies that the current MCP caller holds a given scope.
/// Inject this into McpTools to gate write operations.
/// </summary>
public interface IMcpScopeEnforcer
{
    /// <summary>
    /// Returns <c>null</c> when the caller has the required scope.
    /// Returns a non-null error string (suitable for returning
    /// directly from an MCP tool) when access is denied.
    /// </summary>
    /// <param name="requiredScope">
    /// One of the constants in <c>PolarionApiScopes</c>.
    /// </param>
    string? CheckScope(string requiredScope);
}

/// <summary>
/// Centralised scope constant strings that both the enforcer
/// implementations and the write tools reference.
/// Mirrors <c>PolarionRemoteMcpServer.Authentication.ApiScopes</c>
/// but lives in the shared PolarionMcpTools project so that
/// tool code does not need a reference to the server project.
/// </summary>
public static class PolarionApiScopes
{
    /// <summary>Read-only access to Polarion data.</summary>
    public const string Read   = "polarion:read";

    /// <summary>
    /// Write access: create/update work items, add comments,
    /// perform workflow actions, bulk operations, link management.
    /// </summary>
    public const string Write  = "polarion:write";

    /// <summary>Admin-level operations (future use).</summary>
    public const string Admin  = "polarion:admin";

    /// <summary>All declared scopes for policy registration.</summary>
    public static readonly string[] All = { Read, Write, Admin };
}
