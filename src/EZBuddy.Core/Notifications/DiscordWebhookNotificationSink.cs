using System.Net.Http.Json;

namespace EZBuddy.Core.Notifications;

public sealed class DiscordWebhookNotificationSink : INotificationSink, IDisposable
{
    private readonly Uri _webhookUri;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    public DiscordWebhookNotificationSink(Uri webhookUri, HttpClient? httpClient = null)
    {
        _webhookUri = ValidateWebhookUri(webhookUri);
        _httpClient = httpClient ?? new HttpClient();
        _ownsClient = httpClient is null;
    }

    public string Key => "discord-webhook";

    public async Task SendAsync(EZNotification notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        cancellationToken.ThrowIfCancellationRequested();

        var fields = notification.Fields?
            .Take(20)
            .Select(field => new
            {
                name = Truncate(field.Key, 256),
                value = Truncate(field.Value, 1024),
                inline = false
            })
            .ToArray();

        var payload = new
        {
            username = "EZBuddy",
            allowed_mentions = new { parse = Array.Empty<string>() },
            embeds = new[]
            {
                new
                {
                    title = Truncate(notification.Title, 256),
                    description = Truncate(notification.Message, 4096),
                    timestamp = notification.TimestampUtc.ToUniversalTime().ToString("O"),
                    fields
                }
            }
        };

        using var response = await _httpClient.PostAsJsonAsync(_webhookUri, payload, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }

    private static Uri ValidateWebhookUri(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Discord webhook URL must be an absolute HTTPS URI.", nameof(uri));
        }

        var host = uri.Host;
        if (!host.Equals("discord.com", StringComparison.OrdinalIgnoreCase) &&
            !host.Equals("discordapp.com", StringComparison.OrdinalIgnoreCase) &&
            !host.EndsWith(".discord.com", StringComparison.OrdinalIgnoreCase) &&
            !host.EndsWith(".discordapp.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Discord webhook host is not allowlisted.", nameof(uri));
        }

        if (!uri.AbsolutePath.Contains("/api/webhooks/", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("URI is not a Discord webhook endpoint.", nameof(uri));
        }

        return uri;
    }

    private static string Truncate(string? value, int maximumLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }
}
