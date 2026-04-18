using Microsoft.AspNetCore.Mvc;
using service_agent.Filters;
using System.Diagnostics;

namespace service_agent.Controllers;

[ApiController]
[Route("[controller]/service")]
[TypeFilter(typeof(ApiKeyAuthFilter))]
public class AgentController : ControllerBase
{
  private readonly ILogger<AgentController> _logger;

  public AgentController(ILogger<AgentController> logger)
  {
    _logger = logger;
  }
  // [HttpGet("/agent")]
  public string Get()
  {
    return "Hello from the Agent Controller!";
  }

  [HttpPost("restart/{serviceName}")]
  public IActionResult RestartService(string serviceName)
  {
    _logger.LogInformation("[RestartService] Request to restart service: {ServiceName}", serviceName);
    var process = new System.Diagnostics.Process
    {
      StartInfo = new System.Diagnostics.ProcessStartInfo
      {
        FileName = "/usr/bin/sudo",
        Arguments = $"/usr/bin/systemctl restart {serviceName}",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
      }
    };
    process.Start();
    string output = process.StandardOutput.ReadToEnd();
    string error = process.StandardError.ReadToEnd();
    process.WaitForExit();

    if (process.ExitCode == 0)
    {
      _logger.LogInformation("[RestartService] Successfully restarted: {ServiceName}", serviceName);
      return Ok(new { success = true, message = $"Service '{serviceName}' restarted successfully." });
    }
    else
    {
      _logger.LogWarning("[RestartService] Failed to restart: {ServiceName}. Error: {Error}", serviceName, error);
      return BadRequest(new { success = false, message = $"Failed to restart service '{serviceName}'", error = error });
    }
  }

  [HttpPost("stop/{serviceName}")]
  public async Task<IActionResult> StopService(string serviceName)
  {
    _logger.LogInformation("[StopService] Request to stop service: {ServiceName}", serviceName);
    var process = new System.Diagnostics.Process
    {
      StartInfo = new System.Diagnostics.ProcessStartInfo
      {
        FileName = "/usr/bin/sudo",
        Arguments = $"/usr/bin/systemctl stop {serviceName} --no-block",
        RedirectStandardOutput = false,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
      }
    };

    process.Start();
    var errorTask = process.StandardError.ReadToEndAsync();

    bool finished = process.WaitForExit(5000);

    if (!finished)
    {
      process.Kill();
      _logger.LogWarning("[StopService] Timeout stopping service: {ServiceName}", serviceName);
      return StatusCode(500, new { success = false, message = "Timeout: systemctl stop took too long to complete." });
    }

    string error = await errorTask;

    if (process.ExitCode == 0)
    {
      _logger.LogInformation("[StopService] Successfully stopped: {ServiceName}", serviceName);
      return Ok(new { success = true, message = $"Service '{serviceName}' stopped successfully." });
    }
    else
    {
      _logger.LogWarning("[StopService] Failed to stop: {ServiceName}. Error: {Error}", serviceName, error);
      return BadRequest(new { success = false, message = $"Failed to stop service", error = error });
    }
  }

  [HttpGet("status/{serviceName}")]
  public IActionResult StatusService(string serviceName)
  {
    _logger.LogInformation("[StatusService] Request to check status: {ServiceName}", serviceName);
    string command = $"/usr/bin/systemctl status {serviceName}";
    var process = new System.Diagnostics.Process
    {
      StartInfo = new System.Diagnostics.ProcessStartInfo
      {
        FileName = "/bin/bash",
        Arguments = $"-c \"{command}\"",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
      }
    };
    process.Start();
    string output = process.StandardOutput.ReadToEnd();
    string error = process.StandardError.ReadToEnd();
    process.WaitForExit();

    if (process.ExitCode == 0)
    {
      _logger.LogInformation("[StatusService] Status check success: {ServiceName}", serviceName);
    }
    else
    {
      _logger.LogWarning("[StatusService] Status check failed: {ServiceName}. Error: {Error}", serviceName, error);
    }

    return Ok(new
    {
      success = process.ExitCode == 0,
      output = output,
      error = error,
      exitCode = process.ExitCode
    });
  }

  [HttpGet("systemd/{serviceName}")]
  public IActionResult GetSystemdConfig(string serviceName)
  {
    _logger.LogInformation("[GetSystemdConfig] Request to get config: {ServiceName}", serviceName);
    var process = new System.Diagnostics.Process
    {
      StartInfo = new System.Diagnostics.ProcessStartInfo
      {
        FileName = "/bin/bash",
        Arguments = $"-c \"cat /etc/systemd/system/{serviceName}.service\"",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
      }
    };

    process.Start();
    string output = process.StandardOutput.ReadToEnd();
    string error = process.StandardError.ReadToEnd();
    process.WaitForExit();

    if (process.ExitCode == 0)
    {
      _logger.LogInformation("[GetSystemdConfig] Successfully retrieved config: {ServiceName}", serviceName);
      return Ok(new { success = true, config = output });
    }
    else
    {
      _logger.LogWarning("[GetSystemdConfig] Failed to retrieve config: {ServiceName}. Error: {Error}", serviceName, error);
      return BadRequest(new { success = false, message = "Failed to retrieve systemd configuration", error = error });
    }
  }

  [HttpGet("logs/{serviceName}")]
  public async Task<IActionResult> StreamServiceLogs(string serviceName, [FromQuery] int lines = 200, [FromQuery] bool follow = true)
  {
    if (!IsValidServiceName(serviceName))
    {
      return BadRequest(new
      {
        success = false,
        message = "Invalid service name. Allowed characters: letters, numbers, '-', '_', '.', '@'."
      });
    }

    if (lines < 1 || lines > 5000)
    {
      return BadRequest(new
      {
        success = false,
        message = "Query parameter 'lines' must be between 1 and 5000."
      });
    }

    _logger.LogInformation("[StreamServiceLogs] Request logs for: {ServiceName}, lines={Lines}, follow={Follow}", serviceName, lines, follow);

    var requestAborted = HttpContext.RequestAborted;
    (bool IsSuccess, int StatusCode, string Error, bool UseSudo) preflight;
    try
    {
      preflight = await ValidateJournalAccessAsync(serviceName, requestAborted);
    }
    catch (OperationCanceledException)
    {
      _logger.LogInformation("[StreamServiceLogs] Request canceled before stream started: {ServiceName}", serviceName);
      return new EmptyResult();
    }

    if (!preflight.IsSuccess)
    {
      _logger.LogWarning("[StreamServiceLogs] Preflight failed for: {ServiceName}. Error: {Error}", serviceName, preflight.Error);
      return StatusCode(preflight.StatusCode, new
      {
        success = false,
        message = $"Unable to access logs for service '{serviceName}'.",
        error = preflight.Error
      });
    }

    using var process = new Process
    {
      StartInfo = CreateJournalctlStartInfo(serviceName, lines, follow, preflight.UseSudo)
    };

    try
    {
      if (!process.Start())
      {
        return StatusCode(StatusCodes.Status500InternalServerError, new
        {
          success = false,
          message = "Failed to start journalctl process."
        });
      }

      Response.StatusCode = StatusCodes.Status200OK;
      Response.ContentType = "text/plain; charset=utf-8";
      Response.Headers["Cache-Control"] = "no-cache, no-store";
      Response.Headers["X-Accel-Buffering"] = "no";
      await Response.StartAsync(requestAborted);

      var stdErrTask = process.StandardError.ReadToEndAsync();

      while (!requestAborted.IsCancellationRequested)
      {
        string? line = await process.StandardOutput.ReadLineAsync().WaitAsync(requestAborted);
        if (line is null)
        {
          break;
        }

        await Response.WriteAsync(line + Environment.NewLine, requestAborted);
        await Response.Body.FlushAsync(requestAborted);
      }

      if (!process.HasExited)
      {
        await process.WaitForExitAsync(requestAborted);
      }

      string stdErr = await stdErrTask;
      if (!string.IsNullOrWhiteSpace(stdErr) && !requestAborted.IsCancellationRequested)
      {
        _logger.LogWarning("[StreamServiceLogs] journalctl stderr for {ServiceName}: {Error}", serviceName, stdErr);
      }
    }
    catch (OperationCanceledException)
    {
      _logger.LogInformation("[StreamServiceLogs] Client disconnected: {ServiceName}", serviceName);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "[StreamServiceLogs] Unexpected stream error: {ServiceName}", serviceName);
    }
    finally
    {
      StopProcess(process);
    }

    return new EmptyResult();
  }
  
  [HttpPut("systemd/{serviceName}")]
  public IActionResult EditSystemdConfig(string serviceName, [FromBody] EditSystemdConfigRequest request)
  {
    _logger.LogInformation("[EditSystemdConfig] Request to edit config: {ServiceName}", serviceName);
    // Step 1: Write config to file using sudo tee
    var writeProcess = new System.Diagnostics.Process
    {
      StartInfo = new System.Diagnostics.ProcessStartInfo
      {
        FileName = "/usr/bin/sudo",
        Arguments = $"/usr/bin/tee /etc/systemd/system/{serviceName}.service",
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
      }
    };
    writeProcess.Start();
    writeProcess.StandardInput.Write(request.Config);
    writeProcess.StandardInput.Close();
    string writeError = writeProcess.StandardError.ReadToEnd();
    writeProcess.WaitForExit();
    if (writeProcess.ExitCode != 0)
    {
      _logger.LogWarning("[EditSystemdConfig] Failed to write config: {ServiceName}. Error: {Error}", serviceName, writeError);
      return BadRequest(new { success = false, step = "write_config", error = writeError });
    }

    _logger.LogInformation("[EditSystemdConfig] Successfully wrote config: {ServiceName}", serviceName);

    // Step 2: daemon-reload
    var reloadProcess = new System.Diagnostics.Process
    {
      StartInfo = new System.Diagnostics.ProcessStartInfo
      {
        FileName = "/usr/bin/sudo",
        Arguments = "/usr/bin/systemctl daemon-reload",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
      }
    };
    reloadProcess.Start();
    string reloadError = reloadProcess.StandardError.ReadToEnd();
    reloadProcess.WaitForExit();
    if (reloadProcess.ExitCode != 0)
    {
      _logger.LogWarning("[EditSystemdConfig] Failed daemon-reload: {ServiceName}. Error: {Error}", serviceName, reloadError);
      return BadRequest(new { success = false, step = "daemon_reload", error = reloadError });
    }

    _logger.LogInformation("[EditSystemdConfig] Successfully daemon-reload: {ServiceName}", serviceName);

    // Step 3: restart service
    var restartProcess = new System.Diagnostics.Process
    {
      StartInfo = new System.Diagnostics.ProcessStartInfo
      {
        FileName = "/usr/bin/sudo",
        Arguments = $"/usr/bin/systemctl restart {serviceName}",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
      }
    };
    restartProcess.Start();
    string restartError = restartProcess.StandardError.ReadToEnd();
    restartProcess.WaitForExit();
    if (restartProcess.ExitCode != 0)
    {
      _logger.LogWarning("[EditSystemdConfig] Failed to restart: {ServiceName}. Error: {Error}", serviceName, restartError);
      return BadRequest(new { success = false, step = "restart", error = restartError });
    }

    _logger.LogInformation("[EditSystemdConfig] Successfully updated and restarted: {ServiceName}", serviceName);
    return Ok(new { success = true, message = $"Config updated and service '{serviceName}' restarted successfully." });
  }

  private static bool IsValidServiceName(string serviceName)
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

  private static ProcessStartInfo CreateJournalctlStartInfo(string serviceName, int lines, bool follow, bool useSudo)
  {
    var startInfo = new ProcessStartInfo
    {
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true,
    };

    if (useSudo)
    {
      startInfo.FileName = "/usr/bin/sudo";
      startInfo.ArgumentList.Add("-n");
      startInfo.ArgumentList.Add("/usr/bin/journalctl");
    }
    else
    {
      startInfo.FileName = "/usr/bin/journalctl";
    }

    startInfo.ArgumentList.Add("-u");
    startInfo.ArgumentList.Add(serviceName);
    startInfo.ArgumentList.Add("--no-pager");
    startInfo.ArgumentList.Add("-q");
    startInfo.ArgumentList.Add("-n");
    startInfo.ArgumentList.Add(lines.ToString());

    if (follow)
    {
      startInfo.ArgumentList.Add("-f");
    }

    return startInfo;
  }

  private async Task<(bool IsSuccess, int StatusCode, string Error, bool UseSudo)> ValidateJournalAccessAsync(string serviceName, CancellationToken cancellationToken)
  {
    var direct = await RunJournalPreflightAsync(serviceName, useSudo: false, cancellationToken);
    if (direct.IsSuccess)
    {
      return (true, StatusCodes.Status200OK, string.Empty, false);
    }

    if (!IsPermissionError(direct.Error))
    {
      return (false, direct.StatusCode, direct.Error, false);
    }

    var sudo = await RunJournalPreflightAsync(serviceName, useSudo: true, cancellationToken);
    if (sudo.IsSuccess)
    {
      return (true, StatusCodes.Status200OK, string.Empty, true);
    }

    if (ContainsSudoPasswordRequired(sudo.Error))
    {
      return (false, StatusCodes.Status403Forbidden, "sudo: a password is required. Configure NOPASSWD for /usr/bin/journalctl or grant journal read access to runtime user.", true);
    }

    if (IsPermissionError(sudo.Error))
    {
      return (false, StatusCodes.Status403Forbidden, "Permission denied reading journal. Configure NOPASSWD for /usr/bin/journalctl or grant journal read access to runtime user.", true);
    }

    return (false, sudo.StatusCode, sudo.Error, true);
  }

  private async Task<(bool IsSuccess, int StatusCode, string Error)> RunJournalPreflightAsync(string serviceName, bool useSudo, CancellationToken cancellationToken)
  {
    using var process = new Process
    {
      StartInfo = CreateJournalctlStartInfo(serviceName, 1, false, useSudo)
    };

    try
    {
      if (!process.Start())
      {
        return (false, StatusCodes.Status500InternalServerError, "Failed to start journalctl preflight check.");
      }

      var stdErrTask = process.StandardError.ReadToEndAsync();
      await process.WaitForExitAsync(cancellationToken);
      string stdErr = (await stdErrTask).Trim();

      if (process.ExitCode == 0)
      {
        return (true, StatusCodes.Status200OK, string.Empty);
      }

      int statusCode = ResolveJournalErrorStatusCode(stdErr);
      return (false, statusCode, string.IsNullOrWhiteSpace(stdErr) ? "journalctl exited with a non-zero code." : stdErr);
    }
    catch (Exception ex)
    {
      return (false, StatusCodes.Status500InternalServerError, ex.Message);
    }
  }

  private static bool IsPermissionError(string errorMessage)
  {
    string error = errorMessage.ToLowerInvariant();
    return error.Contains("permission denied")
      || error.Contains("not allowed")
      || error.Contains("a password is required")
      || error.Contains("access denied")
      || error.Contains("not in the sudoers")
      || error.Contains("insufficient permissions")
      || error.Contains("not seeing messages from other users and the system")
      || error.Contains("no journal files were opened due to insufficient permissions");
  }

  private static bool ContainsSudoPasswordRequired(string errorMessage)
  {
    string error = errorMessage.ToLowerInvariant();
    return error.Contains("a password is required") || error.Contains("no tty present");
  }

  private static int ResolveJournalErrorStatusCode(string errorMessage)
  {
    string error = errorMessage.ToLowerInvariant();

    if (error.Contains("permission denied")
      || error.Contains("not allowed")
      || error.Contains("a password is required")
      || error.Contains("sudo")
      || error.Contains("insufficient permissions")
      || error.Contains("not seeing messages from other users and the system")
      || error.Contains("no journal files were opened due to insufficient permissions"))
    {
      return StatusCodes.Status403Forbidden;
    }

    if (error.Contains("failed to add match") || error.Contains("invalid") || error.Contains("bad unit"))
    {
      return StatusCodes.Status400BadRequest;
    }

    if (error.Contains("no journal files were found") || error.Contains("not found"))
    {
      return StatusCodes.Status404NotFound;
    }

    return StatusCodes.Status400BadRequest;
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
      // Best effort clean-up for disconnected clients or aborted requests.
    }
  }
}

public record EditSystemdConfigRequest(string Config);
