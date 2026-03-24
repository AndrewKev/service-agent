using Microsoft.AspNetCore.Mvc;
using service_agent.Filters;

namespace service_agent.Controllers
{
    [ApiController]
    [Route("[controller]")]
    [TypeFilter(typeof(ApiKeyAuthFilter))]
    public class HealthController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get()
        {
            try
            {
                return Ok(new { success = true, message = "Connection success" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Connection failed", error = ex.Message });
            }
        }
    }
}
