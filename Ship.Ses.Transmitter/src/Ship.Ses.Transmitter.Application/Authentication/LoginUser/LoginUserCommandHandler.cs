using Ship.Ses.Transmitter.Application.Authentication.LoginUser;
using Ship.Ses.Transmitter.Application.Shared;
using MassTransit;

namespace Ship.Ses.Transmitter.Application.Authentication.LoginCustomer
{
    public sealed class LoginUserCommandHandler : IConsumer<LoginUserCommand>
    {
        private readonly IAuthenticationService _keycloakAuthService;

        public LoginUserCommandHandler(IAuthenticationService keycloakAuthService)
        {
            _keycloakAuthService = keycloakAuthService;
        }

        public async Task Consume(ConsumeContext<LoginUserCommand> query)
        {
            (string accessToken, string refreshToken) tokens = await _keycloakAuthService
                .RequestTokenUsingPasswordGrantAsync(query.Message.Username, query.Message.Password);

            await query.RespondAsync(new LoginUserDto(tokens.accessToken, tokens.refreshToken));
        }
    }
}
