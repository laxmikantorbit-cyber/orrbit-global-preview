using System.Security.Cryptography;
using System.Text;

namespace BusinessOS.Payments;

public static class WebhookSignatureVerifier
{
    public static string Compute(string rawBody, string secret)
    {
        var key = Encoding.UTF8.GetBytes(secret);
        var body = Encoding.UTF8.GetBytes(rawBody);
        using var hmac = new HMACSHA256(key);
        return Convert.ToHexString(hmac.ComputeHash(body)).ToLowerInvariant();
    }

    public static bool Verify(string rawBody, string suppliedSignature, string secret)
    {
        if (string.IsNullOrWhiteSpace(suppliedSignature)) return false;

        var expected = Encoding.ASCII.GetBytes(Compute(rawBody, secret));
        var supplied = Encoding.ASCII.GetBytes(suppliedSignature.Trim().ToLowerInvariant());
        if (expected.Length != supplied.Length) return false;

        return CryptographicOperations.FixedTimeEquals(expected, supplied);
    }
}
