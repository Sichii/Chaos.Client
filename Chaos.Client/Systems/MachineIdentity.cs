#region
using System.Buffers.Binary;
using System.Runtime.Versioning;
using System.Xml.Linq;
using Chaos.Cryptography;
using Microsoft.Win32;
#endregion

namespace Chaos.Client.Systems;

public static class MachineIdentity
{
    //identity the retail client falls back to when the registry can't be read or written
    private const uint DEFAULT_CLIENT_ID1 = 0xFF00FF00;

    public static uint ClientId1 { get; }
    public static uint ClientId2 { get; }

    static MachineIdentity()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                ClientId1 = LoadOrCreateWindows();
            else if (OperatingSystem.IsMacOS())
                ClientId1 = LoadOrCreateMacOs();
            else if (OperatingSystem.IsLinux())
                ClientId1 = LoadOrCreateLinux();
            else
                ClientId1 = DEFAULT_CLIENT_ID1;
        } catch
        {
            ClientId1 = DEFAULT_CLIENT_ID1;
        }

        //id2 is not an independent value - it is always the checksum of id1
        ClientId2 = Checksum(ClientId1);
    }

    private static ushort Checksum(uint clientId1) => Crc.Generate16(BitConverter.GetBytes(clientId1));

    //4 random bytes; zero is reserved to mean "not set"
    private static uint Generate() => (uint)Random.Shared.NextInt64(1, uint.MaxValue + 1L);

    #region Checksum obfuscation
    /// <summary>
    ///     Packs a checksum the way the retail client stores it: checksum low byte, a random byte, checksum high byte, another
    ///     random byte - each xor'd with 0xAC plus its index.
    /// </summary>
    private static uint EncodeChecksum(ushort checksum)
    {
        Span<byte> bytes =
        [
            (byte)checksum,
            (byte)Random.Shared.Next(256),
            (byte)(checksum >> 8),
            (byte)Random.Shared.Next(256)
        ];

        for (var i = 0; i < bytes.Length; i++)
            bytes[i] ^= (byte)(0xAC + i);

        return BinaryPrimitives.ReadUInt32LittleEndian(bytes);
    }

    private static ushort DecodeChecksum(uint stored)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, stored);

        for (var i = 0; i < bytes.Length; i++)
            bytes[i] ^= (byte)(0xAC + i);

        return (ushort)(bytes[0] | (bytes[2] << 8));
    }
    #endregion

    #region Windows - Registry keys disguised as COM control registrations
    [SupportedOSPlatform("windows")]
    private static uint LoadOrCreateWindows()
    {
        var clientId1 = ReadDword("NXKRI.Ctrl.1");

        if (clientId1 is null or 0)
        {
            clientId1 = Generate();
            WriteDword("NXKRI.Ctrl.1", clientId1.Value);
        }

        var checksum = Checksum(clientId1.Value);
        var stored = ReadDword("KRIHC.Ctrl.1");

        //rewrite whenever the stored value doesn't decode back to the checksum of id1
        if (stored is null or 0 || (DecodeChecksum(stored.Value) != checksum))
            WriteDword("KRIHC.Ctrl.1", EncodeChecksum(checksum));

        return clientId1.Value;
    }

    //read from hkcr (merged view of hklm + hkcu, no admin needed for reads)
    [SupportedOSPlatform("windows")]
    private static uint? ReadDword(string keyName)
    {
        using var key = Registry.ClassesRoot.OpenSubKey(keyName);

        return key?.GetValue("CLSID") is int value ? unchecked((uint)value) : null;
    }

    //write to hkcu\software\classes (no admin needed, visible via hkcr)
    [SupportedOSPlatform("windows")]
    private static void WriteDword(string keyName, uint value)
    {
        using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{keyName}");

        key.SetValue("CLSID", unchecked((int)value), RegistryValueKind.DWord);
    }
    #endregion

    #region macOS - Plist file disguised as app preferences
    [SupportedOSPlatform("macos")]
    private static uint LoadOrCreateMacOs()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library",
            "Preferences",
            "com.nxkri.Ctrl.1.plist");

        var id = ReadPlistDword(path);

        if (id is null or 0)
        {
            id = Generate();
            WritePlistDword(path, id.Value);
        }

        return id.Value;
    }

    private static uint? ReadPlistDword(string path)
    {
        if (!File.Exists(path))
            return null;

        var doc = XDocument.Load(path);

        var elements = doc.Root
                          ?.Element("dict")
                          ?.Elements()
                          .ToList();

        if (elements is null)
            return null;

        for (var i = 0; i < (elements.Count - 1); i++)
            if ((elements[i].Name == "key")
                && (elements[i].Value == "CLSID")
                && (elements[i + 1].Name == "integer")
                && uint.TryParse(elements[i + 1].Value, out var val))
                return val;

        return null;
    }

    private static void WritePlistDword(string path, uint value)
        => File.WriteAllText(
            path,
            $"""
             <?xml version="1.0" encoding="UTF-8"?>
             <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
             <plist version="1.0">
             <dict>
                 <key>CLSID</key>
                 <integer>{value}</integer>
             </dict>
             </plist>
             """);
    #endregion

    #region Linux - Binary file disguised as app data
    [SupportedOSPlatform("linux")]
    private static uint LoadOrCreateLinux()
    {
        var dataDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrEmpty(dataDir))
            dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");

        var path = Path.Combine(dataDir, "nxkri.ctrl.1");
        var id = ReadBinaryDword(path);

        if (id is null or 0)
        {
            id = Generate();
            WriteBinaryDword(path, id.Value);
        }

        return id.Value;
    }

    private static uint? ReadBinaryDword(string path)
    {
        if (!File.Exists(path))
            return null;

        var bytes = File.ReadAllBytes(path);

        if (bytes.Length < 4)
            return null;

        return BitConverter.ToUInt32(bytes, 0);
    }

    private static void WriteBinaryDword(string path, uint value)
    {
        var dir = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllBytes(path, BitConverter.GetBytes(value));
    }
    #endregion
}