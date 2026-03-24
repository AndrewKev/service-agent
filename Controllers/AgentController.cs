using Microsoft.AspNetCore.Mvc;
using service_agent.Filters;

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
}

public record EditSystemdConfigRequest(string Config);