using System.Security.Cryptography;

namespace ChatGpt.Accounts;

/// <summary>Computes a one-way revision fingerprint without retaining credential bytes.</summary>
public static class CredentialFingerprint
{
    public static string Compute(ReadOnlySpan<byte> credentialBytes) =>
        Convert.ToHexStringLower(SHA256.HashData(credentialBytes));
}
