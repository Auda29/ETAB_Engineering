using System.Security.Cryptography;
using System.Text;

namespace ETAB.Engineering.Core.Generation;

public static class DeterministicGuid
{
    public static Guid CreateVersion5(Guid namespaceId, string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        Span<byte> namespaceBytes = stackalloc byte[16];
        namespaceId.TryWriteBytes(namespaceBytes, bigEndian: true, out _);

        var nameBytes = Encoding.UTF8.GetBytes(name);
        var hashInput = new byte[namespaceBytes.Length + nameBytes.Length];
        namespaceBytes.CopyTo(hashInput);
        nameBytes.CopyTo(hashInput.AsSpan(namespaceBytes.Length));

        var hash = SHA1.HashData(hashInput);
        hash[6] = (byte)((hash[6] & 0x0f) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3f) | 0x80);

        return new Guid(hash.AsSpan(0, 16), bigEndian: true);
    }
}
