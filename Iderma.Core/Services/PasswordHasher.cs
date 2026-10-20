using System.Security.Cryptography;

namespace Iderma.Core.Services;

public static class PasswordHasher
{
    public const int Iterations = 600_000;
    public const int LegacyIterations = 100_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public static (string Hash, string Salt, int Iterations) Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt), Iterations);
    }

    public static bool Verify(string password, string hash, string salt, int iterations = 0)
    {
        if (string.IsNullOrWhiteSpace(hash) || string.IsNullOrWhiteSpace(salt))
        {
            return false;
        }

        var rounds = iterations > 0 ? iterations : LegacyIterations;
        var saltBytes = Convert.FromBase64String(salt);
        var computed = Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, rounds, HashAlgorithmName.SHA256, HashSize);
        var stored = Convert.FromBase64String(hash);
        return CryptographicOperations.FixedTimeEquals(computed, stored);
    }
}
