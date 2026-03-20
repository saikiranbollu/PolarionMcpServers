// ============================================================================
// PolarionClientWriteExtensions.cs
//
// Extension methods for IPolarionClient that provide write operations
// (create, update, link, unlink, workflow, comments) by wrapping the
// underlying TrackerWebService SOAP calls.
//
// These methods bridge the gap until the Polarion NuGet package
// (peakflames/PolarionApiClient) adds native support for these operations.
//
// SOAP Mapping:
//   CreateWorkItemAsync                → trackerService.createWorkItemAsync(workItem)
//   UpdateWorkItemAsync                → trackerService.updateWorkItemAsync(workItem)
//   AddLinkedItemAsync                 → trackerService.addLinkedItemAsync(uri, linkedUri, role)
//   RemoveLinkedItemAsync              → trackerService.removeLinkedItemAsync(uri, linkedUri, role)
//   GetAvailableWorkflowActionsAsync   → trackerService.getAvailableActionsAsync(workitemURI)
//   PerformWorkflowActionAsync         → trackerService.performWorkflowActionAsync(workitemURI, actionId)
//   AddCommentAsync                    → trackerService.addCommentAsync(parentObjectUri, title, content)
//   GetWorkItemApprovalsAsync          → client.GetWorkItemByIdAsync(id) → workItem.approvals
//   GetAllowedApproversAsync           → trackerService.getAllowedApproversAsync(workitemURI)
//   AddApproveeAsync                   → trackerService.addApproveeAsync(workitemURI, approveeId)
//   EditApprovalAsync                  → trackerService.editApprovalAsync(workitemURI, approveeId, status)
//   RemoveApproveeAsync                → trackerService.removeApproveeAsync(workitemURI, approveeId)
// ============================================================================

namespace PolarionMcpTools;

/// <summary>
/// Represents a single available workflow action (status transition) for a work item.
/// </summary>
public record WorkflowActionInfo(
    /// <summary>The action identifier. Prefers the native string ID (e.g., "approve")
    /// and falls back to the numeric SOAP actionId.</summary>
    string ActionId,
    /// <summary>Human-readable display name for the action.</summary>
    string NativeName,
    /// <summary>The target status id the work item will move to (may be null if unknown).</summary>
    string? TargetStatus);

/// <summary>
/// Extension methods for <see cref="IPolarionClient"/> that provide write operations
/// by wrapping the underlying TrackerWebService SOAP calls with FluentResults error handling.
/// </summary>
public static class PolarionClientWriteExtensions
{
    /// <summary>
    /// Creates a new WorkItem in the specified Polarion project.
    /// Wraps <c>TrackerWebService.createWorkItemAsync()</c> SOAP call.
    /// </summary>
    /// <param name="client">The Polarion client instance.</param>
    /// <param name="projectId">The Polarion project ID to create the work item in.</param>
    /// <param name="workItem">The work item to create, with at minimum type and title set.</param>
    /// <returns>A <c>Result&lt;string&gt;</c> containing the new work item ID on success, or error details.</returns>
    [RequiresUnreferencedCode("Uses WCF services which require reflection")]
    public static async Task<Result<string>> CreateWorkItemAsync(
        this IPolarionClient client,
        string projectId,
        WorkItem workItem)
    {
        try
        {
            // Set the project on the WorkItem so Polarion knows which project to create in.
            // The SOAP createWorkItem uses the WorkItem's project field.
            workItem.project = new Project { id = projectId };

            var response = await client.TrackerService.createWorkItemAsync(
                new createWorkItemRequest { content = workItem });
            var uri = response?.createWorkItemReturn;

            if (string.IsNullOrEmpty(uri))
            {
                return Result.Fail("WorkItem was created but no URI was returned by Polarion.");
            }

            // Extract work item ID from the returned URI
            var workItemId = ExtractWorkItemIdFromUri(uri);
            return Result.Ok(workItemId ?? uri);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to create WorkItem: {ex.Message}");
        }
    }

    /// <summary>
    /// Updates an existing WorkItem in Polarion. The work item must have its URI set
    /// (obtained via <c>GetWorkItemByIdAsync</c> first).
    /// Wraps <c>TrackerWebService.updateWorkItemAsync()</c> SOAP call.
    /// </summary>
    /// <param name="client">The Polarion client instance.</param>
    /// <param name="workItem">The work item with updated fields. Must have its URI set.</param>
    /// <returns>A <c>Result</c> indicating success or failure.</returns>
    [RequiresUnreferencedCode("Uses WCF services which require reflection")]
    public static async Task<Result> UpdateWorkItemAsync(
        this IPolarionClient client,
        WorkItem workItem)
    {
        try
        {
            if (string.IsNullOrEmpty(workItem.uri))
            {
                return Result.Fail(
                    "WorkItem URI is not set. Fetch the work item first using GetWorkItemByIdAsync to obtain its URI.");
            }

            await client.TrackerService.updateWorkItemAsync(
                new updateWorkItemRequest { content = workItem });
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to update WorkItem: {ex.Message}");
        }
    }

    /// <summary>
    /// Adds a traceability link from a source work item to a target work item.
    /// Wraps <c>TrackerWebService.addLinkedItemAsync()</c> SOAP call.
    /// </summary>
    /// <param name="client">The Polarion client instance.</param>
    /// <param name="sourceWorkItemUri">The URI of the source (owning) work item.</param>
    /// <param name="linkedWorkItem">The linked work item descriptor containing target URI and link role.</param>
    /// <returns>A <c>Result</c> indicating success or failure.</returns>
    [RequiresUnreferencedCode("Uses WCF services which require reflection")]
    public static async Task<Result> AddLinkedItemAsync(
        this IPolarionClient client,
        string sourceWorkItemUri,
        LinkedWorkItem linkedWorkItem)
    {
        try
        {
            if (string.IsNullOrEmpty(sourceWorkItemUri))
            {
                return Result.Fail("Source work item URI cannot be empty.");
            }

            if (string.IsNullOrEmpty(linkedWorkItem.workItemURI))
            {
                return Result.Fail("Target work item URI (linkedWorkItem.workItemURI) cannot be empty.");
            }

            var role = linkedWorkItem.role as EnumOptionId;
            if (role == null || string.IsNullOrEmpty(role.id))
            {
                return Result.Fail("Link role must be specified as an EnumOptionId with a non-empty id.");
            }

            // The SOAP method takes a request wrapper with URI, URI, and role fields
            await client.TrackerService.addLinkedItemAsync(
                new addLinkedItemRequest
                {
                    workitemURI       = sourceWorkItemUri,
                    linkedWorkitemURI = linkedWorkItem.workItemURI,
                    role              = role
                });

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to add linked item: {ex.Message}");
        }
    }

    /// <summary>
    /// Removes a traceability link between two work items.
    /// Wraps <c>TrackerWebService.removeLinkedItemAsync()</c> SOAP call.
    /// </summary>
    /// <param name="client">The Polarion client instance.</param>
    /// <param name="sourceWorkItemUri">The URI of the source (owning) work item.</param>
    /// <param name="linkedWorkItem">The linked work item descriptor identifying the link to remove (target URI and role).</param>
    /// <returns>A <c>Result</c> indicating success or failure.</returns>
    [RequiresUnreferencedCode("Uses WCF services which require reflection")]
    public static async Task<Result> RemoveLinkedItemAsync(
        this IPolarionClient client,
        string sourceWorkItemUri,
        LinkedWorkItem linkedWorkItem)
    {
        try
        {
            if (string.IsNullOrEmpty(sourceWorkItemUri))
            {
                return Result.Fail("Source work item URI cannot be empty.");
            }

            if (string.IsNullOrEmpty(linkedWorkItem.workItemURI))
            {
                return Result.Fail("Target work item URI (linkedWorkItem.workItemURI) cannot be empty.");
            }

            var role = linkedWorkItem.role as EnumOptionId;
            if (role == null || string.IsNullOrEmpty(role.id))
            {
                return Result.Fail("Link role must be specified.");
            }

            // The SOAP method takes a request wrapper with URI, URI, and role fields
            await client.TrackerService.removeLinkedItemAsync(
                new removeLinkedItemRequest
                {
                    workitemURI   = sourceWorkItemUri,
                    linkedItemURI = linkedWorkItem.workItemURI,
                    role          = role
                });

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to remove linked item: {ex.Message}");
        }
    }

    /// <summary>
    /// Extracts the work item ID from a Polarion URI.
    /// URI format: <c>subterra:data-service:objects:/default/{project}${WorkItem}{id}</c>
    /// </summary>
    private static string? ExtractWorkItemIdFromUri(string uri)
    {
        // Primary format: subterra:data-service:objects:/default/ProjectName${WorkItem}WI-123
        var marker = "${WorkItem}";
        var idx = uri.IndexOf(marker, StringComparison.Ordinal);
        if (idx >= 0)
        {
            return uri[(idx + marker.Length)..];
        }

        // Fallback: return the last segment after the last /
        var lastSlash = uri.LastIndexOf('/');
        if (lastSlash >= 0 && lastSlash < uri.Length - 1)
        {
            return uri[(lastSlash + 1)..];
        }

        return null;
    }

    // =========================================================================
    // Workflow & Comment Extensions
    // =========================================================================

    /// <summary>
    /// Retrieves the list of available workflow actions (status transitions) for a work item.
    /// Wraps <c>TrackerWebService.getAvailableActionsAsync(workitemURI)</c>.
    /// </summary>
    /// <param name="client">The Polarion client instance.</param>
    /// <param name="projectId">The Polarion project ID (used to construct the work item URI).</param>
    /// <param name="workItemId">The work item ID (e.g., "REQ-1234").</param>
    /// <returns>A <c>Result&lt;IEnumerable&lt;WorkflowActionInfo&gt;&gt;</c> with available transitions.</returns>
    [RequiresUnreferencedCode("Uses WCF services which require reflection")]
    public static async Task<Result<IEnumerable<WorkflowActionInfo>>> GetAvailableWorkflowActionsAsync(
        this IPolarionClient client,
        string projectId,
        string workItemId)
    {
        try
        {
            // Fetch the work item to obtain its canonical URI.
            var wiResult = await client.GetWorkItemByIdAsync(workItemId);
            if (wiResult.IsFailed)
            {
                return Result.Fail<IEnumerable<WorkflowActionInfo>>(
                    $"Could not fetch work item '{workItemId}': {wiResult.Errors.FirstOrDefault()?.Message}");
            }

            var workItem = wiResult.Value;
            if (workItem == null || string.IsNullOrEmpty(workItem.uri))
            {
                return Result.Fail<IEnumerable<WorkflowActionInfo>>(
                    $"Work item '{workItemId}' has no URI.");
            }

            var response = await client.TrackerService.getAvailableActionsAsync(
                new getAvailableActionsRequest { workitemURI = workItem.uri });
            var actions = response?.getAvailableActionsReturn;

            if (actions == null || actions.Length == 0)
            {
                return Result.Ok(Enumerable.Empty<WorkflowActionInfo>());
            }

            var result = actions.Select(a => new WorkflowActionInfo(
                ActionId: a.nativeActionId ?? a.actionId.ToString(),
                NativeName: a.actionName ?? a.nativeActionId ?? a.actionId.ToString(),
                TargetStatus: a.targetStatus?.id)).ToList();

            return Result.Ok<IEnumerable<WorkflowActionInfo>>(result);
        }
        catch (Exception ex)
        {
            return Result.Fail<IEnumerable<WorkflowActionInfo>>(
                $"Failed to get workflow actions for '{workItemId}': {ex.Message}");
        }
    }

    /// <summary>
    /// Performs a workflow action (status transition) on a work item.
    /// Wraps <c>TrackerWebService.performWorkflowActionAsync(workitemURI, actionId)</c>.
    /// </summary>
    /// <param name="client">The Polarion client instance.</param>
    /// <param name="projectId">The Polarion project ID.</param>
    /// <param name="workItemId">The work item ID (e.g., "REQ-1234").</param>
    /// <param name="actionId">The action identifier — either a native action ID string (e.g., "approve")
    /// or a numeric action ID. The method resolves the string to the SOAP integer ID automatically.</param>
    /// <returns>A <c>Result</c> indicating success or failure.</returns>
    [RequiresUnreferencedCode("Uses WCF services which require reflection")]
    public static async Task<Result> PerformWorkflowActionAsync(
        this IPolarionClient client,
        string projectId,
        string workItemId,
        string actionId)
    {
        try
        {
            // Fetch the work item to obtain its canonical URI.
            var wiResult = await client.GetWorkItemByIdAsync(workItemId);
            if (wiResult.IsFailed)
            {
                return Result.Fail(
                    $"Could not fetch work item '{workItemId}': {wiResult.Errors.FirstOrDefault()?.Message}");
            }

            var workItem = wiResult.Value;
            if (workItem == null || string.IsNullOrEmpty(workItem.uri))
            {
                return Result.Fail($"Work item '{workItemId}' has no URI.");
            }

            // The SOAP method requires an int actionId.
            // If the caller passed a numeric string, use it directly.
            // Otherwise, resolve the nativeActionId → int via getAvailableActionsAsync.
            int soapActionId;
            if (int.TryParse(actionId, out soapActionId))
            {
                // Caller passed a numeric ID — use directly.
            }
            else
            {
                // Resolve the native string action ID to the SOAP int.
                var actionsResponse = await client.TrackerService.getAvailableActionsAsync(
                    new getAvailableActionsRequest { workitemURI = workItem.uri });
                var actions = actionsResponse?.getAvailableActionsReturn;

                if (actions == null || actions.Length == 0)
                {
                    return Result.Fail(
                        $"No workflow actions available for '{workItemId}'. " +
                        $"Cannot resolve action '{actionId}'.");
                }

                var match = actions.FirstOrDefault(a =>
                    string.Equals(a.nativeActionId, actionId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a.actionName, actionId, StringComparison.OrdinalIgnoreCase));

                if (match == null)
                {
                    var available = string.Join(", ", actions.Select(a =>
                        $"'{a.nativeActionId ?? a.actionId.ToString()}'"));
                    return Result.Fail(
                        $"Action '{actionId}' not found for '{workItemId}'. " +
                        $"Available actions: {available}");
                }

                soapActionId = match.actionId;
            }

            await client.TrackerService.performWorkflowActionAsync(
                new performWorkflowActionRequest { workitemURI = workItem.uri, actionId = soapActionId });
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail(
                $"Failed to perform workflow action '{actionId}' on '{workItemId}': {ex.Message}");
        }
    }

    /// <summary>
    /// Adds a comment to a Polarion work item.
    /// Wraps <c>TrackerWebService.addCommentAsync(parentObjectUri, title, content)</c>.
    /// </summary>
    /// <param name="client">The Polarion client instance.</param>
    /// <param name="projectId">The Polarion project ID.</param>
    /// <param name="workItemId">The work item ID to add a comment to.</param>
    /// <param name="commentText">The comment text/body. Supports basic HTML for rich text.</param>
    /// <param name="commentTitle">The comment title/subject.</param>
    /// <returns>A <c>Result</c> indicating success or failure.</returns>
    [RequiresUnreferencedCode("Uses WCF services which require reflection")]
    public static async Task<Result> AddCommentAsync(
        this IPolarionClient client,
        string projectId,
        string workItemId,
        string commentText,
        string commentTitle)
    {
        try
        {
            // Fetch the work item to obtain its canonical URI.
            var wiResult = await client.GetWorkItemByIdAsync(workItemId);
            if (wiResult.IsFailed)
            {
                return Result.Fail(
                    $"Could not fetch work item '{workItemId}': {wiResult.Errors.FirstOrDefault()?.Message}");
            }

            var workItem = wiResult.Value;
            if (workItem == null || string.IsNullOrEmpty(workItem.uri))
            {
                return Result.Fail($"Work item '{workItemId}' has no URI.");
            }

            // Build the Text object for the comment body.
            var content = new Text
            {
                type = "text/html",
                content = commentText,
                contentLossy = false
            };

            await client.TrackerService.addCommentAsync(
                new addCommentRequest { parentObjectUri = workItem.uri, title = commentTitle, content = content });
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail(
                $"Failed to add comment to '{workItemId}': {ex.Message}");
        }
    }

    // =========================================================================
    // Approval Extensions
    // =========================================================================

    /// <summary>
    /// Retrieves the list of approvals for a work item by fetching the work item with the approvals field.
    /// </summary>
    /// <param name="client">The Polarion client instance.</param>
    /// <param name="workItemId">The work item ID (e.g., "REQ-1234").</param>
    /// <returns>A <c>Result&lt;Approval[]&gt;</c> with the approvals (may be empty).</returns>
    [RequiresUnreferencedCode("Uses WCF services which require reflection")]
    public static async Task<Result<Approval[]>> GetWorkItemApprovalsAsync(
        this IPolarionClient client,
        string workItemId)
    {
        try
        {
            var wiResult = await client.GetWorkItemByIdAsync(workItemId);
            if (wiResult.IsFailed)
            {
                return Result.Fail<Approval[]>(
                    $"Could not fetch work item '{workItemId}': {wiResult.Errors.FirstOrDefault()?.Message}");
            }

            var workItem = wiResult.Value;
            if (workItem == null)
            {
                return Result.Fail<Approval[]>($"Work item '{workItemId}' not found.");
            }

            return Result.Ok(workItem.approvals ?? Array.Empty<Approval>());
        }
        catch (Exception ex)
        {
            return Result.Fail<Approval[]>(
                $"Failed to get approvals for '{workItemId}': {ex.Message}");
        }
    }

    /// <summary>
    /// Retrieves the list of users allowed to approve a work item.
    /// Wraps <c>TrackerWebService.getAllowedApproversAsync(workitemURI)</c>.
    /// </summary>
    /// <param name="client">The Polarion client instance.</param>
    /// <param name="workItemId">The work item ID (e.g., "REQ-1234").</param>
    /// <returns>A <c>Result&lt;User[]&gt;</c> with the allowed approvers.</returns>
    [RequiresUnreferencedCode("Uses WCF services which require reflection")]
    public static async Task<Result<User[]>> GetAllowedApproversAsync(
        this IPolarionClient client,
        string workItemId)
    {
        try
        {
            var wiResult = await client.GetWorkItemByIdAsync(workItemId);
            if (wiResult.IsFailed)
            {
                return Result.Fail<User[]>(
                    $"Could not fetch work item '{workItemId}': {wiResult.Errors.FirstOrDefault()?.Message}");
            }

            var workItem = wiResult.Value;
            if (workItem == null || string.IsNullOrEmpty(workItem.uri))
            {
                return Result.Fail<User[]>($"Work item '{workItemId}' has no URI.");
            }

            var response = await client.TrackerService.getAllowedApproversAsync(
                new getAllowedApproversRequest { workitemURI = workItem.uri });

            return Result.Ok(response?.getAllowedApproversReturn ?? Array.Empty<User>());
        }
        catch (Exception ex)
        {
            return Result.Fail<User[]>(
                $"Failed to get allowed approvers for '{workItemId}': {ex.Message}");
        }
    }

    /// <summary>
    /// Adds an approver to a work item.
    /// Wraps <c>TrackerWebService.addApproveeAsync(workitemURI, approveeId)</c>.
    /// </summary>
    /// <param name="client">The Polarion client instance.</param>
    /// <param name="workItemId">The work item ID (e.g., "REQ-1234").</param>
    /// <param name="approveeUserId">The Polarion user ID to add as approver.</param>
    /// <returns>A <c>Result</c> indicating success or failure.</returns>
    [RequiresUnreferencedCode("Uses WCF services which require reflection")]
    public static async Task<Result> AddApproveeAsync(
        this IPolarionClient client,
        string workItemId,
        string approveeUserId)
    {
        try
        {
            var wiResult = await client.GetWorkItemByIdAsync(workItemId);
            if (wiResult.IsFailed)
            {
                return Result.Fail(
                    $"Could not fetch work item '{workItemId}': {wiResult.Errors.FirstOrDefault()?.Message}");
            }

            var workItem = wiResult.Value;
            if (workItem == null || string.IsNullOrEmpty(workItem.uri))
            {
                return Result.Fail($"Work item '{workItemId}' has no URI.");
            }

            await client.TrackerService.addApproveeAsync(
                new addApproveeRequest { workitemURI = workItem.uri, approveeId = approveeUserId });

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail(
                $"Failed to add approver '{approveeUserId}' to '{workItemId}': {ex.Message}");
        }
    }

    /// <summary>
    /// Edits an approval status on a work item.
    /// Wraps <c>TrackerWebService.editApprovalAsync(workitemURI, approveeId, status)</c>.
    /// </summary>
    /// <param name="client">The Polarion client instance.</param>
    /// <param name="workItemId">The work item ID (e.g., "REQ-1234").</param>
    /// <param name="approveeUserId">The Polarion user ID whose approval to edit.</param>
    /// <param name="newStatus">The new approval status (e.g., "approved", "disapproved", "waiting").</param>
    /// <returns>A <c>Result</c> indicating success or failure.</returns>
    [RequiresUnreferencedCode("Uses WCF services which require reflection")]
    public static async Task<Result> EditApprovalAsync(
        this IPolarionClient client,
        string workItemId,
        string approveeUserId,
        string newStatus)
    {
        try
        {
            var wiResult = await client.GetWorkItemByIdAsync(workItemId);
            if (wiResult.IsFailed)
            {
                return Result.Fail(
                    $"Could not fetch work item '{workItemId}': {wiResult.Errors.FirstOrDefault()?.Message}");
            }

            var workItem = wiResult.Value;
            if (workItem == null || string.IsNullOrEmpty(workItem.uri))
            {
                return Result.Fail($"Work item '{workItemId}' has no URI.");
            }

            await client.TrackerService.editApprovalAsync(
                new editApprovalRequest
                {
                    workitemURI = workItem.uri,
                    approveeId = approveeUserId,
                    status = new EnumOptionId { id = newStatus }
                });

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail(
                $"Failed to edit approval for '{approveeUserId}' on '{workItemId}': {ex.Message}");
        }
    }

    /// <summary>
    /// Removes an approver from a work item.
    /// Wraps <c>TrackerWebService.removeApproveeAsync(workitemURI, approveeId)</c>.
    /// </summary>
    /// <param name="client">The Polarion client instance.</param>
    /// <param name="workItemId">The work item ID (e.g., "REQ-1234").</param>
    /// <param name="approveeUserId">The Polarion user ID to remove as approver.</param>
    /// <returns>A <c>Result</c> indicating success or failure.</returns>
    [RequiresUnreferencedCode("Uses WCF services which require reflection")]
    public static async Task<Result> RemoveApproveeAsync(
        this IPolarionClient client,
        string workItemId,
        string approveeUserId)
    {
        try
        {
            var wiResult = await client.GetWorkItemByIdAsync(workItemId);
            if (wiResult.IsFailed)
            {
                return Result.Fail(
                    $"Could not fetch work item '{workItemId}': {wiResult.Errors.FirstOrDefault()?.Message}");
            }

            var workItem = wiResult.Value;
            if (workItem == null || string.IsNullOrEmpty(workItem.uri))
            {
                return Result.Fail($"Work item '{workItemId}' has no URI.");
            }

            await client.TrackerService.removeApproveeAsync(
                new removeApproveeRequest { workitemURI = workItem.uri, approveeId = approveeUserId });

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail(
                $"Failed to remove approver '{approveeUserId}' from '{workItemId}': {ex.Message}");
        }
    }
}
