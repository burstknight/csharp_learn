// Modbus/TCP 範例刻意「零外部相依」——只用 BCL 裡的 Socket/NetworkStream。
// 工業上有成熟的 Modbus 套件（NModbus 等），但讀懂封包格式才是重點，
// 而且現場碰到套件做不到的需求時，你得自己改封包。
//
// BinaryPrimitives 用來做位元組序轉換（.NET Core 3.0+ 內建，不需要 BitConverter 的手動翻轉）。
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace CSharpLearn.Day3;

public static class ModbusDemo
{
    public static async Task RunAsync(CancellationToken ct)
    {
        // 沒有真的 PLC，所以用 loopback 開一個「假 Modbus slave」，
        // 組出真實的 MBAP + PDU 封包送給它，再解析回應。
        // 這樣你可以在沒有設備的情況下驗證封包組裝與解析的邏輯。

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var endpoint = (IPEndPoint?)listener.LocalEndpoint;
        if (endpoint is null)
        {
            throw new InvalidOperationException("拿不到 listener 的本機端點");
        }

        Console.WriteLine("--- Modbus/TCP：讀 holding register（function code 0x03）---");
        Console.WriteLine($"  假的 PLC slave 監聽 127.0.0.1:{endpoint.Port}（對應標準 Modbus 埠 502）");
        Console.WriteLine();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var slave = RunFakeSlaveAsync(listener, cts.Token);

        // 實務上讀同一台 PLC 會「一直用同一條連線」，不是每次讀取都重連。
        // 這裡刻意用一條長連線送兩次請求，讓 fake slave 只需要 accept 一次。
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, endpoint.Port, ct);
        await using var stream = client.GetStream();

        try
        {
            await ReadHoldingRegistersAsync(stream, 1, 0x0000, count: 4, ct);
            await ReadHoldingRegistersAsync(stream, 1, 0x0002, count: 2, ct);
        }
        finally
        {
            // 先關掉連線，slave 端的 ReadExactlyAsync 才會結束；
            // 再取消 token 保證 accept 還沒完成時也能收尾。
            client.Dispose();
            await cts.CancelAsync();
            listener.Stop();
            await slave;
        }
    }

    // ── Client ────────────────────────────────────────────────────────

    /// <summary>
    /// 讀 holding register（function code 0x03）。
    /// 這是工業上最常用的一個功能代碼。
    ///
    /// 接收已建立的 <see cref="Stream"/> 而不是 port —— 示範「多個請求共用一條長連線」，
    /// 這是讀 PLC 的標準做法：減少 TCP 交握，也避免佔用 PLC 的連線數上限。
    /// </summary>
    private static async Task ReadHoldingRegistersAsync(
        Stream stream,
        byte unitId,
        ushort startAddress,
        ushort count,
        CancellationToken ct)
    {
        var request = BuildReadHoldingRegistersRequest(unitId, startAddress, count);

        Console.WriteLine($"  請求: unit {unitId}, address 0x{startAddress:X4}, count {count}, {request.Length} bytes");
        Console.WriteLine($"    {request.ToHex()}");
        Console.WriteLine();

        // Modbus 沒有內建逾時。設備斷線時 TCP 連線會一直維持在 ESTABLISHED，
        // 所以這裡一定要自己設，否則 ReadExactlyAsync 可能永久卡住。
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        var token = linkedCts.Token;

        await stream.WriteAsync(request, token);
        await stream.FlushAsync(token);

        // Modbus/TCP 的回應長度是確定的：
        //   MBAP(7) + FunctionCode(1) + ByteCount(1) + Data(2 × count) = 9 + 2 × count
        // 但嚴謹的做法是先讀 MBAP 看長度欄位。兩種都示範。
        var expectedLength = 9 + (2 * count);
        var response = new byte[expectedLength];
        await stream.ReadExactlyAsync(response, token);

        Console.WriteLine($"  回應: {response.Length} bytes");
        Console.WriteLine($"    {response.ToHex()}");
        Console.WriteLine();

        var values = ParseReadHoldingRegistersResponse(response, expectedLength);

        Console.WriteLine($"  解析結果（unit {unitId}, address 0x{startAddress:X4}）:");
        for (var i = 0; i < values.Length; i++)
        {
            Console.WriteLine($"    [{startAddress + i:X4}] = {values[i]}");
        }

        Console.WriteLine();
    }

    /// <summary>
    /// 組出讀 holding register 的請求封包。
    ///
    /// MBAP header（7 bytes，Modbus Application Protocol）
    ///   [0..1] Transaction ID  2 bytes  請求／回應配對用的流水號
    ///   [2..3] Protocol ID     2 bytes  固定 0（Modbus 的標誌就是 0，不是 1）
    ///   [4..5] Length          2 bytes  後面還有幾 bytes（Unit ID + PDU）
    ///   [6]    Unit ID         1 byte   站號（0 = 廣播，1..247 = 實際站號）
    ///
    /// PDU（function code 0x03）
    ///   [7]      Function Code 1 byte   0x03 = Read Holding Registers
    ///   [8..9]   Start Address 2 bytes  起始暫存器位址
    ///   [10..11] Quantity      2 bytes  要讀幾個（1..125）
    /// </summary>
    private static byte[] BuildReadHoldingRegistersRequest(byte unitId, ushort startAddress, ushort count)
    {
        if (count is 0 or > 125)
        {
            // 0x83 = 0x03 + 0x80：例外回應的最高位元是 1
            throw new ArgumentOutOfRangeException(
                nameof(count),
                count,
                "讀取數量必須介於 1..125");
        }

        var request = new byte[12];

        // 交易識別碼。實務上用遞增的計數器，這裡固定 1 方便閱讀。
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(0, 2), 1);

        // Protocol ID：Modbus 固定 0。這個 0 是「Modbus over TCP」和「Modbus over RTU」的分別。
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(2, 2), 0);

        // Length：Unit ID + PDU 全部 = 1 + 5 = 6
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(4, 2), 6);

        // Unit ID（站號）
        request[6] = unitId;

        // PDU
        request[7] = 0x03;                                          // Read Holding Registers
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(8, 2), startAddress);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(10, 2), count);

        return request;
    }

    /// <summary>
    /// 解析 0x03 的回應。
    ///
    ///   [0..1]  Transaction ID  必須和請求相同，否則是別的請求的回應
    ///   [2..3]  Protocol ID     應該是 0
    ///   [4..5]  Length
    ///   [6]     Unit ID
    ///   [7]     Function Code   0x03 正常 / 0x83 例外
    ///   [8]     Byte Count      資料區有幾 bytes（= count × 2）
    ///   [9..]   Data            每 2 bytes 一個 16-bit 暫存器
    /// </summary>
    private static ushort[] ParseReadHoldingRegistersResponse(byte[] response, int expectedLength)
    {
        // 交易識別碼不符 → 這是別的請求的回應（實務上要丟棄並繼續收）
        var transactionId = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(0, 2));
        var protocolId = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(2, 2));
        var functionCode = response[7];

        Console.WriteLine($"  Transaction ID: {transactionId}（必須和請求相同）");
        Console.WriteLine($"  Protocol ID:    {protocolId}（Modbus 固定 0）");

        // 最高位元是 1 → 例外回應
        if ((functionCode & 0x80) != 0)
        {
            var exceptionCode = response[8];
            throw new InvalidOperationException(
                $"Modbus 例外回應：function 0x{functionCode:X2}, exception code {exceptionCode} ({DescribeExceptionCode(exceptionCode)})");
        }

        if (functionCode != 0x03)
        {
            throw new InvalidOperationException($"預期收到 0x03，實際收到 0x{functionCode:X2}");
        }

        var byteCount = response[8];
        Console.WriteLine($"  Byte Count:     {byteCount}（= {byteCount / 2} 個 16-bit 暫存器）");
        Console.WriteLine();

        if (byteCount + 9 != expectedLength)
        {
            throw new InvalidOperationException(
                $"長度不一致：Byte Count 欄位說有 {byteCount + 9} bytes，實際收到 {expectedLength}");
        }

        var values = new ushort[byteCount / 2];
        for (var i = 0; i < values.Length; i++)
        {
            // Modbus 16-bit 暫存器都是 big-endian
            values[i] = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(9 + (i * 2), 2));
        }

        return values;
    }

    private static string DescribeExceptionCode(byte code) => code switch
    {
        0x01 => "Illegal Function：功能代碼不支援",
        0x02 => "Illegal Data Address：位址超出範圍",
        0x03 => "Illegal Data Value：數值不合法",
        0x04 => "Server Device Failure：從屬裝置內部錯誤",
        0x06 => "Server Device Busy：裝置忙，請稍後再試",
        0x0B => "Gateway Target Device Failed to Respond：閘道器下游沒回應",
        _ => "未知代碼",
    };

    // ── 假的 PLC slave ────────────────────────────────────────────────

    /// <summary>
    /// 假的 Modbus slave：收到 0x03 就回一組固定值。
    /// 存在的目的是讓你離線也能驗證封包組裝／解析的邏輯。
    /// 真實 PLC 會回什麼樣的值，取決於你接的是什麼設備 —— 一定要用設備手冊確認暫存器對應的意義。
    ///
    /// 這裡只服務「一條」長連線（accept 一次），對應 client 端在多個請求之間重用同一個 stream。
    /// 實務上如果每個請求都新開連線，slave 這邊就要用迴圈反覆 accept。
    /// </summary>
    private static async Task RunFakeSlaveAsync(TcpListener listener, CancellationToken ct)
    {
        // 假資料：模擬 4 個溫度感測器
        ushort[] registers = [235, 236, 240, 245];

        try
        {
            using var client = await listener.AcceptTcpClientAsync(ct);
            await using var stream = client.GetStream();

            while (!ct.IsCancellationRequested)
            {
                // 請求固定 12 bytes
                var request = new byte[12];
                await stream.ReadExactlyAsync(request, ct);

                var unitId = request[6];
                var functionCode = request[7];
                var startAddress = BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(8, 2));
                var quantity = BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(10, 2));

                Console.WriteLine($"  [slave] 收到: unit {unitId}, function 0x{functionCode:X2}, " +
                                  $"address 0x{startAddress:X4}, quantity {quantity}");

                if (functionCode != 0x03)
                {
                    // 回例外：function code 最高位元設 1
                    var errorResponse = new byte[9];
                    BinaryPrimitives.WriteUInt16BigEndian(errorResponse.AsSpan(0, 2),
                        BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(0, 2)));
                    BinaryPrimitives.WriteUInt16BigEndian(errorResponse.AsSpan(2, 2), 0);
                    BinaryPrimitives.WriteUInt16BigEndian(errorResponse.AsSpan(4, 2), 3);
                    errorResponse[6] = unitId;
                    errorResponse[7] = (byte)(functionCode | 0x80);
                    errorResponse[8] = 0x02;    // Illegal Data Address

                    await stream.WriteAsync(errorResponse, ct);
                    await stream.FlushAsync(ct);
                    continue;
                }

                // 位址超出我們的假暫存器範圍 → 回 Illegal Data Address
                if (startAddress + quantity > registers.Length)
                {
                    var errorResponse = new byte[9];
                    BinaryPrimitives.WriteUInt16BigEndian(errorResponse.AsSpan(0, 2),
                        BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(0, 2)));
                    BinaryPrimitives.WriteUInt16BigEndian(errorResponse.AsSpan(2, 2), 0);
                    BinaryPrimitives.WriteUInt16BigEndian(errorResponse.AsSpan(4, 2), 3);
                    errorResponse[6] = unitId;
                    errorResponse[7] = 0x83;
                    errorResponse[8] = 0x02;

                    await stream.WriteAsync(errorResponse, ct);
                    await stream.FlushAsync(ct);
                    continue;
                }

                var count = (ushort)Math.Min((int)quantity, registers.Length - startAddress);
                var response = new byte[9 + (count * 2)];

                // 回應的 Transaction ID 要和請求相同
                request.AsSpan(0, 6).CopyTo(response.AsSpan(0, 6));
                BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(4, 2), (ushort)(3 + (count * 2)));
                response[6] = unitId;
                response[7] = 0x03;
                response[8] = (byte)(count * 2);

                for (var i = 0; i < count; i++)
                {
                    BinaryPrimitives.WriteUInt16BigEndian(
                        response.AsSpan(9 + (i * 2), 2),
                        registers[startAddress + i]);
                }

                await stream.WriteAsync(response, ct);
                await stream.FlushAsync(ct);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException or IOException)
        {
            // 範例結束（client 關閉連線或被取消），正常收尾。
            // 工業實作要把「意外斷線」和「正常收尾」分開處理，前者要重連。
            Console.WriteLine($"  [slave] 結束：{ex.GetType().Name}");
        }
    }
}

/// <summary>把 byte 陣列印成 16 進位，並在每 8 bytes 換行，方便肉眼比對封包。</summary>
internal static class ByteArrayExtensions
{
    public static string ToHex(this byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return "(空)";
        }

        var builder = new StringBuilder(bytes.Length * 3);
        var column = 0;

        foreach (var b in bytes)
        {
            builder.Append(b.ToString("X2"));
            builder.Append(' ');

            if (++column % 8 == 0)
            {
                builder.Append('\n');
                builder.Append(' ', 18);
            }
        }

        return builder.ToString().TrimEnd();
    }
}