using System;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;

namespace FieldGraphX.Services
{
    /// <summary>Resolves the Power Platform environment id for building flow portal URLs.</summary>
    public sealed class EnvironmentInfoService
    {
        private readonly IOrganizationService _service;

        public EnvironmentInfoService(IOrganizationService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        /// <summary>
        /// Returns the environment id, or an empty string when the org doesn't expose it.
        /// The failure reason is reported via <paramref name="onWarning"/> instead of being
        /// silently swallowed — flow links without an environment segment may not resolve.
        /// </summary>
        public string GetEnvironmentId(Action<string> onWarning = null)
        {
            try
            {
                var response = (RetrieveCurrentOrganizationResponse)_service.Execute(
                    new RetrieveCurrentOrganizationRequest());
                var environmentId = response.Detail?.EnvironmentId;
                if (string.IsNullOrWhiteSpace(environmentId))
                {
                    onWarning?.Invoke("Organization did not report an EnvironmentId; " +
                                      "flow links will open without the environment segment.");
                    return string.Empty;
                }
                return environmentId;
            }
            catch (Exception ex)
            {
                onWarning?.Invoke($"Could not determine environment id ({ex.Message}); " +
                                  "flow links will open without the environment segment.");
                return string.Empty;
            }
        }

        public static string BuildFlowUrl(string environmentId, Guid workflowIdUnique)
        {
            return string.IsNullOrWhiteSpace(environmentId)
                ? $"https://make.powerautomate.com/flows/{workflowIdUnique:D}/details"
                : $"https://make.powerautomate.com/environments/{environmentId}/flows/{workflowIdUnique:D}/details";
        }
    }
}
