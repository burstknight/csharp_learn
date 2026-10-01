// ImplicitUsings 有開，System / System.Linq / System.Threading.Tasks 不用重複 using。
// 但 System.Globalization 不在隱含清單裡，要自己加。
using System.Globalization;

namespace CSharpLearn.Day1;

// 這個檔案是 Day 1 的第一支範例，專講 C# 特有語法。
// 對照族群是 C++ / Python，所以重點都放在「跟它們不一樣」的地方。
//
// ⚠ 重要：這支範例刻意保留了幾處會產生編譯警告的寫法（反例），
//    每一處上面都有標明警告代碼的註解。請照著 01-csharp-syntax.md 把它們修掉。
//
// ⚠ 本檔案未經編譯驗證（撰寫環境沒有 .NET SDK）。第一次 build 看到的警告數量
//    可能比這裡標註的多，先讀懂再改。

/// <summary>C# 特有語法重點。</summary>
public static class SyntaxDemo
{
    // struct 是「值型別」：賦值、傳參都會複製一份。
    private struct PointValue(int x, int y)
    {
        public int X { get; set; } = x;

        public int Y { get; set; } = y;

        public override string ToString() => $"({X}, {Y})";
    }

    // class 是「參考型別」：傳的是「那個物件的參考」。
    private sealed class PointRef
    {
        public int X { get; set; }

        public int Y { get; set; }

        public override string ToString() => $"({X}, {Y})";
    }

    // record 自動產生 Equals / GetHashCode / ToString / with 運算式。
    // 拿來表示「一筆不可變資料」比 class 省很多事。
    private sealed record TagReading(string Name, string Unit, double Value);

    // C# 12：一般類別也能用 primary constructor。
    private sealed class ModbusGateway(string host, int port, byte unitId)
    {
        public string Host { get; } = host;

        public int Port { get; } = port;

        public byte UnitId { get; } = unitId;

        public string Describe() => $"{Host}:{Port} (unit {UnitId})";
    }

    public static Task RunAsync(CancellationToken ct)
    {
        Section1_ValueVsReference();
        Section2_Nullable();
        Section3_PatternMatching();
        Section4_CollectionExpressions();
        Section5_RecordsAndWith();
        Section6_PrimaryConstructorAndRanges();

        return Task.CompletedTask;
    }

    // ── 1. 值型別 vs 參考型別 ────────────────────────────────────────────
    private static void Section1_ValueVsReference()
    {
        Console.WriteLine("--- 1. struct（值型別） vs class（參考型別）---");

        var byValue = new PointValue(1, 2);
        BumpStruct(byValue);
        Console.WriteLine($"struct 傳進 method 後: {byValue}    ← 沒變，method 改到的是副本");

        var byRef = new PointRef { X = 1, Y = 2 };
        BumpClass(byRef);
        Console.WriteLine($"class  傳進 method 後: {byRef}    ← 變了，傳的是參考");

        // 對照 C++：C++ 的 struct 預設傳參也是拷貝，但 C++ 可以用 & 或引用參數。
        // C# 沒有指標語法，要讓 method 改到呼叫端的 struct，必須明寫 ref。
        BumpStructByRef(ref byValue);
        Console.WriteLine($"struct 用 ref 傳遞後: {byValue}    ← 這次有變");

        Console.WriteLine($"預設值型別 ToString: {new PointValue(3, 4)}（struct 預設印出型別名，你覆寫了它）");
    }

    private static void BumpStruct(PointValue point) => point.X = 99;

    private static void BumpClass(PointRef point) => point.X = 99;

    private static void BumpStructByRef(ref PointValue point) => point.X = 99;

    // ── 2. Nullable 參考型別 ───────────────────────────────────────────
    private static void Section2_Nullable()
    {
        Console.WriteLine();
        Console.WriteLine("--- 2. Nullable 參考型別 ---");

        // 反例 1：可能回傳 null，卻賦給「宣告為不可空」的變數。
        // ⚠ CS8600 取得 humidity 的值時可能為 null
        string humidity = FindTag("humidity");
        Console.WriteLine($"humidity = {humidity ?? "（查無此 tag）"}");

        // 修法：型別就說清楚「這裡可能沒有值」。
        string? humidityOk = FindTag("humidity");
        Console.WriteLine($"humidityOk = {humidityOk ?? "（查無此 tag）"}");

        string? maybeName = FindTag("temperature");

        // 反例 2：宣告為可空，卻直接取成員。
        // ⚠ CS8602 對可能為 null 的參考 'maybeName' 取成員（如 'Length'）
        Console.WriteLine($"temperature 長度 = {maybeName.Length}");

        // 修法一：型別檢查。除了消掉警告，這同時是一種執行期安全檢查，
        //        搭配 is not null 讓「到這裡一定不是 null」變成編譯器可以驗證的事實。
        if (maybeName is not null)
        {
            Console.WriteLine($"temperature 長度 = {maybeName.Length}");
        }

        // 修法二：null 傳播（?.）。左邊是 null 時整串短路，結果是 null。
        //        注意：直接插值進字串會印出空白，不容易發現問題。
        Console.WriteLine($"temperature 長度 = {maybeName?.Length}");

        // 修法三：null 傳播 + null 合併（??）。給一個明確的替代值，輸出不會有歧義。
        Console.WriteLine($"temperature 長度 = {maybeName?.Length ?? -1}");

        // 其他常用運算子
        string port = null!;
        port ??= "502";                                  // 為 null 才賦值
        Console.WriteLine($"port 預設值 = {port}");
        Console.WriteLine($"空字串也要當「沒有值」: {(string.IsNullOrEmpty(humidityOk) ? "<無>" : humidityOk)}");
    }

    /// <summary>查 tag 名稱。查不到就回傳 null——這在真實系統非常常見。</summary>
    private static string? FindTag(string name) =>
        name == "temperature" ? "Line1.Temp" : null;

    // ── 3. switch 運算式 + 樣式比對 ─────────────────────────────────────
    private static void Section3_PatternMatching()
    {
        Console.WriteLine();
        Console.WriteLine("--- 3. switch 運算式 + 樣式比對 ---");

        // 對照 C++ 的 std::variant 配對、C 的 switch。
        // C# 的樣式比對是把「型別」和「內容」一起比，寫起來比前兩者短很多。
        object?[] values = [42, 250, "hello", "", 3.5d, null, true];

        foreach (var value in values)
        {
            Console.WriteLine($"  {value,-8} → {Describe(value)}");
        }
    }

    private static string Describe(object? value) => value switch
    {
        null => "沒有值",

        // when 加上額外條件
        int i when i > 100 => $"大整數 {i}",
        int i => $"整數 {i}",

        // 巢狀樣式：型別 + 屬性條件
        string { Length: > 0 } s => $"非空字串：{s}",
        string => "空字串",

        double => "浮點數",

        // 預設分支。上面已擋掉 null，所以這裡不必再檢查 value。
        _ => "其他型別",
    };

    // ── 4. 集合運算式（collection expressions, C# 12）──────────────────
    private static void Section4_CollectionExpressions()
    {
        Console.WriteLine();
        Console.WriteLine("--- 4. 集合運算式（collection expressions, C# 12）---");

        // C# 12 以前要明寫型別：
        List<int> oldList = new List<int> { 1, 2, 3 };
        int[] oldArray = new int[] { 1, 2, 3 };

        // C# 12 之後可以省略型別名稱，編譯器從左邊的目標型別推斷：
        List<int> newList = [1, 2, 3];
        int[] newArray = [1, 2, 3];
        Span<int> span = [1, 2, 3];        // Span 不配置到堆積，適合高頻迴圈
        List<int> empty = [];

        // 展開（spread）：把既有集合的元素攤進新集合
        int[] extended = [.. newArray, 99, 100];

        // 巢狀集合：對應到二維（jagged）陣列
        int[][] jagged =
        [
            [1, 2],
            [3, 4, 5],
        ];

        Console.WriteLine($"oldList = {string.Join(", ", oldList)}");
        Console.WriteLine($"newList = {string.Join(", ", newList)}  ← 寫法不同，結果相同");
        Console.WriteLine($"newArray = {string.Join(", ", newArray)}");
        Console.WriteLine($"span     = {string.Join(", ", span.ToArray())}");
        Console.WriteLine($"empty.Count = {empty.Count}");
        Console.WriteLine($"extended = {string.Join(", ", extended)}");
        Console.WriteLine($"jagged 長度 = {string.Join(" / ", jagged.Select(row => row.Length))}");
    }

    // ── 5. record、with 運算式、字串插值 ───────────────────────────────
    private static void Section5_RecordsAndWith()
    {
        Console.WriteLine();
        Console.WriteLine("--- 5. record 與 with 運算式 ---");

        // target-typed new：左邊已經宣告型別，右邊可以省略型別名稱
        TagReading temp = new("Line1.Temp", "°C", 25.5);
        TagReading humidity = new("Line1.Humidity", "%", 60.0);

        Console.WriteLine(temp);

        // record 自動產生 Equals / == ：欄位值一樣就視為相等。
        // 一般 class 要另外寫 Equals + GetHashCode，record 免寫。
        var sameAsTemp = new TagReading("Line1.Temp", "°C", 25.5);
        Console.WriteLine($"record ==  : {temp == sameAsTemp}  ← 內容相同就 true");
        Console.WriteLine($"class ==   : {ReferenceEquals(temp, sameAsTemp)} ← 這裡是 false（不同物件）");

        // with 運算式：不可變更新，產生「新」的記錄而不是改原來那筆
        var alarm = temp with { Value = 85.0 };
        Console.WriteLine($"原本: {temp}");
        Console.WriteLine($"with 後: {alarm}   ← 原來那筆沒被動到");

        // 字串插值
        Console.WriteLine($"預設格式: {temp.Value}");
        Console.WriteLine($"小數一位: {temp.Value:F1}");
        Console.WriteLine($"靠右對齊: {temp.Value,10:F2}|");
        Console.WriteLine($"format 用法: {temp.Value.ToString("F3", CultureInfo.InvariantCulture)}");
    }

    // ── 6. primary constructor、索引與範圍 ─────────────────────────────
    private static void Section6_PrimaryConstructorAndRanges()
    {
        Console.WriteLine();
        Console.WriteLine("--- 6. primary constructor、Index 與 Range ---");

        // C# 12 把 primary constructor 開放給一般類別，
        // 小型資料持有者（DTO、設定物件）不再需要寫一整組欄位 + ctor + 屬性。
        var gateway = new ModbusGateway(host: "192.168.1.10", port: 502, unitId: 1);
        Console.WriteLine(gateway.Describe());

        // Index（^1 = 倒數第一個）與 Range（[..3] = 從頭取 3 個）
        string text = "temperature";
        Console.WriteLine($"原文: {text}");
        Console.WriteLine($"最後一個字元: {text[^1]}");
        Console.WriteLine($"前三個字元:   {text[..3]}");
        Console.WriteLine($"第 7 個到結尾: {text[6..]}");
        Console.WriteLine($"反轉:         {string.Concat(text.Reverse())}");
    }
}