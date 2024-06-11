using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrcanodeMonitor.Core;
using OrcanodeMonitor.Data;
using OrcanodeMonitor.Models;

namespace OrcanodeMonitor.Api
{
    [Route("api/ifttt/v1/queries/[controller]")]
    [ApiController]
    public class OrcanodesController : ControllerBase
    {
        const int _defaultLimit = 50;
        private readonly OrcanodeMonitorContext _databaseContext;

        public OrcanodesController(OrcanodeMonitorContext context)
        {
            _databaseContext = context;
        }

        // GET is not used by IFTTT so this does not require a service key.
        // GET: api/ifttt/v1/queries/Orcanodes
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Orcanode>>> GetOrcanode()
        {
            return await GetJsonNodesAsync(_defaultLimit);
        }

#if false
        // GET: api/ifttt/v1/queries/orcanodes/5
        [HttpGet("{id}")]
        public async Task<ActionResult<Orcanode>> GetOrcanode(int id)
        {
            var orcanode = await _databaseContext.Orcanodes.FindAsync(id);

            if (orcanode == null)
            {
                return NotFound();
            }

            return orcanode;
        }
#endif

        private async Task<JsonResult> GetJsonNodesAsync(int limit)
        {
            var nodes = await _databaseContext.Orcanodes.ToListAsync();

            // Convert to IFTTT data transfer objects.
            var jsonNodes = new List<OrcanodeIftttDTO>();
            foreach (Orcanode node in nodes)
            {
                jsonNodes.Add(node.ToIftttDTO());
            }

            var dataResult = new { data = jsonNodes };

            var jsonString = JsonSerializer.Serialize(dataResult);
            var jsonDocument = JsonDocument.Parse(jsonString);

            // Get the JSON data as an array.
            var jsonElement = jsonDocument.RootElement;

            return new JsonResult(jsonElement);
        }

        // POST: api/ifttt/v1/queries/orcanodes
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<Orcanode>> PostOrcanode([FromBody] JsonElement requestBody)
        {
            ObjectResult failure = Fetcher.CheckIftttServiceKey(Request);
            if (failure != null)
            {
                return failure;
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
                if (requestBody.TryGetProperty("queryFields", out JsonElement triggerFields))
                {
                    // TODO: use queryFields.
                }

                return await GetJsonNodesAsync(limit);
            }
            catch (JsonException)
            {
                return BadRequest("Invalid JSON data.");
            }
        }

#if false
        // POST: api/ifttt/v1/queries/orcanodes
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<Orcanode>> PostOrcanode(Orcanode orcanode)
        {
            _databaseContext.Orcanodes.Add(orcanode);
            await _databaseContext.SaveChangesAsync();

            return CreatedAtAction("GetOrcanode", new { id = orcanode.ID }, orcanode);
        }

        // PUT: api/ifttt/v1/queries/orcanodes/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutOrcanode(int id, Orcanode orcanode)
        {
            if (id != orcanode.ID)
            {
                return BadRequest();
            }

            _databaseContext.Entry(orcanode).State = EntityState.Modified;

            try
            {
                await _databaseContext.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!OrcanodeExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }

        // POST: api/ifttt/v1/queries/orcanodes
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<Orcanode>> PostOrcanode(Orcanode orcanode)
        {
            _databaseContext.Orcanodes.Add(orcanode);
            await _databaseContext.SaveChangesAsync();

            return CreatedAtAction("GetOrcanode", new { id = orcanode.ID }, orcanode);
        }

        // DELETE: api/ifttt/v1/queries/orcanodes/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteOrcanode(int id)
        {
            var orcanode = await _databaseContext.Orcanodes.FindAsync(id);
            if (orcanode == null)
            {
                return NotFound();
            }

            _databaseContext.Orcanodes.Remove(orcanode);
            await _databaseContext.SaveChangesAsync();

            return NoContent();
        }

        private bool OrcanodeExists(int id)
        {
            return _databaseContext.Orcanodes.Any(e => e.ID == id);
        }
#endif
    }
}
