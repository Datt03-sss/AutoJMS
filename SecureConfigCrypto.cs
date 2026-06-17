#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AutoJMS
{
    internal static class SecureConfigCrypto
    {
        private const int SaltSize = 16;
        private const int IvSize = 16;
        private const string AlgorithmName = "AES-CBC-HMACSHA256-MD5-SHA256";

        private sealed class ProtectedPayload
        {
            public int Version { get; set; } = 1;
            public string Algorithm { get; set; } = AlgorithmName;
            public string Salt { get; set; } = "";
            public string IV { get; set; } = "";
            public string CipherText { get; set; } = "";
            public string Hash { get; set; } = "";
        }

        public static string ProtectString(string plaintext, string secret)
        {
            if (plaintext == null) throw new ArgumentNullException(nameof(plaintext));
            if (string.IsNullOrWhiteSpace(secret)) throw new ArgumentException("Secret is required.", nameof(secret));

            byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
            byte[] iv = RandomNumberGenerator.GetBytes(IvSize);
            byte[] plainBytes = Encoding.UTF8.GetBytes(plaintext);

            byte[] cipherBytes;
            using (Aes aes = Aes.Create())
            {
                aes.Key = DeriveKey(secret, salt, "aes");
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using ICryptoTransform encryptor = aes.CreateEncryptor();
                cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
            }

            byte[] mac = ComputeHash(secret, salt, iv, cipherBytes);
            var payload = new ProtectedPayload
            {
                Salt = Convert.ToBase64String(salt),
                IV = Convert.ToBase64String(iv),
                CipherText = Convert.ToBase64String(cipherBytes),
                Hash = Convert.ToBase64String(mac)
            };

            return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        }

        public static string UnprotectString(string protectedJson, string secret)
        {
            if (string.IsNullOrWhiteSpace(protectedJson)) throw new ArgumentException("Protected payload is required.", nameof(protectedJson));
            if (string.IsNullOrWhiteSpace(secret)) throw new ArgumentException("Secret is required.", nameof(secret));

            ProtectedPayload? payload = JsonSerializer.Deserialize<ProtectedPayload>(protectedJson);
            if (payload == null || payload.Version != 1 || !string.Equals(payload.Algorithm, AlgorithmName, StringComparison.Ordinal))
                throw new InvalidDataException("Định dạng config mã hóa không hợp lệ.");

            byte[] salt = Convert.FromBase64String(payload.Salt);
            byte[] iv = Convert.FromBase64String(payload.IV);
            byte[] cipherBytes = Convert.FromBase64String(payload.CipherText);
            byte[] expectedHash = Convert.FromBase64String(payload.Hash);
            byte[] actualHash = ComputeHash(secret, salt, iv, cipherBytes);

            if (!CryptographicOperations.FixedTimeEquals(expectedHash, actualHash))
                throw new CryptographicException("Config mã hóa không còn hợp lệ hoặc sai khóa giải mã.");

            using Aes aes = Aes.Create();
            aes.Key = DeriveKey(secret, salt, "aes");
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using ICryptoTransform decryptor = aes.CreateDecryptor();
            byte[] plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
            return Encoding.UTF8.GetString(plainBytes);
        }

        private static byte[] ComputeHash(string secret, byte[] salt, byte[] iv, byte[] cipherBytes)
        {
            byte[] macKey = DeriveKey(secret, salt, "sha");
            using var hmac = new HMACSHA256(macKey);
            byte[] header = Encoding.UTF8.GetBytes(AlgorithmName);
            byte[] data = Combine(header, salt, iv, cipherBytes);
            return hmac.ComputeHash(data);
        }

        private static byte[] DeriveKey(string secret, byte[] salt, string purpose)
        {
            byte[] secretBytes = Encoding.UTF8.GetBytes(secret);
            byte[] purposeBytes = Encoding.UTF8.GetBytes(purpose);

            using MD5 md5 = MD5.Create();
            byte[] md5Hash = md5.ComputeHash(secretBytes);

            using SHA256 sha256 = SHA256.Create();
            return sha256.ComputeHash(Combine(purposeBytes, md5Hash, secretBytes, salt));
        }

        private static byte[] Combine(params byte[][] arrays)
        {
            byte[] result = new byte[arrays.Sum(a => a.Length)];
            int offset = 0;
            foreach (byte[] array in arrays)
            {
                Buffer.BlockCopy(array, 0, result, offset, array.Length);
                offset += array.Length;
            }
            return result;
        }
    }
}
