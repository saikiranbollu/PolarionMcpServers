
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
    /// Bindable DTO for the "SessionConfig" section in appsettings.json.
    /// Unlike <see cref="PolarionClientConfiguration"/> (from the Polarion NuGet
    /// package) this class has a parameterless constructor so the .NET
    /// configuration binder can always create it, even when Password is absent
    /// (PAT-only configs).
    /// </summary>
    public class PolarionSessionConfig
    {
        public string ServerUrl { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string? Password { get; set; }
        public string? PersonalAccessToken { get; set; }
        public string ProjectId { get; set; } = string.Empty;
        public int TimeoutSeconds { get; set; } = 60;
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
        /// </summary>
        public PolarionSessionConfig? SessionConfig { get; set; }

        /// <summary>
        /// Optional Polarion Personal Access Token (PAT) at the project level.
        /// When set here (or inside SessionConfig), it is used as the credential
        /// instead of <c>SessionConfig.Password</c>.
        /// </summary>
        public string? PersonalAccessToken { get; set; }

        // -------------------------------------------------------
        // MCP Scope Enforcement (NEW in v0.13.0)
        // -------------------------------------------------------

        /// <summary>
        /// When <c>true</c>, MCP write tools require the caller to
        /// hold the <c>polarion:write</c> scope.  Defaults to <c>true</c>
        /// for the remote HTTP server.
        /// </summary>
        public bool EnforceMcpScopes { get; set; } = true;

        /// <summary>
        /// A string pattern used to filter out spaces that contain this string.
        /// If null or empty, no filtering is applied.
        /// </summary>
        public string? BlacklistSpaceContainingMatch { get; set; }

        /// <summary>
        /// Optional work item ID prefix for this project (e.g., "STR", "OCT").
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
        /// <c>PolarionClient.CreateAsync()</c>.  Resolves PAT from either
        /// the project-level property (set by env var overrides in Program.cs) 
        /// or SessionConfig.PersonalAccessToken, and substitutes it for Password.
        /// </summary>
        public PolarionClientConfiguration? GetEffectiveClientConfig()
        {
            if (SessionConfig == null) return null;

            // Project-level PAT (set by env var override) takes priority
            // over SessionConfig.PersonalAccessToken (may contain unresolved placeholders)
            var pat = PersonalAccessToken;
            if (string.IsNullOrWhiteSpace(pat))
            {
                pat = SessionConfig.PersonalAccessToken;
            }

            var effectivePassword = !string.IsNullOrWhiteSpace(pat) ? pat : SessionConfig.Password ?? string.Empty;

            return new PolarionClientConfiguration(
                SessionConfig.ServerUrl,
                SessionConfig.Username,
                effectivePassword,
                SessionConfig.ProjectId,
                SessionConfig.TimeoutSeconds);
        }
    }
}
