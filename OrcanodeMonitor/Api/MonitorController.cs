// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace OrcanodeMonitor.Api
{
    [Route("api/[controller]")]
    [ApiController]
    public class MonitorController : ControllerBase
    {
        public IActionResult Get()
        {
            // Logic for handling GET requests
            return Ok("Hello from API!");
        }
    }
}
