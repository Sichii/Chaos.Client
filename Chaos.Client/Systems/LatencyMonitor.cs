#region
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Chaos.Client.Networking;
#endregion

namespace Chaos.Client.Systems;

/// <summary>
///     Background ICMP-ping loop against the currently connected server. Exposes the most recent round-trip time and
///     fires <see cref="LatencyChanged" /> when it changes so HUDs can refresh their indicators.
/// </summary>
public static class LatencyMonitor
{
    private const int PING_INTERVAL_MS = 15_000;
    private const int PING_TIMEOUT_MS = 4_000;

    private static readonly Lock StateLock = new();
    private static CancellationTokenSource? Cts;

    /// <summary>
    ///     The most recent round-trip time in milliseconds, or null if no measurement is available (loop not started, host
    ///     unreachable, ICMP blocked, etc.).
    /// </summary>
    public static long? LatencyMs { get; private set; }

    /// <summary>
    ///     Fires whenever <see cref="LatencyMs" /> changes. Always raised on a thread-pool thread, so subscribers must not
    ///     touch UI state directly — poll <see cref="LatencyMs" /> from the game-loop thread instead.
    /// </summary>
    public static event Action? LatencyChanged;

    /// <summary>
    ///     Starts a background loop that pings <paramref name="client" />'s current remote endpoint every 15 seconds.
    ///     Calling Start while a previous loop is running stops the previous loop first.
    /// </summary>
    public static void Start(GameClient client)
    {
        Stop();

        using var scope = StateLock.EnterScope();
        Cts = new CancellationTokenSource();
        var token = Cts.Token;
        _ = Task.Run(() => RunAsync(client, token), token);
    }

    /// <summary>
    ///     Cancels the background loop and clears the latency value.
    /// </summary>
    public static void Stop()
    {
        CancellationTokenSource? toCancel;

        using (StateLock.EnterScope())
        {
            toCancel = Cts;
            Cts = null;
        }

        toCancel?.Cancel();
        toCancel?.Dispose();

        if (LatencyMs is not null)
        {
            LatencyMs = null;
            LatencyChanged?.Invoke();
        }
    }

    private static async Task RunAsync(GameClient client, CancellationToken token)
    {
        using var ping = new Ping();

        while (!token.IsCancellationRequested)
        {
            await PingOnceAsync(client, ping, token);

            try
            {
                await Task.Delay(PING_INTERVAL_MS, token);
            } catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private static async Task PingOnceAsync(GameClient client, Ping ping, CancellationToken token)
    {
        var endpoint = client.RemoteEndPoint;

        if (endpoint is null)
            return;

        long? newValue;

        try
        {
            var reply = await ping.SendPingAsync(endpoint.Address, PING_TIMEOUT_MS);

            if (token.IsCancellationRequested)
                return;

            newValue = reply.Status == IPStatus.Success ? reply.RoundtripTime : null;
        } catch (PingException)
        {
            newValue = null;
        } catch (SocketException)
        {
            newValue = null;
        }

        if (newValue == LatencyMs)
            return;

        LatencyMs = newValue;
        LatencyChanged?.Invoke();
    }
}
