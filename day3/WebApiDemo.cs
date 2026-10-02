// HttpClient 在 System.Net.Http —— console 專案的 ImplicitUsings 有包含這個。
// GetFromJsonAsync / PostAsJsonAsync 是 System.Net.Http.Json 的擴充方法，要自己 using。
// 離線 fallback 用的 HttpListener 在 System.Net。
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace CSharpLearn.Day3;

// Day 3 第四支範例：HttpClient 與 System.Text.Json。
//
// 這個範例**優先打公開的測試 API**（https://jsonplaceholder.typicode.com），讓你看到真實的
// HTTP 行為。若連不上（離線、被防火牆擋、網路中斷），會自動改用程式內開在本機的
// HttpListener 假 API 跑完全相同的流程 —— 所以離線也跑得起來，輸出格式一致。

public static class WebApiDemo
{
    // 公開的測試 API，不需要註冊。
    private const string ExternalBaseUrl = "https://jsonplaceholder.typicode.com";

    public static async Task RunAsync(CancellationToken ct)
    {
        Console.WriteLine("--- HttpClient ---");
        Console.WriteLine();

        await RunAgainstExternalOrFallbackAsync(ct);
    }

    private static async Task RunAgainstExternalOrFallbackAsync(CancellationToken ct)
    {
        // 先試外部站台。給它 20 秒：很多網路下第一次連線會先嘗試 IPv6 再 fallback 到 IPv4，
        // 這段來回可能吃掉好幾秒，timeout 設太短會誤判成「連不上」而永遠走不到外部路徑。
        using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        probeCts.CancelAfter(TimeSpan.FromSeconds(20));

        try
        {
            using var probe = new HttpClient
            {
                BaseAddress = new Uri(ExternalBaseUrl),
                Timeout = TimeSpan.FromSeconds(20),
            };

            using var resp = await probe.GetAsync("/posts/1", probeCts.Token);
            resp.EnsureSuccessStatusCode();

            Console.WriteLine($"  使用外部 API: {ExternalBaseUrl}");
            Console.WriteLine();

            // 外部可用 → 用外部跑完整流程。
            using var client = CreateClient(new Uri(ExternalBaseUrl));
            await RunLessonAsync(client, ct);
            return;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            Console.WriteLine($"  外部 API 連不上（{ex.GetType().Name}），改用本機假 API。");
            Console.WriteLine();
        }

        // 外部不可用 → 在本機開一個假 API，內容跟 jsonplaceholder 一模一樣。
        await using var fake = FakeJsonPlaceholder.Start();

        using var localClient = CreateClient(fake.BaseAddress);
        await RunLessonAsync(localClient, ct);
    }

    private static HttpClient CreateClient(Uri baseAddress)
    {
        // ⚠ 重點 1：HttpClient 應該「重複使用同一個實例」。
        //
        //    為什麼不能在迴圈裡 new？
        //      - 每個 HttpClient 內部都會維護自己的連線池。一直 new 會讓 socket 被耗盡
        //        （SocketException: Address already in use）
        //      - DNS 名稱不會被重新解析，DNS 更新後會拿到舊 IP
        //
        //    console app 沒有 DI 容器，用一個 static 欄位就夠了。
        //    ASP.NET Core 請用 IHttpClientFactory（見本檔案下方說明）。
        //
        //    這裡因為是教學範例，整段結束就結束，所以用 using 沒問題；
        //    真正的長駐服務應該是 static readonly HttpClient，或交給 IHttpClientFactory 管理。
        //
        //    ⚠ UseProxy = false：HttpClient 預設會讀 http_proxy / https_proxy 環境變數。
        //    連 127.0.0.1 也會照樣走 proxy，結果本機假 API 反而連不上
        //    （"Connection refused (127.0.0.1:9)" 就是 proxy 埠沒服務）。
        //    打本機的服務時一定要記得關掉 proxy。
        var handler = new SocketsHttpHandler { UseProxy = false };

        var client = new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = baseAddress,
            Timeout = TimeSpan.FromSeconds(15),
        };

        // 送出前加一個自訂標頭，模擬真實系統的認證／追蹤
        client.DefaultRequestHeaders.Add("User-Agent", "CSharpLearn/1.0");
        return client;
    }

    private static async Task RunLessonAsync(HttpClient client, CancellationToken ct)
    {
        await GetWithRetryAsync(client, "/posts/1", ct);
        await PostJsonAsync(client, ct);
        await DeserializeListAsync(client, ct);
        await UseCancellationAsync(client, ct);
        await HandleErrorsAsync(client, ct);

        Console.WriteLine("=== HttpClient 重點 ===");
        Console.WriteLine("1. HttpClient 要重複使用，不要在迴圈裡 new");
        Console.WriteLine("2. 一律設定 Timeout（預設 100 秒，常常太久）");
        Console.WriteLine("3. 讀取用 GetAsync/PostAsync，不要用 .Result");
        Console.WriteLine("4. 傳 token 給 GetAsync，否則逾時時停不下來");
        Console.WriteLine("5. 4xx/5xx 不會丟例外，要自己檢查 IsSuccessStatusCode");
        Console.WriteLine("6. 序列化用 System.Text.Json，不要用 Newtonsoft.Json（除非既有相依需要）");
    }

    // ── 1. GET ────────────────────────────────────────────────────────
    private static async Task GetWithRetryAsync(HttpClient client, string path, CancellationToken ct)
    {
        Console.WriteLine("--- 1. GET 與簡單重試 ---");

        var response = await client.GetAsync(path, ct);

        // ⚠ 重點 2：4xx / 5xx 不會丟例外，必須自己檢查。
        //    忘記檢查的話，你會把「404 的 HTML 錯誤頁」當成成功的資料解析。
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"  HTTP {(int)response.StatusCode} {response.StatusCode}");
            return;
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        Console.WriteLine($"  取得 {json.Length} bytes，開頭: {json[..Math.Min(80, json.Length)]}");
        Console.WriteLine();

        // 重試要自己寫。逾時（408）、暫時性的 5xx 通常值得重試，
        // 但 4xx（除了 429）是永久性錯誤，重試沒用只會浪費時間。
        Console.WriteLine("  重試原則:");
        Console.WriteLine("    408 / 429 / 5xx → 通常值得重試（用指數退避 + 抖動）");
        Console.WriteLine("    其他 4xx         → 永久性錯誤，重試沒有用");
        Console.WriteLine();
    }

    // ── 2. POST JSON ──────────────────────────────────────────────────
    private static async Task PostJsonAsync(HttpClient client, CancellationToken ct)
    {
        Console.WriteLine("--- 2. POST JSON ---");

        // 物件會自動用 System.Text.Json 序列化。
        // 預設屬性名稱策略是「保持原樣」(PropertyNameCaseInsensitive = true 給反序列化)，
        // 所以 Post 出去的 JSON 欄位名是你宣告的 C# 屬性名。
        var payload = new CreatePostRequest
        {
            Title = "csharp_learn",
            Body = "每日 24 小時的學習筆記",
            UserId = 1,
        };

        // PostAsJsonAsync 是擴充方法（在 System.Net.Http.Json）
        var response = await client.PostAsJsonAsync("/posts", payload, ct);

        Console.WriteLine($"  POST /posts → HTTP {(int)response.StatusCode}");

        if (response.IsSuccessStatusCode)
        {
            var created = await response.Content.ReadFromJsonAsync<Post>(ct);
            Console.WriteLine($"  伺服器回傳 id={created?.Id}, title={created?.Title}");
        }

        Console.WriteLine();
    }

    // ── 3. 反序列化 ───────────────────────────────────────────────────
    private static async Task DeserializeListAsync(HttpClient client, CancellationToken ct)
    {
        Console.WriteLine("--- 3. 反序列化清單 ---");

        // 型別參數是必要的：ReadFromJsonAsync 無法從 return 型別推斷。
        var posts = await client.GetFromJsonAsync<List<Post>>("/posts?_limit=3", ct);

        if (posts is null)
        {
            Console.WriteLine("  沒有資料");
            return;
        }

        foreach (var post in posts)
        {
            Console.WriteLine($"  [{post.Id,3}] {post.Title}");
        }

        Console.WriteLine();
        Console.WriteLine("  重點：只要 JSON 裡的欄位名符合 C# 屬性名，");
        Console.WriteLine("        沒在 JSON 裡的欄位會被忽略，多出來的欄位也會被忽略。");
        Console.WriteLine("  所以「多接收」預設是安全的，不必先定義完整的模型。");
        Console.WriteLine();
    }

    // ── 4. 取消 ───────────────────────────────────────────────────────
    private static async Task UseCancellationAsync(HttpClient client, CancellationToken ct)
    {
        Console.WriteLine("--- 4. 逾時與取消 ---");

        // ⚠ HttpClient.Timeout（預設 100 秒）和 CancellationToken 是兩種不同的逾時：
        //    Timeout    給「我不管怎樣都不管理等超過這裡」
        //    Token      給「使用者關掉視窗 / 應用程式關閉 / 上層要求停止」
        //    實務上兩者都要有。

        using var shortTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        try
        {
            // 本機假 API 會刻意延遲 5 秒，所以這裡必然逾時，能穩定示範取消行為。
            await client.GetAsync("/posts/1?slow=1", shortTimeout.Token);
            Console.WriteLine("  讀取完成");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Console.WriteLine("  因為 CancellationToken 被取消而放棄（不是外部取消，是我們自己設的 timeout）");
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"  連線失敗: {ex.Message}");
            Console.WriteLine("  離線環境跑這個範例會走到這裡 —— 那是預期行為。");
        }

        Console.WriteLine();
    }

    // ── 5. 錯誤處理 ───────────────────────────────────────────────────
    private static async Task HandleErrorsAsync(HttpClient client, CancellationToken ct)
    {
        Console.WriteLine("--- 5. 錯誤處理 ---");

        try
        {
            // 這個路徑不存在 → 404
            var response = await client.GetAsync("/this-does-not-exist", ct);
            Console.WriteLine($"  HTTP {(int)response.StatusCode} {response.StatusCode}");
            Console.WriteLine("  ⚠ 注意：這裡沒有丟例外。一定要自己檢查 IsSuccessStatusCode。");

            // 真的想對非 2xx 丟例外，開這個選項：
            //   await client.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, ct);
            //   .EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"  HttpRequestException: {ex.Message}");
        }
        catch (TaskCanceledException ex)
        {
            Console.WriteLine($"  TaskCanceledException（多半是逾時）: {ex.Message}");
        }

        Console.WriteLine();
        Console.WriteLine("  例外對照:");
        Console.WriteLine("    HttpRequestException     網路層問題（DNS 失敗、連線被拒、TLS 失敗）");
        Console.WriteLine("    TaskCanceledException    逾時或取消（繼承自 OperationCanceledException）");
        Console.WriteLine("    OperationCanceledException  被取消");
        Console.WriteLine();
    }
}

/// <summary>
/// 程式內的假 JSONPlaceholder API。外部站台連不上時接手，讓範例離線也能跑完。
///
/// 為什麼值得寫這個：
///   - HttpListener 是 BCL 內建的，不需要任何套件。
///   - 可以驗證 HttpClient 那一側的程式碼完全正確 —— 換掉的是「對端」而不是「客戶端」。
///   - 真實專案裡做整合測試就是這個套路（WireMock、TestServer 都同一個思路）。
/// </summary>
internal sealed class FakeJsonPlaceholder : IAsyncDisposable
{
    private readonly HttpListener _listener = new();

    private FakeJsonPlaceholder(Uri baseAddress)
    {
        BaseAddress = baseAddress;
    }

    public Uri BaseAddress { get; }

    /// <summary>在 127.0.0.1 上找一個可用的 port，開一個假 API，回傳它的控制物件。</summary>
    public static FakeJsonPlaceholder Start()
    {
        var port = FindFreePort();
        var baseAddress = new Uri($"http://127.0.0.1:{port}/");

        var fake = new FakeJsonPlaceholder(baseAddress);
        fake._listener.Prefixes.Add(baseAddress.ToString());
        fake._listener.Start();

        // 背景執行緒處理請求。BeginGetContext 是同步的，用 Task.Run 讓它不佔住主流程。
        _ = Task.Run(() => fake.ServeAsync());

        Console.WriteLine($"  本機假 API 已啟動: {baseAddress}");
        Console.WriteLine();

        return fake;
    }

    private static int FindFreePort()
    {
        // 綁一個 port 0 讓 OS 挑一個沒人用的，然後立刻放掉。
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private async Task ServeAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;

            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (HttpListenerException)
            {
                return;   // listener 已關閉
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            try
            {
                await HandleAsync(context);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [fake api] 處理失敗: {ex.Message}");
            }
        }
    }

    private static async Task HandleAsync(HttpListenerContext context)
    {
        var path = context.Request.Url?.AbsolutePath ?? "/";
        var query = context.Request.Url?.Query ?? string.Empty;

        // /posts/1?slow=1 → 刻意延遲 5 秒，用來示範 HttpClient 的逾時與取消。
        if (query.Contains("slow=1", StringComparison.Ordinal))
        {
            await Task.Delay(TimeSpan.FromSeconds(5));
        }

        if (path == "/posts" && context.Request.HttpMethod == "POST")
        {
            using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
            var body = await reader.ReadToEndAsync();

            var echo = new Post
            {
                Id = 101,
                UserId = 1,
                Title = "csharp_learn",
                Body = body,
            };

            await WriteJsonAsync(context, HttpStatusCode.Created, echo);
            return;
        }

        if (path.StartsWith("/posts/", StringComparison.Ordinal))
        {
            var idText = path["/posts/".Length..];
            var id = int.TryParse(idText, out var parsed) ? parsed : 0;

            var post = new Post
            {
                Id = id,
                UserId = 1,
                Title = $"sunt aut facere {id}",
                Body = "quia et suscipit suscipit recusandae consequuntur expedita et cum",
            };

            await WriteJsonAsync(context, HttpStatusCode.OK, post);
            return;
        }

        if (path == "/posts")
        {
            var list = Enumerable.Range(1, 3).Select(i => new Post
            {
                Id = i,
                UserId = 1,
                Title = $"sunt aut facere repellat provisione sed quia {i}",
                Body = "quia et suscipit suscipit recusandae consequuntur expedita et cum",
            }).ToList();

            await WriteJsonAsync(context, HttpStatusCode.OK, list);
            return;
        }

        // 其餘一律 404 —— 讓第 5 節的錯誤處理示範有東西可以印。
        context.Response.StatusCode = (int)HttpStatusCode.NotFound;
        var bytes = Encoding.UTF8.GetBytes("{\"error\":\"not found\"}");
        context.Response.ContentType = "application/json";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }

    private static async Task WriteJsonAsync(HttpListenerContext context, HttpStatusCode status, object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);

        context.Response.StatusCode = (int)status;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;

        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }

    public ValueTask DisposeAsync()
    {
        if (_listener.IsListening)
        {
            _listener.Stop();
            _listener.Close();
        }

        return ValueTask.CompletedTask;
    }
}

// ── 資料模型 ────────────────────────────────────────────────────────────

/// <summary>送出用的請求模型。</summary>
internal sealed class CreatePostRequest
{
    public string Title { get; set; } = "";

    public string Body { get; set; } = "";

    public int UserId { get; set; }
}

/// <summary>
/// 接收用的回應模型。
///
/// 注意：宣告成 sealed 而且沒有建構式。
/// System.Text.Json 需要「無參數建構式」或 [JsonConstructor] 標示的建構式。
/// sealed 沒關係，但屬性要有 setter 或可用的建構式。
/// </summary>
internal sealed class Post
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public string Title { get; set; } = "";

    public string Body { get; set; } = "";
}