namespace service_agent.Services;

public static class ManagementEndpointBuilder
{
  public static string Build(string baseUrl, string endpointPath)
  {
    string normalizedBase = baseUrl.Trim().TrimEnd('/');
    string normalizedPath = endpointPath.Trim().TrimStart('/');
    return $"{normalizedBase}/{normalizedPath}";
  }
}
