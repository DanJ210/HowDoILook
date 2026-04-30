using System.Security.Cryptography;
using System.Text;
using AiStyleApp.Api.Infrastructure;
using Microsoft.Extensions.Options;

namespace AiStyleApp.Api.Services;

public interface IReplicateSignatureVerifier
{
    bool IsValid(string payload, string signature);
}

public class ReplicateSignatureVerifier : IReplicateSignatureVerifier
{
    private readonly byte[] _secretBytes;

    public ReplicateSignatureVerifier(IOptions<ReplicateOptions> options)
    {
        _secretBytes = Encoding.UTF8.GetBytes(options.Value.WebhookSecret);
    }

    public bool IsValid(string payload, string signature)
    {
        if (string.IsNullOrEmpty(signature)) return false;

        using var hmac = new HMACSHA256(_secretBytes);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var computed = hmac.ComputeHash(payloadBytes);
        var computedHex = Convert.ToHexString(computed).ToLowerInvariant();

        // Replicate sends "sha256=<hex>"
        var expected = signature.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase)
            ? signature[7..]
            : signature;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computedHex),
            Encoding.UTF8.GetBytes(expected.ToLowerInvariant()));
    }
}
