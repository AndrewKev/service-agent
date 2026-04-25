namespace service_agent.Options;

public class ServiceMonitoringOptions
{
  public const string SectionName = "ServiceMonitoring";

  public bool Enabled { get; set; } = false;
  public int PollingIntervalSeconds { get; set; } = 30;
  public int CommandTimeoutSeconds { get; set; } = 10;
  public string ManagementBaseUrl { get; set; } = string.Empty;
  public string RegisteredServicesEndpoint { get; set; } = "/api/agent/registered-services";
  public string AlertEndpoint { get; set; } = "/api/alert";
  public string ServerId { get; set; } = string.Empty;
}
