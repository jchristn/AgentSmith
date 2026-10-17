namespace Hugo.Server.Services
{
    using Hugo.Core.Models;
    using Hugo.Core.Security;

    /// <summary>Resolved request-local standalone chat identity. Never returned to visitors.</summary>
    public class StandaloneChatAccess
    {
        /// <summary>Freshly validated profile.</summary>
        public AgentProfile Profile { get; set; } = new AgentProfile();

        /// <summary>Visitor or publishing-credential identity.</summary>
        public RequestContext Principal { get; set; } = new RequestContext();
    }
}
