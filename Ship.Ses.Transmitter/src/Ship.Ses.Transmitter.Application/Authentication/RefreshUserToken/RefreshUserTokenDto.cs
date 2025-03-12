namespace Ship.Ses.Transmitter.Application.Authentication.ReLoginCustomer
{
    public sealed record RefreshUserTokenDto(string AccessToken, string RefreshToken);
}
