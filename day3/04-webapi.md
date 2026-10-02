# Day 3 — HttpClient 與 System.Text.Json

> 範例：[`WebApiDemo.cs`](WebApiDemo.cs)
> 跑法：`dotnet run --project day3 -- webapi`

✅ 已用 .NET 10 SDK 編譯並實跑驗證過。會先試 `jsonplaceholder.typicode.com`；連不上（離線、被防火牆擋）
時會自動改用程式內的 `HttpListener` 假 API 跑完相同流程，**兩條路徑輸出格式一致**。

---

## 1. HttpClient

### ⚠ 重點 1：不要在迴圈裡 `new HttpClient()`

這是 .NET 裡最常見的效能錯誤之一。

```csharp
// ❌ 千萬不要
foreach (var url in urls)
{
    using var client = new HttpClient();     // 每次都 new！
    await client.GetStringAsync(url);
}
```

**為什麼會出事：**

- `HttpClient` 內部有**自己的連線池**和 DNS 快取。`Dispose()` 會拋棄整個池
- 大量建立 → **socket 被耗盡** → `SocketException: Address already in use`
  （Windows 上尤其明顯，預設暫用 port 範圍只有 ~16000 個）
- DNS 名稱不會重新解析 → **DNS 更新（負載平衡）後永遠拿到舊 IP**

### 正確做法

```csharp
// console app：static 欄位
private static readonly HttpClient Client = new()
{
    BaseAddress = new Uri("https://api.example.com"),
    Timeout = TimeSpan.FromSeconds(15),
};

// ASP.NET Core：用 IHttpClientFactory
services.AddHttpClient("plc", c => c.BaseAddress = new Uri("https://..."))
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),   // 定期換連線，配合 DNS
        });
```

**為什麼要有 `PooledConnectionLifetime`？** .NET Core 3.0+ 預設是 **∞**，也就是連線永不被回收。DNS 更新後就抓不到新 IP。設定一個合理的存活時間（5–15 分鐘）可以解決。

### ⚠ 重點 2：一定要設 Timeout

**預設是 100 秒。** 幾乎所有情況都太久。10–30 秒比較合理。

不過要注意 `HttpClient.Timeout` 有個限制：**`GetAsync` 加上 timeout 會和 `CancellationToken` 互動**。要精確控制就用外部的 `CancellationTokenSource`，不要只靠 `Timeout`。

---

## 2. 呼叫

```csharp
// GET
var response = await client.GetAsync(path, ct);
var text = await response.Content.ReadAsStringAsync(ct);

// 簡捷寫法（直接拿內容，內部會 EnsureSuccessStatusCode）
var text2 = await client.GetStringAsync(path, ct);

// POST JSON
var response = await client.PostAsJsonAsync("/posts", payload, ct);

// 讀 header
response.Headers.TryGetValues("X-Request-Id", out var ids);

// 判斷成功
if (!response.IsSuccessStatusCode) { ... }
```

### ⚠ 重點 3：4xx / 5xx 不會丟例外

**這是最容易忘記的一件事。**

```csharp
var response = await client.GetAsync("/not-found", ct);
var text = await response.Content.ReadAsStringAsync(ct);

// 如果你忘了檢查 IsSuccessStatusCode，
// text 就是「404 錯誤頁的 HTML」，你會拿它去解析 JSON，然後收到莫名其妙的例外。
```

**永遠先檢查 `IsSuccessStatusCode`**，或者明確呼叫 `EnsureSuccessStatusCode()`：

```csharp
response.EnsureSuccessStatusCode();   // 非 2xx 就丟 HttpRequestException
```

### 例外對照

| 例外 | 什麼時候 |
|---|---|
| `HttpRequestException` | 網路層失敗（DNS、連線被拒、TLS、**以及 4xx/5xx 若呼叫了 `EnsureSuccessStatusCode`**） |
| `TaskCanceledException` | 逾時（繼承自 `OperationCanceledException`） |
| `OperationCanceledException` | 被取消 |
| `JsonException` | JSON 格式錯誤 |

```csharp
try
{
    var response = await client.GetAsync(path, ct);
    response.EnsureSuccessStatusCode();
    var data = await response.Content.ReadFromJsonAsync<MyModel>(ct);
}
catch (HttpRequestException ex) { /* 網路問題 */ }
catch (TaskCanceledException ex) { /* 逾時 */ }
catch (JsonException ex) { /* 格式錯誤 */ }
```

⚠ **catch 順序有意義**：`TaskCanceledException` 繼承自 `OperationCanceledException`，所以要放在 `OperationCanceledException` **之前**。

---

## 3. 取消與逾時

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
await client.GetAsync(path, cts.Token);
```

**兩種逾時用途不同：**

| | 用途 |
|---|---|
| `HttpClient.Timeout` | 「我不管怎樣都不該等超過這裡」 |
| `CancellationToken` | 「使用者關掉視窗 / 應用程式關閉 / 上層要求停止」 |

**實務上兩者都要有。** 外部的取消（關視窗）用 token，內部的保護用 Timeout。

判斷是哪一種逾時：

```csharp
catch (OperationCanceledException) when (ct.IsCancellationRequested)
{
    // 上層要求停止 → 乾淨退出
}
catch (TaskCanceledException)
{
    // 自己的 timeout 到了
}
```

---

## 4. 重試

**`HttpClient` 沒有內建重試。** 要自己寫：

```csharp
private static async Task<T> GetWithRetryAsync<T>(
    HttpClient client, string path, int maxAttempts = 3, CancellationToken ct = default)
{
    var delay = TimeSpan.FromMilliseconds(500);

    for (var attempt = 1; ; attempt++)
    {
        try
        {
            var response = await client.GetAsync(path, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<T>(ct)
                   ?? throw new InvalidOperationException("回應內容為空");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && attempt < maxAttempts)
        {
            Console.Error.WriteLine($"第 {attempt} 次失敗：{ex.Message}，{delay.TotalSeconds} 秒後重試");

            await Task.Delay(delay, ct);
            delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * 2);   // 指數退避
        }
    }
}
```

### 什麼該重試

| 狀況 | 重試？ |
|---|---|
| 408 Request Timeout | ✅ |
| 429 Too Many Requests | ✅ **要讀 `Retry-After` header** |
| 500 / 502 / 503 / 504 | ✅ |
| 其他 4xx | ❌ 永久性錯誤，重試只是浪費 |
| 連線失敗 | ✅ |
| `OperationCanceledException` | ❌ **不要重試取消** |

⚠ **POST 要小心**。重試 POST 可能造成重複建立資源（重複下單、重複寫入）。**只有確認 idempotent 的操作才自動重試。**

⚠ **加隨機抖動**（jitter）避免大量 client 同時重連把服務打死。

---

## 5. System.Text.Json

.NET Core 3.0+ 內建，不需要套件。**不要再用 `Newtonsoft.Json`**，除非既有相依需要。

### 序列化

```csharp
// 擴充方法（在 System.Net.Http.Json）
await client.PostAsJsonAsync("/posts", new CreatePostRequest { ... }, ct);

// 直接用
var json = JsonSerializer.Serialize(obj);
var bytes = JsonSerializer.SerializeToUtf8Bytes(obj);       // 少一次編碼
```

### 反序列化

```csharp
// 擴充方法
var post = await response.Content.ReadFromJsonAsync<Post>(ct);        // 單一物件
var posts = await response.Content.ReadFromJsonAsync<List<Post>>(ct); // 集合
```

⚠ **泛型參數一定要寫。** `ReadFromJsonAsync` 無法從回傳型別推斷，要嘛寫明確型別，要嘛用 `var`（讓它從指派目標推斷 —— 這個要 `var` 才行）。

### 命名策略

預設是 **PascalCase 原樣輸出**（不做 camelCase 轉換）：

```csharp
public sealed class Post
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
}

// 序列化 → {"Id":1,"Title":"..."}        ← PascalCase
```

如果要 camelCase：

```csharp
// 全域設定
JsonSerializerOptions.Default.JsonSerializerOptions =
    new JsonSerializerOptions(JsonSerializerDefaults.Web);   // camelCase + 忽略大小寫
```

⚠ **實務上 API 幾乎都用 camelCase。** 讀別人的 API 時建議直接用 `JsonSerializerDefaults.Web`。

反序列化時 `PropertyNameCaseInsensitive` 預設是 `false`（`ReadFromJsonAsync` 預設開啟），所以 `{"id": 1}` 也能對到 `Id`。但 `JsonSerializer.Serialize` 不會自動轉小寫。

### 大小寫、缺欄位、多欄位

| 情況 | 行為 |
|---|---|
| JSON 欄位名和 C# 屬性名大小寫不同 | `ReadFromJsonAsync` 可行；`JsonSerializer.Deserialize` 預設不行 |
| JSON 少某個欄位 | 忽略，用屬性的預設值 |
| JSON 多出某個欄位 | 忽略 |

**所以多接收是安全的** — 你不需要先定義完整的模型。但「少接收」不行：欄位名拼錯會靜默地變成預設值，沒有任何警告。

⚠ **這個坑很危險**：屬性名打錯 → 每次都拿到預設值 → 資料看起來「都是 0」。加上測試或驗證。

### AOT / Trimmed 專案

**反射式的序列化在 Native AOT 或裁剪後會失效。** 解法是 source generator：

```xml
<PropertyGroup>
  <PublishTrimmed>true</PublishTrimmed>
</PropertyGroup>
```

```csharp
[JsonSerializable(typeof(Post))]
internal partial class AppJsonContext : JsonSerializerContext;

// 用法
var json = JsonSerializer.Serialize(post, AppJsonContext.Default.Post);
var obj = JsonSerializer.Deserialize(json, AppJsonContext.Default.Post);

// HttpClient
await client.PostAsJsonAsync("/posts", payload, AppJsonContext.Default.CreatePostRequest, ct);
```

詳見 [`00-tooling.md`〈發佈〉](../00-tooling.md#發佈)。

---

## 6. Minimal API 概覽

工作上如果碰到 Web API，ASP.NET Core 的 Minimal API 是最輕量的做法：

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient();                    // ← 內建 IHttpClientFactory
builder.Services.AddHostedService<CollectorWorker>();  // 背景服務（銜接 day2）

var app = builder.Build();

app.MapGet("/sensors", async (IHttpClientFactory factory, CancellationToken ct) =>
{
    var client = factory.CreateClient();
    return await client.GetFromJsonAsync<Sensor[]>("/api/sensors", ct);
});

app.MapPost("/sensors", async (Sensor sensor) => Results.Created($"/sensors/{sensor.Id}", sensor));

app.Run();
```

幾個重點：

- **`IHttpClientFactory` 內建** — 不用自己管重複使用問題
- **`AddHostedService` + `BackgroundService`** — 長時間背景工作的標準做法，`ExecuteAsync` 的 token 在關閉時會被取消
- **`app.MapGet` 的第二個參數就是 DI 容器** — 直接宣告參數就有，不用 `[FromServices]`
- 整個 API 可以是**一個檔案、幾十行**

銜接：[`day2/02-channels-and-collector.md`](../day2/02-channels-and-collector.md) 有 `BackgroundService` + `Channel` 的實際寫法。

---

## 速查

```csharp
// 設定
var client = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(15) };

// GET
var response = await client.GetAsync(path, ct);
response.EnsureSuccessStatusCode();
var text = await response.Content.ReadAsStringAsync(ct);

// JSON
var one = await response.Content.ReadFromJsonAsync<Post>(ct);
var many = await response.Content.ReadFromJsonAsync<List<Post>>(ct);
await client.PostAsJsonAsync("/posts", new Post { ... }, ct);

// 檢查
if (!response.IsSuccessStatusCode) { /* 4xx/5xx 不會自己丟 */ }
if (response.Headers.TryGetValues("X-Id", out var ids)) { }

// 逾時 / 取消
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
await client.GetAsync(path, cts.Token);

// 例外
try { }
catch (TaskCanceledException) { }        // 逾時（要在 OperationCanceledException 之前）
catch (HttpRequestException) { }
catch (OperationCanceledException) { }
catch (JsonException) { }

// AOT / Trimmed
[JsonSerializable(typeof(Post))] partial class Ctx : JsonSerializerContext;
```

---

## 練習

1. 把 `HttpClient` 改成在迴圈裡建立，用 `/proc/net/tcp`（Linux）或 `netstat`（Windows）觀察 socket 數量變化 —— 親眼看到「為什麼不能這樣寫」。
2. 實作 `GetWithRetryAsync`：指數退避 + 隨機抖動，只對 408/429/5xx 重試，且不對 `OperationCanceledException` 重試。
3. 讀一個回傳陣列的 API，用 `ImmutableArray<T>` 接，體會不可變集合的好處。
4. 加一個 `record` 型別當 API 回應模型，並示範 `with` 表達式做局部更新。
5. 串接兩個 API：第一個的結果當第二個的參數，處理中間失敗的情況。
6. 加一個整合測試（`WebApplicationFactory` + `HttpClient`），測 Minimal API 的 endpoint。
7. 把 WebApiDemo 的模型改用 source generator，觀察和反射式的差別。
