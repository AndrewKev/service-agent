using Microsoft.Extensions.Options;
using service_agent.Models;
using service_agent.Options;

namespace service_agent.Services;

public sealed class ServiceMonitoringWorker : BackgroundService
{
  private readonly ILogger<ServiceMonitoringWorker> _logger;
  private readonly IOptionsMonitor<ServiceMonitoringOptions> _options;
  private readonly IRegisteredServiceClient _registeredServiceClient;
  private readonly ISystemdServiceReader _systemdServiceReader;
  private readonly IAlertClient _alertClient;
  private readonly Dictionary<string, string> _lastSentStates = new(StringComparer.OrdinalIgnoreCase);

  public ServiceMonitoringWorker(
    ILogger<ServiceMonitoringWorker> logger,
    IOptionsMonitor<ServiceMonitoringOptions> options,
    IRegisteredServiceClient registeredServiceClient,
    ISystemdServiceReader systemdServiceReader,
    IAlertClient alertClient)
  {
    _logger = logger;
    _options = options;
    _registeredServiceClient = registeredServiceClient;
    _systemdServiceReader = systemdServiceReader;
    _alertClient = alertClient;
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    _logger.LogInformation("[ServiceMonitoringWorker] Worker started.");

    while (!stoppingToken.IsCancellationRequested)
    {
      ServiceMonitoringOptions options = _options.CurrentValue;

      try
      {
        await RunCycleAsync(options, stoppingToken);
      }
      catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
      {
        break;
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "[ServiceMonitoringWorker] Unexpected error during cycle.");
      }

      TimeSpan delay = GetPollingDelay(options);
      try
      {
        await Task.Delay(delay, stoppingToken);
      }
      catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
      {
        break;
      }
    }

    _logger.LogInformation("[ServiceMonitoringWorker] Worker stopped.");
  }

  private async Task RunCycleAsync(ServiceMonitoringOptions options, CancellationToken cancellationToken)
  {
    if (!options.Enabled)
    {
      _logger.LogDebug("[ServiceMonitoringWorker] Monitoring is disabled.");
      return;
    }

    if (!TryValidateConfiguration(options, out string validationError))
    {
      _logger.LogWarning("[ServiceMonitoringWorker] Invalid configuration: {Error}", validationError);
      return;
    }

    RegisteredServicesResult registeredServicesResult =
      await _registeredServiceClient.GetRegisteredServicesAsync(cancellationToken);

    if (!registeredServicesResult.IsSuccess)
    {
      _logger.LogWarning(
        "[ServiceMonitoringWorker] Failed to fetch registered services. Cycle skipped. Error: {Error}",
        registeredServicesResult.ErrorMessage);
      return;
    }

    IReadOnlyList<string> registeredServices = registeredServicesResult.Services;
    if (registeredServices.Count == 0)
    {
      _lastSentStates.Clear();
      _logger.LogInformation("[ServiceMonitoringWorker] No registered services returned by management.");
      return;
    }

    RemoveStaleServiceState(registeredServices);

    int unhealthyCount = 0;
    int alertsSent = 0;

    foreach (string serviceName in registeredServices)
    {
      ServiceStatusReadResult statusResult =
        await _systemdServiceReader.ReadStatusAsync(serviceName, cancellationToken);

      if (!statusResult.IsSuccess || statusResult.Snapshot is null)
      {
        _logger.LogWarning(
          "[ServiceMonitoringWorker] Failed to read service status for {Service}. Error: {Error}",
          serviceName,
          statusResult.ErrorMessage);
        continue;
      }

      ServiceStatusSnapshot snapshot = statusResult.Snapshot;
      if (!IsUnhealthy(snapshot))
      {
        _lastSentStates.Remove(serviceName);
        continue;
      }

      unhealthyCount++;
      string stateSignature = BuildStateSignature(snapshot);
      if (_lastSentStates.TryGetValue(serviceName, out string? lastState) && lastState == stateSignature)
      {
        continue;
      }

      var alertRequest = new ServiceAlertRequest
      {
        Service = snapshot.Service,
        State = snapshot.State,
        SubState = snapshot.SubState,
        DetectedAt = DateTime.UtcNow
      };

      AlertSendResult sendResult = await _alertClient.SendAlertAsync(alertRequest, cancellationToken);
      if (!sendResult.IsSuccess)
      {
        _logger.LogWarning(
          "[ServiceMonitoringWorker] Failed to send alert for {Service}. Error: {Error}",
          serviceName,
          sendResult.ErrorMessage);
        continue;
      }

      _lastSentStates[serviceName] = stateSignature;
      alertsSent++;
    }

    _logger.LogInformation(
      "[ServiceMonitoringWorker] Cycle completed. Registered={Registered}, Unhealthy={Unhealthy}, AlertsSent={AlertsSent}",
      registeredServices.Count,
      unhealthyCount,
      alertsSent);
  }

  private static TimeSpan GetPollingDelay(ServiceMonitoringOptions options)
  {
    int intervalSeconds = Math.Max(5, options.PollingIntervalSeconds);
    return TimeSpan.FromSeconds(intervalSeconds);
  }

  private static bool TryValidateConfiguration(ServiceMonitoringOptions options, out string error)
  {
    if (string.IsNullOrWhiteSpace(options.ManagementBaseUrl))
    {
      error = "ServiceMonitoring:ManagementBaseUrl is required.";
      return false;
    }

    if (!Uri.TryCreate(options.ManagementBaseUrl, UriKind.Absolute, out _))
    {
      error = "ServiceMonitoring:ManagementBaseUrl must be a valid absolute URL.";
      return false;
    }

    if (string.IsNullOrWhiteSpace(options.ServerId))
    {
      error = "ServiceMonitoring:ServerId is required.";
      return false;
    }

    if (!Guid.TryParse(options.ServerId, out _))
    {
      error = "ServiceMonitoring:ServerId must be a valid GUID.";
      return false;
    }

    if (string.IsNullOrWhiteSpace(options.RegisteredServicesEndpoint))
    {
      error = "ServiceMonitoring:RegisteredServicesEndpoint is required.";
      return false;
    }

    if (string.IsNullOrWhiteSpace(options.AlertEndpoint))
    {
      error = "ServiceMonitoring:AlertEndpoint is required.";
      return false;
    }

    error = string.Empty;
    return true;
  }

  private void RemoveStaleServiceState(IReadOnlyList<string> registeredServices)
  {
    var registeredSet = new HashSet<string>(registeredServices, StringComparer.OrdinalIgnoreCase);
    List<string> keysToRemove = _lastSentStates.Keys
      .Where(serviceName => !registeredSet.Contains(serviceName))
      .ToList();

    foreach (string key in keysToRemove)
    {
      _lastSentStates.Remove(key);
    }
  }

  private static bool IsUnhealthy(ServiceStatusSnapshot snapshot)
  {
    if (!string.Equals(snapshot.State, "active", StringComparison.OrdinalIgnoreCase))
    {
      return true;
    }

    return snapshot.SubState is "dead" or "failed" or "auto-restart";
  }

  private static string BuildStateSignature(ServiceStatusSnapshot snapshot)
  {
    return $"{snapshot.State}|{snapshot.SubState}";
  }
}
