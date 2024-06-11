using Microsoft.AspNetCore.Mvc;
using OrcanodeMonitor.Core;
using System.Net;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace OrcanodeMonitor.Api
{
    [Route("api/ifttt/v1/[controller]")]
    [ApiController]
    public class StatusController : ControllerBase
    {
        // GET: api/ifttt/v1/<StatusController>
        [HttpGet]
        public IActionResult Get()
        {
            ObjectResult failure = Fetcher.CheckIftttServiceKey(Request);
            if (failure != null)
            {
                return failure;
            }
            return Ok();
        }
    }
}
