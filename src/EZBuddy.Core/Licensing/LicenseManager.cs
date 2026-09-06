using System.Text.Json;

namespace EZBuddy.Core.Licensing;

public sealed class LicenseManager
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly IHardwareIdentityProvider _hardwareIdentityProvider;
    private readonly ILicenseStore _store;
    private readonly ILicenseTokenValidator _validator;
    private readonly IOnlineLicenseClient? _onlineClient;
    private readonly Func<DateTimeOffset> _utcNow;
    private LicenseStatus _currentStatus;

    public LicenseManager(
        IHardwareIdentityProvider hardwareIdentityProvider,
        ILicenseStore store,
        ILicenseTokenValidator validator,
        IOnlineLicenseClient? onlineClient = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        _hardwareIdentityProvider = hardwareIdentityProvider ?? throw new ArgumentNullException(nameof(hardwareIdentityProvider));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _onlineClient = onlineClient;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);

        var hardwareId = _hardwareIdentityProvider.GetAnonymousHardwareId();
        _currentStatus = LicenseStatus.NotEvaluated(hardwareId);
    }

    public LicenseStatus CurrentStatus => _currentStatus;

    public event EventHandler<LicenseStatus>? StatusChanged;

    public async Task<LicenseStatus> InitializeAsync(CancellationToken cancellationToken = default)
        => await RefreshStatusAsync(cancellationToken).ConfigureAwait(false);

    public async Task<LicenseStatus> RefreshStatusAsync(CancellationToken cancellationToken = default)
    {
        var hardwareId = _hardwareIdentityProvider.GetAnonymousHardwareId();
        var serialized = await _store.ReadAsync(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(serialized))
        {
            return UpdateStatus(new LicenseStatus(
                false,
                LicenseTier.None,
                null,
                "No EZBuddy license is installed. Activate a trial or enter a license.",
                hardwareId,
                LicenseValidationSource.None,
                EmptyFeatureSet()));
        }

        if (!TryDeserialize(serialized, out var entitlement, out var deserializeFailure))
        {
            return UpdateStatus(new LicenseStatus(
                false,
                LicenseTier.None,
                null,
                deserializeFailure,
                hardwareId,
                LicenseValidationSource.None,
                EmptyFeatureSet()));
        }

        if (!_validator.TryValidate(entitlement!, hardwareId, _utcNow(), out var validationFailure))
        {
            return UpdateStatus(new LicenseStatus(
                false,
                entitlement!.Tier,
                entitlement.ExpiresUtc,
                validationFailure,
                hardwareId,
                LicenseValidationSource.OfflineToken,
                entitlement.Features));
        }

        return UpdateStatus(ToValidStatus(entitlement!, hardwareId, LicenseValidationSource.OfflineToken));
    }

    public async Task<LicenseStatus> ActivateOnlineTrialAsync(string email, CancellationToken cancellationToken = default)
    {
        if (_onlineClient is null)
        {
            return UpdateStatus(CurrentStatus with { Message = "Online trial activation is not configured in this build." });
        }

        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@', StringComparison.Ordinal))
        {
            return UpdateStatus(CurrentStatus with { Message = "Enter a valid email address to start a trial." });
        }

        var hardwareId = _hardwareIdentityProvider.GetAnonymousHardwareId();
        var serialized = await _onlineClient.RequestTrialAsync(email.Trim(), hardwareId, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(serialized))
        {
            return UpdateStatus(CurrentStatus with { Message = "Trial activation was not approved by the licensing service." });
        }

        if (!TryDeserialize(serialized, out var entitlement, out var deserializeFailure))
        {
            return UpdateStatus(CurrentStatus with { Message = deserializeFailure });
        }

        if (!_validator.TryValidate(entitlement!, hardwareId, _utcNow(), out var validationFailure))
        {
            return UpdateStatus(CurrentStatus with { Message = $"The licensing service returned an invalid entitlement: {validationFailure}" });
        }

        await _store.WriteAsync(serialized, cancellationToken).ConfigureAwait(false);
        return UpdateStatus(ToValidStatus(entitlement!, hardwareId, LicenseValidationSource.OnlineLease));
    }

    public async Task<LicenseStatus> InstallOfflineTokenAsync(string serializedEntitlement, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serializedEntitlement);

        var hardwareId = _hardwareIdentityProvider.GetAnonymousHardwareId();
        if (!TryDeserialize(serializedEntitlement, out var entitlement, out var deserializeFailure))
        {
            return UpdateStatus(CurrentStatus with { Message = deserializeFailure });
        }

        if (!_validator.TryValidate(entitlement!, hardwareId, _utcNow(), out var validationFailure))
        {
            return UpdateStatus(CurrentStatus with { Message = validationFailure });
        }

        await _store.WriteAsync(serializedEntitlement, cancellationToken).ConfigureAwait(false);
        return UpdateStatus(ToValidStatus(entitlement!, hardwareId, LicenseValidationSource.OfflineToken));
    }

    public async Task<LicenseStatus> TryRefreshOnlineLeaseAsync(CancellationToken cancellationToken = default)
    {
        if (_onlineClient is null || !CurrentStatus.IsValid)
        {
            return CurrentStatus;
        }

        var serializedCurrent = await _store.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (!TryDeserialize(serializedCurrent, out var currentEntitlement, out _))
        {
            return await RefreshStatusAsync(cancellationToken).ConfigureAwait(false);
        }

        var hardwareId = _hardwareIdentityProvider.GetAnonymousHardwareId();
        var refreshed = await _onlineClient.RefreshLeaseAsync(currentEntitlement!.LicenseId, hardwareId, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(refreshed))
        {
            return await RefreshStatusAsync(cancellationToken).ConfigureAwait(false);
        }

        return await InstallOfflineTokenAsync(refreshed, cancellationToken).ConfigureAwait(false);
    }

    public LicenseStatus IssueDevelopmentLease(TimeSpan duration)
    {
        var enabled = string.Equals(
            Environment.GetEnvironmentVariable("EZBUDDY_ALLOW_DEV_LICENSES"),
            "1",
            StringComparison.Ordinal);

        if (!enabled)
        {
            return UpdateStatus(CurrentStatus with { Message = "Development licenses are disabled. Set EZBUDDY_ALLOW_DEV_LICENSES=1 only on development machines." });
        }

        if (duration <= TimeSpan.Zero || duration > TimeSpan.FromDays(7))
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "Development leases must be greater than zero and no longer than seven days.");
        }

        var hardwareId = _hardwareIdentityProvider.GetAnonymousHardwareId();
        var expires = _utcNow().Add(duration);
        return UpdateStatus(new LicenseStatus(
            true,
            LicenseTier.Development,
            expires,
            $"Development lease active until {expires:yyyy-MM-dd HH:mm} UTC.",
            hardwareId,
            LicenseValidationSource.DevelopmentLease,
            new HashSet<string>(["*"], StringComparer.OrdinalIgnoreCase)));
    }

    public async Task ClearLicenseAsync(CancellationToken cancellationToken = default)
    {
        await _store.DeleteAsync(cancellationToken).ConfigureAwait(false);
        await RefreshStatusAsync(cancellationToken).ConfigureAwait(false);
    }

    private LicenseStatus UpdateStatus(LicenseStatus status)
    {
        _currentStatus = status;
        StatusChanged?.Invoke(this, status);
        return status;
    }

    private static LicenseStatus ToValidStatus(
        LicenseEntitlement entitlement,
        string hardwareId,
        LicenseValidationSource source)
    {
        var expiration = entitlement.ExpiresUtc is null
            ? "without expiration"
            : $"until {entitlement.ExpiresUtc:yyyy-MM-dd HH:mm} UTC";

        return new LicenseStatus(
            true,
            entitlement.Tier,
            entitlement.ExpiresUtc,
            $"{entitlement.Tier} license active {expiration}.",
            hardwareId,
            source,
            entitlement.Features);
    }

    private static bool TryDeserialize(
        string? serialized,
        out LicenseEntitlement? entitlement,
        out string failure)
    {
        entitlement = null;
        if (string.IsNullOrWhiteSpace(serialized))
        {
            failure = "License file is empty.";
            return false;
        }

        try
        {
            entitlement = JsonSerializer.Deserialize<LicenseEntitlement>(serialized, JsonOptions);
            if (entitlement is null)
            {
                failure = "License file could not be parsed.";
                return false;
            }

            failure = string.Empty;
            return true;
        }
        catch (JsonException)
        {
            failure = "License file is corrupt or has an unsupported format.";
            return false;
        }
    }

    private static IReadOnlySet<string> EmptyFeatureSet()
        => new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}
