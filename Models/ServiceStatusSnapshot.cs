namespace service_agent.Models;

public sealed record ServiceStatusSnapshot(
  string Service,
  string State,
  string SubState);
