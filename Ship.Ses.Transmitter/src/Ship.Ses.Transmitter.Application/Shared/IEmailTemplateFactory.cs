using Ship.Ses.Transmitter.Domain.Enums;

namespace Ship.Ses.Transmitter.Application.Shared
{
    public interface IEmailTemplateFactory
    {
        Task<string> GetTemplateAsync(EmailTemplateType templateType);
    }
}
