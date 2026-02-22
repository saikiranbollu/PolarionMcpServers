// ============================================================
// FILE: PolarionRemoteMcpServer/Authentication/HttpMcpScopeEnforcer.cs
// ============================================================
// PURPOSE: Scope enforcer for the HTTP/SSE remote server.
//          Reads scope claims from the authenticated principal
//          injected by ApiKeyAuthenticationHandler, then
//          compares against the required scope.
//
// REGISTRATION (PolarionRemoteMcpServer/Program.cs):
//   builder.Services.AddScoped<IMcpScopeEnforcer,
//       HttpMcpScopeEnforcer>();
//
// HOW IT WORKS:
//   ApiKeyAuthenticationHandler adds a "scope" claim for each
//   entry in ApiConsumerConfig.AllowedScopes.  This enforcer
//   retrieves those claims from IHttpContextAccessor and checks
//   membership.  If no HTTP context is present (e.g., during
//   integration tests or health checks) it falls back to DENY.
// ============================================================

using Microsoft.AspNetCore.Http;
using PolarionMcpTools;
using Serilog;

namespace PolarionRemoteMcpServer.Authentication;

/// <summary>
/// HTTP-context-aware scope enforcer.
/// Returns a non-null error string when the caller lacks the
/// required scope; <c>null</c> when access is granted.
/// </summary>
public sealed class HttpMcpScopeEnforcer : IMcpScopeEnforcer
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpMcpScopeEnforcer(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc/>
    public string? CheckScope(string requiredScope)
    {
        var context = _httpContextAccessor.HttpContext;

        if (context == null)
        {
            // No HTTP context — deny by default (safe fallback).
            Log.Warning("MCP Scope Enforcer: No HTTP context present. Denying scope '{Scope}'.", requiredScope);
            return $"ERROR: (4031) Unauthorized. No HTTP context for scope check '{requiredScope}'.";
        }

        var user = context.User;

        // Must be authenticated first.
        if (user?.Identity == null || !user.Identity.IsAuthenticated)
        {
            Log.Warning("MCP Scope Enforcer: Unauthenticated caller attempted to use scope '{Scope}'.", requiredScope);
            return $"ERROR: (4011) Unauthorized. Authentication required for scope '{requiredScope}'. " +
                   "Provide a valid X-API-Key header.";
        }

        // Collect all "scope" claims.
        var grantedScopes = user.FindAll("scope")
                                .Select(c => c.Value)
                                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (grantedScopes.Contains(requiredScope))
        {
            var consumerId = user.FindFirst("consumer_id")?.Value ?? "unknown";
            Log.Debug("MCP Scope Enforcer: Consumer '{ConsumerId}' granted scope '{Scope}'.",
                consumerId, requiredScope);
            return null; // ✓ Access granted.
        }

        var consumerIdDenied = user.FindFirst("consumer_id")?.Value ?? "unknown";
        Log.Warning(
            "MCP Scope Enforcer: Consumer '{ConsumerId}' denied scope '{Scope}'. " +
            "Consumer has scopes: [{GrantedScopes}].",
            consumerIdDenied, requiredScope, string.Join(", ", grantedScopes));

        return $"ERROR: (4033) Forbidden. Consumer '{consumerIdDenied}' does not have scope " +
               $"'{requiredScope}'. Required for this write operation. " +
               $"Add '{requiredScope}' to AllowedScopes in appsettings.json ApiConsumers section.";
    }
}
