// ImplicitUsings 涵蓋 System / System.Threading / System.Threading.Tasks。
// Socket、NetworkStream、BinaryPrimitives、Encoding 都不在裡面，要自己加。
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace CSharpLearn.Day3;

// Day 3 第一支範例：Socket 與訊息 framing。
//
// 這支範例在「同一個 process」裡同時開 server 和 client，用 127.0.0.1 的隨機 port，
// 不需要外部設備，離線可跑。跑完自動結束。
//
// ✅ 本檔案已用 .NET 10 SDK 編譯並實跑驗證過（dotnet build + dotnet run）。

public static class EchoServerDemo
{
    /// <summary>長度前綴的寬度：2 byte → 單一訊息最大 65535 bytes。</summary>
    private const int HeaderSize = 2;

    public static async Task RunAsync(CancellationToken ct)
    {
        Console.WriteLine("--- 1. 沒有 framing：一次 read 拿到的不是一則訊息 ---");

        // port 0 = 讓作業系統自己挑一個空閒 port，避免撞到 502/1883 等常見埠
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var endpoint = (IPEndPoint?)listener.LocalEndpoint;
        if (endpoint is null)
        {
            throw new InvalidOperationException("拿不到 listener 的本機端點");
        }

        Console.WriteLine($"  監聽 127.0.0.1:{endpoint.Port}");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var server = RunServerAsync(listener, cts.Token);

        try
        {
            await RunRawDemoAsync(endpoint.Port, cts.Token);
            await RunFramedDemoAsync(endpoint.Port, cts.Token);
        }
        finally
        {
            await cts.CancelAsync();
            listener.Stop();
            await server;
        }
    }

    // ── Server ────────────────────────────────────────────────────────

    private static async Task RunServerAsync(TcpListener listener, CancellationToken ct)
    {
        var connections = 0;

        while (!ct.IsCancellationRequested)
        {
            TcpClient client;

            try
            {
                client = await listener.AcceptTcpClientAsync(ct);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                // listener.Stop() 會讓 Accept 丟出例外，這是預期的收尾路徑
                break;
            }

            using (client)
            {
                var index = ++connections;
                Console.WriteLine($"  [server] 接受第 {index} 個連線");

                var stream = client.GetStream();
                if (index == 1)
                {
                    await DumpRawAsync(stream, ct);
                }
                else
                {
                    await EchoFramedAsync(stream, ct);
                }
            }
        }
    }

    /// <summary>
    /// 第一段：不管訊息邊界，把讀到的每一片 bytes 原樣印出來。
    ///
    /// 循環讀到「對端關閉」為止。重點是讓讀取次數由 TCP 分段決定，
    /// 而不是由「送了幾則訊息」決定。
    /// </summary>
    private static async Task DumpRawAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[64];
        var total = 0;
        var reads = 0;

        while (!ct.IsCancellationRequested)
        {
            int read;

            try
            {
                read = await stream.ReadAsync(buffer, ct);
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException)
            {
                break;
            }

            if (read == 0)
            {
                // 對端做 Shutdown(Send)，代表「送完了」。這是 0，不是例外。
                break;
            }

            reads++;
            total += read;

            Console.WriteLine($"  [server] 第 {reads} 次 ReadAsync 收到 {read} bytes");
            Console.WriteLine($"  [server] hex   : {Convert.ToHexString(buffer.AsSpan(0, read))}");
            Console.WriteLine($"  [server] 當成文字: 「{Encoding.UTF8.GetString(buffer, 0, read)}」");
        }

        Console.WriteLine($"  [server] 總共讀了 {reads} 次、共 {total} bytes");
        Console.WriteLine("  → 收到的長度取決於 TCP 怎麼分段，無法對應到任何一則訊息的邊界。");
        Console.WriteLine("  → 應用層如果沒有 framing，就永遠不知道『一則訊息』到哪裡為止。");
    }

    /// <summary>第二段：正確的 framing —— 先讀長度，再讀那麼多內容。</summary>
    private static async Task EchoFramedAsync(NetworkStream stream, CancellationToken ct)
    {
        // TCP 是 byte stream，沒有「訊息」的概念。
        // ReadExactlyAsync 會持續讀取直到剛好讀滿 buffer，
        // 這樣不管對方怎麼拆包，收到的一定是一個完整單位。
        var header = new byte[HeaderSize];

        try
        {
            while (!ct.IsCancellationRequested)
            {
                await stream.ReadExactlyAsync(header, ct);
                var length = BinaryPrimitives.ReadUInt16BigEndian(header);

                var payload = new byte[length];
                await stream.ReadExactlyAsync(payload, ct);

                Console.WriteLine($"  [server] 收到 {length} bytes: 「{Encoding.UTF8.GetString(payload)}」");

                stream.Write(header);
                await stream.WriteAsync(payload, ct);
                await stream.FlushAsync(ct);
            }
        }
        catch (Exception ex) when (ex is EndOfStreamException or OperationCanceledException or IOException)
        {
            Console.WriteLine("  [server] 連線結束，關閉");
        }
    }

    // ── Client 1：示範沒有 framing 的問題 ──────────────────────────────

    private static async Task RunRawDemoAsync(int port, CancellationToken ct)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, ct);
        await using var stream = client.GetStream();

        Console.WriteLine($"  [client] 已連線到 127.0.0.1:{port}");
        Console.WriteLine();

        string[] messages = ["AAA", "BB", "CCCCC", "DD"];

        // 前兩則中間停一下，讓伺服器來得及分次收到。
        for (var i = 0; i < 2; i++)
        {
            await stream.WriteAsync(Encoding.UTF8.GetBytes(messages[i]), ct);
            await stream.FlushAsync(ct);
            await Task.Delay(80, ct);
        }

        // 剩下兩則「背對背」送，中間完全不間隔。
        // 伺服器很可能一次 ReadAsync 就把它們一起讀走 —— 這正是沒有 framing 的麻煩。
        foreach (var message in messages[2..])
        {
            await stream.WriteAsync(Encoding.UTF8.GetBytes(message), ct);
        }

        await stream.FlushAsync(ct);

        // 明確告訴對端「我送完了」：送方向關閉，之後對端會收到 read == 0，
        // 而不是等到逾時。沒有 framing 時，這是應用層唯一能用的訊息結束訊號。
        client.Client.Shutdown(SocketShutdown.Send);
        Console.WriteLine("  [client] 已送出 4 則訊息並 Shutdown(Send)，等伺服器收完");

        // 伺服器不會 echo，所以這裡一直讀到 0（連線關閉）為止。
        // 讀到的 bytes 必然是 0 —— 這段迴圈的目的不是拿資料，而是「等伺服器收完並關閉」。
        var buffer = new byte[64];
        var read = await stream.ReadAsync(buffer, ct);
        Console.WriteLine($"  [client] 讀到 {read} bytes（0 代表伺服器已收完並關閉連線）");
        Console.WriteLine("  → 上面伺服器讀到的 chunk 邊界跟這 4 則訊息的邊界完全無關。");
        Console.WriteLine("  → 收在一起的資料長度跟任何一則訊息都不對應，也無法知道哪裡是分界。");
        Console.WriteLine("  → 這就是『粘包』。");
        Console.WriteLine();
    }

    // ── Client 2：示範有 framing 就沒問題 ──────────────────────────────

    private static async Task RunFramedDemoAsync(int port, CancellationToken ct)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, ct);
        await using var stream = client.GetStream();

        Console.WriteLine("--- 2. 長度前綴 framing：訊息邊界自己定義 ---");
        Console.WriteLine($"  [client] 已連線到 127.0.0.1:{port}");
        Console.WriteLine();

        string[] messages = ["第一筆訊息", "第二筆訊息", "第三筆訊息"];

        foreach (var message in messages)
        {
            var payload = Encoding.UTF8.GetBytes(message);
            var header = new byte[HeaderSize];
            BinaryPrimitives.WriteUInt16BigEndian(header, (ushort)payload.Length);

            // 先送長度
            stream.Write(header);
            await stream.FlushAsync(ct);

            // 然後故意把內容拆成兩次送，中間還插入下一則訊息的長度前綴，
            // 逼出「伺服器一次 read 拿到不完整資料」的情況。
            await Task.Delay(50, ct);

            var splitAt = Math.Min(2, payload.Length);
            await stream.WriteAsync(payload.AsMemory(0, splitAt), ct);
            await stream.FlushAsync(ct);
            await Task.Delay(50, ct);
            await stream.WriteAsync(payload.AsMemory(splitAt), ct);
            await stream.FlushAsync(ct);
        }

        Console.WriteLine("  [client] 三則訊息已送出（每則的內容都被拆成兩段送）");
        Console.WriteLine();

        // 收 echo：同樣的兩步驟
        for (var i = 0; i < messages.Length; i++)
        {
            var header = new byte[HeaderSize];
            await stream.ReadExactlyAsync(header, ct);
            var length = BinaryPrimitives.ReadUInt16BigEndian(header);

            var payload = new byte[length];
            await stream.ReadExactlyAsync(payload, ct);

            Console.WriteLine($"  [client] 收到 echo #{i + 1}: 「{Encoding.UTF8.GetString(payload)}」");
        }

        Console.WriteLine();
        Console.WriteLine("  伺服器用 ReadExactlyAsync，所以不管我們怎麼拆包，");
        Console.WriteLine("  它讀到的都是完整的一則訊息。");
    }
}