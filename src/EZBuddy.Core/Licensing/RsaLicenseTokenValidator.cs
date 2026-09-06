using System.Security.Cryptography;
using System.Text;

namespace EZBuddy.Core.Licensing;

public sealed class RsaLicenseTokenValidator : ILicenseTokenValidator
{
    private readonly IReadOnlyDictionary<string, string> _publicKeysById;
    private readonly TimeSpan _clockSkewTolerance;

    public RsaLicenseTokenValidator(
        IReadOnlyDictionary<string, string> publicKeysById,
        TimeSpan? clockSkewTolerance = null)
    {
        _publicKeysById = publicKeysById ?? throw new ArgumentNullException(nameof(publicKeysById));
        _clockSkewTolerance = clockSkewTolerance ?? TimeSpan.FromMinutes(5);
    }

    public bool TryValidate(
        LicenseEntitlement entitlement,
        string expectedHardwareId,
        DateTimeOffset nowUtc,
        out string failureReason)
    {
        ArgumentNullException.ThrowIfNull(entitlement);

        if (entitlement.SchemaVersion != 1)
        {
            failureReason = $"Unsupported license schema version {entitlement.SchemaVersion}.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(entitlement.LicenseId))
        {
            failureReason = "License ID is missing.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(entitlement.KeyId) ||
            !_publicKeysById.TryGetValue(entitlement.KeyId, out var publicKeyPem) ||
            string.IsNullOrWhiteSpace(publicKeyPem))
        {
            failureReason = $"Unknown signing key '{entitlement.KeyId}'.";
            return false;
        }

        if (!FixedTimeEquals(entitlement.HardwareId, expectedHardwareId))
        {
            failureReason = "License is bound to a different machine.";
            return false;
        }

        if (entitlement.IssuedUtc > nowUtc + _clockSkewTolerance)
        {
            failureReason = "License issue time is in the future.";
            return false;
        }

        if (entitlement.ExpiresUtc is { } expiresUtc && expiresUtc <= nowUtc - _clockSkewTolerance)
        {
            failureReason = $"License expired on {expiresUtc:yyyy-MM-dd HH:mm} UTC.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(entitlement.Signature))
        {
            failureReason = "License signature is missing.";
            return false;
        }

        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(entitlement.Signature);
        }
        catch (FormatException)
        {
            failureReason = "License signature is malformed.";
            return false;
        }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);
            var payload = Encoding.UTF8.GetBytes(LicenseCanonicalizer.BuildPayload(entitlement));
            var valid = rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
            failureReason = valid ? string.Empty : "License cryptographic signature is invalid.";
            return valid;
        }
        catch (CryptographicException)
        {
            failureReason = "License signing key is invalid or incompatible.";
            return false;
        }
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = SHA256.HashData(Encoding.UTF8.GetBytes(left ?? string.Empty));
        var rightBytes = SHA256.HashData(Encoding.UTF8.GetBytes(right ?? string.Empty));
        return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}

public static class LicenseCanonicalizer
{
    public static string BuildPayload(LicenseEntitlement entitlement)
    {
        var features = entitlement.Features
            .Where(feature => !string.IsNullOrWhiteSpace(feature))
            .Select(feature => feature.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(feature => feature, StringComparer.Ordinal);

        return string.Join("\n",
            $"schema={entitlement.SchemaVersion}",
            $"licenseId={entitlement.LicenseId.Trim()}",
            $"hardwareId={entitlement.HardwareId.Trim().ToUpperInvariant()}",
            $"account={entitlement.AccountEmail.Trim().ToLowerInvariant()}",
            $"tier={(int)entitlement.Tier}",
            $"issued={entitlement.IssuedUtc.ToUnixTimeSeconds()}",
            $"expires={(entitlement.ExpiresUtc?.ToUnixTimeSeconds().ToString() ?? "never")}",
            $"features={string.Join(",", features)}",
            $"keyId={entitlement.KeyId.Trim()}");
    }
}
