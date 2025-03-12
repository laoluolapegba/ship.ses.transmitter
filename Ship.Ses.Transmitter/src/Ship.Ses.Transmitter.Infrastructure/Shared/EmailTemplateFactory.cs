using Ship.Ses.Transmitter.Application.Shared;
using Ship.Ses.Transmitter.Domain.Enums;
using Ship.Ses.Transmitter.Infrastructure.Exceptions;

namespace Ship.Ses.Transmitter.Infrastructure.Shared
{
    public class EmailTemplateFactory : IEmailTemplateFactory
    {
        private readonly string _templateDirectory = "EmailTemplates";

        public async Task<string> GetTemplateAsync(EmailTemplateType templateType)
        {
            var fileName = $"{templateType}.html";
            var filePath = Path.Combine(Directory.GetCurrentDirectory(), "..", "Ship.Ses.Transmitter.Infrastructure", "EmailTemplates", fileName);

            if (!File.Exists(filePath))
            {
                throw new InfrastructureException($"Template '{fileName}' not found in '{_templateDirectory}'.");
            }

            return await File.ReadAllTextAsync(filePath);
        }

    }

}
