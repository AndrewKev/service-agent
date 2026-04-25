using System.Diagnostics;
using Microsoft.Extensions.Options;
using service_agent.Models;
using service_agent.Options;

namespace service_agent.Services;

public interface ISystemdServiceReader
{
  Task<ServiceStatusReadResult> ReadStatusAsync(string serviceName, CancellationToken cancellationToken);
}

public sealed record ServiceStatusReadResult(
  bool IsSuccess,
  ServiceStatusSnapshot? Snapshot,
  string ErrorMessage);

public sealed class SystemdServiceReader : ISystemdServiceReader
{
  private readonly IOptionsMonitor<ServiceMonitoringOptions> _options;

  public SystemdServiceReader(IOptionsMonitor<ServiceMonitoringOptions> options)
  {
    _options = options;
  }

  public async Task<ServiceStatusReadResult> ReadStatusAsync(string serviceName, CancellationToken cancellationToken)
  {
    string normalizedServiceName = serviceName.Trim();
    if (!SystemdServiceNameValidator.IsValid(normalizedServiceName))
    {
      return new ServiceStatusReadResult(
        false,
        null,
        $"Invalid service name '{serviceName}'.");
    }

    CommandResult directResult = await RunCommandAsync(normalizedServiceName, useSudo: false, cancellationToken);
    if (directResult.IsSuccess)
    {
      return BuildReadResult(normalizedServiceName, directResult.StandardOutput);
    }

    if (!IsPermissionError(directResult.StandardError))
    {
      return new ServiceStatusReadResult(false, null, directResult.StandardError);
    }

    CommandResult sudoResult = await RunCommandAsync(normalizedServiceName, useSudo: true, cancellationToken);
    if (!sudoResult.IsSuccess)
    {
      return new ServiceStatusReadResult(false, null, sudoResult.StandardError);
    }

    return BuildReadResult(normalizedServiceName, sudoResult.StandardOutput);
  }

  private async Task<CommandResult> RunCommandAsync(string serviceName, bool useSudo, CancellationToken cancellationToken)
  {
    using var process = new Process
    {
      StartInfo = CreateProcessStartInfo(serviceName, useSudo)
    };

    try
    {
      if (!process.Start())
      {
        return new CommandResult(false, string.Empty, "Failed to start systemctl process.");
      }

      Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync();
      Task<string> standardErrorTask = process.StandardError.ReadToEndAsync();

      int timeoutSeconds = Math.Max(3, _options.CurrentValue.CommandTimeoutSeconds);
      using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
      using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

      await process.WaitForExitAsync(linkedCts.Token);

      string standardOutput = (await standardOutputTask).Trim();
      string standardError = (await standardErrorTask).Trim();

      if (process.ExitCode != 0)
      {
        if (string.IsNullOrWhiteSpace(standardError))
        {
          standardError = $"systemctl exited with code {process.ExitCode}.";
        }

        return new CommandResult(false, standardOutput, standardError);
      }

      return new CommandResult(true, standardOutput, standardError);
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
      StopProcess(process);
      return new CommandResult(
        false,
        string.Empty,
        $"systemctl command timed out for service '{serviceName}'.");
    }
    catch (OperationCanceledException)
    {
      throw;
    }
    catch (Exception ex)
    {
      StopProcess(process);
      return new CommandResult(false, string.Empty, ex.Message);
    }
  }

  private static ProcessStartInfo CreateProcessStartInfo(string serviceName, bool useSudo)
  {
    var processStartInfo = new ProcessStartInfo
    {
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true
    };

    if (useSudo)
    {
      processStartInfo.FileName = "/usr/bin/sudo";
      processStartInfo.ArgumentList.Add("-n");
      processStartInfo.ArgumentList.Add("/usr/bin/systemctl");
    }
    else
    {
      processStartInfo.FileName = "/usr/bin/systemctl";
    }

    processStartInfo.ArgumentList.Add("--no-pager");
    processStartInfo.ArgumentList.Add("show");
    processStartInfo.ArgumentList.Add(serviceName);
    processStartInfo.ArgumentList.Add("--property=ActiveState");
    processStartInfo.ArgumentList.Add("--property=SubState");
    processStartInfo.ArgumentList.Add("--value");

    return processStartInfo;
  }

  private static ServiceStatusReadResult BuildReadResult(string serviceName, string output)
  {
    string[] lines = output
      .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    if (lines.Length == 0)
    {
      return new ServiceStatusReadResult(
        false,
        null,
        $"systemctl returned empty state for service '{serviceName}'.");
    }

    string activeState = lines[0].Trim().ToLowerInvariant();
    string subState = lines.Length > 1 ? lines[1].Trim().ToLowerInvariant() : "unknown";
    var snapshot = new ServiceStatusSnapshot(serviceName, activeState, subState);
    return new ServiceStatusReadResult(true, snapshot, string.Empty);
  }

  private static bool IsPermissionError(string errorMessage)
  {
    string error = errorMessage.ToLowerInvariant();
    return error.Contains("permission denied")
      || error.Contains("not allowed")
      || error.Contains("a password is required")
      || error.Contains("access denied")
      || error.Contains("not in the sudoers")
      || error.Contains("insufficient permissions");
  }

  private static void StopProcess(Process process)
  {
    try
    {
      if (!process.HasExited)
      {
        process.Kill(entireProcessTree: true);
        process.WaitForExit(2000);
      }
    }
    catch
    {
      // Best effort clean-up.
    }
  }

  private sealed record CommandResult(
    bool IsSuccess,
    string StandardOutput,
    string StandardError);
}
