using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.SignalR;
using TcgULtimateServer.Api.Models;
namespace TcgULtimateServer.Api.Services;
public sealed class SecurePacketService
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> seen = new(); private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> rates = new();
    public string Validate(User user, string token, string scope, string packetId, long sentAtUnixMs, string compressedPayload, string signature, int maxPayloadBytes = 2048)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(packetId) || packetId.Length > 80 || string.IsNullOrWhiteSpace(compressedPayload) || compressedPayload.Length > 16_384 || string.IsNullOrWhiteSpace(signature)) throw new HubException("Invalid packet.");
        if (Math.Abs((DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds(sentAtUnixMs)).TotalSeconds) > 30) throw new HubException("Expired packet.");
        var signed = Encoding.UTF8.GetBytes($"{scope}.{packetId}.{sentAtUnixMs}.{compressedPayload}");
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(token), signed);
        byte[] supplied;
        try { supplied = Convert.FromBase64String(signature); } catch (FormatException) { throw new HubException("Invalid packet signature."); }
        if (!CryptographicOperations.FixedTimeEquals(expected, supplied)) throw new HubException("Invalid packet signature.");
        if (!seen.TryAdd($"{user.Id}:{packetId}", DateTimeOffset.UtcNow)) throw new HubException("Packet replay detected.");
        var queue = rates.GetOrAdd(user.Id, _ => new Queue<DateTimeOffset>()); lock (queue) { while (queue.Count > 0 && DateTimeOffset.UtcNow - queue.Peek() > TimeSpan.FromMinutes(1)) queue.Dequeue(); if (queue.Count >= 60) throw new HubException("Rate limit exceeded."); queue.Enqueue(DateTimeOffset.UtcNow); }
        try
        {
            var compressed = Convert.FromBase64String(compressedPayload);
            if (compressed.Length > 12_288) throw new HubException("Packet is too large.");
            using var input = new MemoryStream(compressed);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            gzip.CopyTo(new LimitedWriteStream(output, maxPayloadBytes));
            return Encoding.UTF8.GetString(output.ToArray());
        }
        catch (HubException) { throw; }
        catch (Exception exception) when (exception is FormatException or InvalidDataException or IOException)
        {
            throw new HubException("Invalid compressed packet.");
        }
    }

    private sealed class LimitedWriteStream(Stream inner, int limit) : Stream
    {
        private int written;
        public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
        public override long Length => written; public override long Position { get => written; set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length > limit - written) throw new HubException("Packet is too large.");
            inner.Write(buffer); written += buffer.Length;
        }
    }
}
