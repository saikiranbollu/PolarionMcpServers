
using System.Diagnostics.CodeAnalysis;
using FluentResults;
using Microsoft.Extensions.Logging;
using Polarion;
using PolarionMcpTools;

namespace PolarionMcpServer
{
    public class PolarionStdioClientFactory : IPolarionClientFactory
    {
        private readonly List<PolarionProjectConfig> _projectConfigs; // Changed from single configuration
        private readonly ILogger<PolarionStdioClientFactory> _logger;
        private readonly string? _commandLineProjectAlias; // Project alias from command line arguments


        // Constructor updated to inject the list of project configurations and optional command line project alias
        public PolarionStdioClientFactory(
            List<PolarionProjectConfig> projectConfigs, // Changed parameter type
            ILogger<PolarionStdioClientFactory> logger,
            string? commandLineProjectAlias = null)
        {
            _projectConfigs = projectConfigs; // Assign the injected list
            _logger = logger;
            _commandLineProjectAlias = commandLineProjectAlias;
        }

        // Public property to get the projectId from route data
        public string? ProjectId => null;

        [RequiresUnreferencedCode("Uses Polarion API which requires reflection")]
        public async Task<Result<IPolarionClient>> CreateClientAsync()
        {
            // First priority: Command line project alias
            string? effectiveProjectAlias = _commandLineProjectAlias;
            string projectAliasSource = "command line";
                        
            _logger.LogDebug("Attempting to create Polarion client for requested Project Alias: {ProjectAlias} (from {Source})", 
                effectiveProjectAlias ?? "[Not Provided]", projectAliasSource);

            PolarionProjectConfig? selectedConfig = null;

            // Try to find a configuration matching the effective project alias (case-insensitive)
            if (!string.IsNullOrEmpty(effectiveProjectAlias))
            {
                selectedConfig = _projectConfigs.FirstOrDefault(p => 
                    p.ProjectUrlAlias.Equals(effectiveProjectAlias, StringComparison.OrdinalIgnoreCase));
                
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
                    var errorMessage = $"Configuration error: No specific or default Polarion project configuration found for requested alias '{effectiveProjectAlias ?? "[Not Provided]"}'. Check appsettings.json.";
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

            // Project-level PAT (set by env var override in Program.cs) takes
            // priority over SessionConfig.PersonalAccessToken which may contain
            // unresolved placeholder text like "${env:POLARION_PAT}".
            var pat = selectedConfig.PersonalAccessToken;
            if (string.IsNullOrWhiteSpace(pat))
            {
                var sessionPat = selectedConfig.SessionConfig?.PersonalAccessToken;
                // Guard against unresolved ${env:...} placeholders from appsettings
                if (!string.IsNullOrWhiteSpace(sessionPat) && !sessionPat.Contains("${env:"))
                {
                    pat = sessionPat;
                }
            }

            // Determine whether SOAP password is also available (for fallback)
            var hasPassword = !string.IsNullOrWhiteSpace(clientConfig.Password)
                              && !clientConfig.Password.Contains("${env:");

            var authMode = !string.IsNullOrWhiteSpace(pat) ? "PAT (Bearer)" : "Password (SOAP)";
            _logger.LogDebug(
                "Creating Polarion client using Server: {ServerUrl}, User: {Username}, " +
                "Project: {RealProjectId}, AuthMode: {AuthMode}",
                clientConfig.ServerUrl, clientConfig.Username, clientConfig.ProjectId, authMode);

            Result<IPolarionClient> clientResult;
            if (!string.IsNullOrWhiteSpace(pat))
            {
                // Use Bearer token auth — adds Authorization: Bearer <PAT> header
                // to every WCF/SOAP request (no SOAP logIn call needed).
                clientResult = await PolarionBearerTokenClient.CreateAsync(clientConfig, pat);

                // If Bearer client was created but may not be authorized,
                // fall back to SOAP password auth when available
                if (clientResult.IsSuccess && hasPassword)
                {
                    try
                    {
                        // Quick validation — attempt a lightweight operation
                        await clientResult.Value.GetWorkItemByIdAsync("__ping__");
                    }
                    catch (Exception ex) when (ex.Message.Contains("Not authorized") ||
                                                ex.InnerException?.Message?.Contains("Not authorized") == true)
                    {
                        _logger.LogWarning(
                            "Bearer token not authorized on {ServerUrl}, falling back to SOAP password auth",
                            clientConfig.ServerUrl);
                        authMode = "Password (SOAP, fallback)";

                        // Build config with the actual password (not the PAT that
                        // GetEffectiveClientConfig substituted into the password field)
                        var actualPassword = selectedConfig.SessionConfig?.Password ?? string.Empty;
                        var soapConfig = new PolarionClientConfiguration(
                            clientConfig.ServerUrl,
                            clientConfig.Username,
                            actualPassword,
                            clientConfig.ProjectId,
                            clientConfig.TimeoutSeconds);
                        var soapResult = await PolarionClient.CreateAsync(soapConfig);
                        clientResult = soapResult.IsSuccess
                            ? Result.Ok<IPolarionClient>(soapResult.Value)
                            : Result.Fail<IPolarionClient>(soapResult.Errors);
                    }
                    catch
                    {
                        // Non-auth error (e.g. work item not found) — Bearer client is fine
                    }
                }
            }
            else
            {
                // Fall back to SOAP username/password login
                var soapResult = await PolarionClient.CreateAsync(clientConfig);
                clientResult = soapResult.IsSuccess
                    ? Result.Ok<IPolarionClient>(soapResult.Value)
                    : Result.Fail<IPolarionClient>(soapResult.Errors);
            }

            if (clientResult.IsFailed)
            {
                var errorMessage = clientResult.Errors.FirstOrDefault()?.Message ?? "Unknown error";
                _logger.LogError(
                    "Failed to create Polarion client via factory for server: {ServerUrl} " +
                    "(Alias: {Alias}). AuthMode: {AuthMode}. Error: {ErrorMessage}",
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
