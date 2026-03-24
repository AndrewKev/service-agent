using System;

namespace service_agent.Services
{
    public class ApiKeyService
    {
        private readonly string _apiKey;
        public string ApiKey => _apiKey;

        public ApiKeyService()
        {
            _apiKey = GenerateApiKey();
        }

        private string GenerateApiKey()
        {
            return Guid.NewGuid().ToString("N");
        }
    }
}
