using System.Text.Json.Serialization;

namespace service_agent.Models;

public sealed class ServiceAlertRequest
{
  [JsonPropertyName("service")]
  public string Service { get; init; } = string.Empty;

  [JsonPropertyName("state")]
  public string State { get; init; } = string.Empty;

  [JsonPropertyName("sub_state")]
  public string SubState { get; init; } = string.Empty;

  [JsonPropertyName("detected_at")]
  public DateTime DetectedAt { get; init; }
}
