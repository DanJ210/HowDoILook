using System.Security.Cryptography;
using System.Text;
using AiStyleApp.Api.Infrastructure;
using Microsoft.Extensions.Options;

namespace AiStyleApp.Api.Services;

public interface IReplicateSignatureVerifier
{
    ReplicateSignatureVerificationResult Verify(byte[] payloadBytes, string webhookId, string webhookTimestamp, string webhookSignature);
}

public sealed record ReplicateSignatureVerificationResult(bool IsValid, string FailureReason)
{
    public static ReplicateSignatureVerificationResult Success() => new(true, string.Empty);
    public static ReplicateSignatureVerificationResult Fail(string reason) => new(false, reason);
}

public class ReplicateSignatureVerifier : IReplicateSignatureVerifier
{
    private readonly byte[] _secretBytes;
    private const int MaxTimestampAgeSeconds = 5 * 60;

    public ReplicateSignatureVerifier(IOptions<ReplicateOptions> options)
    {
        var secret = options.Value.WebhookSigningSecret;
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException("Replicate:WebhookSigningSecret is not configured.");
        }

        var encodedSecret = secret.StartsWith("whsec_", StringComparison.OrdinalIgnoreCase)
            ? secret["whsec_".Length..]
            : secret;

        try
        {
            _secretBytes = Convert.FromBase64String(encodedSecret);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("Replicate:WebhookSigningSecret is not a valid Replicate signing secret.", ex);
        }
    }

    public ReplicateSignatureVerificationResult Verify(byte[] payloadBytes, string webhookId, string webhookTimestamp, string webhookSignature)
    {
        if (string.IsNullOrWhiteSpace(webhookId)
            || string.IsNullOrWhiteSpace(webhookTimestamp)
            || string.IsNullOrWhiteSpace(webhookSignature))
        {
            return ReplicateSignatureVerificationResult.Fail("Missing verification values.");
        }

        if (!long.TryParse(webhookTimestamp, out var timestampSeconds))
        {
            return ReplicateSignatureVerificationResult.Fail("Invalid webhook timestamp.");
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var timestampAgeSeconds = Math.Abs(now - timestampSeconds);
        if (timestampAgeSeconds > MaxTimestampAgeSeconds)
        {
            return ReplicateSignatureVerificationResult.Fail($"Webhook timestamp is outside tolerance ({timestampAgeSeconds}s). ");
        }

        var prefixBytes = Encoding.UTF8.GetBytes($"{webhookId}.{webhookTimestamp}.");
        var signedContentBytes = new byte[prefixBytes.Length + payloadBytes.Length];
        Buffer.BlockCopy(prefixBytes, 0, signedContentBytes, 0, prefixBytes.Length);
        Buffer.BlockCopy(payloadBytes, 0, signedContentBytes, prefixBytes.Length, payloadBytes.Length);

        using var hmac = new HMACSHA256(_secretBytes);
        var computed = hmac.ComputeHash(signedContentBytes);
        var computedSignature = Convert.ToBase64String(computed);
        var computedBytes = Encoding.UTF8.GetBytes(computedSignature);

        var foundVersionedSignature = false;
        foreach (var part in webhookSignature.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separatorIndex = part.IndexOf(',');
            if (separatorIndex < 0 || separatorIndex == part.Length - 1)
            {
                continue;
            }

            foundVersionedSignature = true;

            var expectedBytes = Encoding.UTF8.GetBytes(part[(separatorIndex + 1)..]);
            if (expectedBytes.Length != computedBytes.Length)
            {
                continue;
            }

            if (CryptographicOperations.FixedTimeEquals(expectedBytes, computedBytes))
            {
                return ReplicateSignatureVerificationResult.Success();
            }
        }

        if (!foundVersionedSignature)
        {
            return ReplicateSignatureVerificationResult.Fail("Webhook signature header did not contain any versioned signatures.");
        }

        return ReplicateSignatureVerificationResult.Fail(
            $"Computed signature did not match any provided signatures. Header count: {webhookSignature.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length}.");
    }
}
