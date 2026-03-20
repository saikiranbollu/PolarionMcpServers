// ============================================================================
// McpTools_WorkItemApprovals.cs
//
// MCP tools for querying and managing work item approvals in Polarion.
//
// Tools:
//   get_workitem_approvals  - Get approval status for a specific work item
//   get_pending_approvals   - Search for work items pending a user's approval
// ============================================================================

namespace PolarionMcpTools;

public sealed partial class McpTools
{
    [RequiresUnreferencedCode("Uses Polarion API which requires reflection")]
    [McpServerTool(Name = "get_workitem_approvals"),
     Description("Gets the approval status (approvers and their verdict) for a specific work item. " +
                 "Shows who needs to approve, who has approved/disapproved, and the current approval state.")]
    public async Task<string> GetWorkitemApprovals(
        [Description("The WorkItem ID (e.g., 'WI-12345').")] string workitemId)
    {
        if (string.IsNullOrWhiteSpace(workitemId))
        {
            return "ERROR: workitemId parameter cannot be empty.";
        }

        // Scope enforcement
        var scopeEnforcer = _serviceProvider.GetRequiredService<IMcpScopeEnforcer>();
        var scopeError = scopeEnforcer.CheckScope(PolarionApiScopes.Read);
        if (scopeError != null) return scopeError;

        await using (var scope = _serviceProvider.CreateAsyncScope())
        {
            var clientFactory = scope.ServiceProvider.GetRequiredService<IPolarionClientFactory>();
            var clientResult = await clientFactory.CreateClientAsync();
            if (clientResult.IsFailed)
            {
                return clientResult.Errors.FirstOrDefault()?.Message
                       ?? "ERROR: Unknown error when creating Polarion client.";
            }

            var polarionClient = clientResult.Value;

            try
            {
                // Get work item with approvals
                var approvalsResult = await polarionClient.GetWorkItemApprovalsAsync(workitemId);
                if (approvalsResult.IsFailed)
                {
                    return $"ERROR: {approvalsResult.Errors.FirstOrDefault()?.Message ?? "Unknown error"}";
                }

                var approvals = approvalsResult.Value;

                // Also get the work item basic info for context
                var wiResult = await polarionClient.GetWorkItemByIdAsync(workitemId);
                var workItem = wiResult.IsSuccess ? wiResult.Value : null;

                var sb = new StringBuilder();
                sb.AppendLine($"# Approvals for WorkItem {workitemId}");
                sb.AppendLine();

                if (workItem != null)
                {
                    sb.AppendLine($"- **Title**: {workItem.title ?? "N/A"}");
                    sb.AppendLine($"- **Type**: {workItem.type?.id ?? "N/A"}");
                    sb.AppendLine($"- **Status**: {workItem.status?.id ?? "N/A"}");
                    sb.AppendLine();
                }

                if (approvals.Length == 0)
                {
                    sb.AppendLine("No approvals configured for this work item.");
                    return sb.ToString();
                }

                sb.AppendLine($"## Approval Records ({approvals.Length} total)");
                sb.AppendLine();

                var waitingCount = 0;
                var approvedCount = 0;
                var disapprovedCount = 0;

                foreach (var approval in approvals)
                {
                    var userId = approval.user?.id ?? "Unknown";
                    var userName = approval.user?.name ?? userId;
                    var statusId = approval.status?.id ?? "unknown";

                    var statusEmoji = statusId.ToLowerInvariant() switch
                    {
                        "approved" => "[APPROVED]",
                        "disapproved" => "[DISAPPROVED]",
                        "waiting" => "[WAITING]",
                        _ => $"[{statusId.ToUpperInvariant()}]"
                    };

                    sb.AppendLine($"- {statusEmoji} **{userName}** (id={userId}) — Status: {statusId}");

                    switch (statusId.ToLowerInvariant())
                    {
                        case "waiting": waitingCount++; break;
                        case "approved": approvedCount++; break;
                        case "disapproved": disapprovedCount++; break;
                    }
                }

                sb.AppendLine();
                sb.AppendLine("## Summary");
                sb.AppendLine();
                sb.AppendLine($"- **Total Approvers**: {approvals.Length}");
                sb.AppendLine($"- **Approved**: {approvedCount}");
                sb.AppendLine($"- **Disapproved**: {disapprovedCount}");
                sb.AppendLine($"- **Waiting**: {waitingCount}");

                if (waitingCount > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"**{waitingCount} approval(s) still pending.**");
                }
                else if (disapprovedCount > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("**All approvers have voted. There are disapprovals.**");
                }
                else
                {
                    sb.AppendLine();
                    sb.AppendLine("**All approvals granted.**");
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"ERROR: Failed due to exception '{ex.Message}'";
            }
        }
    }

    [RequiresUnreferencedCode("Uses Polarion API which requires reflection")]
    [McpServerTool(Name = "get_pending_approvals"),
     Description("Searches for work items that are pending approval by a specific user. " +
                 "Finds work items where the given user is listed as an approver with 'waiting' status. " +
                 "Useful for checking 'what do I need to approve?' scenarios.")]
    public async Task<string> GetPendingApprovals(
        [Description("The Polarion user ID to check for pending approvals (e.g., 'j.smith').")] string userId,
        [Description("Optional Lucene query to further filter work items (e.g., 'type:requirement' or 'document.title:\"My Doc\"'). " +
                     "Leave empty to search all work items.")] string? additionalQuery = null,
        [Description("Maximum number of work items to scan for pending approvals. Default 200, max 1000. " +
                     "Higher values may be slower but more thorough.")] int? maxScan = 200)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return "ERROR: userId parameter cannot be empty.";
        }

        if (maxScan < 1) maxScan = 1;
        if (maxScan > 1000) maxScan = 1000;
        var effectiveMaxScan = maxScan ?? 200;

        // Scope enforcement
        var scopeEnforcer = _serviceProvider.GetRequiredService<IMcpScopeEnforcer>();
        var scopeError = scopeEnforcer.CheckScope(PolarionApiScopes.Read);
        if (scopeError != null) return scopeError;

        await using (var scope = _serviceProvider.CreateAsyncScope())
        {
            var clientFactory = scope.ServiceProvider.GetRequiredService<IPolarionClientFactory>();
            var clientResult = await clientFactory.CreateClientAsync();
            if (clientResult.IsFailed)
            {
                return clientResult.Errors.FirstOrDefault()?.Message
                       ?? "ERROR: Unknown error when creating Polarion client.";
            }

            var polarionClient = clientResult.Value;

            try
            {
                // Build a query that searches for work items that have the user as an approver.
                // Polarion supports querying by approvals.User.id in Lucene.
                var luceneQuery = $"approvals.User.id:{userId}";
                if (!string.IsNullOrWhiteSpace(additionalQuery))
                {
                    luceneQuery = $"({luceneQuery}) AND ({additionalQuery.Trim()})";
                }

                var fieldList = new List<string>
                {
                    "id", "title", "type", "status", "approvals",
                    "updated", "outlineNumber"
                };

                var searchResult = await polarionClient.SearchWorkitemAsync(
                    luceneQuery,
                    "updated",
                    fieldList);

                if (searchResult.IsFailed)
                {
                    var errorMsg = searchResult.Errors.FirstOrDefault()?.Message ?? "Unknown error";

                    // If the approvals.User.id query fails, try a fallback approach:
                    // search all work items and filter client-side
                    return await GetPendingApprovalsFallback(polarionClient, userId, additionalQuery, effectiveMaxScan);
                }

                var workItems = searchResult.Value;
                if (workItems == null || workItems.Length == 0)
                {
                    // Try fallback approach in case the Lucene field didn't work
                    return await GetPendingApprovalsFallback(polarionClient, userId, additionalQuery, effectiveMaxScan);
                }

                // Filter to only items where the user's approval status is "waiting"
                var pendingItems = new List<(WorkItem wi, Approval approval)>();
                foreach (var wi in workItems.Take(effectiveMaxScan))
                {
                    if (wi?.approvals == null) continue;

                    var userApproval = wi.approvals.FirstOrDefault(a =>
                        string.Equals(a.user?.id, userId, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(a.status?.id, "waiting", StringComparison.OrdinalIgnoreCase));

                    if (userApproval != null)
                    {
                        pendingItems.Add((wi, userApproval));
                    }
                }

                return FormatPendingApprovals(userId, pendingItems, luceneQuery, workItems.Length);
            }
            catch (Exception ex)
            {
                return $"ERROR: Failed due to exception '{ex.Message}'";
            }
        }
    }

    /// <summary>
    /// Fallback method when the Lucene approvals.User.id query is not supported.
    /// Searches for work items with a broader query and filters client-side.
    /// </summary>
    [RequiresUnreferencedCode("Uses Polarion API which requires reflection")]
    private async Task<string> GetPendingApprovalsFallback(
        IPolarionClient polarionClient,
        string userId,
        string? additionalQuery,
        int maxScan)
    {
        try
        {
            // Use a broad query — either the additional query or a catch-all
            var luceneQuery = !string.IsNullOrWhiteSpace(additionalQuery)
                ? additionalQuery.Trim()
                : "NOT HAS_VALUE:outlineNumber OR HAS_VALUE:outlineNumber";

            var fieldList = new List<string>
            {
                "id", "title", "type", "status", "approvals",
                "updated", "outlineNumber"
            };

            var searchResult = await polarionClient.SearchWorkitemAsync(
                luceneQuery,
                "updated",
                fieldList);

            if (searchResult.IsFailed)
            {
                return $"ERROR: Failed to search work items: {searchResult.Errors.FirstOrDefault()?.Message}";
            }

            var workItems = searchResult.Value;
            if (workItems == null || workItems.Length == 0)
            {
                return $"No work items found to check for pending approvals for user '{userId}'.";
            }

            // Filter to items where this user has a "waiting" approval
            var pendingItems = new List<(WorkItem wi, Approval approval)>();
            foreach (var wi in workItems.Take(maxScan))
            {
                if (wi?.approvals == null) continue;

                var userApproval = wi.approvals.FirstOrDefault(a =>
                    string.Equals(a.user?.id, userId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(a.status?.id, "waiting", StringComparison.OrdinalIgnoreCase));

                if (userApproval != null)
                {
                    pendingItems.Add((wi, userApproval));
                }
            }

            return FormatPendingApprovals(userId, pendingItems, $"(fallback) {luceneQuery}", workItems.Length);
        }
        catch (Exception ex)
        {
            return $"ERROR: Fallback search failed: '{ex.Message}'";
        }
    }

    /// <summary>
    /// Formats the pending approvals results as markdown.
    /// </summary>
    private static string FormatPendingApprovals(
        string userId,
        List<(WorkItem wi, Approval approval)> pendingItems,
        string queryUsed,
        int totalScanned)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Pending Approvals for User '{userId}'");
        sb.AppendLine();
        sb.AppendLine($"- **User**: {userId}");
        sb.AppendLine($"- **Work Items Scanned**: {totalScanned}");
        sb.AppendLine($"- **Pending Approvals Found**: {pendingItems.Count}");
        sb.AppendLine($"- **Query**: {queryUsed}");
        sb.AppendLine();

        if (pendingItems.Count == 0)
        {
            sb.AppendLine($"No work items found pending approval by '{userId}' in the scanned results.");
            sb.AppendLine();
            sb.AppendLine("**Tips:**");
            sb.AppendLine("- Try increasing `maxScan` to search more work items.");
            sb.AppendLine("- Use `additionalQuery` to narrow down to a specific document or type.");
            sb.AppendLine("- Verify the user ID is correct (Polarion user IDs are case-sensitive).");
            return sb.ToString();
        }

        sb.AppendLine("## Work Items Pending Your Approval");
        sb.AppendLine();

        foreach (var (wi, approval) in pendingItems)
        {
            var lastUpdated = wi.updatedSpecified ? wi.updated.ToString("yyyy-MM-dd HH:mm:ss") : "N/A";

            sb.AppendLine($"### {wi.id ?? "N/A"} — {wi.title ?? "N/A"}");
            sb.AppendLine();
            sb.AppendLine($"- **Type**: {wi.type?.id ?? "N/A"}");
            sb.AppendLine($"- **Status**: {wi.status?.id ?? "N/A"}");
            sb.AppendLine($"- **Outline Number**: {wi.outlineNumber ?? "N/A"}");
            sb.AppendLine($"- **Last Updated**: {lastUpdated}");
            sb.AppendLine($"- **Your Approval Status**: waiting");

            // Show other approvers' status for context
            if (wi.approvals != null && wi.approvals.Length > 1)
            {
                sb.AppendLine($"- **Other Approvers**:");
                foreach (var other in wi.approvals)
                {
                    if (string.Equals(other.user?.id, userId, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var otherUser = other.user?.name ?? other.user?.id ?? "Unknown";
                    var otherStatus = other.status?.id ?? "unknown";
                    sb.AppendLine($"  - {otherUser}: {otherStatus}");
                }
            }

            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
