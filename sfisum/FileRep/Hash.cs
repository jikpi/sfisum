using System.Buffers;
using System.IO.Hashing;
using System.Security.Cryptography;
using sfisum.Utils;

namespace sfisum.FileRep;

internal readonly struct Hash : IEquatable<Hash>
{
    // struct(some data, reference to: _hash): no other hashes in cache on access
    // ideal: (some data, _hash) solution: AOS or unsafe
    private readonly byte[] _hash = new byte[16];

    public Hash(string filePath)
    {
        var hash = new XxHash128();
        using FileStream fs = File.OpenRead(filePath);

        byte[] buffer = ArrayPool<byte>.Shared.Rent(Constants.FileBufferSize);
        try
        {
            int bytesRead;
            while ((bytesRead = fs.Read(buffer, 0, buffer.Length)) > 0)
            {
                hash.Append(buffer.AsSpan(0, bytesRead));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        _hash = hash.GetCurrentHash();
    }

    // public Hash(string filePath)
    // {
    //     using var md5 = MD5.Create();
    //     using FileStream fs = File.OpenRead(filePath);
    //
    //     byte[] buffer = ArrayPool<byte>.Shared.Rent(Constants.FileBufferSize);
    //     try
    //     {
    //         int bytesRead;
    //         while ((bytesRead = fs.Read(buffer, 0, buffer.Length)) > 0)
    //         {
    //             md5.TransformBlock(buffer, 0, bytesRead, null, 0);
    //         }
    //
    //         md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
    //         _hash = md5.Hash!;
    //     }
    //     finally
    //     {
    //         ArrayPool<byte>.Shared.Return(buffer);
    //     }
    // }

    public Hash(ReadOnlySpan<char> hashText)
    {
        if (hashText.IsEmpty)
        {
            throw new LocalException("Hash string cannot be null or empty");
        }

        if (hashText.Length != 32)
        {
            throw new LocalException("Hash string must be 32 characters long");
        }

        try
        {
            _hash = Convert.FromHexString(hashText.ToString());
        }
        catch (FormatException)
        {
            throw new LocalException("Invalid hex string format");
        }
    }

    public override string ToString()
    {
        return Convert.ToHexString(_hash);
    }

    public override int GetHashCode()
    {
        return BitConverter.ToInt32(_hash, 0);
    }

    public bool Equals(Hash other)
    {
        return _hash.AsSpan().SequenceEqual(other._hash);
    }

    public override bool Equals(object? obj)
    {
        return obj is Hash other && Equals(other);
    }

    public static bool operator ==(Hash left, Hash right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Hash left, Hash right)
    {
        return !left.Equals(right);
    }
}