// ============================================================
// FILE: PolarionMcpTools/Tools/McpTools_TraceabilityGraph.cs
// ============================================================
// TOOL: get_traceability_graph
//
// PURPOSE: Traverses linked work items and renders a Mermaid
//   flowchart diagram of the traceability chain.  AI agents can
//   embed the output directly in markdown reports.
//
// SCOPE REQUIREMENT: polarion:read
//
// AUTOMOTIVE USE CASES (ISO 26262 / ASPICE):
//   • Requirement → Test coverage gap analysis
//   • HW-spec → SW-req → implementation → test full chain
//   • Safety goal → ASIL decomposition tree
//   • Identify orphaned requirements (no linked tests)
//   • Impact analysis: what tests cover requirement STR-100?
//
// OUTPUT FORMAT:
//   Mermaid LR flowchart with:
//     • Nodes:  ID[TYPE: Title (Status)]
//     • Edges:  SOURCE -->|roleLabel| TARGET
//     • Colour: green = tested/approved, orange = inReview,
//               red = rejected, blue = draft, grey = unknown
// ============================================================

namespace PolarionMcpTools;

public sealed partial class McpTools
{
    [RequiresUnreferencedCode("Uses Polarion API which requires reflection")]
    [McpServerTool(Name = "get_traceability_graph"),
     Description(
         "Builds a Mermaid flowchart showing the traceability graph for one or more WorkItems. " +
         "Traverses linked work items up to the specified depth and renders directional link roles " +
         "(e.g., verifies, implements, tests, derives). " +
         "Paste the output into any Markdown renderer that supports Mermaid (GitHub, Obsidian, etc.). " +
         "Ideal for ISO 26262 / ASPICE traceability reports: requirement → test, " +
         "HW-spec → SW-req → implementation chain, and safety goal decomposition trees.")]
    public async Task<string> GetTraceabilityGraph(
        [Description("Comma-separated list of root WorkItem IDs to start from (e.g., 'STR-100,STR-101').")] string workitemIds,
        [Description(
            "Maximum link depth to traverse. 1 = direct links only, max 4. " +
            "Higher values may take longer and produce large graphs.")] int depth = 2,
        [Description(
            "Link direction to follow: " +
            "'outgoing' (items this links TO), " +
            "'incoming' (items linking TO this), " +
            "'both'.")] string direction = "both",
        [Description(
            "Filter by link role. Comma-separated (e.g., 'verifies,implements,tests'). " +
            "Leave empty to include all link roles.")] string? linkRoleFilter = null,
        [Description(
            "Include work item status colour coding in the graph nodes. " +
            "Colour legend: green=approved/tested, orange=inReview, red=rejected, blue=draft."
        )] bool colourCode = true)
    {
        // -------------------------------------------------------
        // Input validation
        // -------------------------------------------------------
        if (string.IsNullOrWhiteSpace(workitemIds))
            return "ERROR: (4001) workitemIds parameter cannot be empty.";

        if (depth < 1) depth = 1;
        if (depth > 4) depth = 4;

        var validDirections = new[] { "outgoing", "incoming", "both" };
        direction = (direction ?? "both").ToLower().Trim();
        if (!validDirections.Contains(direction))
            return $"ERROR: (4002) direction must be one of: {string.Join(", ", validDirections)}. Got: '{direction}'";

        var rootIds = workitemIds
            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct()
            .ToList();

        var roleFilters = string.IsNullOrWhiteSpace(linkRoleFilter)
            ? new HashSet<string>()
            : linkRoleFilter.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(r => r.ToLower())
                .ToHashSet();

        // Scope check
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
        var markdownConverter = new ReverseMarkdown.Converter();

        // -------------------------------------------------------
        // Graph data structures
        // -------------------------------------------------------
        // node  key  = workitemId
        // node  label = "TYPE: Title (Status)"
        // edges = (fromId, toId, role)
        var nodes   = new Dictionary<string, (string Label, string Status)>(StringComparer.OrdinalIgnoreCase);
        var edges   = new HashSet<string>(); // deduplicated "from|to|role"
        var edgeList = new List<(string From, string To, string Role)>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // -------------------------------------------------------
        // Recursive traversal
        // -------------------------------------------------------
        async Task TraverseAsync(string id, int currentDepth)
        {
            if (currentDepth > depth || visited.Contains(id)) return;
            visited.Add(id);

            var wiResult = await polarionClient.GetWorkItemByIdAsync(id);
            if (wiResult.IsFailed || wiResult.Value == null) return;

            var wi = wiResult.Value;
            var type   = wi.type?.id    ?? "workitem";
            var title  = wi.title       ?? "(no title)";
            var status = wi.status?.id  ?? "unknown";

            // Truncate long titles for readability.
            if (title.Length > 50) title = title[..47] + "...";
            // Escape Mermaid special chars.
            title  = title.Replace("\"", "'").Replace("[", "(").Replace("]", ")");
            var label = $"{type}: {title} ({status})";
            nodes[id] = (label, status);

            if (currentDepth >= depth) return; // stop at leaf, don't add more edges

            // ---- Outgoing links (this WI → linked) ---
            if ((direction == "outgoing" || direction == "both")
                && wi.linkedWorkItems != null)
            {
                foreach (var link in wi.linkedWorkItems)
                {
                    var role = Utils.PolarionValueToString(link.role, markdownConverter)
                               ?? "linked";
                    if (roleFilters.Count > 0 && !roleFilters.Contains(role.ToLower())) continue;

                    var targetId = ExtractWorkItemId(link.workItemURI);
                    if (string.IsNullOrEmpty(targetId)) continue;

                    var edgeKey = $"{id}|{targetId}|{role}";
                    if (edges.Add(edgeKey))
                        edgeList.Add((id, targetId, role));

                    await TraverseAsync(targetId, currentDepth + 1);
                }
            }

            // ---- Incoming links (linked → this WI) ---
            if ((direction == "incoming" || direction == "both")
                && wi.linkedWorkItemsDerived != null)
            {
                foreach (var link in wi.linkedWorkItemsDerived)
                {
                    var role = Utils.PolarionValueToString(link.role, markdownConverter)
                               ?? "linked";
                    if (role == "subsection_of") continue;
                    if (roleFilters.Count > 0 && !roleFilters.Contains(role.ToLower())) continue;

                    var sourceId = ExtractWorkItemId(link.workItemURI);
                    if (string.IsNullOrEmpty(sourceId)) continue;

                    var edgeKey = $"{sourceId}|{id}|{role}";
                    if (edges.Add(edgeKey))
                        edgeList.Add((sourceId, id, role));

                    await TraverseAsync(sourceId, currentDepth + 1);
                }
            }
        }

        try
        {
            foreach (var rootId in rootIds)
                await TraverseAsync(rootId, 1);
        }
        catch (Exception ex)
        {
            return $"ERROR: (5099) Traversal failed: {ex.Message}";
        }

        if (nodes.Count == 0)
            return $"No WorkItems found for the given IDs: {workitemIds}";

        // -------------------------------------------------------
        // Render Mermaid diagram
        // -------------------------------------------------------
        var sb = new StringBuilder();
        sb.AppendLine("## Traceability Graph");
        sb.AppendLine();
        sb.AppendLine($"- **Root IDs**: {string.Join(", ", rootIds)}");
        sb.AppendLine($"- **Depth**: {depth} | **Direction**: {direction}");
        sb.AppendLine($"- **Nodes**: {nodes.Count} | **Links**: {edgeList.Count}");
        if (roleFilters.Count > 0)
            sb.AppendLine($"- **Role filter**: {string.Join(", ", roleFilters)}");
        sb.AppendLine();

        sb.AppendLine("```mermaid");
        sb.AppendLine("flowchart LR");

        // Node declarations with labels
        foreach (var (nodeId, (label, status)) in nodes)
        {
            var safeId = nodeId.Replace("-", "_");
            sb.AppendLine($"    {safeId}[\"{label}\"]");
        }
        sb.AppendLine();

        // Colour coding via classDef + class assignments
        if (colourCode)
        {
            sb.AppendLine("    classDef approved  fill:#22c55e,color:#fff,stroke:#16a34a");
            sb.AppendLine("    classDef inreview   fill:#f97316,color:#fff,stroke:#ea580c");
            sb.AppendLine("    classDef rejected   fill:#ef4444,color:#fff,stroke:#dc2626");
            sb.AppendLine("    classDef draft      fill:#3b82f6,color:#fff,stroke:#2563eb");
            sb.AppendLine("    classDef unknown    fill:#9ca3af,color:#fff,stroke:#6b7280");
            sb.AppendLine();

            foreach (var (nodeId, (_, status)) in nodes)
            {
                var safeId    = nodeId.Replace("-", "_");
                var cssClass  = status.ToLower() switch
                {
                    "approved" or "closed" or "done" or "verified" or "accepted" => "approved",
                    "inreview" or "in_review" or "review"                        => "inreview",
                    "rejected" or "invalid" or "wontfix"                         => "rejected",
                    "draft" or "open" or "new" or "created"                      => "draft",
                    _                                                             => "unknown"
                };
                sb.AppendLine($"    class {safeId} {cssClass}");
            }
            sb.AppendLine();
        }

        // Edge declarations
        foreach (var (from, to, role) in edgeList)
        {
            var safeFrom = from.Replace("-", "_");
            var safeTo   = to.Replace("-", "_");
            // Ensure both endpoints are declared (may have been cut by depth)
            if (!nodes.ContainsKey(from))
                sb.AppendLine($"    {safeFrom}[\"{from}\"]");
            if (!nodes.ContainsKey(to))
                sb.AppendLine($"    {safeTo}[\"{to}\"]");

            sb.AppendLine($"    {safeFrom} -->|\"{role}\"| {safeTo}");
        }

        sb.AppendLine("```");
        sb.AppendLine();

        // -------------------------------------------------------
        // Highlight root nodes and orphans
        // -------------------------------------------------------
        var linkedIds  = edgeList.SelectMany(e => new[] { e.From, e.To }).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var orphans    = nodes.Keys.Where(n => !linkedIds.Contains(n)).ToList();

        if (orphans.Count > 0)
        {
            sb.AppendLine($"> ⚠️ **Orphaned nodes** (no links found within depth {depth}): " +
                          string.Join(", ", orphans));
            sb.AppendLine();
        }

        sb.AppendLine("_Paste the Mermaid block above into GitHub, GitLab, Obsidian, or any " +
                      "Mermaid-compatible renderer to visualize._");

        return sb.ToString();
    }

    // -------------------------------------------------------
    // Helper: extract work item ID from Polarion URI
    // e.g. "subterra:data-service:objects:/default/..." → "STR-123"
    // -------------------------------------------------------
    private static string? ExtractWorkItemId(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return null;

        // Polarion URI pattern: ...${WorkItem}STR-123
        const string marker = "${WorkItem}";
        var idx = uri.IndexOf(marker, StringComparison.Ordinal);
        if (idx >= 0)
            return uri[(idx + marker.Length)..].Trim();

        // Fallback: last segment after '/'
        var parts = uri.TrimEnd('/').Split('/');
        return parts.Length > 0 ? parts[^1] : null;
    }
}
