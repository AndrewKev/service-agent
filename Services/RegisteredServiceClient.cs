using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Options;
using service_agent.Options;

namespace service_agent.Services;

public interface IRegisteredServiceClient
{
  Task<RegisteredServicesResult> GetRegisteredServicesAsync(CancellationToken cancellationToken);
}

public sealed record RegisteredServicesResult(
  bool IsSuccess,
  IReadOnlyList<string> Services,
  string ErrorMessage);

public sealed class RegisteredServiceClient : IRegisteredServiceClient
{
  private const string HttpClientName = "ServiceMonitoringManagementClient";
  private readonly IHttpClientFactory _httpClientFactory;
  private readonly ApiKeyService _apiKeyService;
  private readonly IOptionsMonitor<ServiceMonitoringOptions> _options;

  public RegisteredServiceClient(
    IHttpClientFactory httpClientFactory,
    ApiKeyService apiKeyService,
    IOptionsMonitor<ServiceMonitoringOptions> options)
  {
    _httpClientFactory = httpClientFactory;
    _apiKeyService = apiKeyService;
    _options = options;
  }

  public async Task<RegisteredServicesResult> GetRegisteredServicesAsync(CancellationToken cancellationToken)
  {
    ServiceMonitoringOptions options = _options.CurrentValue;
    string url = ManagementEndpointBuilder.Build(options.ManagementBaseUrl, options.RegisteredServicesEndpoint);
    using var request = new HttpRequestMessage(HttpMethod.Get, url);
    request.Headers.TryAddWithoutValidation("X-Api-Key", _apiKeyService.ApiKey);
    request.Headers.TryAddWithoutValidation("X-Server-Id", options.ServerId);

    try
    {
      HttpClient client = _httpClientFactory.CreateClient(HttpClientName);
      using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
      string content = await response.Content.ReadAsStringAsync();

      if (!response.IsSuccessStatusCode)
      {
        string responseBody = string.IsNullOrWhiteSpace(content) ? "(empty body)" : content;
        return new RegisteredServicesResult(
          false,
          Array.Empty<string>(),
          $"Fetch registered services failed: {(int)response.StatusCode} {response.ReasonPhrase}. Body: {responseBody}");
      }

      IReadOnlyList<string> services = ParseServiceNames(content);
      return new RegisteredServicesResult(true, services, string.Empty);
    }
    catch (OperationCanceledException)
    {
      throw;
    }
    catch (Exception ex)
    {
      return new RegisteredServicesResult(
        false,
        Array.Empty<string>(),
        $"Fetch registered services failed: {ex.Message}");
    }
  }

  private static IReadOnlyList<string> ParseServiceNames(string jsonContent)
  {
    if (string.IsNullOrWhiteSpace(jsonContent))
    {
      return Array.Empty<string>();
    }

    using JsonDocument jsonDocument = JsonDocument.Parse(jsonContent);
    JsonElement root = jsonDocument.RootElement;
    IEnumerable<string> rawServiceNames = ExtractRawServiceNames(root);

    var serviceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (string serviceName in rawServiceNames)
    {
      string normalized = serviceName.Trim();
      if (!SystemdServiceNameValidator.IsValid(normalized))
      {
        continue;
      }

      serviceNames.Add(normalized);
    }

    return serviceNames.ToList();
  }

  private static IEnumerable<string> ExtractRawServiceNames(JsonElement root)
  {
    if (root.ValueKind == JsonValueKind.Array)
    {
      return ReadStringArray(root);
    }

    if (root.ValueKind != JsonValueKind.Object)
    {
      return Array.Empty<string>();
    }

    if (TryGetArray(root, "services", out JsonElement servicesArray))
    {
      return ReadStringArray(servicesArray);
    }

    if (TryGetArray(root, "data", out JsonElement dataArray))
    {
      return ReadStringArray(dataArray);
    }

    return Array.Empty<string>();
  }

  private static bool TryGetArray(JsonElement root, string propertyName, out JsonElement result)
  {
    result = default;
    return root.TryGetProperty(propertyName, out result) && result.ValueKind == JsonValueKind.Array;
  }

  private static IEnumerable<string> ReadStringArray(JsonElement arrayElement)
  {
    foreach (JsonElement item in arrayElement.EnumerateArray())
    {
      if (item.ValueKind == JsonValueKind.String)
      {
        string? value = item.GetString();
        if (!string.IsNullOrWhiteSpace(value))
        {
          yield return value;
        }

        continue;
      }

      if (item.ValueKind == JsonValueKind.Object
        && item.TryGetProperty("nama_systemd", out JsonElement namaSystemdElement)
        && namaSystemdElement.ValueKind == JsonValueKind.String)
      {
        string? value = namaSystemdElement.GetString();
        if (!string.IsNullOrWhiteSpace(value))
        {
          yield return value;
        }
      }
    }
  }
}
