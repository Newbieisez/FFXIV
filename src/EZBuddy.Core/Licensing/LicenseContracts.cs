using EZBuddy.Core.Engine;

namespace EZBuddy.Core.Licensing;

public enum LicenseTier
{
    None = 0,
    FreeTrial = 1,
    Standard = 2,
    Pro = 3,
    Lifetime = 4,
    Development = 99
}

public enum LicenseValidationSource
{
    None,
    OfflineToken,
    OnlineLease,
    DevelopmentLease
}

public sealed record LicenseEntitlement(
    string LicenseId,
    string HardwareId,
    string AccountEmail,
    LicenseTier Tier,
    DateTimeOffset IssuedUtc,
    DateTimeOffset? ExpiresUtc,
    HashSet<string> Features,
    string Signature,
    string KeyId,
    int SchemaVersion = 1);

public sealed record LicenseStatus(
    bool IsValid,
    LicenseTier Tier,
    DateTimeOffset? ExpiresUtc,
    string Message,
    string HardwareId,
    LicenseValidationSource Source,
    IReadOnlySet<string> Features)
{
    public bool HasFeature(string featureKey)
        => IsValid && (Features.Contains("*") || Features.Contains(featureKey));

    public static LicenseStatus NotEvaluated(string hardwareId)
        => new(false, LicenseTier.None, null, "License has not been evaluated.", hardwareId, LicenseValidationSource.None, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
}

public interface IHardwareIdentityProvider
{
    string GetAnonymousHardwareId();
}

public interface ILicenseTokenValidator
{
    bool TryValidate(LicenseEntitlement entitlement, string expectedHardwareId, DateTimeOffset nowUtc, out string failureReason);
}

public interface ILicenseStore
{
    Task<string?> ReadAsync(CancellationToken cancellationToken = default);
    Task WriteAsync(string serializedEntitlement, CancellationToken cancellationToken = default);
    Task DeleteAsync(CancellationToken cancellationToken = default);
}

public interface IOnlineLicenseClient
{
    Task<string?> RequestTrialAsync(string email, string hardwareId, CancellationToken cancellationToken = default);
    Task<string?> RefreshLeaseAsync(string currentLicenseId, string hardwareId, CancellationToken cancellationToken = default);
}

public interface IExecutionGate
{
    bool CanExecute(ActivityCategory category, out string message);
}

public sealed class AllowAllExecutionGate : IExecutionGate
{
    public static AllowAllExecutionGate Instance { get; } = new();
    private AllowAllExecutionGate() { }

    public bool CanExecute(ActivityCategory category, out string message)
    {
        message = "Execution permitted.";
        return true;
    }
}
