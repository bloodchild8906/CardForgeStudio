using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace TcgServer.Client
{
    public readonly struct SecurePacket
    {
        public SecurePacket(string payload, string packetId, long sentAtUnixMs, string signature)
        {
            Payload = payload;
            PacketId = packetId;
            SentAtUnixMs = sentAtUnixMs;
            Signature = signature;
        }

        public string Payload { get; }
        public string PacketId { get; }
        public long SentAtUnixMs { get; }
        public string Signature { get; }
    }

    public static class SecurePacketFactory
    {
        public static SecurePacket Create(string token, string scope, string plainText)
        {
            if (string.IsNullOrWhiteSpace(token))
                throw new ArgumentException("A session token is required.", nameof(token));
            if (string.IsNullOrWhiteSpace(scope))
                throw new ArgumentException("A packet scope is required.", nameof(scope));

            var source = Encoding.UTF8.GetBytes(plainText ?? string.Empty);
            byte[] compressed;
            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(output, CompressionLevel.Optimal, true))
                    gzip.Write(source, 0, source.Length);
                compressed = output.ToArray();
            }

            var payload = Convert.ToBase64String(compressed);
            var packetId = Guid.NewGuid().ToString("N");
            var sentAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var signedText = $"{scope}.{packetId}.{sentAt}.{payload}";

            byte[] signature;
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(token)))
                signature = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedText));

            return new SecurePacket(payload, packetId, sentAt, Convert.ToBase64String(signature));
        }
    }
}

