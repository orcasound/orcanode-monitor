using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.IdentityModel.Tokens;
using OrcanodeMonitor.Core;
using OrcanodeMonitor.Data;

namespace OrcanodeMonitor.Pages
{
    public class OrcaHelloNodeModel : PageModel
    {
        public string AksUrl => Environment.GetEnvironmentVariable("AZURE_AKS_URL") ?? "";

        private OrcanodeMonitorContext _databaseContext;
        private readonly ILogger<OrcaHelloNodeModel> _logger;
        private string _logData;
        private string _slug;

        public string LogData => _logData;

        public OrcaHelloNodeModel(OrcanodeMonitorContext context, ILogger<OrcaHelloNodeModel> logger)
        {
            _databaseContext = context;
            _logger = logger;
            _slug = string.Empty;
            _logData = string.Empty;
        }

        public async Task<IActionResult> OnGetAsync(string slug)
        {
            _slug = slug;
            _logData = await Fetcher.GetOrcaHelloLogAsync(slug, _logger);
            if (_logData.IsNullOrEmpty())
            {
                return NotFound(); // Returns a 404 error page
            }
            return Page();
        }
    }
}
