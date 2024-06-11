using Microsoft.AspNetCore.Mvc;
using OrcanodeMonitor.Core;
using OrcanodeMonitor.Data;
using OrcanodeMonitor.Models;
using System.Dynamic;
using System.Text.Json;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace OrcanodeMonitor.Api
{
    [Route("api/ifttt/v1/triggers/[controller]")]
    [ApiController]
    public class NodeStateEventsController : ControllerBase
    {
        const int _defaultLimit = 50;
        private readonly OrcanodeMonitorContext _databaseContext;

        public NodeStateEventsController(OrcanodeMonitorContext context)
        {
            _databaseContext = context;
        }

        private JsonResult GetEvents(int limit)
        {
            List<OrcanodeEvent> events = Core.Fetcher.GetEvents(_databaseContext, limit);

            // Convert to IFTTT data transfer objects.
            var latestEvents = new List<OrcanodeIftttEventDTO>();
            foreach (OrcanodeEvent e in events)
            {
                latestEvents.Add(e.ToIftttEventDTO());
            }

            var dataResult = new { data = latestEvents };

            var jsonString = JsonSerializer.Serialize(dataResult);
            var jsonDocument = JsonDocument.Parse(jsonString);

            // Get the JSON data as an array.
            var jsonElement = jsonDocument.RootElement;

            return new JsonResult(jsonElement);
        }

        // GET is not used by IFTTT so this does not require a service key.
        // GET: api/ifttt/v1/triggers/<TestController>
        [HttpGet]
        public IActionResult Get()
        {
            return GetEvents(_defaultLimit);
        }

        // POST api/ifttt/v1/triggers/<TestController>
        [HttpPost]
        public IActionResult Post([FromBody] JsonElement requestBody)
        {
            ObjectResult failure = Fetcher.CheckIftttServiceKey(Request);
            if (failure != null)
            {
                return failure;
            }

            if (requestBody.ValueKind != JsonValueKind.Object)
            {
                return BadRequest("Invalid JSON data.");
            }

            try
            {
                int limit = _defaultLimit;
                if (requestBody.TryGetProperty("limit", out JsonElement limitElement))
                {
                    if (limitElement.TryGetInt32(out int explicitLimit))
                    {
                        limit = explicitLimit;
                    }
                }
                if (requestBody.TryGetProperty("triggerFields", out JsonElement triggerFields))
                {
                    // TODO: use triggerFields to see if the caller only wants
                    // events for a given node.
                }

                return GetEvents(limit);
            }
            catch (JsonException)
            {
                return BadRequest("Invalid JSON data.");
            }
        }
    }
}
