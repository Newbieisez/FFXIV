# EZBuddy Licensing & Trial Operations

EZBuddy uses signed entitlements so trial and paid access can be validated offline without shipping a private signing secret in the client.

## Security model

- The EZBuddy client contains only the RSA public verification key.
- The RSA private key must remain outside GitHub, outside CI, and outside every distributed EZBuddy package.
- Issued entitlements are bound to the anonymous hardware ID shown on the EZBuddy **License & Trial Activation** page.
- The hardware ID is a SHA-256 digest derived by the Windows host from stable machine identifiers; raw identifiers are not written to the entitlement.
- The activity queue performs entitlement and feature checks before executing activity steps.
- `.pem`, `.key`, `.pfx`, `.p12`, and `.ezlic` files are ignored by Git.

## Keep this file safe

The active private signing key for key ID `ezbuddy-2026-01` should be kept in secure private storage. Losing it means future entitlements for that key ID cannot be issued. Exposing it requires rotating the public key and releasing a new client build.

Do not commit the key and do not place it in the EZBuddy install directory.

## Build the issuer

```powershell
dotnet build tools/EZBuddy.LicenseIssuer/EZBuddy.LicenseIssuer.csproj -c Release
```

The CI pipeline also builds the issuer as a separate artifact. It is never copied into the EZBuddy client ZIP.

## Issue a 7-day full-feature trial

1. Ask the tester to open **EZBuddy → License & Trial Activation**.
2. Have them use **Copy HWID** and send you the anonymous hardware ID.
3. Run:

```powershell
dotnet run --project tools/EZBuddy.LicenseIssuer/EZBuddy.LicenseIssuer.csproj -- issue `
  --email tester@example.com `
  --hardware <PASTE-HWID> `
  --days 7 `
  --tier FreeTrial `
  --features * `
  --private-key C:\Secure\EZBuddy_License_Private_Key_2026-01.pem `
  --out C:\Secure\Trials\tester.ezlic
```

The issuer verifies that the supplied private key matches the public key trusted by this EZBuddy build before it signs anything.

## Issue a feature-limited trial

Use comma-separated feature keys instead of `*`:

```powershell
--features core,utility,retainers,duty,progression,daily-weekly
```

Current queue feature keys are:

- `core`
- `duty`
- `progression`
- `gathering`
- `crafting`
- `retainers`
- `marketboard`
- `relics`
- `events`
- `sanctuary`
- `gold-saucer`
- `triple-triad`
- `daily-weekly`
- `utility`

## Tester activation

The tester can either:

- paste the complete `.ezlic` JSON into **Install Signed License**, or
- use a future configured HTTPS licensing API to request a trial by email.

The client validates the signature, machine binding, issue time, expiration and feature set before saving the entitlement under the user's LocalAppData EZBuddy licensing directory.

## Key rotation

The issuer has a `keygen` command for generating a future rotation keypair. A newly generated pair is **not** trusted by existing clients. Add its public key to `LicenseSigningKeys`, assign a new key ID, release the updated client, and only then begin signing with the new private key.

## Revocation and online licensing

Offline entitlements remain valid until their signed expiration date. Immediate revocation requires an online lease/refresh policy. The client already supports an HTTPS `IOnlineLicenseClient`; the backend can be added without changing the entitlement signature format.
