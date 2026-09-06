namespace EZBuddy.Core.Licensing;

public static class LicenseSigningKeys
{
    public const string CurrentKeyId = "ezbuddy-2026-01";

    public const string CurrentPublicKeyPem = """
-----BEGIN PUBLIC KEY-----
MIIBojANBgkqhkiG9w0BAQEFAAOCAY8AMIIBigKCAYEAnpksFFbhrLHFWyCowkiK
7f93sC/ExM9QibP5opLjqMl/gDIIxraig4k6Qqc2kfD36C+s0MxYllEqkPdQ+3+q
SjxXhq9sEYcRkZOIh6SRnmGJOAnV/bQ02N8HOdS3VrdEbJkyIf+F4uysnt4naVUl
jGEYnppLN7QRh5X8JJt3bURWmzj8NJW4yKJedAlhALrdQ8AjvUJSqK0GLK8A9slU
nFuxX267hwIAMtAf2hzUUGxIl9CFO62UfQ3zp98yo5WHkAfUgKpozCZTXHsw91wX
VrIawJsWayWodASFa6tNpr2S9lUMelV8d0SNXsIAklcw8bJ2yeb+l2fazIpHslMK
e5xnZQD0/al64YTyRPGavsiSsyrWNorBwu9DYdfa59qNEHeuaydeMlPkgEbPyCop
QSjmZWhYHUN2h6X52LUTjMQVm7mLCV+BLb2urSiGuwDgdzpgcA0OXSN1okjWwa0S
o1/O+kAR4BjBIMgXvVsQvZ1KtN8K+6hpu6zm7M6J1ZmrAgMBAAE=
-----END PUBLIC KEY-----
""";

    public static IReadOnlyDictionary<string, string> PublicKeys { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [CurrentKeyId] = CurrentPublicKeyPem
        };
}
