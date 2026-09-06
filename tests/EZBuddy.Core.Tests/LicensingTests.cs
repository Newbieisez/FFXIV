using System.Security.Cryptography;
using System.Text;
using EZBuddy.Core.Engine;
using EZBuddy.Core.Licensing;

namespace EZBuddy.Core.Tests;

public sealed class LicensingTests
{
    [Fact]
    public void ValidSignedEntitlementPasses()
    {
        using var keys = TestSigningKeys.Create();
        var entitlement = keys.Sign(CreateEntitlement(hardwareId: "ABC123", expiresUtc: DateTimeOffset.UtcNow.AddDays(7)));
        var validator = new RsaLicenseTokenValidator(keys.PublicKeys, TimeSpan.Zero);

        var valid = validator.TryValidate(entitlement, "ABC123", DateTimeOffset.UtcNow, out var failure);

        Assert.True(valid, failure);
        Assert.Equal(string.Empty, failure);
    }

    [Fact]
    public void HardwareMismatchIsRejected()
    {
        using var keys = TestSigningKeys.Create();
        var entitlement = keys.Sign(CreateEntitlement(hardwareId: "ABC123", expiresUtc: DateTimeOffset.UtcNow.AddDays(7)));
        var validator = new RsaLicenseTokenValidator(keys.PublicKeys, TimeSpan.Zero);

        var valid = validator.TryValidate(entitlement, "DIFFERENT", DateTimeOffset.UtcNow, out var failure);

        Assert.False(valid);
        Assert.Contains("different machine", failure, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExpiredEntitlementIsRejected()
    {
        using var keys = TestSigningKeys.Create();
        var now = DateTimeOffset.UtcNow;
        var entitlement = keys.Sign(CreateEntitlement(hardwareId: "ABC123", issuedUtc: now.AddDays(-8), expiresUtc: now.AddSeconds(-1)));
        var validator = new RsaLicenseTokenValidator(keys.PublicKeys, TimeSpan.Zero);

        var valid = validator.TryValidate(entitlement, "ABC123", now, out var failure);

        Assert.False(valid);
        Assert.Contains("expired", failure, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TamperingWithFeaturesInvalidatesSignature()
    {
        using var keys = TestSigningKeys.Create();
        var entitlement = keys.Sign(CreateEntitlement(hardwareId: "ABC123", expiresUtc: DateTimeOffset.UtcNow.AddDays(7)));
        entitlement = entitlement with
        {
            Features = new HashSet<string>(["core", "duty", "marketboard"], StringComparer.OrdinalIgnoreCase)
        };
        var validator = new RsaLicenseTokenValidator(keys.PublicKeys, TimeSpan.Zero);

        var valid = validator.TryValidate(entitlement, "ABC123", DateTimeOffset.UtcNow, out var failure);

        Assert.False(valid);
        Assert.Contains("signature", failure, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExecutionGuardAllowsOnlyEntitledFeaturesWithoutHardwareOrFileMocks()
    {
        var now = DateTimeOffset.UtcNow;
        var status = new LicenseStatus(
            IsValid: true,
            Tier: LicenseTier.FreeTrial,
            ExpiresUtc: now.AddDays(7),
            Message: "Trial active.",
            HardwareId: "opaque-test-hardware",
            Source: LicenseValidationSource.OfflineToken,
            Features: new HashSet<string>(["core", "utility"], StringComparer.OrdinalIgnoreCase));

        var provider = new StaticLicenseStatusProvider(status);
        var guard = new LicenseExecutionGuard(provider, () => now);

        Assert.True(guard.CanExecute(ActivityCategory.Utility, out _));
        Assert.False(guard.CanExecute(ActivityCategory.Duty, out var failure));
        Assert.Contains("does not include", failure, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, provider.LicenseRequiredNotifications);
    }

    private static LicenseEntitlement CreateEntitlement(
        string hardwareId,
        DateTimeOffset? issuedUtc = null,
        DateTimeOffset? expiresUtc = null,
        HashSet<string>? features = null)
    {
        var issued = issuedUtc ?? DateTimeOffset.UtcNow.AddMinutes(-1);
        return new LicenseEntitlement(
            LicenseId: Guid.NewGuid().ToString("N"),
            HardwareId: hardwareId,
            AccountEmail: "trial@example.com",
            Tier: LicenseTier.FreeTrial,
            IssuedUtc: issued,
            ExpiresUtc: expiresUtc ?? issued.AddDays(7),
            Features: features ?? new HashSet<string>(["core", "utility"], StringComparer.OrdinalIgnoreCase),
            Signature: string.Empty,
            KeyId: "test-key",
            SchemaVersion: 1);
    }

    private sealed class StaticLicenseStatusProvider(LicenseStatus? status) : ILicenseStatusProvider
    {
        public LicenseStatus? CurrentStatus { get; } = status;
        public int LicenseRequiredNotifications { get; private set; }

        public void NotifyLicenseRequired() => LicenseRequiredNotifications++;
    }

    private sealed class TestSigningKeys : IDisposable
    {
        private readonly RSA _privateKey;

        private TestSigningKeys(RSA privateKey, string publicPem)
        {
            _privateKey = privateKey;
            PublicKeys = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["test-key"] = publicPem
            };
        }

        public IReadOnlyDictionary<string, string> PublicKeys { get; }

        public static TestSigningKeys Create()
        {
            var rsa = RSA.Create(2048);
            return new TestSigningKeys(rsa, rsa.ExportSubjectPublicKeyInfoPem());
        }

        public LicenseEntitlement Sign(LicenseEntitlement entitlement)
        {
            var payload = Encoding.UTF8.GetBytes(LicenseCanonicalizer.BuildPayload(entitlement));
            var signature = _privateKey.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
            return entitlement with { Signature = Convert.ToBase64String(signature) };
        }

        public void Dispose() => _privateKey.Dispose();
    }
}
