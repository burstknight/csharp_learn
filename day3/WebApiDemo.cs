// HttpClient 在 System.Net.Http —— console 專案的 ImplicitUsings 有包含這個。
// GetFromJsonAsync / PostAsJsonAsync 是 System.Net.Http.Json 的擴充方法，要自己 using。
using System.Net.Http.Json;

namespace CSharpLearn.Day3;

// Day 3 第四支範例：HttpClient 與 System.Text.Json。
//
// ⚠ 這個範例需要網路連線才能完整跑完。離線時它會印出錯誤訊息然後結束 —— 那是預期行為。
//    想離線驗證，可以把 BaseUrl 換成自己開的本機服務。

public static class WebApiDemo
{
    // 公開的測試 API，不需要註冊。
    private const string BaseUrl = "https://jsonplaceholder.typicode.com";

    public static async Task RunAsync(CancellationToken ct)
    {
        Console.WriteLine("--- HttpClient ---");
        Console.WriteLine();

        // ⚠ 重點 1：HttpClient 應該「重複使用同一個實例」。
        //
        //    為什麼不能在迴圈裡 new？
        //      - 每個 HttpClient 內部都會維護自己的連線池。一直 new 會讓 socket 被耗盡
        //        （SocketException: Address already in use）
        //      - DNS 名稱不會被重新解析，DNS 更新後會拿到舊 IP
        //
        //    console app 沒有 DI 容器，用一個 static 欄位就夠了。
        //    ASP.NET Core 請用 IHttpClientFactory（見本檔案下方說明）。
        using var client = new HttpClient
        {
            BaseAddress = new Uri(BaseUrl),
            Timeout = TimeSpan.FromSeconds(15),
        };

        // 送出前加一個自訂標頭，模擬真實系統的認證／追蹤
        client.DefaultRequestHeaders.Add("User-Agent", "CSharpLearn/1.0");

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
        //    Timeout    給「我不管怎樣都不該等超過這裡」
        //    Token      給「使用者關掉視窗 / 應用程式關閉 / 上層要求停止」
        //    實務上兩者都要有。

        using var shortTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        try
        {
            // 這個 API 在正式環境通常很快。用 1 秒的 token 示範「來不及讀完就放棄」。
            await client.GetAsync("/posts/1", shortTimeout.Token);
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
