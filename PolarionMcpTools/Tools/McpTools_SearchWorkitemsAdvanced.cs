// ============================================================================
// Issue #6: Advanced Lucene Query Search MCP Tool
// File: PolarionMcpTools/Tools/McpTools_SearchWorkitemsAdvanced.cs
//
// Adds a new MCP tool: search_workitems_advanced
//
// DIFFERENCE FROM search_workitems (existing tool):
//   - search_workitems: keyword-based, wraps query for you, limited to text+type+status
//   - search_workitems_advanced: accepts the raw Polarion Lucene query string directly,
//     giving full access to ALL Polarion query fields and operators.
//
// AUTOMOTIVE / ISO 26262 USE CASES:
//   - "All draft requirements modified in the last 7 days assigned to me"
//     → type:requirement AND status:draft AND assignee.id:jsmith AND updated:[NOW-7DAYS TO NOW]
//   - "All ASIL-B or higher requirements without a linked test case"
//     → type:requirement AND customField.safetyLevel:(ASIL-B ASIL-C ASIL-D)
//   - "All open defects linked to requirement PROJ-100"
//     → type:defect AND status:open AND linkedWorkItems.workItem.id:PROJ-100
// ============================================================================

namespace PolarionMcpTools;

public sealed partial class McpTools
{
    [RequiresUnreferencedCode("Uses Polarion API which requires reflection")]
    [McpServerTool(Name = "search_workitems_advanced"),
     Description(
         "Searches for WorkItems using a raw Polarion Lucene query string. " +
         "Provides full access to the Polarion query language including field filters, " +
         "date ranges, custom field values, and boolean logic. " +
         "For simple text searches, use search_workitems instead.\n\n" +
         "POLARION QUERY SYNTAX:\n" +
         "  Field search:     type:requirement status:draft\n" +
         "  Boolean AND:      type:requirement AND status:approved\n" +
         "  Boolean OR:       status:open OR status:inReview\n" +
         "  Phrase search:    title:\"voltage regulator\"\n" +
         "  Date range:       updated:[NOW-7DAYS TO NOW]\n" +
         "  Assignee:         assignee.id:jsmith\n" +
         "  Custom field:     customField.safetyLevel:ASIL-B\n" +
         "  Linked to item:   linkedWorkItems.workItem.id:PROJ-100\n" +
         "  Parentheses:      (type:requirement OR type:testCase) AND status:approved\n\n" +
         "VALID FIELD NAMES: type, status, id, title, assignee.id, author.id, " +
         "priority, severity, resolution, created, updated, outlineNumber, " +
         "document.title, document.id, customField.<fieldName>\n\n" +
         "NOTE: Do NOT prefix description fields (searches all text fields by default).")]
    public async Task<string> SearchWorkitemsAdvanced(
        [Description(
            "Full Polarion Lucene query string. Examples:\n" +
            "  type:requirement AND status:draft\n" +
            "  type:requirement AND assignee.id:jsmith AND updated:[NOW-7DAYS TO NOW]\n" +
            "  (type:requirement OR type:testCase) AND document.title:\"System Requirements\"\n" +
            "  type:defect AND priority:high AND status:(open inReview)\n" +
            "  customField.safetyLevel:(ASIL-B ASIL-C ASIL-D) AND status:approved")]
        string luceneQuery,

        [Description(
            "Comma-separated list of fields to include in the results. " +
            "Default includes: id, title, type, status, assignee, updated. " +
            "Add custom fields with 'customField.<name>' syntax. " +
            "Use 'all' for all default fields.")]
        string? fields = null,

        [Description("Sort field. Options: 'created' (default), 'updated', 'id', 'title', 'priority', 'severity'.")]
        string? sortBy = "updated",

        [Description("Sort direction: 'asc' or 'desc' (default).")]
        string? sortOrder = "desc",

        [Description("Maximum number of results to return. Default 50, max 500.")]
        int maxResults = 50)
    {
        // Input validation
        if (string.IsNullOrWhiteSpace(luceneQuery))
        {
            return "ERROR: (100) luceneQuery cannot be empty. " +
                   "Example: 'type:requirement AND status:draft'";
        }

        // Validate sortBy
        var validSortFields = new[] { "created", "updated", "id", "title", "priority", "severity" };
        var effectiveSortBy = (sortBy ?? "updated").ToLower();
        if (!validSortFields.Contains(effectiveSortBy))
        {
            return $"ERROR: (101) Invalid sortBy '{sortBy}'. " +
                   $"Valid options: {string.Join(", ", validSortFields)}.";
        }

        // Validate sortOrder
        var validOrders = new[] { "asc", "desc" };
        var effectiveSortOrder = (sortOrder ?? "desc").ToLower();
        if (!validOrders.Contains(effectiveSortOrder))
        {
            return $"ERROR: (102) Invalid sortOrder '{sortOrder}'. Must be 'asc' or 'desc'.";
        }

        // Clamp maxResults
        if (maxResults < 1) maxResults = 1;
        if (maxResults > 500) maxResults = 500;

        // Reject dangerous query patterns that cause Polarion parse errors
        // (leading wildcards are not supported)
        if (luceneQuery.TrimStart().StartsWith("*"))
        {
            return "ERROR: (103) Leading wildcards (*term) are not supported in Polarion Lucene queries. " +
                   "Remove the leading wildcard or use a field-based search instead.";
        }

        // Build field list for the API call
        var fieldList = BuildAdvancedFieldList(fields);

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
                // Pass the raw Lucene query directly to Polarion
                var searchResult = await polarionClient.SearchWorkitemAsync(
                    luceneQuery,
                    effectiveSortBy,
                    fieldList);

                if (searchResult.IsFailed)
                {
                    var errorMsg = searchResult.Errors.FirstOrDefault()?.Message ?? "Unknown error";

                    // Give Lucene-specific feedback for parse errors
                    if (errorMsg.Contains("parse", StringComparison.OrdinalIgnoreCase) ||
                        errorMsg.Contains("syntax", StringComparison.OrdinalIgnoreCase) ||
                        errorMsg.Contains("unexpected", StringComparison.OrdinalIgnoreCase))
                    {
                        return $"ERROR: (200) Polarion rejected the query due to a syntax error.\n\n" +
                               $"**Query used**: `{luceneQuery}`\n" +
                               $"**Error**: {errorMsg}\n\n" +
                               $"**Tips**:\n" +
                               $"- Wrap field values with spaces in quotes: `document.title:\"My Document\"`\n" +
                               $"- Use AND/OR in uppercase\n" +
                               $"- Check parentheses are balanced\n" +
                               $"- Do NOT use leading wildcards (*term)\n" +
                               $"- Do NOT search description fields directly; text searches all indexed fields";
                    }

                    return $"ERROR: (201) Search failed: {errorMsg}";
                }

                var workItems = searchResult.Value;

                // Apply sort direction — Polarion returns ascending by default.
                if (workItems != null && effectiveSortOrder == "desc")
                {
                    Array.Reverse(workItems);
                }

                if (workItems is null || workItems.Length == 0)
                {
                    return $"## No Results Found\n\n" +
                           $"- **Query**: `{luceneQuery}`\n" +
                           $"- No work items matched the query in the current project.\n\n" +
                           $"**Suggestions**:\n" +
                           $"- Check field names and values are correct (type IDs are case-sensitive)\n" +
                           $"- Try a broader query first: `type:requirement`\n" +
                           $"- Use `list_workitem_types` to check valid type IDs";
                }

                return FormatAdvancedSearchResults(workItems, luceneQuery, maxResults, effectiveSortBy, effectiveSortOrder);
            }
            catch (Exception ex)
            {
                return $"ERROR: (299) Search failed due to exception: {ex.Message}";
            }
        }
    }

    // -------------------------------------------------------------------------
    // Helper: Build the field list for the Polarion API call
    // -------------------------------------------------------------------------
    private static List<string> BuildAdvancedFieldList(string? fields)
    {
        // Default fields always returned
        var defaultFields = new List<string>
        {
            "id", "title", "type", "status", "assignee", "author",
            "priority", "severity", "created", "updated", "outlineNumber"
        };

        if (string.IsNullOrWhiteSpace(fields) || fields.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return defaultFields;
        }

        // User-specified fields: merge with required base fields
        var userFields = fields
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        // Always include id so we can identify work items
        if (!userFields.Contains("id", StringComparer.OrdinalIgnoreCase))
        {
            userFields.Insert(0, "id");
        }

        return userFields;
    }

    // -------------------------------------------------------------------------
    // Helper: Format results as Markdown
    // -------------------------------------------------------------------------
    private static string FormatAdvancedSearchResults(
        Polarion.WorkItem[] workItems,
        string query,
        int maxResults,
        string sortBy,
        string sortOrder)
    {
        var sb = new StringBuilder();
        var totalFound = workItems.Length;
        var displayed = Math.Min(totalFound, maxResults);

        sb.AppendLine($"## Advanced Search Results");
        sb.AppendLine();
        sb.AppendLine($"- **Query**: `{query}`");
        sb.AppendLine($"- **Results**: {displayed} shown (of {totalFound} found)");
        sb.AppendLine($"- **Sorted by**: {sortBy} ({sortOrder})");
        sb.AppendLine();

        // Group by type for better readability
        var grouped = workItems
            .Take(maxResults)
            .GroupBy(wi => wi.type?.id ?? "Unknown")
            .OrderBy(g => g.Key);

        foreach (var group in grouped)
        {
            sb.AppendLine($"### {group.Key} ({group.Count()})");
            sb.AppendLine();
            sb.AppendLine($"| ID | Title | Status | Assignee | Updated |");
            sb.AppendLine($"| --- | --- | --- | --- | --- |");

            foreach (var wi in group.OrderBy(w => w.id))
            {
                var id = wi.id ?? "N/A";
                var title = (wi.title ?? "N/A").Length > 80
                    ? (wi.title!)[..77] + "..."
                    : (wi.title ?? "N/A");
                var status = wi.status?.id ?? "N/A";
                var assigneeId = wi.assignee?.FirstOrDefault()?.id ?? "unassigned";
                var updated = wi.updatedSpecified
                    ? wi.updated.ToString("yyyy-MM-dd")
                    : "N/A";

                // Escape pipe characters in titles (Markdown table)
                title = title.Replace("|", "\\|");

                sb.AppendLine($"| {id} | {title} | {status} | {assigneeId} | {updated} |");
            }

            sb.AppendLine();
        }

        if (totalFound > maxResults)
        {
            sb.AppendLine();
            sb.AppendLine($"⚠️ **{totalFound - maxResults} more results not shown.** " +
                          $"Narrow your query or increase maxResults (max 500).");
        }

        return sb.ToString();
    }
}
