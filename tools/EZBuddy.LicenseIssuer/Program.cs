using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EZBuddy.Core.Licensing;

try
{
    var options = ParseArgs(args);
    if (args.Length == 0 || options.ContainsKey("help"))
    {
        PrintUsage();
        return 0;
    }

    var command = args[0].Trim().ToLowerInvariant();
    return command switch
    {
        "issue" => Issue(options),
        "keygen" => Keygen(options),
        _ => Fail($"Unknown command '{command}'.")
    };
}
catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or CryptographicException)
{
    return Fail(ex.Message);
}

static int Issue(IReadOnlyDictionary<string, string> options)
{
    var email = Required(options, "email").Trim();
    var hardwareId = Required(options, "hardware").Trim().ToUpperInvariant();
    var privateKeyPath = options.TryGetValue("private-key", out var explicitKey)
        ? explicitKey
        : Environment.GetEnvironmentVariable("EZBUDDY_LICENSE_PRIVATE_KEY_FILE") ?? string.Empty;

    if (string.IsNullOrWhiteSpace(privateKeyPath) || !File.Exists(privateKeyPath))
    {
        return Fail("Private signing key not found. Use --private-key <path> or EZBUDDY_LICENSE_PRIVATE_KEY_FILE.");
    }

    if (!email.Contains('@', StringComparison.Ordinal))
    {
        return Fail("--email must be a valid email address.");
    }

    if (hardwareId.Length < 32)
    {
        return Fail("--hardware does not look like a valid EZBuddy anonymous hardware ID.");
    }

    var tier = ParseTier(options.TryGetValue("tier", out var tierText) ? tierText : "FreeTrial");
    var days = ParseInt(options, "days", 7, min: 1, max: tier == LicenseTier.FreeTrial ? 30 : 3650);
    var keyId = options.TryGetValue("key-id", out var keyIdText) ? keyIdText : LicenseSigningKeys.CurrentKeyId;

    if (!LicenseSigningKeys.PublicKeys.TryGetValue(keyId, out var expectedPublicKeyPem))
    {
        return Fail($"Key ID '{keyId}' is not trusted by the current EZBuddy client build.");
    }

    var outputPath = options.TryGetValue("out", out var outText)
        ? outText
        : Path.Combine(Environment.CurrentDirectory, $"EZBuddy_{Sanitize(email)}_{DateTime.UtcNow:yyyyMMddHHmmss}.ezlic");

    var features = ParseFeatures(options.TryGetValue("features", out var featureText) ? featureText : "*");
    if (features.Count == 0)
    {
        return Fail("At least one feature must be enabled. Use --features * for all current features.");
    }

    using var signer = RSA.Create();
    signer.ImportFromPem(File.ReadAllText(privateKeyPath));

    if (!PrivateKeyMatchesTrustedPublicKey(signer, expectedPublicKeyPem))
    {
        return Fail($"The private key does not match EZBuddy trusted public key '{keyId}'. Refusing to issue an unusable license.");
    }

    var now = DateTimeOffset.UtcNow;
    var unsigned = new LicenseEntitlement(
        LicenseId: Guid.NewGuid().ToString("N"),
        HardwareId: hardwareId,
        AccountEmail: email.ToLowerInvariant(),
        Tier: tier,
        IssuedUtc: now,
        ExpiresUtc: tier == LicenseTier.Lifetime ? null : now.AddDays(days),
        Features: features,
        Signature: string.Empty,
        KeyId: keyId,
        SchemaVersion: 1);

    var payload = Encoding.UTF8.GetBytes(LicenseCanonicalizer.BuildPayload(unsigned));
    var signature = signer.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
    var signed = unsigned with { Signature = Convert.ToBase64String(signature) };

    var fullOutputPath = Path.GetFullPath(outputPath);
    Directory.CreateDirectory(Path.GetDirectoryName(fullOutputPath)!);
    var json = JsonSerializer.Serialize(signed, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
    File.WriteAllText(fullOutputPath, json, Encoding.UTF8);

    Console.WriteLine("EZBuddy entitlement issued successfully.");
    Console.WriteLine($"License ID: {signed.LicenseId}");
    Console.WriteLine($"Tier: {signed.Tier}");
    Console.WriteLine($"Email: {signed.AccountEmail}");
    Console.WriteLine($"Hardware: {signed.HardwareId}");
    Console.WriteLine($"Issued: {signed.IssuedUtc:u}");
    Console.WriteLine($"Expires: {(signed.ExpiresUtc?.ToString("u") ?? "Never")}");
    Console.WriteLine($"Features: {string.Join(", ", signed.Features)}");
    Console.WriteLine($"Signing key: {signed.KeyId}");
    Console.WriteLine($"Output: {fullOutputPath}");
    return 0;
}

static int Keygen(IReadOnlyDictionary<string, string> options)
{
    var directory = options.TryGetValue("out-dir", out var dir) ? dir : Environment.CurrentDirectory;
    Directory.CreateDirectory(directory);

    using var rsa = RSA.Create(3072);
    var privatePem = rsa.ExportPkcs8PrivateKeyPem();
    var publicPem = rsa.ExportSubjectPublicKeyInfoPem();
    var suffix = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
    var privatePath = Path.Combine(directory, $"EZBuddy_License_Private_Key_{suffix}.pem");
    var publicPath = Path.Combine(directory, $"EZBuddy_License_Public_Key_{suffix}.pem");

    File.WriteAllText(privatePath, privatePem, Encoding.ASCII);
    File.WriteAllText(publicPath, publicPem, Encoding.ASCII);

    Console.WriteLine("Generated a NEW signing keypair for future key rotation.");
    Console.WriteLine("This keypair is NOT trusted by existing EZBuddy clients until its public key is added to LicenseSigningKeys and a new client build is released.");
    Console.WriteLine($"PRIVATE (keep secret): {Path.GetFullPath(privatePath)}");
    Console.WriteLine($"PUBLIC: {Path.GetFullPath(publicPath)}");
    return 0;
}

static bool PrivateKeyMatchesTrustedPublicKey(RSA signer, string expectedPublicKeyPem)
{
    using var expected = RSA.Create();
    expected.ImportFromPem(expectedPublicKeyPem);

    var actualParameters = signer.ExportParameters(includePrivateParameters: false);
    var expectedParameters = expected.ExportParameters(includePrivateParameters: false);

    if (actualParameters.Modulus is null || actualParameters.Exponent is null ||
        expectedParameters.Modulus is null || expectedParameters.Exponent is null)
    {
        return false;
    }

    return CryptographicOperations.FixedTimeEquals(actualParameters.Modulus, expectedParameters.Modulus) &&
           CryptographicOperations.FixedTimeEquals(actualParameters.Exponent, expectedParameters.Exponent);
}

static Dictionary<string, string> ParseArgs(string[] args)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 1; i < args.Length; i++)
    {
        var arg = args[i];
        if (!arg.StartsWith("--", StringComparison.Ordinal))
        {
            continue;
        }

        var key = arg[2..];
        if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
        {
            result[key] = args[++i];
        }
        else
        {
            result[key] = "true";
        }
    }

    return result;
}

static string Required(IReadOnlyDictionary<string, string> options, string key)
{
    if (!options.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
    {
        throw new ArgumentException($"Missing required --{key} value.");
    }

    return value;
}

static int ParseInt(IReadOnlyDictionary<string, string> options, string key, int defaultValue, int min, int max)
{
    if (!options.TryGetValue(key, out var text))
    {
        return defaultValue;
    }

    if (!int.TryParse(text, out var value) || value < min || value > max)
    {
        throw new ArgumentException($"--{key} must be between {min} and {max}.");
    }

    return value;
}

static LicenseTier ParseTier(string text)
    => Enum.TryParse<LicenseTier>(text, ignoreCase: true, out var tier) &&
       tier is LicenseTier.FreeTrial or LicenseTier.Standard or LicenseTier.Pro or LicenseTier.Lifetime
        ? tier
        : throw new ArgumentException("--tier must be FreeTrial, Standard, Pro, or Lifetime.");

static HashSet<string> ParseFeatures(string text)
    => new(
        text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.ToLowerInvariant()),
        StringComparer.OrdinalIgnoreCase);

static string Sanitize(string value)
    => string.Concat(value.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    PrintUsage();
    return 1;
}

static void PrintUsage()
{
    Console.WriteLine("EZBuddy License Issuer");
    Console.WriteLine("  issue --email user@example.com --hardware <HWID> [--days 7] [--tier FreeTrial] [--features *] [--private-key path] [--out file.ezlic]");
    Console.WriteLine("  keygen [--out-dir path]");
    Console.WriteLine();
    Console.WriteLine("The issuer never stores or uploads the private key. The issue command refuses to sign if the private key does not match the trusted public key embedded in the current EZBuddy client.");
}
