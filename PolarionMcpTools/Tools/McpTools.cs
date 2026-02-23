using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection; // Added for IServiceProvider and CreateAsyncScope
using ModelContextProtocol.Server;
using Polarion;
using Polarion.Generated.Tracker;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System; // Added for IServiceProvider
using System.Collections.Generic; // Added for List<>
using System.Linq; // Added for LINQ operations

namespace PolarionMcpTools;

public sealed partial class McpTools
{
    private readonly IServiceProvider _serviceProvider;

    public McpTools(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Gets the current project configuration based on the project ID from the client factory.
    /// </summary>
    /// <returns>The current project configuration, or null if not found.</returns>
    private PolarionProjectConfig? GetCurrentProjectConfig()
    {
        // Get the current project ID from the client factory
        var clientFactory = _serviceProvider.GetRequiredService<IPolarionClientFactory>();
        string? projectId = clientFactory.ProjectId;
        
        // Get all project configurations
        var projectConfigs = _serviceProvider.GetRequiredService<List<PolarionProjectConfig>>();
        
        // Find the matching configuration
        return projectConfigs.FirstOrDefault(p => 
            p.ProjectUrlAlias.Equals(projectId, StringComparison.OrdinalIgnoreCase)) 
            ?? projectConfigs.FirstOrDefault(p => p.Default);
    }

    // ------------------------------------------------------------------
    // Shared custom-field helpers (used by create, update, bulk tools)
    // ------------------------------------------------------------------

    /// <summary>
    /// Parses custom fields from a newline-separated "key=value" string.
    /// Returns a <see cref="Result{T}"/> with the parsed dictionary, or a failure with a descriptive error message.
    /// </summary>
    private static Result<Dictionary<string, string>> ParseCustomFields(string? customFields)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(customFields))
            return Result.Ok(dict);

        foreach (var line in customFields.Split('\n',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eqIdx = line.IndexOf('=');
            if (eqIdx <= 0)
                return Result.Fail<Dictionary<string, string>>(
                    $"Invalid custom field format '{line}'. Expected 'fieldName=value'.");

            var key = line[..eqIdx].Trim();
            var val = line[(eqIdx + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(key))
                return Result.Fail<Dictionary<string, string>>(
                    $"Empty field name in custom fields at line '{line}'.");

            dict[key] = val;
        }

        return Result.Ok(dict);
    }

    /// <summary>
    /// Converts a custom-field dictionary into a <see cref="Custom"/> array suitable for new work items.
    /// </summary>
    private static Custom[] ToCustomFieldArray(Dictionary<string, string> fields)
    {
        return fields.Select(kvp => new Custom { key = kvp.Key, value = kvp.Value }).ToArray();
    }

    /// <summary>
    /// Merges custom-field updates into an existing array. Existing fields are updated by key;
    /// new fields are appended. Returns the merged array.
    /// </summary>
    private static Custom[] MergeCustomFields(Custom[]? existing, Dictionary<string, string> updates)
    {
        var list = existing?.ToList() ?? new List<Custom>();
        foreach (var kvp in updates)
        {
            var field = list.FirstOrDefault(
                f => string.Equals(f.key, kvp.Key, StringComparison.OrdinalIgnoreCase));

            if (field != null)
                field.value = kvp.Value;
            else
                list.Add(new Custom { key = kvp.Key, value = kvp.Value });
        }
        return list.ToArray();
    }

}
