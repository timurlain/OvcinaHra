namespace OvcinaHra.Api.Configuration;

public sealed class BotConsultOptions
{
    public string? Url { get; set; }
    public string? ApiKey { get; set; }

    public bool IsConfigured =>
        Uri.TryCreate(Url, UriKind.Absolute, out _)
        && !string.IsNullOrWhiteSpace(ApiKey);
}
