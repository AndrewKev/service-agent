using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using service_agent.Models;
using service_agent.Options;

namespace service_agent.Services;

public interface IAlertClient
{
  Task<AlertSendResult> SendAlertAsync(ServiceAlertRequest request, CancellationToken cancellationToken);
}

public sealed record AlertSendResult(bool IsSuccess, string ErrorMessage);

public sealed class AlertClient : IAlertClient
{
  private const string HttpClientName = "ServiceMonitoringManagementClient";
  private readonly IHttpClientFactory _httpClientFactory;
  private readonly ApiKeyService _apiKeyService;
  private readonly IOptionsMonitor<ServiceMonitoringOptions> _options;

  public AlertClient(
    IHttpClientFactory httpClientFactory,
    ApiKeyService apiKeyService,
    IOptionsMonitor<ServiceMonitoringOptions> options)
  {
    _httpClientFactory = httpClientFactory;
    _apiKeyService = apiKeyService;
    _options = options;
  }

  public async Task<AlertSendResult> SendAlertAsync(ServiceAlertRequest request, CancellationToken cancellationToken)
  {
    ServiceMonitoringOptions options = _options.CurrentValue;

    string url = ManagementEndpointBuilder.Build(options.ManagementBaseUrl, options.AlertEndpoint);
    string json = JsonSerializer.Serialize(request);

    using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
    httpRequest.Headers.TryAddWithoutValidation("X-Api-Key", _apiKeyService.ApiKey);
    httpRequest.Headers.TryAddWithoutValidation("X-Server-Id", options.ServerId);
    httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

    try
    {
      HttpClient client = _httpClientFactory.CreateClient(HttpClientName);
      using HttpResponseMessage response = await client.SendAsync(httpRequest, cancellationToken);

      if (response.IsSuccessStatusCode)
      {
        return new AlertSendResult(true, string.Empty);
      }

      string responseBody = await response.Content.ReadAsStringAsync();
      if (string.IsNullOrWhiteSpace(responseBody))
      {
        responseBody = "(empty body)";
      }

      return new AlertSendResult(
        false,
        $"Send alert failed: {(int)response.StatusCode} {response.ReasonPhrase}. Body: {responseBody}"
      );
    }
    catch (OperationCanceledException)
    {
      throw;
    }
    catch (Exception ex)
    {
      return new AlertSendResult(false, $"Send alert failed: {ex.Message}");
    }
  }
}
