using System.ServiceModel;
using System.ServiceModel.Channels;
using System.ServiceModel.Description;
using System.ServiceModel.Dispatcher;

namespace PolarionMcpTools;

/// <summary>
/// Creates a <see cref="PolarionClient"/> using Bearer token (Personal Access Token)
/// authentication instead of SOAP username/password login.
///
/// Polarion PATs require an <c>Authorization: Bearer &lt;token&gt;</c> HTTP header
/// on every request rather than the traditional SOAP <c>logIn()</c> call.
/// </summary>
public static class PolarionBearerTokenClient
{
    [RequiresUnreferencedCode("Uses WCF services which require reflection")]
    public static Task<Result<IPolarionClient>> CreateAsync(
        PolarionClientConfiguration config,
        string bearerToken)
    {
        try
        {
            var binding = new BasicHttpBinding();
            if (config.ServerUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                binding.Security.Mode = BasicHttpSecurityMode.Transport;
            }

            binding.MaxReceivedMessageSize = 10_000_000; // 10 MB
            binding.OpenTimeout = TimeSpan.FromSeconds(config.TimeoutSeconds);
            binding.CloseTimeout = TimeSpan.FromSeconds(config.TimeoutSeconds);
            binding.SendTimeout = TimeSpan.FromSeconds(config.TimeoutSeconds);
            binding.ReceiveTimeout = TimeSpan.FromSeconds(config.TimeoutSeconds);
            binding.AllowCookies = true;

            var trackerEndpoint = new EndpointAddress(
                $"{config.ServerUrl.TrimEnd('/')}/polarion/ws/services/TrackerWebService");
            var trackerClient = new TrackerWebServiceClient(binding, trackerEndpoint);

            // Add Bearer token Authorization header to all outgoing requests
            trackerClient.Endpoint.EndpointBehaviors.Add(
                new BearerTokenEndpointBehavior(bearerToken));

            // PolarionClient has a public primary constructor accepting (TrackerWebService, PolarionClientConfiguration).
            // TrackerWebServiceClient implements the TrackerWebService interface (WCF channel shape).
            IPolarionClient client = new PolarionClient(trackerClient, config);
            return Task.FromResult(Result.Ok(client));
        }
        catch (Exception ex)
        {
            return Task.FromResult(
                Result.Fail<IPolarionClient>(
                    $"Failed to create Polarion client with Bearer token: {ex.Message}"));
        }
    }
}

/// <summary>
/// WCF endpoint behavior that installs a <see cref="BearerTokenMessageInspector"/>
/// to add an <c>Authorization: Bearer</c> header to every outgoing HTTP request.
/// </summary>
internal sealed class BearerTokenEndpointBehavior : IEndpointBehavior
{
    private readonly string _token;

    public BearerTokenEndpointBehavior(string token) => _token = token;

    public void AddBindingParameters(ServiceEndpoint endpoint, BindingParameterCollection bindingParameters) { }

    public void ApplyClientBehavior(ServiceEndpoint endpoint, ClientRuntime clientRuntime)
    {
        clientRuntime.ClientMessageInspectors.Add(new BearerTokenMessageInspector(_token));
    }

    public void ApplyDispatchBehavior(ServiceEndpoint endpoint, EndpointDispatcher endpointDispatcher) { }

    public void Validate(ServiceEndpoint endpoint) { }
}

/// <summary>
/// WCF message inspector that adds an <c>Authorization: Bearer &lt;token&gt;</c>
/// header to every outgoing HTTP request.
/// </summary>
internal sealed class BearerTokenMessageInspector : IClientMessageInspector
{
    private readonly string _token;

    public BearerTokenMessageInspector(string token) => _token = token;

    public object? BeforeSendRequest(ref Message request, IClientChannel channel)
    {
        HttpRequestMessageProperty httpRequest;

        if (request.Properties.TryGetValue(HttpRequestMessageProperty.Name, out var existingProp) &&
            existingProp is HttpRequestMessageProperty existing)
        {
            httpRequest = existing;
        }
        else
        {
            httpRequest = new HttpRequestMessageProperty();
            request.Properties[HttpRequestMessageProperty.Name] = httpRequest;
        }

        httpRequest.Headers["Authorization"] = $"Bearer {_token}";
        return null;
    }

    public void AfterReceiveReply(ref Message reply, object correlationState) { }
}
