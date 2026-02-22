// ============================================================
// FILE: PolarionMcpTools/Tools/McpTools_BulkOperations.cs
// ============================================================
// TOOLS:
//   • bulk_update_workitems — update fields on N items at once
//   • bulk_add_comment      — post the same comment to N items
//
// SCOPE REQUIREMENT: polarion:write
//
// SOAP DEPENDENCIES:
//   IPolarionClient.UpdateWorkItemAsync(workItem)
//   IPolarionClient.AddCommentAsync(projectId, workItemId, text, title)
//   Requires PolarionApiClient >= 2.1.0
//
// DESIGN:
//   • Sequential execution with per-item error capture —
//     one failing item does not abort the rest.
//   • Returns a summary table: ID | Result | Details.
//   • Max 50 items per call to prevent runaway operations.
//
// AUTOMOTIVE USE CASES:
//   • Transition all "draft" requirements in a module to "inReview"
//     after AI-assisted completeness check.
//   • Add the same test-execution result comment to a list of
//     test cases from a CI/CD pipeline.
//   • Bulk-reassign work items to a new owner after team change.
//   • Mass-set priority on a safety-critical requirements set.
// ============================================================

namespace PolarionMcpTools;

public sealed partial class McpTools
{
    private const int BulkMaxItems = 50;

    // =========================================================
    // TOOL 1: bulk_update_workitems
    // =========================================================

    [RequiresUnreferencedCode("Uses Polarion API which requires reflection")]
    [McpServerTool(Name = "bulk_update_workitems"),
     Description(
         "Updates one or more fields on multiple WorkItems in a single operation. " +
         "Provide only the fields you want to change; unspecified fields are left untouched. " +
         "At least one of: status, assignee, priority, severity, or customFields must be provided. " +
         "Maximum 50 work items per call. " +
         "Returns a per-item result table. " +
         "Requires polarion:write scope on the remote server.")]
    public async Task<string> BulkUpdateWorkitems(
        [Description(
            "Comma-separated list of WorkItem IDs to update (e.g., 'STR-100,STR-101,STR-102'). " +
            "Maximum 50 items.")] string workitemIds,
        [Description("New status ID (e.g., 'inReview', 'approved', 'draft'). Leave empty to keep existing.")] string? status = null,
        [Description("New assignee username (e.g., 'alice.smith'). Leave empty to keep existing.")] string? assignee = null,
        [Description("New priority ID (e.g., 'high', 'medium', 'low'). Leave empty to keep existing.")] string? priority = null,
        [Description("New severity ID (e.g., 'critical', 'major', 'minor'). Leave empty to keep existing.")] string? severity = null,
        [Description(
            "Custom field updates. Format: 'fieldId=value' pairs separated by newlines. " +
            "Example: 'asil_level=B\\ncustom_reviewed=true'. " +
            "Leave empty to make no custom field changes.")] string? customFields = null,
        [Description(
            "When true, adds a comment to each successfully updated item recording what changed. " +
            "Useful for audit trails.")] bool addAuditComment = true)
    {
        // -------------------------------------------------------
        // Input validation
        // -------------------------------------------------------
        if (string.IsNullOrWhiteSpace(workitemIds))
            return "ERROR: (4001) workitemIds parameter cannot be empty.";

        var hasAtLeastOneChange =
            !string.IsNullOrWhiteSpace(status)       ||
            !string.IsNullOrWhiteSpace(assignee)     ||
            !string.IsNullOrWhiteSpace(priority)     ||
            !string.IsNullOrWhiteSpace(severity)     ||
            !string.IsNullOrWhiteSpace(customFields);

        if (!hasAtLeastOneChange)
            return "ERROR: (4003) At least one field to update must be provided " +
                   "(status, assignee, priority, severity, or customFields).";

        var ids = workitemIds
            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct()
            .ToList();

        if (ids.Count > BulkMaxItems)
            return $"ERROR: (4004) Too many work items ({ids.Count}). Maximum is {BulkMaxItems} per call. " +
                   "Split into multiple calls.";

        // -------------------------------------------------------
        // Scope enforcement
        // -------------------------------------------------------
        var scopeEnforcer = _serviceProvider.GetRequiredService<IMcpScopeEnforcer>();
        var scopeError    = scopeEnforcer.CheckScope(PolarionApiScopes.Write);
        if (scopeError != null) return scopeError;

        // -------------------------------------------------------
        // Parse custom fields
        // -------------------------------------------------------
        var customFieldMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(customFields))
        {
            foreach (var line in customFields.Split(new[] { '\n', '\r' },
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var eqIdx = line.IndexOf('=');
                if (eqIdx <= 0) continue;
                var key = line[..eqIdx].Trim();
                var val = line[(eqIdx + 1)..].Trim();
                if (!string.IsNullOrEmpty(key))
                    customFieldMap[key] = val;
            }
        }

        // -------------------------------------------------------
        // Build change summary for audit comment
        // -------------------------------------------------------
        var changeSummaryParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(status))   changeSummaryParts.Add($"status → {status}");
        if (!string.IsNullOrWhiteSpace(assignee))  changeSummaryParts.Add($"assignee → {assignee}");
        if (!string.IsNullOrWhiteSpace(priority))  changeSummaryParts.Add($"priority → {priority}");
        if (!string.IsNullOrWhiteSpace(severity))  changeSummaryParts.Add($"severity → {severity}");
        foreach (var kv in customFieldMap)
            changeSummaryParts.Add($"{kv.Key} → {kv.Value}");
        var changeSummary = string.Join(", ", changeSummaryParts);

        // -------------------------------------------------------
        // Execute per-item
        // -------------------------------------------------------
        await using var scope  = _serviceProvider.CreateAsyncScope();
        var clientFactory      = scope.ServiceProvider.GetRequiredService<IPolarionClientFactory>();
        var clientResult       = await clientFactory.CreateClientAsync();
        if (clientResult.IsFailed)
            return clientResult.Errors.FirstOrDefault()?.Message
                   ?? "ERROR: Unknown error when creating Polarion client.";

        var polarionClient = clientResult.Value;
        var projectConfig  = GetCurrentProjectConfig();
        if (projectConfig?.SessionConfig == null)
            return "ERROR: (5001) Could not determine current project configuration.";

        var projectId = projectConfig.SessionConfig.ProjectId;
        var results   = new List<(string Id, bool Success, string Detail)>();

        foreach (var id in ids)
        {
            try
            {
                // Fetch current state (partial-update pattern: read → modify → write).
                var wiResult = await polarionClient.GetWorkItemByIdAsync(id);
                if (wiResult.IsFailed || wiResult.Value == null)
                {
                    results.Add((id, false,
                        $"Fetch failed: {wiResult.Errors.FirstOrDefault()?.Message ?? "not found"}"));
                    continue;
                }

                var wi = wiResult.Value;

                // Apply requested changes.
                if (!string.IsNullOrWhiteSpace(status))
                    wi.status = new EnumOptionId { id = status.Trim() };

                if (!string.IsNullOrWhiteSpace(priority))
                    wi.priority = new EnumOptionId { id = priority.Trim() };

                if (!string.IsNullOrWhiteSpace(severity))
                    wi.severity = new EnumOptionId { id = severity.Trim() };

                if (!string.IsNullOrWhiteSpace(assignee))
                {
                    wi.assignee = new User[] {
                        new User { id = assignee.Trim() }
                    };
                }

                // Merge custom fields.
                if (customFieldMap.Count > 0)
                {
                    var existingFields = wi.customFields?.Custom?.ToList()
                                        ?? new List<Custom>();

                    foreach (var kv in customFieldMap)
                    {
                        var existing = existingFields.FirstOrDefault(
                            f => f.id?.Equals(kv.Key, StringComparison.OrdinalIgnoreCase) == true);

                        if (existing != null)
                            existing.value = kv.Value;
                        else
                            existingFields.Add(new Custom
                                { id = kv.Key, value = kv.Value });
                    }

                    wi.customFields = new ArrayOfCustom
                    {
                        Custom = existingFields.ToArray()
                    };
                }

                // Write back.
                var updateResult = await polarionClient.UpdateWorkItemAsync(wi);
                if (updateResult.IsFailed)
                {
                    results.Add((id, false,
                        $"Update failed: {updateResult.Errors.FirstOrDefault()?.Message ?? "Unknown error"}"));
                    continue;
                }

                // Optionally add an audit comment.
                if (addAuditComment)
                {
                    await polarionClient.AddCommentAsync(
                        projectId, id,
                        $"Bulk update applied via MCP. Changes: {changeSummary}",
                        "Bulk Update Audit");
                }

                results.Add((id, true, changeSummary));
            }
            catch (Exception ex)
            {
                results.Add((id, false, $"Exception: {ex.Message}"));
            }
        }

        // -------------------------------------------------------
        // Build result report
        // -------------------------------------------------------
        var successCount = results.Count(r => r.Success);
        var failCount    = results.Count - successCount;

        var sb = new StringBuilder();
        sb.AppendLine($"## Bulk Update Results");
        sb.AppendLine();
        sb.AppendLine($"- **Total**: {ids.Count} items");
        sb.AppendLine($"- **Succeeded**: {successCount}");
        sb.AppendLine($"- **Failed**: {failCount}");
        sb.AppendLine($"- **Changes applied**: {changeSummary}");
        sb.AppendLine();
        sb.AppendLine("| WorkItem ID | Result | Detail |");
        sb.AppendLine("| --- | --- | --- |");

        foreach (var (id, success, detail) in results)
        {
            var icon = success ? "✅" : "❌";
            sb.AppendLine($"| {id} | {icon} {(success ? "Success" : "Failed")} | {detail} |");
        }

        return sb.ToString();
    }

    // =========================================================
    // TOOL 2: bulk_add_comment
    // =========================================================

    [RequiresUnreferencedCode("Uses Polarion API which requires reflection")]
    [McpServerTool(Name = "bulk_add_comment"),
     Description(
         "Posts the same comment to multiple WorkItems in a single operation. " +
         "Useful for CI/CD pipelines (post test results to all covered items), " +
         "review workflows (notify all requirements in a module), or " +
         "AI analysis outputs (attach findings to each affected item). " +
         "Maximum 50 work items per call. " +
         "Requires polarion:write scope on the remote server.")]
    public async Task<string> BulkAddComment(
        [Description(
            "Comma-separated list of WorkItem IDs (e.g., 'STR-100,STR-101,STR-102'). " +
            "Maximum 50 items.")] string workitemIds,
        [Description("Comment text to post on every item. Supports basic HTML.")] string commentText,
        [Description("Optional title for the comment. Defaults to 'Bulk Comment'.")] string? commentTitle = null)
    {
        // -------------------------------------------------------
        // Input validation
        // -------------------------------------------------------
        if (string.IsNullOrWhiteSpace(workitemIds))
            return "ERROR: (4001) workitemIds parameter cannot be empty.";

        if (string.IsNullOrWhiteSpace(commentText))
            return "ERROR: (4002) commentText parameter cannot be empty.";

        var ids = workitemIds
            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct()
            .ToList();

        if (ids.Count > BulkMaxItems)
            return $"ERROR: (4004) Too many work items ({ids.Count}). Maximum is {BulkMaxItems} per call.";

        // -------------------------------------------------------
        // Scope enforcement
        // -------------------------------------------------------
        var scopeEnforcer = _serviceProvider.GetRequiredService<IMcpScopeEnforcer>();
        var scopeError    = scopeEnforcer.CheckScope(PolarionApiScopes.Write);
        if (scopeError != null) return scopeError;

        // -------------------------------------------------------
        // Execute
        // -------------------------------------------------------
        await using var scope  = _serviceProvider.CreateAsyncScope();
        var clientFactory      = scope.ServiceProvider.GetRequiredService<IPolarionClientFactory>();
        var clientResult       = await clientFactory.CreateClientAsync();
        if (clientResult.IsFailed)
            return clientResult.Errors.FirstOrDefault()?.Message
                   ?? "ERROR: Unknown error when creating Polarion client.";

        var polarionClient = clientResult.Value;
        var projectConfig  = GetCurrentProjectConfig();
        if (projectConfig?.SessionConfig == null)
            return "ERROR: (5001) Could not determine current project configuration.";

        var projectId = projectConfig.SessionConfig.ProjectId;
        var title     = string.IsNullOrWhiteSpace(commentTitle) ? "Bulk Comment" : commentTitle.Trim();

        var results = new List<(string Id, bool Success, string Detail)>();

        foreach (var id in ids)
        {
            try
            {
                var commentResult = await polarionClient.AddCommentAsync(projectId, id, commentText, title);

                results.Add(commentResult.IsSuccess
                    ? (id, true, "Comment added")
                    : (id, false,
                        $"Failed: {commentResult.Errors.FirstOrDefault()?.Message ?? "Unknown error"}"));
            }
            catch (Exception ex)
            {
                results.Add((id, false, $"Exception: {ex.Message}"));
            }
        }

        // -------------------------------------------------------
        // Result report
        // -------------------------------------------------------
        var successCount = results.Count(r => r.Success);
        var failCount    = results.Count - successCount;

        var sb = new StringBuilder();
        sb.AppendLine($"## Bulk Comment Results");
        sb.AppendLine();
        sb.AppendLine($"- **Total**: {ids.Count} items");
        sb.AppendLine($"- **Succeeded**: {successCount}");
        sb.AppendLine($"- **Failed**: {failCount}");
        sb.AppendLine($"- **Comment Title**: {title}");
        sb.AppendLine();
        sb.AppendLine("| WorkItem ID | Result | Detail |");
        sb.AppendLine("| --- | --- | --- |");

        foreach (var (id, success, detail) in results)
        {
            var icon = success ? "✅" : "❌";
            sb.AppendLine($"| {id} | {icon} {(success ? "Success" : "Failed")} | {detail} |");
        }

        return sb.ToString();
    }
}
