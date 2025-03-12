using Ship.Ses.Transmitter.Application.Authentication.RefreshUserToken;
using Ship.Ses.Transmitter.Application.Shared;
using MassTransit;

namespace Ship.Ses.Transmitter.Application.Authentication.ReLoginCustomer
{
    public sealed class RefreshUserTokenCommandHandler : IConsumer<RefreshUserTokenCommand>
    {
        private readonly IAuthenticationService _keycloakAuthService;

        public RefreshUserTokenCommandHandler(IAuthenticationService keycloakAuthService)
        {
            _keycloakAuthService = keycloakAuthService;
        }

        public async Task Consume(ConsumeContext<RefreshUserTokenCommand> query)
        {
            (string accessToken, string refreshToken) tokens = await _keycloakAuthService
                .RefreshAccessTokenAsync(query.Message.RefreshToken);

            await query.RespondAsync(new RefreshUserTokenDto(tokens.accessToken, tokens.refreshToken));
        }
    }
}
