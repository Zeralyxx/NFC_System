using System;
using System.Security.Cryptography;

namespace NFC_System;

public static class PinHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 100_000;

    public static (string Salt, string Hash) HashPin(string pin)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(pin, salt, Iterations, HashAlgorithmName.SHA256, HashSize);

        return (Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    public static bool VerifyPin(string pin, string saltBase64, string hashBase64)
    {
        if (string.IsNullOrWhiteSpace(pin) ||
            string.IsNullOrWhiteSpace(saltBase64) ||
            string.IsNullOrWhiteSpace(hashBase64))
        {
            return false;
        }

        byte[] salt = Convert.FromBase64String(saltBase64);
        byte[] expectedHash = Convert.FromBase64String(hashBase64);
        byte[] actualHash = Rfc2898DeriveBytes.Pbkdf2(pin, salt, Iterations, HashAlgorithmName.SHA256, HashSize);

        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}