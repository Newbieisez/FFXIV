using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    public async Task ExecutionGuardAllowsOnlyEntitledFeatures()
    {
        using var keys = TestSigningKeys.Create();
        var now = DateTimeOffset.UtcNow;
        var entitlement = keys.Sign(CreateEntitlement(
            hardwareId: "ABC123",
            issuedUtc: now.AddMinutes(-1),
            expiresUtc: now.AddDays(7),
            features: new HashSet<string>(["core", "utility"], StringComparer.OrdinalIgnoreCase)));

        var serialized = JsonSerializer.Serialize(entitlement, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var store = new MemoryLicenseStore(serialized);
        var manager = new LicenseManager(
            new StaticHardwareIdentityProvider("ABC123"),
            store,
            new RsaLicenseTokenValidator(keys.PublicKeys, TimeSpan.Zero),
            utcNow: () => now);

        await manager.InitializeAsync(TestContext.Current.CancellationToken);
        LicenseRuntime.Configure(manager);

        Assert.True(LicenseExecutionGuard.Instance.CanExecute(ActivityCategory.Utility, out _));
        Assert.False(LicenseExecutionGuard.Instance.CanExecute(ActivityCategory.Duty, out var failure));
        Assert.Contains("does not include", failure, StringComparison.OrdinalIgnoreCase);
    }

    private static LicenseEntitlement CreateEntitlement(
        string hardwareId,
        DateTimeOffset? issuedUtc = null,
        DateTimeOffset? expiresUtc = null,
        IReadOnlySet<string>? features = null)
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

    private sealed class StaticHardwareIdentityProvider(string hardwareId) : IHardwareIdentityProvider
    {
        public string GetAnonymousHardwareId() => hardwareId;
    }

    private sealed class MemoryLicenseStore(string? value = null) : ILicenseStore
    {
        private string? _value = value;

        public Task<string?> ReadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_value);

        public Task WriteAsync(string serializedEntitlement, CancellationToken cancellationToken = default)
        {
            _value = serializedEntitlement;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(CancellationToken cancellationToken = default)
        {
            _value = null;
            return Task.CompletedTask;
        }
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
