namespace Ship.Ses.Transmitter.Infrastructure.Settings;

public class FhirApiClientCertSettings
{
    public string? Path { get; set; }
    public string? Password { get; set; }

    public bool HasValue => !string.IsNullOrWhiteSpace(Path);
}
