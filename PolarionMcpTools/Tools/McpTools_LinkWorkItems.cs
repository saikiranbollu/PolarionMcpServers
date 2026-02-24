// ============================================================================
// Issue #5: Link Work Items MCP Tools
// File: PolarionMcpTools/Tools/McpTools_LinkWorkItems.cs
//
// Adds two new MCP tools:
//   1. link_workitems      - creates a traceability link between two work items
//   2. unlink_workitems    - removes a traceability link between two work items
//
// Requires IPolarionClient.AddLinkedItemAsync() / RemoveLinkedItemAsync()
// See notes in each method below.
//
// NOTE ON DEPENDENCY:
//   The Polarion NuGet package (peakflames/PolarionApiClient) must expose:
//     Task<Result> AddLinkedItemAsync(string sourceWorkItemUri, LinkedWorkItem link)
//     Task<Result> RemoveLinkedItemAsync(string sourceWorkItemUri, string targetWorkItemUri, string role)
//   which wrap the SOAP TrackerWebService calls.
//   If not yet available, these methods must be added to the PolarionApiClient package.
//
// AUTOMOTIVE / ISO 26262 USE CASE:
//   link_workitems enables AI agents to build traceability chains like:
//     SW-Requirement --[verifies]--> Test Case
//     HW-Spec        --[derives]-->  SW-Requirement
//     Requirement    --[implements]-->  Code Module
// ============================================================================

namespace PolarionMcpTools;

public sealed partial class McpTools
{
    // -------------------------------------------------------------------------
    // Tool 1: link_workitems
    // -------------------------------------------------------------------------
    [RequiresUnreferencedCode("Uses Polarion API which requires reflection")]
    [McpServerTool(Name = "link_workitems"),
     Description(
         "Creates a traceability link between two WorkItems in Polarion. " +
         "Essential for building ISO 26262 / ASPICE traceability chains " +
         "(e.g., requirement → test case, HW spec → SW requirement). " +
         "The source WorkItem is the one that 'owns' the link. " +
         "Common link roles: 'verifies', 'validates', 'implements', 'derives', " +
         "'tests', 'refines', 'duplicates'. Use get_workitem_details to inspect existing links.")]
    public async Task<string> LinkWorkItems(
        [Description("The source WorkItem ID (e.g., 'PROJ-100'). This item will have an outgoing link to the target.")]
        string sourceWorkitemId,

        [Description("The target WorkItem ID (e.g., 'PROJ-200'). This item receives an incoming link from the source.")]
        string targetWorkitemId,

        [Description(
            "The link role ID defining the relationship type. " +
            "Common automotive roles: 'verifies' (test→req), 'implements' (code→req), " +
            "'derives' (sw-req→hw-spec), 'validates', 'refines', 'duplicates'. " +
            "Must match a valid role configured in the Polarion project.")]
        string linkRole,

        [Description(
            "If true, also creates the reverse link from target back to source using the opposite role. " +
            "Default is false. " +
            "Note: Polarion often manages back-links automatically via 'linkedWorkItemsDerived'.")]
        bool createReverseLink = false)
    {
        // Input validation
        if (string.IsNullOrWhiteSpace(sourceWorkitemId))
        {
            return "ERROR: (100) sourceWorkitemId cannot be empty.";
        }

        if (string.IsNullOrWhiteSpace(targetWorkitemId))
        {
            return "ERROR: (101) targetWorkitemId cannot be empty.";
        }

        if (string.IsNullOrWhiteSpace(linkRole))
        {
            return "ERROR: (102) linkRole cannot be empty. " +
                   "Common roles: 'verifies', 'validates', 'implements', 'derives', 'tests', 'refines'.";
        }

        if (sourceWorkitemId.Equals(targetWorkitemId, StringComparison.OrdinalIgnoreCase))
        {
            return "ERROR: (103) sourceWorkitemId and targetWorkitemId cannot be the same work item.";
        }

        // Scope enforcement — write operation
        var scopeEnforcer = _serviceProvider.GetRequiredService<IMcpScopeEnforcer>();
        var scopeError = scopeEnforcer.CheckScope(PolarionApiScopes.Write);
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
                // Verify source work item exists and get its URI
                var sourceResult = await polarionClient.GetWorkItemByIdAsync(sourceWorkitemId);
                if (sourceResult.IsFailed || sourceResult.Value is null)
                {
                    return $"ERROR: (200) Source WorkItem '{sourceWorkitemId}' not found. " +
                           $"Verify the ID is correct.";
                }

                var sourceWorkItem = sourceResult.Value;

                // Verify target work item exists and get its URI
                var targetResult = await polarionClient.GetWorkItemByIdAsync(targetWorkitemId);
                if (targetResult.IsFailed || targetResult.Value is null)
                {
                    return $"ERROR: (201) Target WorkItem '{targetWorkitemId}' not found. " +
                           $"Verify the ID is correct.";
                }

                var targetWorkItem = targetResult.Value;

                // Check for duplicate link (avoid creating the same link twice)
                var existingLinks = sourceWorkItem.linkedWorkItems ?? Array.Empty<LinkedWorkItem>();
                var duplicateExists = existingLinks.Any(link =>
                {
                    var linkedId = link.workItemURI?.Split("${WorkItem}").LastOrDefault() ?? "";
                    var roleId = (link.role as EnumOptionId)?.id ?? link.role?.ToString() ?? "";
                    return string.Equals(linkedId, targetWorkitemId, StringComparison.OrdinalIgnoreCase)
                           && string.Equals(roleId, linkRole, StringComparison.OrdinalIgnoreCase);
                });

                if (duplicateExists)
                {
                    return $"## Link Already Exists\n\n" +
                           $"- A '{linkRole}' link from '{sourceWorkitemId}' to '{targetWorkitemId}' already exists.\n" +
                           $"- No action taken.";
                }

                // Build the LinkedWorkItem structure for the SOAP call
                var linkedWorkItem = new LinkedWorkItem
                {
                    workItemURI = targetWorkItem.uri,
                    role = new EnumOptionId { id = linkRole },
                    suspect = false,
                    suspectSpecified = true
                };

                // Call Polarion API to add the link
                // NOTE: Requires IPolarionClient.AddLinkedItemAsync() in PolarionApiClient package
                var addResult = await polarionClient.AddLinkedItemAsync(sourceWorkItem.uri, linkedWorkItem);
                if (addResult.IsFailed)
                {
                    var errorMsg = addResult.Errors.FirstOrDefault()?.Message ?? "Unknown error";
                    return $"ERROR: (300) Failed to create link from '{sourceWorkitemId}' to '{targetWorkitemId}' " +
                           $"with role '{linkRole}': {errorMsg}. " +
                           $"Verify the link role is valid for this project.";
                }

                var sb = new StringBuilder();
                sb.AppendLine($"## Traceability Link Created Successfully");
                sb.AppendLine();
                sb.AppendLine($"- **Source**: {sourceWorkitemId} ({sourceWorkItem.type?.id ?? "N/A"}: {sourceWorkItem.title ?? "N/A"})");
                sb.AppendLine($"- **Link Role**: {linkRole}");
                sb.AppendLine($"- **Target**: {targetWorkitemId} ({targetWorkItem.type?.id ?? "N/A"}: {targetWorkItem.title ?? "N/A"})");

                // Optionally create reverse link
                if (createReverseLink)
                {
                    var reverseRole = GetReverseRole(linkRole);
                    var reverseLinkedWorkItem = new LinkedWorkItem
                    {
                        workItemURI = sourceWorkItem.uri,
                        role = new EnumOptionId { id = reverseRole },
                        suspect = false,
                        suspectSpecified = true
                    };

                    var reverseResult = await polarionClient.AddLinkedItemAsync(targetWorkItem.uri, reverseLinkedWorkItem);
                    if (reverseResult.IsFailed)
                    {
                        sb.AppendLine();
                        sb.AppendLine($"⚠️ **Warning**: Forward link created, but reverse link failed: " +
                                      $"{reverseResult.Errors.FirstOrDefault()?.Message ?? "Unknown error"}. " +
                                      $"This may be normal if Polarion auto-manages back-links.");
                    }
                    else
                    {
                        sb.AppendLine($"- **Reverse link also created**: {targetWorkitemId} --[{reverseRole}]--> {sourceWorkitemId}");
                    }
                }

                sb.AppendLine();
                sb.AppendLine($"Use `get_workitem_details` with id `{sourceWorkitemId}` to verify the traceability links.");

                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"ERROR: (399) Failed to link work items due to exception: {ex.Message}";
            }
        }
    }

    // -------------------------------------------------------------------------
    // Tool 2: unlink_workitems
    // -------------------------------------------------------------------------
    [RequiresUnreferencedCode("Uses Polarion API which requires reflection")]
    [McpServerTool(Name = "unlink_workitems"),
     Description(
         "Removes a traceability link between two WorkItems in Polarion. " +
         "Use get_workitem_details first to see existing links and their exact role IDs. " +
         "Both the source ID, target ID, and link role must match an existing link exactly.")]
    public async Task<string> UnlinkWorkItems(
        [Description("The source WorkItem ID that owns the link (e.g., 'PROJ-100').")]
        string sourceWorkitemId,

        [Description("The target WorkItem ID that the link points to (e.g., 'PROJ-200').")]
        string targetWorkitemId,

        [Description("The exact link role ID to remove (e.g., 'verifies', 'implements'). Must match the existing link role exactly.")]
        string linkRole)
    {
        // Input validation
        if (string.IsNullOrWhiteSpace(sourceWorkitemId))
        {
            return "ERROR: (100) sourceWorkitemId cannot be empty.";
        }

        if (string.IsNullOrWhiteSpace(targetWorkitemId))
        {
            return "ERROR: (101) targetWorkitemId cannot be empty.";
        }

        if (string.IsNullOrWhiteSpace(linkRole))
        {
            return "ERROR: (102) linkRole cannot be empty.";
        }

        // Scope enforcement — write operation
        var scopeEnforcer2 = _serviceProvider.GetRequiredService<IMcpScopeEnforcer>();
        var scopeError2 = scopeEnforcer2.CheckScope(PolarionApiScopes.Write);
        if (scopeError2 != null) return scopeError2;

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
                // Verify source work item exists
                var sourceResult = await polarionClient.GetWorkItemByIdAsync(sourceWorkitemId);
                if (sourceResult.IsFailed || sourceResult.Value is null)
                {
                    return $"ERROR: (200) Source WorkItem '{sourceWorkitemId}' not found.";
                }

                var sourceWorkItem = sourceResult.Value;

                // Verify target work item exists  
                var targetResult = await polarionClient.GetWorkItemByIdAsync(targetWorkitemId);
                if (targetResult.IsFailed || targetResult.Value is null)
                {
                    return $"ERROR: (201) Target WorkItem '{targetWorkitemId}' not found.";
                }

                var targetWorkItem = targetResult.Value;

                // Check the link actually exists before attempting removal
                var existingLinks = sourceWorkItem.linkedWorkItems ?? Array.Empty<LinkedWorkItem>();
                var linkExists = existingLinks.Any(link =>
                {
                    var linkedId = link.workItemURI?.Split("${WorkItem}").LastOrDefault() ?? "";
                    var roleId = (link.role as EnumOptionId)?.id ?? link.role?.ToString() ?? "";
                    return string.Equals(linkedId, targetWorkitemId, StringComparison.OrdinalIgnoreCase)
                           && string.Equals(roleId, linkRole, StringComparison.OrdinalIgnoreCase);
                });

                if (!linkExists)
                {
                    return $"ERROR: (202) No '{linkRole}' link found from '{sourceWorkitemId}' to '{targetWorkitemId}'. " +
                           $"Use get_workitem_details to see existing outgoing links and their exact role IDs.";
                }

                // Build the LinkedWorkItem for removal
                var linkedWorkItemToRemove = new LinkedWorkItem
                {
                    workItemURI = targetWorkItem.uri,
                    role = new EnumOptionId { id = linkRole }
                };

                // Call Polarion API to remove the link
                // NOTE: Requires IPolarionClient.RemoveLinkedItemAsync() in PolarionApiClient package
                var removeResult = await polarionClient.RemoveLinkedItemAsync(sourceWorkItem.uri, linkedWorkItemToRemove);
                if (removeResult.IsFailed)
                {
                    var errorMsg = removeResult.Errors.FirstOrDefault()?.Message ?? "Unknown error";
                    return $"ERROR: (300) Failed to remove link from '{sourceWorkitemId}' to '{targetWorkitemId}': {errorMsg}";
                }

                return $"## Traceability Link Removed Successfully\n\n" +
                       $"- **Removed**: {sourceWorkitemId} --[{linkRole}]--> {targetWorkitemId}\n\n" +
                       $"Use `get_workitem_details` with id `{sourceWorkitemId}` to verify the link was removed.";
            }
            catch (Exception ex)
            {
                return $"ERROR: (399) Failed to unlink work items due to exception: {ex.Message}";
            }
        }
    }

    // -------------------------------------------------------------------------
    // Helper: get a sensible reverse role for bidirectional linking
    // -------------------------------------------------------------------------
    private static string GetReverseRole(string role) =>
        role.ToLower() switch
        {
            "verifies"   => "verified_by",
            "verified_by" => "verifies",
            "validates"  => "validated_by",
            "validated_by" => "validates",
            "implements" => "implemented_by",
            "implemented_by" => "implements",
            "derives"    => "derived_from",
            "derived_from" => "derives",
            "tests"      => "tested_by",
            "tested_by"  => "tests",
            "refines"    => "refined_by",
            "refined_by" => "refines",
            "duplicates" => "duplicated_by",
            "duplicated_by" => "duplicates",
            _            => $"{role}_reverse"   // fallback for custom roles
        };
}
