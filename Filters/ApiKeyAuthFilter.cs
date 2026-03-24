using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using service_agent.Services;

namespace service_agent.Filters
{
    public class ApiKeyAuthFilter : IAuthorizationFilter
    {
        private const string ApiKeyHeaderName = "X-Api-Key";

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var apiKeyService = context.HttpContext.RequestServices.GetService<ApiKeyService>();
            var request = context.HttpContext.Request;

            if (!request.Headers.TryGetValue(ApiKeyHeaderName, out var providedApiKey))
            {
                context.Result = new UnauthorizedResult();
                return;
            }

            if (apiKeyService == null || providedApiKey != apiKeyService.ApiKey)
            {
                context.Result = new UnauthorizedResult();
            }
        }
    }
}
