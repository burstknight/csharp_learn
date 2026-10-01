# Day 3 — Modbus/TCP

> 範例：[`ModbusDemo.cs`](ModbusDemo.cs)
> 跑法：`dotnet run --project day3 -- modbus`

⚠ **未經編譯驗證**（撰寫環境沒有 .NET SDK）。範例在 127.0.0.1 的隨機 port 開一個「假 PLC slave」，組出真實封包送給它，不需要實際設備。

> Modbus 是工業上最常見的協定，幾乎所有 PLC 都支援。**重點不是背功能代碼，而是看懂封包格式** —— 現場碰到套件做不到的需求時，你得自己改封包。

---

## 1. Modbus 是什麼

一種 1979 年發表的協定，特點是**簡單**到能用當時的序列埠傳輸。

- **Master（主站）**：通常是 PC / PLC / SCADA，發起請求
- **Slave（從站）**：現場設備（PLC、儀表、感測器）
- 一次只能由 master 發起；slave 只能回應

**架構限制要先知道**：標準 Modbus/TCP 是單 master 架構。一個 port 上所有 slave 依序回應，沒辦法同時傳輸。多 master 要靠 serial-to-Ethernet gateway。

### 兩種傳輸

| | Modbus/RTU | Modbus/TCP |
| --- | --- | --- |
| 傳輸 | 序列埠（RS-232/485） | 乙太網 |
| 封裝 | CRC16 檢查碼 | TCP + MBAP header |
| 埠 | 序列埠 | **502** |
| 位址 | slave 用站號 | 用 IP + 站號 |
| 現況 | 仍在大量使用（舊設備） | 新系統主流 |

---

## 2. 封包結構

Modbus/TCP 的封包分兩塊：

```
┌──────────────── MBAP Header (7 bytes) ────────────────┬──── PDU ────┐
│ Transaction ID │ Protocol ID │ Length │ Unit ID        │ Function │ Data │
│     2 bytes    │   2 bytes   │2 bytes │   1 byte       │   Code   │  N   │
└────────────────────────────────────────────────────────┴─────────────┘
```

### MBAP Header（Modbus Application Protocol）

| 欄位 | 大小 | 值 |
|---|---|---|
| Transaction ID | 2 bytes | 請求／回應配對用的流水號，可遞增 |
| Protocol ID | 2 bytes | **固定 0** |
| Length | 2 bytes | 後面還有幾 bytes（Unit ID + PDU） |
| Unit ID | 1 byte | 站號。**0 = 廣播（不回應），1–247 = 實際站號** |

⚠ **Protocol ID 是 0，不是 1。** 這是 Modbus over TCP 和 Modbus over RTU 的主要差異 —— RTU 沒有 MBAP。

⚠ **Length 不包含 MBAP 自己**，只算 Unit ID + PDU。

### PDU（Protocol Data Unit）

| 欄位 | 大小 | 說明 |
|---|---|---|
| Function Code | 1 byte | 要做什麼 |
| Data | 0–253 bytes | 依功能代碼而定 |

**例外回應**（Error Response）：`Function Code` 的**最高位元設 1**，後面的 Data 換成 1 byte 例外代碼。

例：請求 `0x03` 失敗 → 回應 `0x83` + 例外碼 `0x02`。

---

## 3. 功能代碼

| 代碼 | 名稱 | 說明 |
|---|---|---|
| `0x01` | Read Coils | 讀位元（1 bit / 點） |
| `0x02` | Read Discrete Inputs | 讀離散輸入（唯讀位元） |
| `0x03` | **Read Holding Registers** | 讀保持暫存器（可讀寫，16-bit）— **最常用** |
| `0x04` | Read Input Registers | 讀輸入暫存器（唯讀，16-bit） |
| `0x05` | Write Single Coil | 寫單一位元 |
| `0x06` | Write Single Register | 寫單一暫存器 |
| `0x0F` | Write Multiple Coils | 寫多位元 |
| `0x10` | **Write Multiple Registers** | 寫多暫存器 — **最常用** |

### `0x03` 和 `0x04` 的差別

功能幾乎一樣，只差**可不可寫**：

- `0x03` Holding Registers：**可讀可寫**，用來存設定值
- `0x04` Input Registers：**唯讀**，用來存感測器讀數

**實務上很多設備的暫存器表不守規矩**，有些工廠把感測器值也放在 holding register。**一定要看設備手冊的「register map」**，不能靠功能代碼猜。

---

## 4. 讀 Holding Registers（0x03）實作

### 請求（12 bytes）

```
00 01 | 00 00 | 00 06 | 01 | 03 | 00 00 | 00 04
  ^       ^       ^      ^    ^     ^       ^
  |       |       |      |    |     |       └─ Quantity: 讀 4 個
  |       |       |      |    |     └───────── Start Address: 0x0000
  |       |       |      |    └─────────────── Function Code: 0x03
  |       |       |      └──────────────────── Unit ID: 1
  |       |       └──────────────────────────── Length: 6 (Unit ID 1 + PDU 5)
  |       └──────────────────────────────────── Protocol ID: 0（固定）
  └──────────────────────────────────────────── Transaction ID: 1
```

```csharp
var request = new byte[12];

BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(0, 2), 1);          // Transaction ID
BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(2, 2), 0);          // Protocol ID 固定 0
BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(4, 2), 6);          // Length
request[6] = 1;                                                          // Unit ID
request[7] = 0x03;                                                       // Function Code
BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(8, 2), 0x0000);     // Start Address
BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(10, 2), 4);         // Quantity
```

### 限制

| 參數 | 合法範圍 |
|---|---|
| Quantity | **1–125**（`0x03`/`0x04`）；寫多個時最多 123 |
| Start Address | 0–65535 |
| Unit ID | 0（廣播）或 1–247 |

⚠ **超過 125 個暫存器會被 slave 回例外碼 `0x03`（Illegal Data Value）。** 一定要在送出去之前就檢查。

### 回應

```
00 01 | 00 00 | 00 0B | 01 | 03 | 08 | 00 EB 00 EC 00 F0 00 F5
  ^       ^       ^      ^    ^    ^    └──── 8 bytes 的資料 = 4 個暫存器
  |       |       |      |    |    └───────── Byte Count
  |       |       |      |    └────────────── Function Code (0x83 才是例外)
  |       |       |      └─────────────────── Unit ID
  |       |       └────────────────────────── Length: 11
  |       └────────────────────────────────── Protocol ID
  └────────────────────────────────────────── Transaction ID（必須和請求相同）
```

```csharp
var byteCount = response[8];
var values = new ushort[byteCount / 2];
for (var i = 0; i < values.Length; i++)
{
    values[i] = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(9 + (i * 2), 2));
}
```

### Transaction ID 必須檢查

**回應的 Transaction ID 要和請求相同。** 不相同代表這是別的請求的回應（如果你有逾時重試，可能已經送出多筆請求）。

`ModbusDemo.cs` 會把兩邊都印出來讓你比對。

---

## 5. 例外回應

```
00 01 | 00 00 | 00 03 | 01 | 83 | 02
                            ^    ^
                            |    └─ Exception Code
                            └────── 0x80 | 0x03 = 0x83
```

| 例外碼 | 意義 | 常見原因 |
|---|---|---|
| `0x01` | Illegal Function | 設備不支援這個功能代碼 |
| `0x02` | Illegal Data Address | 位址超出範圍（**最常見**） |
| `0x03` | Illegal Data Value | 數值不合法（例如數量超過 125） |
| `0x04` | Server Device Failure | 從屬裝置內部錯誤 |
| `0x06` | Server Device Busy | 裝置忙，稍後重試 |
| `0x0B` | Gateway Target Failed to Respond | 閘道器下游沒回應 |

**處理方式**：

```csharp
if ((functionCode & 0x80) != 0)
{
    var exceptionCode = response[8];
    // 0x02 → 位址錯，檢查 register map
    // 0x06 → 忙，稍後重試
    // 其他 → 記錄，可能是協定版本不相容
    throw new InvalidOperationException($"Modbus 例外: {exceptionCode}");
}
```

⚠ **看到 `0x02` 先檢查你的位址和數量，不要急著重試。** 重試不會讓錯的位址變對。

---

## 6. 32-bit 數值怎麼讀

有些設備用 2 個暫存器存一個 32-bit 的 float 或整數。**兩種排列，必須查手冊**：

```
暫存器 A = 高 16 bits，暫存器 B = 低 16 bits     → Word Order: High Low
暫存器 A = 低 16 bits，暫存器 B = 高 16 bits     → Word Order: Low High（Byte Swap）
```

```csharp
// High Low
var value = ((uint)registers[i] << 16) | registers[i + 1];

// Low High
var value = ((uint)registers[i + 1] << 16) | registers[i];
```

⚠ **這是實務上最花時間的地方。** float 還要處理 `Int32` ↔ `Single` 的 bit 重組：

```csharp
var bits = (int)value;
var f = BitConverter.Int32BitsToSingle(bits);   // .NET Core 3.0+
```

**每種設備的 register map 都不一樣，沒有一個通則。** 一定要拿手冊逐一確認，並且**用已知值驗證**（例如手冊上寫某個位址在正常狀態是 25.5，就去確認你讀到 25.5）。

---

## 7. 實務注意事項

### 一定要用 big-endian

```csharp
BinaryPrimitives.ReadUInt16BigEndian(...)   // ✅
// 不要用 BitConverter.ToUInt16 —— x86 上是小端序，結果會錯
```

### 逾時

Modbus/TCP 沒有任何內建逾時。設備斷線時 TCP 連線會維持在「ESTABLISHED」狀態好幾十分鐘。

**一定要用 `CancellationToken` 給每次請求設逾時**：

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
await stream.WriteAsync(request, cts.Token);
await stream.ReadExactlyAsync(response, cts.Token);
```

### 輪詢間隔

**不要把 PLC 輪詢得太快。** 很多 PLC 的通訊處理器（尤其是小型型號）處理不了高頻請求，會造成：

- 其他通訊失敗
- 程式掃描週期被拖慢 → 影響即時控制
- 通訊逾時率上升

**查 PLC 手冊的「最大連線數」和「建議輪詢間隔」**。經驗值：一台 PLC 同時最多 2–4 個連線，單一裝置的輪詢間隔不低於 100–200 ms。

### 長連線 vs 短連線

| | 長連線 | 每次重連 |
|---|---|---|
| 延遲 | 低 | 高（TCP 握手） |
| 資源 | 佔用 PLC 連線數 | 釋放得快 |
| 適合 | 資料量大、高頻 | 低頻、設備會重啟 |

**高頻採集用長連線 + keepalive**。低頻監控用短連線比較安全（不佔用 PLC 資源）。

### 先讀手冊

Register map 沒有任何標準。同一台 PLC 的不同機型，位址可能完全不同。

---

## 8. 要不要用套件

| 做法 | 優點 | 缺點 |
|---|---|---|
| 自己寫 | 完全控制、能處理特殊需求、零相依 | 每個設備都要測、花時間 |
| NModbus 等套件 | 快、有現成封裝 | 客製化困難、版本相容性要確認 |

**建議：先用自己寫的版本跑通一個設備，理解封包格式後，再決定要不要換套件。** 現場碰到套件做不到的需求（例如非標準功能代碼、32-bit 排列不一致）時，你還是得自己改封包。

---

## 速查

```csharp
// MBAP
BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(0, 2), transactionId);
BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(2, 2), 0);   // 固定 0
BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(4, 2), length);
buf[6] = unitId;

// PDU：讀 Holding Registers
buf[7] = 0x03;
BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(8, 2), startAddress);
BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(10, 2), quantity);   // 1..125

// 解析
var isException = (buf[7] & 0x80) != 0;
var byteCount = buf[8];
var value = BinaryPrimitives.ReadUInt16BigEndian(buf.AsSpan(9, 2));

// Hex 檢查用
Convert.ToHexString(bytes);
```

---

## 練習

1. 實作 `0x04`（Read Input Registers），和 `0x03` 比對程式碼差異在哪。
2. 實作 `0x06`（Write Single Register）寫入一個值，再用 `0x03` 讀回來驗證。
3. 實作 `0x10`（Write Multiple Registers）一次寫 10 個暫存器 —— 注意數量上限是 123。
4. 加上錯誤重試：收到 `0x06`（busy）時重試最多 3 次，指數退避。
5. 加上逾時：用 `CancellationTokenSource` 給每次請求 3 秒逾時，逾時時關閉並重連。
6. 實作 32-bit float 讀取，並用假 slave 塞入 `float` 值驗證 High-Low / Low-High 兩種排列。
7. 寫一個 `ModbusMaster` 類別，管理多台設備的並行請求（每台一條長連線 + `SemaphoreSlim` 限制併發）。
