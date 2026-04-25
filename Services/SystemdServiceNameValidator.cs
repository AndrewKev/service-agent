namespace service_agent.Services;

public static class SystemdServiceNameValidator
{
  public static bool IsValid(string serviceName)
  {
    if (string.IsNullOrWhiteSpace(serviceName) || serviceName.Length > 128)
    {
      return false;
    }

    foreach (char c in serviceName)
    {
      bool allowed = char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' || c == '@';
      if (!allowed)
      {
        return false;
      }
    }

    return true;
  }
}
