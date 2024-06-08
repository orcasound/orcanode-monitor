using Microsoft.AspNetCore.Mvc;
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
        private JsonResult GetEvents(int limit)
        {
            List<OrcanodeEvent> events = Core.State.GetEvents(limit);

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

        // GET: api/ifttt/v1/triggers/<TestController>
        [HttpGet]
        public JsonResult Get()
        {
            return GetEvents(50);
        }

        // POST api/ifttt/v1/triggers/<TestController>
        [HttpPost]
        public IActionResult Post([FromBody] string value)
        {
            try
            {
                dynamic requestBody = JsonSerializer.Deserialize<ExpandoObject>(value);
                if (!requestBody.TryGetProperty("limit", out JsonElement limitElement))
                {
                    return BadRequest("Invalid JSON data.");
                }
                int limit = 50;
                if (limitElement.TryGetInt32(out int explicitLimit))
                {
                    limit = explicitLimit;
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
