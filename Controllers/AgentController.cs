using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace service_agent.Controllers;

[ApiController]
[Route("[controller]/service")]
public class AgentController : ControllerBase
{
  // [HttpGet("/agent")]
  public string Get()
  {
    return "Hello from the Agent Controller!";
  }

  // public IActionResult GetServiceInfo()
  // {
  //   var agentInfo = new
  //   {
  //     Name = "Service Agent",
  //     Version = "1.0.0",
  //     Description = "A simple service agent for managing system services."
  //   };
  //   return Ok(agentInfo);
  // }

  [HttpPost("restart/{serviceName}")]
  public IActionResult RestartService(string serviceName)
  {
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
      return Ok(new { success = true, message = $"Service '{serviceName}' restarted successfully." });
    }
    else
    {
      return BadRequest(new { success = false, message = $"Failed to restart service '{serviceName}'", error = error });
    }
  }

  [HttpPost("stop/{serviceName}")]
  public async Task<IActionResult> StopService(string serviceName)
  {
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
      return StatusCode(500, new { success = false, message = "Timeout: systemctl stop memakan waktu terlalu lama." });
    }

    string error = await errorTask;

    if (process.ExitCode == 0)
    {
      return Ok(new { success = true, message = $"Service '{serviceName}' stopped successfully." });
    }
    else
    {
      return BadRequest(new { success = false, message = $"Failed to stop service", error = error });
    }
  }

  [HttpGet("status/{serviceName}")]
  public IActionResult StatusService(string serviceName)
  {
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
      return Ok(new { success = true, config = output });
    }
    else
    {
      return BadRequest(new { success = false, message = "Failed to retrieve systemd configuration", error = error });
    }
  }
}