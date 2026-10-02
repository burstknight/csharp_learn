# Day 3 — Socket 與訊息 framing

> 範例：[`EchoServerDemo.cs`](EchoServerDemo.cs)
> 跑法：`dotnet run --project day3 -- echo`

✅ 已用 .NET 10 SDK 編譯並實跑驗證過。範例在 127.0.0.1 的隨機 port 上開 server 和 client，不需要外部設備。

這是底層通訊最重要的一節。**會寫 Socket 不難，寫對 framing 才是分水嶺。**

---

## 1. TCP 是 byte stream，沒有「訊息」的概念

你的 C++/Python 經驗裡，也許寫過「send 一個 struct，recv 收一個 struct」。**那在 C/C++ 裡也不保證成立** —— 只是 buffer 夠大的話常常「剛好」可以。

原因：

- **TCP 是 stream 協定**。它只保證 byte 順序，不保證訊息邊界
- 一個 `send` 出去的資料，接收端可能：
  - 一次 `recv` 全部收到
  - 拆成 5 次 `recv` 才收到
  - **和下一個 send 的資料黏在一起**被一次收到

第二種叫**粘包**（或 packet coalescing），第三種叫**拆包**。

**`recv()` 回傳的長度跟 `send()` 的長度沒有任何關係。**

範例的輸出會讓你親眼看到：

```
[client] 已連線到 127.0.0.1:52341
[server] 一次 ReadAsync 收到 10 bytes
[server] hex   : 41 41 41 42 42 43 43 43 43 43
[server] 當成文字: 「AAABBCCCCC」
```

送出去的卻是三則長度分別為 3、2、5 的訊息。收到的是 10 bytes 混在一起的東西 —— **你無法知道分界在哪**。

---

## 2. Framing：自己定義訊息邊界

既然 TCP 不給你邊界，你就自己定。常見三種：

### a) 長度前綴（length-prefixed）— 最常用

```
[2 bytes 長度][N bytes 內容]
```

```csharp
// 寫
var header = new byte[2];
BinaryPrimitives.WriteUInt16BigEndian(header, (ushort)payload.Length);
stream.Write(header);
stream.Write(payload);

// 讀 ← 兩步驟，缺一不可
var header = new byte[2];
await stream.ReadExactlyAsync(header, ct);          // 先讀長度
var length = BinaryPrimitives.ReadUInt16BigEndian(header);
var payload = new byte[length];
await stream.ReadExactlyAsync(payload, ct);         // 再讀剛好那麼多內容
```

優點：自我描述、無歧義、實作簡單。Modbus、MQTT、大部分工業協定都用這個。

代價：每則訊息多 2 bytes（或 4 bytes）。2 byte 的上限是 65535 bytes，**不夠就改用 4 byte**。

### b) 固定長度

```
[N bytes 內容]   ← N 編譯期已知
```

最簡單，但浪費空間。只適合所有訊息等長的協定（例如某些感測器協定）。

### c) 分隔符（delimiter）

```
[內容]\n[內容]\n
```

最像文字協定（HTTP 就是 `\r\n\r\n`）。⚠ **內容本身不能包含分隔符**，否則一樣有歧義。讀到 `\n` 之間的內容要自己做跳脫／反跳脫（base64 之類）。

### 選哪個

| 情境 | 建議 |
| --- | --- |
| 自己設計協定 | 長度前綴，4 byte（uint）最保險 |
| 純文字協定 | 分隔符 + 跳脫 |
| 內容長度可預期且差異小 | 固定長度 |
| 對接既有設備 | **必須照設備手冊寫**，不要自己發明 |

---

## 3. `ReadExactlyAsync` 解決拆包

即使有了 framing，一次 `Read` 也可能讀不到剛好那麼多 bytes。.NET 7+ 提供了正確的做法：

```csharp
// ❌ 可能讀不到 2 bytes，也可能讀到比 2 bytes 還多（這裡 buffer 只有 2 所以沒問題，但不可靠）
var header = new byte[2];
var read = await stream.ReadAsync(header, ct);

// ✅ 一直讀到剛好讀滿，中間不管分幾次到
await stream.ReadExactlyAsync(header, ct);
```

`Stream.ReadExactlyAsync` 會內部迴圈直到讀滿或拋出例外。**所有需要「剛好 N bytes」的協定解析都應該用它。**

### 自己實作（理解原理用）

```csharp
private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken ct)
{
    var offset = 0;
    while (offset < buffer.Length)
    {
        var read = await stream.ReadAsync(buffer.AsMemory(offset), ct);
        if (read == 0) throw new EndOfStreamException("連線被對端關閉");
        offset += read;
    }
}
```

**這是你必須理解的 5 行。** 面試和現場除錯都靠它。

### 讀不到資料時

```csharp
if (read == 0)
{
    // 0 = 對端已關閉連線（正常關閉的 EOF）
    break;
}
```

`Read` 回傳 0 代表 stream 結束，不是錯誤，是**對端關掉了 socket**。工業系統裡這很常見（設備重啟），要當成正常狀況處理。

---

## 4. 位元組序

網路協定幾乎都用 **big-endian**（又叫 network byte order）。

```csharp
// 網路協定：big-endian
BinaryPrimitives.WriteUInt16BigEndian(buffer, 0x1234);   // → 12 34
BinaryPrimitives.ReadUInt16BigEndian(buffer);             // → 0x1234

// C# 的 BitConverter：host-dependent（x86 上是 little-endian）
BitConverter.ToUInt16(new byte[] { 0x12, 0x34 }, 0);      // → 0x3412  ← 小端序！
```

⚠ **絕對不要用 `BitConverter` 做協定解析。** 它在 x86 上回傳的結果會和協定不符，換到 ARM 架構（如樹莓派）行為又不一樣。

用 `System.Buffers.Binary.BinaryPrimitives`（.NET Core 3.0+ 內建）：

| 方法 | 用途 |
| --- | --- |
| `WriteUInt16BigEndian` / `ReadUInt16BigEndian` | 2 bytes |
| `WriteUInt32BigEndian` / `ReadUInt32BigEndian` | 4 bytes |
| `WriteInt32BigEndian` / `ReadInt32BigEndian` | 有號整數 |
| `WriteSingleBigEndian` / `ReadSingleBigEndian` | float |

**如果協定指定 little-endian**（有些工業協定確實是），就用 `...LittleEndian` 版本。**一定要查設備手冊。**

---

## 5. 建立連線

### Server

```csharp
var listener = new TcpListener(IPAddress.Loopback, 0);   // 0 = 讓 OS 挑空閒 port
listener.Start();

var port = ((IPEndPoint?)listener.LocalEndpoint)!.Port;  // ⚠ LocalEndpoint 可為 null

while (running)
{
    using var client = await listener.AcceptTcpClientAsync(ct);
    var stream = client.GetStream();
    // 處理這個連線
}
```

⚠ `LocalEndpoint` 型別是 `EndPoint?`，**取出 `Port` 前要先檢查 null**，否則 `Nullable=enable` 會給你 `CS8602`。

**教學時用 `port: 0` 讓 OS 挑空閒 port**，這是測試程式的良好習慣 —— 不會撞到別人用的 502 或 1883。

### Client

```csharp
using var client = new TcpClient();
await client.ConnectAsync(IPAddress.Loopback, port, ct);
await using var stream = client.GetStream();
```

⚠ **`NetworkStream` 要 `await using`**，它實作 `IAsyncDisposable`（`TcpClient` 則只有 `IDisposable`）。

### 逾時一定要設

```csharp
client.ReceiveTimeout = TimeSpan.FromSeconds(5);
client.SendTimeout = TimeSpan.FromSeconds(5);
```

**不設的話，`ReadAsync` 可以永久卡住** —— 設備斷線時 TCP 不會通知你，連線會一直維持在「ESTABLISHED」直到 TCP keepalive 逾時（Windows 預設 2 小時）。

工業程式的標準做法：用 `CancellationTokenSource.CancelAfter()` 給每次操作設逾時。

---

## 6. 一次一個連線 vs 很多連線

範例是最簡單的單一連線。實際的採集程式需要**同時處理多台設備**：

```csharp
// ❌ 循序：一台讀完才讀下一台，N 台就花 N 倍時間
foreach (var device in devices)
{
    await ReadAsync(device, ct);
}

// ✅ 併發：所有設備同時讀
await Task.WhenAll(devices.Select(d => ReadAsync(d, ct)));
```

⚠ 併發時要**限制同時連線數**，否則幾百台設備一次連會把設備和網路都搞垮：

```csharp
using var semaphore = new SemaphoreSlim(initialCount: 10);

async Task ReadAsync(Device device, CancellationToken ct)
{
    await semaphore.WaitAsync(ct);
    try
    {
        // 實際讀取
    }
    finally
    {
        semaphore.Release();      // 一定要在 finally，否則例外會洩漏配額
    }
}
```

更完整的做法是**一台設備一個長駐連線**（各自有一個採集迴圈），而不是每次讀取都重連 —— 除非協定要求短連線。

---

## 7. 緩衝區

```csharp
// ❌ 每次讀都配置新陣列
while (true)
{
    var buffer = new byte[4096];   // 每次迭代都配置，GC 壓力大
    var read = await stream.ReadAsync(buffer, ct);
}

// ✅ 重用一個 buffer
var buffer = new byte[4096];
while (true)
{
    var read = await stream.ReadAsync(buffer, ct);
    // 處理 buffer[0..read]
}
```

高頻讀取時，重用 buffer 是最容易拿到效能改善的地方之一（也更少 GC 壓力）。

用 `ArrayPool<byte>` 可以更進一步，但**先確認你真的需要** —— 收尾要 `Return`，忘記 return 會讓 pool 枯竭。

---

## 8. 除錯

```csharp
// 確認連線狀態
Console.WriteLine(client.Connected);          // ⚠ 這個不準，只反映最後一次 I/O 的結果

// 正確檢查
if (!client.Client.Connected) { ... }

// 或直接看 socket 狀態（Windows / Linux 都要用 try/catch 包起來）
try
{
    var state = client.Client.RemoteEndPoint;
}
catch (SocketException)   // NotConnected
{
}
```

⚠ **`TcpClient.Connected` 不可靠** —— 它只反映最近一次 I/O 操作的結果，連線中斷後要等到下次 I/O 才知道。判斷連線健康狀態要靠心跳或讀取逾時，不是靠這個屬性。

---

## 速查

```csharp
// Server
var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
using var client = await listener.AcceptTcpClientAsync(ct);
var stream = client.GetStream();

// Client
using var client = new TcpClient();
await client.ConnectAsync(IPAddress.Loopback, port, ct);
await using var stream = client.GetStream();

// Framing（2 byte 長度前綴）
var header = new byte[2];
BinaryPrimitives.WriteUInt16BigEndian(header, (ushort)payload.Length);
stream.Write(header);
stream.Write(payload);

await stream.ReadExactlyAsync(header, ct);
var length = BinaryPrimitives.ReadUInt16BigEndian(header);
var payload = new byte[length];
await stream.ReadExactlyAsync(payload, ct);

// 逾時
client.ReceiveTimeout = TimeSpan.FromSeconds(5);
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
```

---

## 練習

1. 把長度前綴從 2 byte 改成 4 byte（`uint`），測試傳送 100 KB 的訊息。
2. 改用**分隔符** framing（`\n` 結尾），親眼體會「內容含分隔符」的歧義問題。
3. 在 client 送出時，故意讓 3 則訊息的 header 和內容交錯送出（header1 + header2 + payload1 + payload2），看 server 的 `ReadExactlyAsync` 還能不能正確還原。
4. 加一個長度上限檢查：如果 `length > 4096` 就直接斷線（防禦畸形／惡意封包導致記憶體被吃光）。
5. 改成 3 台「虛擬設備」同時連線的 server 端，用 `Task.WhenAll` 併發處理。
6. 加心跳：每 5 秒送一筆 ping，若 15 秒沒收到資料就重連。
