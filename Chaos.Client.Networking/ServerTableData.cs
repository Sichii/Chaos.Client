#region
using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Text;
#endregion

namespace Chaos.Client.Networking;

/// <summary>
///     Represents a single server entry from the server table.
/// </summary>
public sealed record ServerTableEntry(
    byte Id,
    string Address,
    int Port,
    string Name,
    string Description);

/// <summary>
///     Represents the parsed server table received from the lobby server.
/// </summary>
public sealed class ServerTableData
{
    /// <summary>
    ///     The list of available servers.
    /// </summary>
    public required List<ServerTableEntry> Servers { get; init; }

    /// <summary>
    ///     Whether the client should display the server selection list to the user.
    /// </summary>
    public required bool ShowServerList { get; init; }

    private static byte[] Decompress(byte[] compressedData)
    {
        using var compressedStream = new MemoryStream(compressedData);
        using var zlibStream = new ZLibStream(compressedStream, CompressionMode.Decompress);
        using var decompressedStream = new MemoryStream();

        zlibStream.CopyTo(decompressedStream);

        return decompressedStream.ToArray();
    }

    /// <summary>
    ///     Parses a server table from Zlib-compressed binary data.
    /// </summary>
    /// <param name="compressedData">
    ///     The raw Zlib-compressed server table bytes from the lobby server.
    /// </param>
    public static ServerTableData Parse(byte[] compressedData)
    {
        var data = Decompress(compressedData);

        if (data.Length == 0)
            return new ServerTableData
            {
                Servers = [],
                ShowServerList = false
            };

        var offset = 0;
        var serverCount = data[offset++];
        var koreanEncoding = Encoding.GetEncoding(949);
        var servers = new List<ServerTableEntry>(serverCount);

        for (var i = 0; i < serverCount; i++)
        {
            if ((offset + 7) > data.Length)
                break;

            var serverId = data[offset++];

            // IP address: 4 raw bytes in network byte order
            var ipAddress = new IPAddress(data.AsSpan(offset, 4));
            offset += 4;

            // Port: big-endian uint16
            var port = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset, 2));
            offset += 2;

            // Null-terminated string in codepage 949: "{Name};{Description}\0"
            var stringStart = offset;

            while ((offset < data.Length) && (data[offset] != 0))
                offset++;

            var rawString = koreanEncoding.GetString(data, stringStart, offset - stringStart);

            // Skip past the null terminator
            if (offset < data.Length)
                offset++;

            // Split on ';' to get Name and Description
            var separatorIndex = rawString.IndexOf(';');
            string name;
            string description;

            if (separatorIndex >= 0)
            {
                name = rawString[..separatorIndex];
                description = rawString[(separatorIndex + 1)..];
            } else
            {
                name = rawString;
                description = string.Empty;
            }

            servers.Add(
                new ServerTableEntry(
                    serverId,
                    ipAddress.ToString(),
                    port,
                    name,
                    description));
        }

        // The trailing byte after all server entries is the ShowServerList flag
        var showServerList = (offset < data.Length) && (data[offset] != 0);

        return new ServerTableData
        {
            Servers = servers,
            ShowServerList = showServerList
        };
    }
}