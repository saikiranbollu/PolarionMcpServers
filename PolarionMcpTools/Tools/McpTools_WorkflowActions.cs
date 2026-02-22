// ============================================================
// FILE: PolarionMcpTools/Tools/McpTools_WorkflowActions.cs
// ============================================================
// TOOLS:
//   • get_workflow_actions(workitemId)      — READ  (polarion:read)
//   • perform_workflow_action(workitemId,   — WRITE (polarion:write)
//                             actionId,
//                             comment?)
//
// SOAP DEPENDENCIES:
//   IPolarionClient.GetAvailableWorkflowActionsAsync(projectId, workItemId)
//   → maps to: TrackerWebService.getAvailableWorkflowActions(projectId, workItem)
//   → returns: WorkflowAction[] with .actionId and .nativeName
//
//   IPolarionClient.PerformWorkflowActionAsync(projectId, workItemId, actionId)
//   → maps to: TrackerWebService.performWorkflowAction(projectId, workItemId, actionId, null)
//
//   Requires PolarionApiClient >= 2.1.0
//
// AUTOMOTIVE USE CASES (ISO 26262 / ASPICE):
//   • AI review agent transitions requirements from "draft" → "inReview"
//   • CI/CD approves test cases after automated execution
//   • Safety manager bulk-promotes items through the review gate
//   • AI agent lists available actions before choosing the right transition
// ============================================================

namespace PolarionMcpTools;

public sealed partial class McpTools
{
    // =========================================================
    // TOOL 1: get_workflow_actions
    // Lists all workflow transitions available for a work item.
    // =========================================================

    [RequiresUnreferencedCode("Uses Polarion API which requires reflection")]
    [McpServerTool(Name = "get_workflow_actions"),
     Description(
         "Lists all available workflow actions (status transitions) for a WorkItem. " +
         "Use this before calling perform_workflow_action to discover valid actionIds. " +
         "Returns a table of actionId + displayName pairs. " +
         "Available actions depend on the item's current status and the caller's Polarion role.")]
    public async Task<string> GetWorkflowActions(
        [Description("The WorkItem ID (e.g., 'STR-1234').")] string workitemId)
    {
        if (string.IsNullOrWhiteSpace(workitemId))
            return "ERROR: (4001) workitemId parameter cannot be empty.";

        // Read-only — only polarion:read scope required.
        var scopeEnforcer = _serviceProvider.GetRequiredService<IMcpScopeEnforcer>();
        var scopeError    = scopeEnforcer.CheckScope(PolarionApiScopes.Read);
        if (scopeError != null) return scopeError;

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

        try
        {
            // First, retrieve the work item to confirm it exists and get current status.
            var workItemResult = await polarionClient.GetWorkItemByIdAsync(workitemId);
            if (workItemResult.IsFailed)
                return $"ERROR: (5020) WorkItem '{workitemId}' not found: " +
                       $"{workItemResult.Errors.FirstOrDefault()?.Message ?? "Unknown error"}";

            var workItem = workItemResult.Value;
            if (workItem == null)
                return $"ERROR: (5021) WorkItem '{workitemId}' returned null.";

            // Retrieve available workflow actions.
            var actionsResult = await polarionClient.GetAvailableWorkflowActionsAsync(projectId, workitemId);
            if (actionsResult.IsFailed)
                return $"ERROR: (5022) Could not retrieve workflow actions for '{workitemId}': " +
                       $"{actionsResult.Errors.FirstOrDefault()?.Message ?? "Unknown error"}";

            var actions = actionsResult.Value;

            var sb = new StringBuilder();
            sb.AppendLine($"## Available Workflow Actions");
            sb.AppendLine();
            sb.AppendLine($"- **WorkItem**: {workitemId}");
            sb.AppendLine($"- **Type**: {workItem.type?.id ?? "N/A"}");
            sb.AppendLine($"- **Current Status**: {workItem.status?.id ?? "N/A"}");
            sb.AppendLine($"- **Project**: {projectId}");
            sb.AppendLine();

            if (actions == null || !actions.Any())
            {
                sb.AppendLine("_No workflow actions are currently available for this work item._");
                sb.AppendLine();
                sb.AppendLine("> This may mean the item is in a terminal state, " +
                              "or you lack the Polarion role required to transition it.");
                return sb.ToString();
            }

            sb.AppendLine($"| Action ID | Display Name | Target Status |");
            sb.AppendLine($"| --- | --- | --- |");

            foreach (var action in actions)
            {
                var actionId     = action.ActionId     ?? "N/A";
                var displayName  = action.NativeName    ?? action.ActionId ?? "N/A";
                var targetStatus = action.TargetStatus  ?? "N/A";
                sb.AppendLine($"| `{actionId}` | {displayName} | {targetStatus} |");
            }

            sb.AppendLine();
            sb.AppendLine($"Use `perform_workflow_action` with the desired **Action ID** to execute the transition.");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"ERROR: (5099) Failed to get workflow actions for '{workitemId}': {ex.Message}";
        }
    }

    // =========================================================
    // TOOL 2: perform_workflow_action
    // Executes a status transition on a work item.
    // =========================================================

    [RequiresUnreferencedCode("Uses Polarion API which requires reflection")]
    [McpServerTool(Name = "perform_workflow_action"),
     Description(
         "Performs a workflow action (status transition) on a WorkItem. " +
         "Call get_workflow_actions first to obtain valid actionIds for the current state. " +
         "Requires polarion:write scope on the remote server. " +
         "Example: transition a requirement from 'draft' to 'inReview', " +
         "or approve a test case after automated execution.")]
    public async Task<string> PerformWorkflowAction(
        [Description("The WorkItem ID to transition (e.g., 'STR-1234').")] string workitemId,
        [Description(
            "The workflow action ID to execute. " +
            "Obtain valid IDs via get_workflow_actions. " +
            "Example: 'start_review', 'approve', 'reject', 'close'.")] string actionId,
        [Description(
            "Optional comment to attach to the transition. " +
            "Useful for audit trails — e.g., 'Approved after ASIL B review by safety manager'."
        )] string? transitionComment = null)
    {
        // -------------------------------------------------------
        // Input validation
        // -------------------------------------------------------
        if (string.IsNullOrWhiteSpace(workitemId))
            return "ERROR: (4001) workitemId parameter cannot be empty.";

        if (string.IsNullOrWhiteSpace(actionId))
            return "ERROR: (4002) actionId parameter cannot be empty. " +
                   "Use get_workflow_actions to list valid action IDs.";

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

        try
        {
            // Capture the status BEFORE the transition for the response.
            var beforeResult = await polarionClient.GetWorkItemByIdAsync(workitemId);
            var statusBefore = beforeResult.IsSuccess
                ? beforeResult.Value?.status?.id ?? "unknown"
                : "unknown";

            // Perform the workflow action.
            var actionResult = await polarionClient.PerformWorkflowActionAsync(
                projectId, workitemId, actionId.Trim());

            if (actionResult.IsFailed)
            {
                return $"ERROR: (5030) Failed to perform action '{actionId}' on '{workitemId}': " +
                       $"{actionResult.Errors.FirstOrDefault()?.Message ?? "Unknown error"}. " +
                       $"Verify the action ID via get_workflow_actions and that you hold the " +
                       $"required Polarion role.";
            }

            // Optionally add a comment to record the transition reason.
            string commentStatus = "N/A";
            if (!string.IsNullOrWhiteSpace(transitionComment))
            {
                var commentResult = await polarionClient.AddCommentAsync(
                    projectId, workitemId, transitionComment,
                    $"Workflow Transition: {actionId}");

                commentStatus = commentResult.IsSuccess
                    ? "Added"
                    : $"Failed — {commentResult.Errors.FirstOrDefault()?.Message}";
            }

            // Retrieve updated status.
            var afterResult  = await polarionClient.GetWorkItemByIdAsync(workitemId);
            var statusAfter  = afterResult.IsSuccess
                ? afterResult.Value?.status?.id ?? "unknown"
                : "unknown (re-fetch failed)";

            var sb = new StringBuilder();
            sb.AppendLine($"## Workflow Action Performed Successfully");
            sb.AppendLine();
            sb.AppendLine($"- **WorkItem**: {workitemId}");
            sb.AppendLine($"- **Project**: {projectId}");
            sb.AppendLine($"- **Action ID**: `{actionId}`");
            sb.AppendLine($"- **Status Before**: `{statusBefore}`");
            sb.AppendLine($"- **Status After**: `{statusAfter}`");

            if (!string.IsNullOrWhiteSpace(transitionComment))
            {
                sb.AppendLine($"- **Comment**: {commentStatus}");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"ERROR: (5099) Failed to perform workflow action '{actionId}' on '{workitemId}': {ex.Message}";
        }
    }
}
