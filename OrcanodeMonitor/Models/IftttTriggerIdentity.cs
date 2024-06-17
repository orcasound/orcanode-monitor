using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json.Linq;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace OrcanodeMonitor.Models
{
    public class IftttTriggerIdentityDTO
    {
        public IftttTriggerIdentityDTO(string identity)
        {
            TriggerIdentity = identity;
        }
        [JsonPropertyName("trigger_identity")]
        public string TriggerIdentity { get; private set; }
    }

    public class IftttTriggerIdentity
    {
        /// <summary>
        /// Database key.
        /// </summary>
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public string Identity { get; set; }

        public IftttTriggerIdentityDTO ToIftttTriggerIdentityDTO() => new IftttTriggerIdentityDTO(Identity);

    }
}
