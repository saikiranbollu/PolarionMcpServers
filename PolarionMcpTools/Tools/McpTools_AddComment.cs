// ============================================================
// FILE: PolarionMcpTools/Tools/McpTools_AddComment.cs
// ============================================================
// SCOPE REQUIREMENT: polarion:write
//
// SOAP DEPENDENCY:
//   IPolarionClient.AddCommentAsync(projectId, workItemId, commentText)
//   → maps to: TrackerWebService.addComment(projectId, workItemId, text)
//   Requires PolarionApiClient >= 2.1.0
//
// AUTOMOTIVE USE CASES:
//   • Auditors adding review notes to ASIL requirements
//   • CI/CD pipelines posting test run results to defects
//   • AI assistants logging analysis results on work items
// ============================================================

namespace PolarionMcpTools;

public sealed partial class McpTools
{
    [RequiresUnreferencedCode("Uses Polarion API which requires reflection")]
    [McpServerTool(Name = "add_comment"),
     Description(
         "Adds a comment to a Polarion WorkItem. " +
         "Useful for logging review notes, CI/CD results, or AI analysis findings. " +
         "Requires polarion:write scope on the remote server. " +
         "Comment text supports basic HTML (Polarion renders it as rich text).")]
    public async Task<string> AddComment(
        [Description("The WorkItem ID to comment on (e.g., 'STR-1234').")] string workitemId,
        [Description(
            "Comment text. Supports basic HTML for formatting. " +
            "Example: 'Review complete. <b>ASIL level confirmed.</b> No issues found.'")] string commentText,
        [Description(
            "Optional title/subject for the comment thread. " +
            "Defaults to 'Comment' when not provided.")] string? commentTitle = null)
    {
        // -------------------------------------------------------
        // Input validation
        // -------------------------------------------------------
        if (string.IsNullOrWhiteSpace(workitemId))
            return "ERROR: (4001) workitemId parameter cannot be empty.";

        if (string.IsNullOrWhiteSpace(commentText))
            return "ERROR: (4002) commentText parameter cannot be empty.";

        // -------------------------------------------------------
        // Scope enforcement
        // -------------------------------------------------------
        var scopeEnforcer = _serviceProvider.GetRequiredService<IMcpScopeEnforcer>();
        var scopeError = scopeEnforcer.CheckScope(PolarionApiScopes.Write);
        if (scopeError != null) return scopeError;

        // -------------------------------------------------------
        // Execute
        // -------------------------------------------------------
        await using var scope = _serviceProvider.CreateAsyncScope();
        var clientFactory = scope.ServiceProvider.GetRequiredService<IPolarionClientFactory>();
        var clientResult  = await clientFactory.CreateClientAsync();
        if (clientResult.IsFailed)
            return clientResult.Errors.FirstOrDefault()?.Message
                   ?? "ERROR: Unknown error when creating Polarion client.";

        var polarionClient = clientResult.Value;
        var projectConfig  = GetCurrentProjectConfig();

        if (projectConfig?.SessionConfig == null)
            return "ERROR: (5001) Could not determine current project configuration.";

        var projectId = projectConfig.SessionConfig.ProjectId;

        try
        {
            var title          = string.IsNullOrWhiteSpace(commentTitle) ? "Comment" : commentTitle.Trim();
            var formattedTitle = $"{title}";

            var result = await polarionClient.AddCommentAsync(projectId, workitemId, commentText, formattedTitle);

            if (result.IsFailed)
            {
                return $"ERROR: (5010) Failed to add comment to '{workitemId}': " +
                       $"{result.Errors.FirstOrDefault()?.Message ?? "Unknown error"}";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"## Comment Added Successfully");
            sb.AppendLine();
            sb.AppendLine($"- **WorkItem**: {workitemId}");
            sb.AppendLine($"- **Project**: {projectId}");
            sb.AppendLine($"- **Title**: {title}");
            sb.AppendLine();
            sb.AppendLine("### Comment Text");
            sb.AppendLine();
            sb.AppendLine(commentText);

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"ERROR: (5099) Failed to add comment to '{workitemId}' due to exception: {ex.Message}";
        }
    }
}
