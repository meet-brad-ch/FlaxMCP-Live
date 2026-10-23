namespace FlaxMcp.Flax;

/// <summary>
/// Reads the GUID/TypeName header of a binary .flax asset. The format is undocumented and was
/// reverse-engineered from real assets (Storage format version 9; checked on Flax 1.12 and 1.13): 4-byte "CFWF" magic,
/// 4-byte format version, 16 reserved bytes, a 4-byte field, then a 16-byte GUID and a null-terminated
/// UTF-16LE TypeName. The GUID's text form is not .NET's Guid.ToString() layout — it's four raw
/// little-endian uint32 chunks hex-formatted and concatenated; this was cross-checked against a real
/// asset reference in a .scene file.
/// </summary>
public static class FlaxBinaryAssetHeaderReader
{
    private const int GuidOffset = 28;
    private const int TypeNameOffset = 44;

    public static (string Id, string TypeName)? TryRead(string filePath)
    {
        try
        {
            using var reader = new BinaryReader(File.OpenRead(filePath));

            var magic = reader.ReadBytes(4);
            if (!magic.AsSpan().SequenceEqual("CFWF"u8))
            {
                return null;
            }

            reader.BaseStream.Position = GuidOffset;
            var guidBytes = reader.ReadBytes(16);
            if (guidBytes.Length != 16)
            {
                return null;
            }

            reader.BaseStream.Position = TypeNameOffset;
            var typeName = ReadNullTerminatedUtf16(reader);
            if (string.IsNullOrEmpty(typeName))
            {
                return null;
            }

            return (FormatFlaxGuid(guidBytes), typeName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EndOfStreamException)
        {
            return null;
        }
    }

    private static string FormatFlaxGuid(byte[] bytes)
    {
        Span<char> chars = stackalloc char[32];
        for (var i = 0; i < 4; i++)
        {
            var chunk = BitConverter.ToUInt32(bytes, i * 4);
            chunk.TryFormat(chars.Slice(i * 8, 8), out _, "x8");
        }
        return new string(chars);
    }

    private static string ReadNullTerminatedUtf16(BinaryReader reader)
    {
        var chars = new List<char>();
        while (reader.ReadUInt16() is var value and not 0)
        {
            chars.Add((char)value);
        }
        return new string(chars.ToArray());
    }
}
