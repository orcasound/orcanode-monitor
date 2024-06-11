using Microsoft.AspNetCore.Mvc;
using OrcanodeMonitor.Core;
using System.Net;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace OrcanodeMonitor.Api
{
    [Route("api/ifttt/v1/test/setup")]
    [ApiController]
    public class TestSetupController : ControllerBase
    {
        // POST api/ifttt/v1/test/setup
        [HttpPost]
        public IActionResult Post()
        {
            ObjectResult failure = Fetcher.CheckIftttServiceKey(Request);
            if (failure != null)
            {
                return failure;
            }

            var result = new
            {
                data = new
                {
                    samples = new
                    {
                        triggers = new
                        {
                        }
                    }
                }
            };

            return Ok(result);
        }
    }
}
