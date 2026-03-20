
using System.Diagnostics.CodeAnalysis;
using FluentResults;
using Polarion;
using PolarionMcpTools;

namespace PolarionRemoteMcpServer
{
    public class PolarionRemoteClientFactory : IPolarionClientFactory
    {
        private readonly List<PolarionProjectConfig> _projectConfigs; // Changed from single configuration
        private readonly ILogger<PolarionRemoteClientFactory> _logger;
        private readonly IHttpContextAccessor? _httpContextAccessor;

        // Constructor updated to inject the list of project configurations
        public PolarionRemoteClientFactory(
            List<PolarionProjectConfig> projectConfigs, // Changed parameter type
            ILogger<PolarionRemoteClientFactory> logger,
            IHttpContextAccessor? httpContextAccessor)
        {
            _projectConfigs = projectConfigs; // Assign the injected list
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;
        }

        // Public property to get the projectId from route data
        public string? ProjectId => _httpContextAccessor?.HttpContext?.GetRouteValue("projectId")?.ToString();

        [RequiresUnreferencedCode("Uses Polarion API which requires reflection")]
        public async Task<Result<IPolarionClient>> CreateClientAsync()
        {
            string? routeProjectId = ProjectId; // Get project ID alias from route
            _logger.LogDebug("Attempting to create Polarion client for requested Project Alias: {RouteProjectId}", routeProjectId ?? "[Not Provided]");

            PolarionProjectConfig? selectedConfig = null;

            // Try to find a configuration matching the route alias (case-insensitive)
            if (!string.IsNullOrEmpty(routeProjectId))
            {
                selectedConfig = _projectConfigs.FirstOrDefault(p => 
                    p.ProjectUrlAlias.Equals(routeProjectId, StringComparison.OrdinalIgnoreCase));
                
                if (selectedConfig != null) 
                {
                     _logger.LogDebug("Found matching configuration for Project Alias: {Alias}", selectedConfig.ProjectUrlAlias);
                }
            }

            // If no specific match found, try to find the default configuration
            if (selectedConfig == null)
            {
                selectedConfig = _projectConfigs.FirstOrDefault(p => p.Default);
                if (selectedConfig != null)
                {
                    _logger.LogDebug("Using default configuration for Project Alias: {Alias}", selectedConfig.ProjectUrlAlias);
                }
                else
                {
                    // If still no config (neither specific nor default), throw an error
                    var errorMessage = $"Configuration error: No specific or default Polarion project configuration found for requested alias '{routeProjectId ?? "[Not Provided]"}'. Check appsettings.json.";
                    _logger.LogError(errorMessage);
                    return Result.Fail(errorMessage);
                }
            }

            var clientConfig = selectedConfig.GetEffectiveClientConfig();

            if (clientConfig == null)
            {
                var errorMessage = "Internal error (539) the selected polarion client configuration variable is null.";
                _logger.LogError(errorMessage);
                return Result.Fail(errorMessage);
            }

            // PAT can live in SessionConfig or at the project level
            var pat = selectedConfig.SessionConfig?.PersonalAccessToken;
            if (string.IsNullOrWhiteSpace(pat))
            {
                pat = selectedConfig.PersonalAccessToken;
            }
            var authMode = !string.IsNullOrWhiteSpace(pat) ? "PAT (Bearer)" : "Password (SOAP)";
            _logger.LogDebug(
                "Creating Polarion client using Server: {ServerUrl}, User: {Username}, " +
                "Project: {RealProjectId}, AuthMode: {AuthMode}",
                clientConfig.ServerUrl, clientConfig.Username, clientConfig.ProjectId, authMode);

            // Create the client: Bearer token for PAT, SOAP login for password
            Result<IPolarionClient> clientResult;
            if (!string.IsNullOrWhiteSpace(pat))
            {
                clientResult = await PolarionBearerTokenClient.CreateAsync(clientConfig, pat);
            }
            else
            {
                var soapResult = await PolarionClient.CreateAsync(clientConfig);
                clientResult = soapResult.IsSuccess
                    ? Result.Ok<IPolarionClient>(soapResult.Value)
                    : Result.Fail<IPolarionClient>(soapResult.Errors);
            }

            if (clientResult.IsFailed)
            {
                var errorMessage = clientResult.Errors.FirstOrDefault()?.Message ?? "Unknown error";
                _logger.LogError(
                    "Failed to create Polarion client via factory for server: {ServerUrl} (Alias: {Alias}). " +
                    "AuthMode: {AuthMode}. Error: {ErrorMessage}",
                    clientConfig.ServerUrl, selectedConfig.ProjectUrlAlias, authMode, errorMessage);
                return Result.Fail(
                    $"Failed to create Polarion client via factory for alias " +
                    $"'{selectedConfig.ProjectUrlAlias}': {errorMessage}");
            }

            _logger.LogDebug("Successfully created new Polarion client for server: {ServerUrl} (Alias: {Alias})", 
                clientConfig.ServerUrl, selectedConfig.ProjectUrlAlias);
            return Result.Ok(clientResult.Value);
        }
    }
}
