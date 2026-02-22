// ============================================================================
// Issue #3: Create Work Item MCP Tool
// File: PolarionMcpTools/Tools/McpTools_CreateWorkItem.cs
//
// Adds a new MCP tool: create_workitem
// Requires IPolarionClient.CreateWorkItemAsync() - see notes below.
//
// NOTE ON DEPENDENCY:
//   The Polarion NuGet package (peakflames/PolarionApiClient) must expose:
//     Task<Result<string>> CreateWorkItemAsync(string projectId, WorkItem workItem)
//   which wraps the SOAP TrackerWebService.createWorkItem() call.
//   If not yet available, this method must be added to the PolarionApiClient package first.
// ============================================================================

namespace PolarionMcpTools;

public sealed partial class McpTools
{
    [RequiresUnreferencedCode("Uses Polarion API which requires reflection")]
    [McpServerTool(Name = "create_workitem"),
     Description(
         "Creates a new WorkItem in the Polarion project. " +
         "Use list_workitem_types to find valid type IDs. " +
         "Use list_custom_fields to discover available custom fields for the type. " +
         "Returns the new WorkItem ID on success.")]
    public async Task<string> CreateWorkItem(
        [Description("The WorkItem type ID (e.g., 'requirement', 'defect', 'testCase'). Use list_workitem_types to see available types.")]
        string workitemType,

        [Description("Title / short description of the new WorkItem.")]
        string title,

        [Description("Optional description content (plain text or basic HTML). Supports Polarion rich text markup.")]
        string? description = null,

        [Description("Optional assignee Polarion username (e.g., 'j.smith').")]
        string? assignee = null,

        [Description("Optional priority ID (e.g., 'high', 'medium', 'low'). Must match a valid Polarion enum value.")]
        string? priority = null,

        [Description("Optional severity ID (e.g., 'major', 'minor', 'critical'). Must match a valid Polarion enum value.")]
        string? severity = null,

        [Description(
            "Optional custom fields as key=value pairs, one per line. " +
            "Example: 'myCustomField=value1\\nsafetyLevel=ASIL-B'. " +
            "Use list_custom_fields to discover available field names.")]
        string? customFields = null)
    {
        // Input validation
        if (string.IsNullOrWhiteSpace(workitemType))
        {
            return "ERROR: (100) workitemType parameter cannot be empty.";
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return "ERROR: (101) title parameter cannot be empty.";
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
            var projectConfig = GetCurrentProjectConfig();

            if (projectConfig?.SessionConfig?.ProjectId is null)
            {
                return "ERROR: (104) Could not determine the current Polarion project ID from configuration.";
            }

            var projectId = projectConfig.SessionConfig.ProjectId;

            try
            {
                // Build WorkItem object for creation
                var newWorkItem = new Polarion.WorkItem
                {
                    title = title,
                    type = new Polarion.EnumOptionId { id = workitemType },
                };

                // Set description if provided
                if (!string.IsNullOrWhiteSpace(description))
                {
                    newWorkItem.description = new Polarion.Text
                    {
                        content = description,
                        type = "text/html",
                        contentLossy = false,
                        contentLossySpecified = true
                    };
                }

                // Set assignee if provided
                if (!string.IsNullOrWhiteSpace(assignee))
                {
                    newWorkItem.assignee = new[] { new Polarion.User { id = assignee } };
                }

                // Set priority if provided
                if (!string.IsNullOrWhiteSpace(priority))
                {
                    newWorkItem.priority = new Polarion.EnumOptionId { id = priority };
                }

                // Set severity if provided
                if (!string.IsNullOrWhiteSpace(severity))
                {
                    newWorkItem.severity = new Polarion.EnumOptionId { id = severity };
                }

                // Set custom fields if provided
                if (customFieldDict.Count > 0)
                {
                    newWorkItem.customFields = customFieldDict
                        .Select(kvp => new Polarion.Custom
                        {
                            key = kvp.Key,
                            value = new Polarion.StringType { value = kvp.Value }
                        })
                        .ToArray();
                }

                // Call Polarion API to create the work item
                // NOTE: Requires IPolarionClient.CreateWorkItemAsync() in PolarionApiClient package
                var createResult = await polarionClient.CreateWorkItemAsync(projectId, newWorkItem);
                if (createResult.IsFailed)
                {
                    var errorMsg = createResult.Errors.FirstOrDefault()?.Message ?? "Unknown error";
                    return $"ERROR: (200) Failed to create WorkItem of type '{workitemType}': {errorMsg}";
                }

                var newWorkItemId = createResult.Value;
                if (string.IsNullOrWhiteSpace(newWorkItemId))
                {
                    return "ERROR: (201) WorkItem was created but no ID was returned by Polarion.";
                }

                // Build success summary
                var sb = new StringBuilder();
                sb.AppendLine($"## WorkItem Created Successfully");
                sb.AppendLine();
                sb.AppendLine($"- **New ID**: {newWorkItemId}");
                sb.AppendLine($"- **Type**: {workitemType}");
                sb.AppendLine($"- **Title**: {title}");
                sb.AppendLine($"- **Project**: {projectId}");

                if (!string.IsNullOrWhiteSpace(assignee))
                {
                    sb.AppendLine($"- **Assignee**: {assignee}");
                }

                if (!string.IsNullOrWhiteSpace(priority))
                {
                    sb.AppendLine($"- **Priority**: {priority}");
                }

                if (!string.IsNullOrWhiteSpace(severity))
                {
                    sb.AppendLine($"- **Severity**: {severity}");
                }

                if (customFieldDict.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"### Custom Fields Set");
                    foreach (var kvp in customFieldDict)
                    {
                        sb.AppendLine($"- **{kvp.Key}**: {kvp.Value}");
                    }
                }

                sb.AppendLine();
                sb.AppendLine($"Use `get_workitem_details` with id `{newWorkItemId}` to verify the created work item.");

                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"ERROR: (299) Failed to create WorkItem due to exception: {ex.Message}";
            }
        }
    }
}
