using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OrcanodeMonitor.Core;
using System.Drawing;

namespace OrcanodeMonitor.Pages
{
    public class IndexModel : PageModel
    {
        private readonly ILogger<IndexModel> _logger;
        public List<Orcanode> Nodes => State.LastResult?.NodeList ?? new List<Orcanode>();

        public IndexModel(ILogger<IndexModel> logger)
        {
            _logger = logger;
        }

        public string NodeColor(Orcanode node)
        {
            OrcanodeStatus status = node.Status;
            if (status == OrcanodeStatus.Offline)
            {
                return ColorTranslator.ToHtml(Color.Red);
            }
            return ColorTranslator.ToHtml(Color.LightGreen);
        }

        public void OnGet()
        {

        }
    }
}
