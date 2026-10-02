# Day 3 — MQTT

> 範例：[`MqttDemo.cs`](MqttDemo.cs)
> 跑法：`dotnet run --project day3 -- mqtt`

✅ 已用 .NET 10 SDK + MQTTnet 5.2.0.1603 編譯並實跑驗證過。注意 MQTTnet 5 把 `MqttTopicFilterBuilder`
和 `MqttProtocolVersion` 搬到了 `MQTTnet.Formatter` 命名空間，舊版範例常見的 `MQTTnet.Client` 已不存在。

> MQTT 是 IoT / 工業資料傳遞最常用的協定。工作上如果是採集資料上雲、或設備之間溝通，MQTT 幾乎是標配。

**MQTT 範例會在 127.0.0.1:1883 開一個 broker。** 如果該 port 已被占用（Docker、已裝的 Mosquitto），範例會直接失敗。

---

## 1. 核心概念

```
[發布者 Publisher]  --PUBLISH-->  [Broker]  --PUSH-->  [訂閱者 Subscriber]
   (感測器/PLC)                    (訊息代理)              (SCADA/雲端)
```

**三個角色：**

- **Publisher**：只負責發布到某個 topic，不關心誰在聽
- **Subscriber**：訂閱 topic，收到符合的訊息
- **Broker**（代理人）：收到發布的訊息，依 topic 分發給所有訂閱者

**關鍵：發布者和訂閱者互相不知道對方存在。** 這是 publish/subscribe 模型和傳統 client/server 的差別 —— 這也是為什麼 MQTT 適合設備規模會變動的場景。

### Topic

階層用 `/` 分隔，支援萬用字元：

| Topic | 說明 |
|---|---|
| `factory/line1/temp` | 精確比對 |
| `factory/line1/+/temp` | `+` 匹配**恰好一層**：`factory/line1/a/temp` ✅、`factory/line1/b/c/temp` ❌ |
| `factory/line1/#` | `#` 匹配**剩餘所有層**，必須放最後：`factory/line1/a/b/c` ✅ |
| `+/temperature` | 任何設備的 temperature |

⚠ **`+` 和 `#` 只能用在訂閱端，不能用在發布端。**

### 本機的 MQTT broker 怎麼找

| 方式 | 說明 |
|---|---|
| Docker | `docker run -it --rm -p 1883:1883 -p 9001:9001 eclipse-mosquitto` |
| 套件安裝 | `apt install mosquitto mosquitto-clients` |
| MQTT Explorer | <https://mqtt-explorer.com/> — 圖形介面看封包，非常推薦 |
| MQTTnet | 就是本 repo 範例的做法，寫在 code 裡 |

`mosquitto_sub` / `mosquitto_pub` 這兩個命令列工具在測試時非常好用。

---

## 2. QoS

QoS（Quality of Service）是「訊息要送得多可靠」的設定，**發布端和訂閱端各自設定，最終取兩者中較低的那個**。

| QoS | 名稱 | 保證 | 成本 | 用途 |
|---|---|---|---|---|
| 0 | AtMostOnce | 不重試 | 最低 | 高頻遙測，可容忍遺失 |
| 1 | AtLeastOnce | **送達，但可能重複** | 中 | **警報、控制指令** |
| 2 | ExactlyOnce | 送達且不重複 | 最高（四次握手） | 金融、計費等不能重複的場合 |

**QoS 1 會重複 —— 這不是 bug，是協定設計。** 接收端如果需要冪等（idempotent）處理，得自己去重。例如用序號去重。

⚠ **工業採集的遙測資料通常用 QoS 0。** 下一筆資料一到，上一筆就沒意義了，重送沒有意義反而佔頻寬。警報和指令才需要 QoS 1 或 2。

### Retained message

`retain = true` 的訊息會被 broker 存起來。**之後任何訂閱該 topic 的 client 一連上就會立刻收到最後一筆**。

**用途**：設備的「目前狀態」。新訂閱者一連上就知道現在值是多少，不用等下一次發布。

⚠ **每個 topic 只保留最後一筆 retained message。** 不要用它當資料儲存。

---

## 3. MQTTnet 5 —— 先讀這段

⚠ **MQTTnet 5 是大改版，網路上大多是 4.x 的教學。直接抄會編譯不過。**

### v4 → v5 的 breaking changes

| v4 | v5 | 說明 |
|---|---|---|
| `new MqttFactory()` | `new MqttClientFactory()`<br>`new MqttServerFactory()` | **拆成兩個** |
| `factory.CreateMqttServer(options)` | `serverFactory.CreateMqttServer(options)` | 名字不變，但 factory 類別不同 |
| `MqttApplicationMessage.Payload` 是 `byte[]` | 是 **`ReadOnlySequence<byte>`** | **取值方式變了** |
| `Encoding.UTF8.GetString(msg.Payload)` | **`msg.ConvertPayloadToString()`** | 擴充方法，回傳 `string?` |
| 預設協定版本 MQTT 3.1.1 | 預設 **MQTT 5.0.0** | 連到舊 broker 可能要明寫版本 |
| `Managed Client` | **已移除** | 不要搜尋到還在講這個的教學 |
| 命名空間也變了 | | |

**其他 v5 改動**（會影響 broker 端）：

- Server 的 `MaxPendingMessagesPerClient` 預設改成 1000
- Server 的 SSL 版本預設改成 `None`（讓 OS 選）→ **舊設定可能需要重寫**
- `TryPrivate`（Mosquitto 特性）處理修正

📖 官方升級指南：<https://github.com/dotnet/MQTTnet/wiki/Upgrading-guide>

**看教學的第一步永遠是確認版本。**

---

## 4. 用法

### 開一個 broker（測試用）

```csharp
// v5：MqttServerFactory（v4 是 MqttFactory）
var serverFactory = new MqttServerFactory();

var serverOptions = new MqttServerOptionsBuilder()
    .WithDefaultEndpoint()            // 預設 port 1883
    .WithDefaultEndpointPort(1883)
    .Build();

using var server = serverFactory.CreateMqttServer(serverOptions);
await server.StartAsync();

// ... 測試 ...

await server.StopAsync();
```

⚠ **預設 endpoint 不加密。** 生產環境要 `WithEncryptedEndpoint()` + 憑證。

### 建立 client

```csharp
var factory = new MqttClientFactory();     // v5；v4 是 MqttFactory
using var client = factory.CreateMqttClient();

var options = new MqttClientOptionsBuilder()
    .WithTcpServer("localhost", 1883)
    .WithClientId("my-client")            // 每個 client 的 ID 要唯一
    .WithProtocolVersion(MqttProtocolVersion.V500)
    .Build();

await client.ConnectAsync(options, ct);
```

**`WithClientId` 要唯一。** 兩個 client 用同一個 ID，broker 會把前一個踢掉線 —— 這是「client 看起來一直斷線重連」最常見的原因。

### 訂閱

```csharp
// ⚠ handler 必須在 ConnectAsync「之前」掛上，
//    官方 sample 的註解說明：沒有 handler 的話先到的訊息會被丟掉
client.ApplicationMessageReceivedAsync += e =>
{
    // v5：Payload 是 ReadOnlySequence<byte>，用擴充方法取值
    var payload = e.ApplicationMessage.ConvertPayloadToString() ?? "(空)";
    Console.WriteLine($"{e.ApplicationMessage.Topic} = {payload}");
    return Task.CompletedTask;
};

await client.ConnectAsync(options, ct);

var subscribeOptions = factory
    .CreateSubscribeOptionsBuilder()
    .WithTopicFilter("csharp-learn/line1/temperature")
    .WithAtLeastOnceQoS()        // QoS 1
    .Build();

await client.SubscribeAsync(subscribeOptions, ct);
```

⚠ **這個 handler 會在網路執行緒上被呼叫。** 它裡面寫入的共用資料一定要上鎖 —— `MqttDemo.cs` 裡的 `lock (received)` 就是示範這點。

### 發布

```csharp
var message = new MqttApplicationMessageBuilder()
    .WithTopic("csharp-learn/line1/temperature")
    .WithPayload("25.5")
    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
    .Build();

await client.PublishAsync(message, ct);
```

### 斷線

```csharp
await client.DisconnectAsync();
```

⚠ **QoS 1 的訊息是非同步確認的。** `PublishAsync` 回來不代表 broker 已經收到。**發完馬上 `DisconnectAsync` 會讓訊息遺失。** 要等的話檢查 `PublishAsync` 的回傳結果或等待一段時間。

---

## 5. ⚠ 在 handler 裡 publish 要用 Task.Run

這是官方 wiki 特別標註的規則：

> Publishing messages inside that received messages handler requires to use `Task.Run` when using a QoS > 0. The reason is that the message handler has to finish first before the next message is received.

```csharp
// ❌ QoS 1 時，這個 handler 必須在下一則訊息進來「之前」完成。
//    如果在裡面 await 一個需要 QoS 確認的 publish，就會卡住 → 連線停滯
client.ApplicationMessageReceivedAsync += async e =>
{
    await client.PublishAsync(response, ct);      // ❌ 可能卡死
    return Task.CompletedTask;
};

// ✅ 用 Task.Run 把 publish 丟出去，handler 立即結束
client.ApplicationMessageReceivedAsync += e =>
{
    _ = Task.Run(() => client.PublishAsync(response, ct));
    return Task.CompletedTask;
};
```

**這是 MQTTnet 最容易造成「無故停滯」的寫法錯誤。** 查問題時先看這裡。

---

## 6. 斷線重連

工業系統的 MQTT 連線**一定會斷**（網路、broker 重啟、設備休眠）。內建的 `DisconnectedAsync` 事件要自己接：

```csharp
client.DisconnectedAsync += e =>
{
    Console.WriteLine($"斷線：{e.Exception?.Message ?? e.DisconnectType.ToString()}");
    // 呼叫端要在這裡安排重連（不要在這裡直接重連，會卡住執行緒）
    ScheduleReconnect();
    return Task.CompletedTask;
};
```

⚠ **不要在 `DisconnectedAsync` 裡直接呼叫 `ConnectAsync` 重連。** 會造成無盡重連迴圈把 broker 打死。用退避（exponential backoff）：

```csharp
var delay = TimeSpan.FromSeconds(1);
while (running)
{
    try
    {
        await client.ConnectAsync(options, ct);
        delay = TimeSpan.FromSeconds(1);      // 連上就重設
        break;
    }
    catch (Exception)
    {
        await Task.Delay(delay, ct);
        delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));   // 上限 30 秒
    }
}
```

### QoS 與重連

QoS 1 的訊息在斷線期間**會被 broker 緩衝**（因為 broker 知道該 client 訂閱了 QoS 1 的 topic）。**但這只在 `CleanSession = false` 時成立**：

```csharp
.WithCleanSession(false)    // 保留訂閱與 QoS 訊息
```

⚠ `CleanSession = false` 會讓 broker 永久保存該 client 的狀態（佔資源）。**離線的 client 要定期發動態式 LWT（Last Will and Testament）或讓 broker 過期清理。**

---

## 7. 安全性

**不要在公網上跑未加密的 1883。**

```csharp
.WithTcpServer("broker.example.com", 8883)
.WithTlsOptions(o =>
{
    o.UseTls();                                 // 開 TLS
    o.WithAllowUntrustedCertificates(false);    // ⚠ 絕對不要設 true
    o.WithClientCertificates(cert);              // 雙向認證
})
.WithCredentials(username, password)
```

| 項目 | 預設 | 建議 |
|---|---|---|
| 加密 | **無** | TLS 8883 |
| 認證 | 無 | 每個 client 獨立帳密或憑證 |
| 授權 | 無 | broker 端設 ACL 限制 topic |
| 埠 | 1883 | 8883（TLS）、9001（WebSocket） |

⚠ 常見錯誤：為了讓憑證驗證通過而設 `AllowUntrustedCertificates = true` —— **這等於完全沒有驗證，任何人都能中間人攻擊。** 憑證有問題要修憑證，不是關驗證。

**broker 端一定要設 ACL**：即使 client 認證成功，也不該讓它訂閱整個 `#`。

---

## 8. 實務主題命名

```
{環境}/{工廠}/{產線}/{區段}/{設備}/{資料類型}
例：prod/factory-A/line1/mixing-tank/temp
    dev/lab/bench-01/temp
```

好處：

- 一眼看出資料來源
- 可以用萬用字元整段訂閱（`prod/factory-A/#`）
- 測試和正式資料天然隔離（**測試 broker 和正式 broker 也要分開**）

⚠ 別把 client ID 當成訂閱的依據。**client ID 是連線識別，topic 是資料分區**，兩回事。

---

## 速查

```csharp
// Broker
var sf = new MqttServerFactory();
using var server = sf.CreateMqttServer(
    new MqttServerOptionsBuilder().WithDefaultEndpoint().Build());
await server.StartAsync();
await server.StopAsync();

// Client
var cf = new MqttClientFactory();
using var client = cf.CreateMqttClient();
var opts = new MqttClientOptionsBuilder()
    .WithTcpServer("localhost", 1883)
    .WithClientId("unique-id")
    .Build();

// ⚠ handler 要在 Connect 之前
client.ApplicationMessageReceivedAsync += e =>
{
    var text = e.ApplicationMessage.ConvertPayloadToString();   // v5；回傳 string?
    return Task.CompletedTask;
};

await client.ConnectAsync(opts, ct);

// 訂閱
await client.SubscribeAsync(
    cf.CreateSubscribeOptionsBuilder()
      .WithTopicFilter("a/b/#")
      .WithAtLeastOnceQoS()
      .Build(),
    ct);

// 發布
await client.PublishAsync(
    new MqttApplicationMessageBuilder()
        .WithTopic("a/b")
        .WithPayload("25.5")
        .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
        .Build(),
    ct);

await client.DisconnectAsync();
```

---

## 練習

1. 把 QoS 改成 0 和 2，觀察封包和行為差異（用 MQTT Explorer 抓）。
2. 加一個 retained message：發布時設 `WithRetainFlag(true)`，然後**重新連線**看會不會立刻收到。
3. 改用 MQTTnet 的 `InterceptingPublishAsync` 在 broker 端攔截並記錄所有訊息。
4. 實作斷線重連：指數退避，上限 30 秒，連上後重設延遲。
5. 實作 LWT（Last Will and Testament）：client 異常斷線時自動發布「離線」訊息，正常斷線則發布「上線」。
6. 加一個 `BackgroundService`，用 `Channel<T>` 把收到的 MQTT 訊息送進採集處理流程（銜接 [`day2/02-channels-and-collector.md`](../day2/02-channels-and-collector.md)）。
7. 加 ACL：讓 broker 拒絕訂閱 `#` 的 client（用 `InterceptingSubscriptionAsync`）。
