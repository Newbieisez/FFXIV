namespace EZBuddy.Core.Licensing;

public static class LicenseSigningKeys
{
    public const string CurrentKeyId = "ezbuddy-2026-01";

    public const string CurrentPublicKeyPem = """
-----BEGIN PUBLIC KEY-----
MIIBojANBgkqhkiG9w0BAQEFAAOCAY8AMIIBigKCAYEAsztwnYIr0ooiorbvyne2
JQSoUzz7CnPr0WzuJQbj6DxbRmkVSUIn0t8e9IOW/EeN3dyPStoUoXLVAw4jSKou
p3lL7QCGCygVG/G8AOi5bYjsMWN/lSVdnI9d6cGGoKA86R0NdGJQra5M88PiKACL
eIGXiBn7FlcKqfrRzAlIINB3N5xsP8cE84ZtSSQR/jntbR+eKGaBC/DDaxev33GG
3bBeqMfZ4OwM/Kke2e9XsQR/T8GDUi4k+eg2EnfD4D7KkNeX1XgQz+vatdOAVLn2
U3G+1Cg4Aq8d4TkzJQChfzeEL9NCQdwgbdrGTy1go2ZeYzGJ6/1hiPMTakpbLH+I
habaFBxCVCd8iuDKnG4ffujX4waDncckj4h5P+pKs5Jp3RFTruUD0twX1ML49TCb
B1tcMXDlmWUO1PAPHfEfv9/RlQjGtUyGYX6bm+m9+i4jqYNG6h/1WIL9BmscIvaB
MtdyiTGBfzOkLI3GBM/s10t8ZfBGrql/wtJyKCSl0TY5AgMBAAE=
-----END PUBLIC KEY-----
""";

    public static IReadOnlyDictionary<string, string> PublicKeys { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [CurrentKeyId] = CurrentPublicKeyPem
        };
}
