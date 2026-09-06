using System.Net.Http.Json;
using System.Text.Json;

namespace EZBuddy.Core.Licensing;

public sealed class HttpOnlineLicenseClient : IOnlineLicenseClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    public HttpOnlineLicenseClient(Uri baseUri, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(baseUri);
        if (!baseUri.IsAbsoluteUri || baseUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("License API endpoint must be an absolute HTTPS URI.", nameof(baseUri));
        }

        _ownsClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.BaseAddress = baseUri;
        _httpClient.Timeout = TimeSpan.FromSeconds(15);
    }

    public Task<string?> RequestTrialAsync(string email, string hardwareId, CancellationToken cancellationToken = default)
        => PostAsync("api/trial/issue", new TrialRequest(email, hardwareId), cancellationToken);

    public Task<string?> RefreshLeaseAsync(string currentLicenseId, string hardwareId, CancellationToken cancellationToken = default)
        => PostAsync("api/license/refresh", new RefreshRequest(currentLicenseId, hardwareId), cancellationToken);

    public void Dispose()
    {
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }

    private async Task<string?> PostAsync<T>(string relativePath, T payload, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(relativePath, payload, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("license", out var licenseElement))
            {
                return licenseElement.ValueKind == JsonValueKind.String
                    ? licenseElement.GetString()
                    : licenseElement.GetRawText();
            }
        }
        catch (JsonException)
        {
            // A service may return the entitlement JSON directly.
        }

        return body;
    }

    private sealed record TrialRequest(string Email, string HardwareId);
    private sealed record RefreshRequest(string LicenseId, string HardwareId);
}
