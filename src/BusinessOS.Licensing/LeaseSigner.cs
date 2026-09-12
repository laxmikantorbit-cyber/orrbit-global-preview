using System.Security.Cryptography;
using System.Text.Json;

namespace BusinessOS.Licensing;

public sealed class LeaseSigner : IDisposable
{
    private const string AlgorithmName = "ECDSA-P256-SHA256";
    private readonly ECDsa _privateKey;

    public LeaseSigner()
    {
        _privateKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    }

    public byte[] ExportPublicKey() => _privateKey.ExportSubjectPublicKeyInfo();

    public SignedLicenseLease Sign(LicenseLeasePayload payload)
    {
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        var signature = _privateKey.SignData(payloadBytes, HashAlgorithmName.SHA256);
        return new SignedLicenseLease(
            AlgorithmName,
            Convert.ToBase64String(payloadBytes),
            Convert.ToBase64String(signature));
    }
    public static LicenseLeasePayload? Verify(
        SignedLicenseLease token,
        byte[] publicKey,
        string expectedDevice,
        DateTimeOffset now)
    {
        if (!string.Equals(token.Algorithm, AlgorithmName, StringComparison.Ordinal)) return null;

        var payloadBytes = Convert.FromBase64String(token.PayloadBase64);
        var signature = Convert.FromBase64String(token.SignatureBase64);
        using var verifier = ECDsa.Create();
        verifier.ImportSubjectPublicKeyInfo(publicKey, out _);

        if (!verifier.VerifyData(payloadBytes, signature, HashAlgorithmName.SHA256)) return null;
        var payload = JsonSerializer.Deserialize<LicenseLeasePayload>(payloadBytes);
        if (payload is null) return null;
        if (!string.Equals(payload.DeviceFingerprint, expectedDevice, StringComparison.Ordinal)) return null;
        if (now > payload.LeaseValidUntil) return null;
        return payload;
    }

    public void Dispose() => _privateKey.Dispose();
}
