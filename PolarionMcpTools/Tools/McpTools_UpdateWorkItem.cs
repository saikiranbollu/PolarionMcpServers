// ============================================================================
// Issue #2: Update Work Item MCP Tool
// File: PolarionMcpTools/Tools/McpTools_UpdateWorkItem.cs
//
// Adds a new MCP tool: update_workitem
// Requires IPolarionClient.UpdateWorkItemAsync() - see notes below.
//
// NOTE ON DEPENDENCY:
//   The Polarion NuGet package (peakflames/PolarionApiClient) must expose:
//     Task<Result> UpdateWorkItemAsync(WorkItem workItem)
//   which wraps the SOAP TrackerWebService.updateWorkItem() call.
//   The WorkItem passed must have its URI set (obtained via GetWorkItemByIdAsync first).
//   If not yet available, this method must be added to the PolarionApiClient package.
// ============================================================================

namespace PolarionMcpTools;

public sealed partial class McpTools
{
    [RequiresUnreferencedCode("Uses Polarion API which requires reflection")]
    [McpServerTool(Name = "update_workitem"),
     Description(
         "Updates an existing WorkItem in Polarion. " +
         "Only specified fields are updated; omitted parameters leave existing values unchanged. " +
         "To update status, use the status parameter (must be a valid workflow transition). " +
         "Use get_workitem_details first to verify current state before updating.")]
    public async Task<string> UpdateWorkItem(
        [Description("The WorkItem ID to update (e.g., 'PROJ-1234').")]
        string workitemId,

        [Description("New title for the WorkItem. Leave null to keep existing title.")]
        string? title = null,

        [Description("New description content (plain text or HTML). Leave null to keep existing description.")]
        string? description = null,

        [Description(
            "New status ID. Must be a valid workflow state for the work item type " +
            "(e.g., 'open', 'inReview', 'approved', 'closed'). Leave null to keep current status.")]
        string? status = null,

        [Description("New assignee username. Leave null to keep existing assignee.")]
        string? assignee = null,

        [Description("New priority ID (e.g., 'high', 'medium', 'low'). Leave null to keep current value.")]
        string? priority = null,

        [Description("New severity ID (e.g., 'major', 'minor', 'critical'). Leave null to keep current value.")]
        string? severity = null,

        [Description(
            "Custom fields to update as key=value pairs, one per line. " +
            "Example: 'safetyLevel=ASIL-B\\nverificationStatus=verified'. " +
            "Only listed fields are updated. Use list_custom_fields for available field names.")]
        string? customFields = null)
    {
        // Input validation
        if (string.IsNullOrWhiteSpace(workitemId))
        {
            return "ERROR: (100) workitemId parameter cannot be empty.";
        }

        // Check that at least one field is being updated
        var anyFieldProvided = !string.IsNullOrWhiteSpace(title)
                               || !string.IsNullOrWhiteSpace(description)
                               || !string.IsNullOrWhiteSpace(status)
                               || !string.IsNullOrWhiteSpace(assignee)
                               || !string.IsNullOrWhiteSpace(priority)
                               || !string.IsNullOrWhiteSpace(severity)
                               || !string.IsNullOrWhiteSpace(customFields);

        if (!anyFieldProvided)
        {
            return "ERROR: (101) At least one field must be provided to update (title, description, status, assignee, priority, severity, or customFields).";
        }

        // Parse custom fields (key=value lines)
        var customFieldDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(customFields))
        {
            foreach (var line in customFields.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var eqIdx = line.IndexOf('=');
                if (eqIdx <= 0)
                {
                    return $"ERROR: (102) Invalid custom field format '{line}'. Expected 'fieldName=value'.";
                }

                var key = line[..eqIdx].Trim();
                var val = line[(eqIdx + 1)..].Trim();
                if (string.IsNullOrWhiteSpace(key))
                {
                    return $"ERROR: (103) Empty field name in custom fields at line '{line}'.";
                }

                customFieldDict[key] = val;
            }
        }

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
                // First fetch the existing work item to get its URI and current state
                var existingResult = await polarionClient.GetWorkItemByIdAsync(workitemId);
                if (existingResult.IsFailed)
                {
                    return $"ERROR: (200) Could not retrieve WorkItem '{workitemId}': " +
                           $"{existingResult.Errors.FirstOrDefault()?.Message ?? "Unknown error"}. " +
                           $"Verify the ID is correct and the work item exists.";
                }

                var existingWorkItem = existingResult.Value;
                if (existingWorkItem is null)
                {
                    return $"ERROR: (201) WorkItem '{workitemId}' not found.";
                }

                // Track what's actually being changed for the response summary
                var changes = new List<string>();

                // Apply only the provided fields (partial update pattern)
                if (!string.IsNullOrWhiteSpace(title))
                {
                    existingWorkItem.title = title;
                    changes.Add($"title → \"{title}\"");
                }

                if (!string.IsNullOrWhiteSpace(description))
                {
                    existingWorkItem.description = new Polarion.Text
                    {
                        content = description,
                        type = "text/html",
                        contentLossy = false,
                        contentLossySpecified = true
                    };
                    changes.Add("description updated");
                }

                if (!string.IsNullOrWhiteSpace(status))
                {
                    var previousStatus = existingWorkItem.status?.id ?? "N/A";
                    existingWorkItem.status = new Polarion.EnumOptionId { id = status };
                    changes.Add($"status: {previousStatus} → {status}");
                }

                if (!string.IsNullOrWhiteSpace(assignee))
                {
                    existingWorkItem.assignee = new[] { new Polarion.User { id = assignee } };
                    changes.Add($"assignee → {assignee}");
                }

                if (!string.IsNullOrWhiteSpace(priority))
                {
                    existingWorkItem.priority = new Polarion.EnumOptionId { id = priority };
                    changes.Add($"priority → {priority}");
                }

                if (!string.IsNullOrWhiteSpace(severity))
                {
                    existingWorkItem.severity = new Polarion.EnumOptionId { id = severity };
                    changes.Add($"severity → {severity}");
                }

                // Merge custom fields: update existing ones, add new ones
                if (customFieldDict.Count > 0)
                {
                    var existingCustomFields = existingWorkItem.customFields?.ToList()
                                              ?? new List<Polarion.Custom>();

                    foreach (var kvp in customFieldDict)
                    {
                        var existingField = existingCustomFields
                            .FirstOrDefault(f => string.Equals(f.key, kvp.Key, StringComparison.OrdinalIgnoreCase));

                        if (existingField != null)
                        {
                            existingField.value = new Polarion.StringType { value = kvp.Value };
                        }
                        else
                        {
                            existingCustomFields.Add(new Polarion.Custom
                            {
                                key = kvp.Key,
                                value = new Polarion.StringType { value = kvp.Value }
                            });
                        }

                        changes.Add($"custom:{kvp.Key} → {kvp.Value}");
                    }

                    existingWorkItem.customFields = existingCustomFields.ToArray();
                }

                // Call Polarion API to persist changes
                // NOTE: Requires IPolarionClient.UpdateWorkItemAsync() in PolarionApiClient package
                var updateResult = await polarionClient.UpdateWorkItemAsync(existingWorkItem);
                if (updateResult.IsFailed)
                {
                    var errorMsg = updateResult.Errors.FirstOrDefault()?.Message ?? "Unknown error";
                    return $"ERROR: (300) Failed to update WorkItem '{workitemId}': {errorMsg}. " +
                           $"If updating status, ensure '{status}' is a valid workflow transition from the current state.";
                }

                // Build success summary
                var sb = new StringBuilder();
                sb.AppendLine($"## WorkItem Updated Successfully");
                sb.AppendLine();
                sb.AppendLine($"- **ID**: {workitemId}");
                sb.AppendLine($"- **Type**: {existingWorkItem.type?.id ?? "N/A"}");
                sb.AppendLine();
                sb.AppendLine($"### Changes Applied ({changes.Count})");
                foreach (var change in changes)
                {
                    sb.AppendLine($"- {change}");
                }

                sb.AppendLine();
                sb.AppendLine($"Use `get_workitem_details` with id `{workitemId}` to verify the updated work item.");

                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"ERROR: (399) Failed to update WorkItem '{workitemId}' due to exception: {ex.Message}";
            }
        }
    }
}
