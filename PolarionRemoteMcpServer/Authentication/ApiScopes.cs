namespace PolarionRemoteMcpServer.Authentication;

/// <summary>
/// Defines the API scopes used for authorization.
/// Register these in appsettings.json under
/// ApiConsumers[*].AllowedScopes to grant access.
/// </summary>
public static class ApiScopes
{
    // -------------------------------------------------------
    // Read scope
    // -------------------------------------------------------

    /// <summary>
    /// Scope for read operations on Polarion data.
    /// Grants access to all GET/search MCP tools and REST endpoints.
    /// </summary>
    public const string PolarionRead   = "polarion:read";

    // -------------------------------------------------------
    // Write scope (NEW in v0.13.0)
    // -------------------------------------------------------

    /// <summary>
    /// Scope for write (create/update/mutate) operations on Polarion data.
    /// Required by: add_comment, perform_workflow_action,
    ///   create_workitem, update_workitem, link_workitems,
    ///   unlink_workitems, bulk_update_workitems, bulk_add_comment.
    /// A consumer with polarion:write implicitly also needs
    /// polarion:read for lookups performed before mutations.
    /// </summary>
    public const string PolarionWrite  = "polarion:write";

    // -------------------------------------------------------
    // Delete scope
    // -------------------------------------------------------

    /// <summary>
    /// Scope for delete operations on Polarion data (future use).
    /// </summary>
    public const string PolarionDelete = "polarion:delete";

    // -------------------------------------------------------
    // Admin scope (NEW in v0.13.0)
    // -------------------------------------------------------

    /// <summary>
    /// Admin-level scope for privileged operations (future use).
    /// </summary>
    public const string PolarionAdmin  = "polarion:admin";

    // -------------------------------------------------------
    // Aggregate
    // -------------------------------------------------------

    /// <summary>
    /// All available scopes — used when registering authorization
    /// policies in AuthenticationExtensions.AddApiKeyAuthentication.
    /// </summary>
    public static readonly string[] All = new[]
    {
        PolarionRead,
        PolarionWrite,
        PolarionDelete,
        PolarionAdmin
    };
}
