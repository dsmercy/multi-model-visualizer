using Microsoft.AspNetCore.Mvc;
using MultiModelVisualizer.Api.Models;

namespace MultiModelVisualizer.Api.Controllers;

[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public ActionResult<HealthResponse> GetHealth()
    {
        return Ok(new HealthResponse("healthy", "1.0.0", DateTimeOffset.UtcNow));
    }
}
