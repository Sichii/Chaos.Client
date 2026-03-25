#region
using System.IO.Compression;
using Chaos.Cryptography;
using Chaos.DarkAges.Definitions;
using Chaos.Networking.Entities.Server;
#endregion

namespace Chaos.Client.Networking;

/// <summary>
///     Manages metadata file synchronization with the server. Compares local checksums against server-reported checksums
///     and requests updated files when they differ. Stores received metadata to disk. Fires OnSyncComplete when all
///     pending files have been received.
/// </summary>
public sealed class MetaDataManager
{
    private readonly ConnectionManager Connection;
    private readonly string MetaFilePath;
    private readonly Dictionary<string, uint> PendingChecksums = new(StringComparer.OrdinalIgnoreCase);
    private bool SyncStarted;

    public MetaDataManager(ConnectionManager connection, string dataPath)
    {
        Connection = connection;
        MetaFilePath = Path.Combine(dataPath, "metafile");

        Directory.CreateDirectory(MetaFilePath);
    }

    private uint ComputeLocalCheckSum(string name)
    {
        var filePath = Path.Combine(MetaFilePath, name);

        if (!File.Exists(filePath))
            return 0;

        try
        {
            using var fileStream = File.OpenRead(filePath);
            using var zlibStream = new ZLibStream(fileStream, CompressionMode.Decompress);
            using var memoryStream = new MemoryStream();

            zlibStream.CopyTo(memoryStream);

            return Crc.Generate32(memoryStream.ToArray());
        } catch
        {
            return 0;
        }
    }

    private void HandleCheckSums(ICollection<MetaDataInfo>? collection)
    {
        if (collection is null || (collection.Count == 0))
        {
            OnSyncComplete?.Invoke();

            return;
        }

        PendingChecksums.Clear();
        SyncStarted = true;
        var staleFiles = new List<string>();

        foreach (var info in collection)
        {
            var localCheckSum = ComputeLocalCheckSum(info.Name);

            if (localCheckSum != info.CheckSum)
            {
                staleFiles.Add(info.Name);
                PendingChecksums[info.Name] = info.CheckSum;
            }
        }

        // Request each stale file
        foreach (var name in staleFiles)
            Connection.SendMetaDataRequest(MetaDataRequestType.DataByName, name);

        // If nothing was stale, sync is already complete
        if (PendingChecksums.Count == 0)
            OnSyncComplete?.Invoke();
    }

    private void HandleFileData(MetaDataInfo? info)
    {
        if (info is null || string.IsNullOrEmpty(info.Name) || (info.Data.Length == 0))
            return;

        // Write compressed data to disk
        var filePath = Path.Combine(MetaFilePath, info.Name);
        File.WriteAllBytes(filePath, info.Data);

        PendingChecksums.Remove(info.Name);

        // All pending files received — sync is complete
        if (SyncStarted && (PendingChecksums.Count == 0))
            OnSyncComplete?.Invoke();
    }

    /// <summary>
    ///     Handles a metadata response from the server. Routes to checksum comparison or file storage depending on the
    ///     response type.
    /// </summary>
    public void HandleMetaData(MetaDataArgs args)
    {
        switch (args.MetaDataRequestType)
        {
            case MetaDataRequestType.AllCheckSums:
                HandleCheckSums(args.MetaDataCollection);

                break;

            case MetaDataRequestType.DataByName:
                HandleFileData(args.MetaDataInfo);

                break;
        }
    }

    /// <summary>
    ///     Fired when all metadata files are up to date — either nothing was stale, or all stale files
    ///     have been received from the server.
    /// </summary>
    public event Action? OnSyncComplete;

    /// <summary>
    ///     Requests all metadata checksums from the server. Call on world entry.
    /// </summary>
    public void RequestSync() => Connection.SendMetaDataRequest(MetaDataRequestType.AllCheckSums);
}