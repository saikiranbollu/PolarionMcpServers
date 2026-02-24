
using System.Collections.Generic;

namespace PolarionMcpTools
{
    /// <summary>
    /// Represents the configuration for a specific artifact type's custom fields.
    /// </summary>
    public class ArtifactCustomFieldConfig
    {
        /// <summary>
        /// The ID or type of the artifact (e.g., "requirement", "testcase").
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// A list of custom field names to be retrieved for this artifact type.
        /// </summary>
        public List<string> Fields { get; set; } = new List<string>();
    }

    /// <summary>
    /// Represents the configuration for a single Polarion project instance
    /// defined in the application settings.
    /// </summary>
    public class PolarionProjectConfig
    {
        /// <summary>
        /// An alias or identifier for this project configuration, 
        /// typically matching the route parameter used to select it.
        /// </summary>
        public string ProjectUrlAlias { get; set; } = string.Empty;

        /// <summary>
        /// Indicates if this configuration should be used when no specific 
        /// project ID is provided in the request route. Only one configuration
        /// should be marked as default.
        /// </summary>
        public bool Default { get; set; } = false;

        /// <summary>
        /// Contains the actual connection details (ServerUrl, Username, Password, etc.)
        /// for this Polarion instance. This property name must match the JSON key ("SessionConfig").
        /// This property is required and expected to be populated by the configuration binder.
        /// </summary>
        public PolarionClientConfiguration? SessionConfig { get; set; }

        // -------------------------------------------------------
        // PAT Authentication (NEW in v0.13.0)
        // -------------------------------------------------------

        /// <summary>
        /// Optional Polarion Personal Access Token (PAT).
        /// When set, it is used as the credential instead of
        /// <c>SessionConfig.Password</c>.
        ///
        /// Polarion's SOAP API accepts the PAT as the password
        /// argument in its logIn() call, so the factory substitutes
        /// it transparently.
        ///
        /// Recommended: Leave this empty in appsettings.json and
        /// supply it via the <c>POLARION_{ALIAS}_PAT</c> environment
        /// variable instead so that tokens never appear in config files.
        ///
        /// Priority over Password: PAT is always preferred when present.
        /// </summary>
        public string? PersonalAccessToken { get; set; }

        // -------------------------------------------------------
        // MCP Scope Enforcement (NEW in v0.13.0)
        // -------------------------------------------------------

        /// <summary>
        /// When <c>true</c>, MCP write tools require the caller to
        /// hold the <c>polarion:write</c> scope.  Defaults to <c>true</c>
        /// for the remote HTTP server.
        ///
        /// Set to <c>false</c> only for trusted internal deployments
        /// where authentication is handled at the network layer.
        ///
        /// Note: This has no effect on the stdio console server
        /// (<c>PolarionMcpServer</c>), which always uses
        /// <c>DefaultMcpScopeEnforcer</c> (permits everything).
        /// </summary>
        public bool EnforceMcpScopes { get; set; } = true;

        // -------------------------------------------------------
        // Existing fields (unchanged)
        // -------------------------------------------------------

        /// <summary>
        /// A string pattern used to filter out spaces that contain this string.
        /// If null or empty, no filtering is applied.
        /// </summary>
        public string? BlacklistSpaceContainingMatch { get; set; }

        /// <summary>
        /// Optional work item ID prefix for this project (e.g., "STR", "OCT").
        /// Used for display and validation purposes.
        /// </summary>
        public string? WorkItemPrefix { get; set; }

        /// <summary>
        /// Gets or sets the list of WorkItem type configurations specific to this project.
        /// Each configuration defines custom fields to be retrieved for a specific WorkItem type.
        /// </summary>
        public List<ArtifactCustomFieldConfig>? PolarionWorkItemTypes { get; set; }

        /// <summary>
        /// Gets or sets the default list of WorkItem fields (standard and custom) to be retrieved
        /// when no specific fields are requested by the user.
        /// </summary>
        public List<string>? PolarionWorkItemDefaultFields { get; set; }

        /// <summary>
        /// Gets or sets the default list of Document fields (standard and custom) to be retrieved
        /// when no specific fields are requested by the user.
        /// </summary>
        public List<string>? PolarionDocumentDefaultFields { get; set; }

        // -------------------------------------------------------
        // Helper: resolve effective credential
        // -------------------------------------------------------

        /// <summary>
        /// Returns a <see cref="PolarionClientConfiguration"/> ready for
        /// <c>PolarionClient.CreateAsync()</c>.  Substitutes the PAT for
        /// Password when a PAT is available.
        /// </summary>
        public PolarionClientConfiguration? GetEffectiveClientConfig()
        {
            if (SessionConfig == null) return null;

            var pat = PersonalAccessToken;
            if (string.IsNullOrWhiteSpace(pat))
            {
                // No PAT — use SessionConfig as-is.
                return SessionConfig;
            }

            // PAT is present: create a modified copy where Password = PAT.
            // Polarion SOAP logIn(username, password) accepts PAT as password.
            return new PolarionClientConfiguration(
                SessionConfig.ServerUrl,
                SessionConfig.Username,
                pat,   // ← PAT substituted here
                SessionConfig.ProjectId,
                SessionConfig.TimeoutSeconds);
        }
    }
}
