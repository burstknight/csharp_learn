// RuntimeInformation 不在 ImplicitUsings 裡。
using System.Runtime.InteropServices;

namespace CSharpLearn.Day4;

// Day 4 第二支範例：.NET 版本差異 + C# 語言版本差異。
//
// ⚠ 本檔案未經編譯驗證（撰寫環境沒有 .NET SDK）。
//
// 這支範例刻意示範「編譯期」和「執行期」是兩件事：
//   * TFM / LangVersion 決定編譯器允許你寫什麼語法
//   * RuntimeInformation 決定執行時實際跑在哪個 runtime 上

public static class VersionsDemo
{
    public static async Task RunAsync(CancellationToken ct)
    {
        Section1_RuntimeInfo();
        Section2_ConditionalCompilation();
        Section3_CollectionExpressions();
        Section4_PrimaryConstructor();
        await Section5_ParamsCollection(ct);
        Section6_BackingFieldKeyword();
        await Section7_NullConditionalAssignment();
        Section8_ExtensionMembers();

        Console.WriteLine("以上每個小節都標註了引入它的語言版本。");
        Console.WriteLine("同一份 csproj 換 TFM（net8.0 / net9.0 / net10.0）就會編譯失敗，");
        Console.WriteLine("因為舊的編譯器看不懂新語法。這就是為什麼要先確認版本。");
    }

    // ── 1. 執行期資訊 ────────────────────────────────────────────────
    private static void Section1_RuntimeInfo()
    {
        Console.WriteLine("--- 1. 執行期是什麼版本 ---");

        Console.WriteLine($"  FrameworkDescription: {RuntimeInformation.FrameworkDescription}");
        Console.WriteLine($"  Environment.Version:  {Environment.Version}");
        Console.WriteLine($"  OSDescription:        {RuntimeInformation.OSDescription}");
        Console.WriteLine($"  ProcessArchitecture:  {RuntimeInformation.ProcessArchitecture}");
        Console.WriteLine($"  ProcessorCount:       {Environment.ProcessorCount}");
        Console.WriteLine($"  Is64BitProcess:       {Environment.Is64BitProcess}");
        Console.WriteLine();

        Console.WriteLine("  為什麼 Environment.Version 可能比 csproj 的 TFM 還高？");
        Console.WriteLine("    • 機器上裝了 .NET 10 runtime，但專案 target net8.0");
        Console.WriteLine("    • 這叫「roll-forward」：可以在更新的 runtime 上跑舊 TFM 的 app");
        Console.WriteLine("    • 所以『編譯用什麼』和『執行用什麼』是分開決定的");
        Console.WriteLine();
    }

    // ── 2. 編譯期條件編譯 ────────────────────────────────────────────
    private static void Section2_ConditionalCompilation()
    {
        Console.WriteLine("--- 2. #if：編譯期判斷 TFM ---");

        Console.WriteLine($"  NET10_0_OR_GREATER 定義: {IsDefined("NET10_0_OR_GREATER")}");
        Console.WriteLine($"  NET9_0_OR_GREATER  定義: {IsDefined("NET9_0_OR_GREATER")}");
        Console.WriteLine($"  NET8_0_OR_GREATER  定義: {IsDefined("NET8_0_OR_GREATER")}");
        Console.WriteLine();

        // 這裡的內容在編譯期就決定好，跑哪個 runtime 都不會變
#if NET10_0_OR_GREATER
        Console.WriteLine("  → 以 .NET 10+ 編譯（#if 走這裡）");
#elif NET9_0_OR_GREATER
        Console.WriteLine("  → 以 .NET 9 編譯（#if 走這裡）");
#else
        Console.WriteLine("  → 以 .NET 8 或更早編譯（#if 走這裡）");
#endif

        // 實務用途：同一份程式碼在 Windows 和 Linux 上跑
#if WINDOWS
        Console.WriteLine("  → Windows 專用程式碼");
#elif LINUX
        Console.WriteLine("  → Linux 專用程式碼");
#endif

        Console.WriteLine();
        Console.WriteLine("  注意：#if 評估的是『編譯時的 TFM』，不是執行時的版本。");
        Console.WriteLine("  這是常見誤解 —— 寫 net8.0 的程式碼在 .NET 10 上跑，");
        Console.WriteLine("  NET10_0_OR_GREATER 仍然是未定義的。");
        Console.WriteLine();
    }

    private static bool IsDefined(string symbol)
    {
#if NET10_0_OR_GREATER
        if (symbol == "NET10_0_OR_GREATER") return true;
#endif
#if NET9_0_OR_GREATER
        if (symbol == "NET9_0_OR_GREATER") return true;
#endif
#if NET8_0_OR_GREATER
        if (symbol == "NET8_0_OR_GREATER") return true;
#endif
        return false;
    }

    // ── 3. 集合表達式 ────────────────────────────────────────────────
    private static void Section3_CollectionExpressions()
    {
        Console.WriteLine("--- 3. 集合表達式（C# 12 / .NET 8+）---");

        // 目標型別決定它是什麼型別。這是 C# 12 的核心：一份寫法，多種型別。
        int[] numbers = [1, 2, 3, 4, 5];
        List<string> names = ["temp", "pressure", "flow"];
        Span<int> inlineSpan = [10, 20, 30];        // ⚠ 不配置堆積記憶體
        double[] readings = [25.5, 26.0, 24.8];

        Console.WriteLine($"  int[]              [{string.Join(", ", numbers)}]");
        Console.WriteLine($"  List<string>       [{string.Join(", ", names)}]");
        Console.WriteLine($"  Span<int>          [{string.Join(", ", inlineSpan.ToArray())}]  ← 不進 GC");
        Console.WriteLine($"  double[]           [{string.Join(", ", readings)}]");
        Console.WriteLine();

        Console.WriteLine("  同一份 [1, 2, 3] 可以是 int[]、List<int>、Span<int>、");
        Console.WriteLine("  ImmutableArray<int>、IReadOnlyList<int>... 取決於宣告的目標型別。");
        Console.WriteLine("  要指定「只讀」語意時用 System.Collections.Immutable（需額外 using）。");
        Console.WriteLine();

        // 空集合：不用 new List<int>() 或 Array.Empty<int>()
        int[] empty = [];
        Console.WriteLine($"  空集合長度: {empty.Length}");
        Console.WriteLine();

        // Spread 運算子
        int[] head = [1, 2];
        int[] tail = [99, 100];
        int[] combined = [.. head, 50, .. tail];
        Console.WriteLine($"  Spread: [.. {string.Join(", ", head)}, 50, .. {string.Join(", ", tail)}] = [{string.Join(", ", combined)}]");
        Console.WriteLine();
        Console.WriteLine("  舊寫法對照：new[] { 1, 2, 3 } / new List<int> { 1, 2, 3 } / Array.Empty<int>()");
        Console.WriteLine();

        // ⚠ 陷阱：沒有目標型別時會編譯失敗
        // var mystery = [1, 2, 3];     // ❌ CS9176 沒有目標型別
        int[] explicitType = [1, 2, 3];   // ✅ 必須寫出型別
        Console.WriteLine($"  沒寫型別不行，明寫才 OK: [{string.Join(", ", explicitType)}]");
        Console.WriteLine();
    }

    // ── 4. Primary constructor ───────────────────────────────────────
    private static void Section4_PrimaryConstructor()
    {
        Console.WriteLine("--- 4. Primary constructor（C# 12 / .NET 8+）---");

        var sensor = new TemperatureSensor("Line1.MixingTank", 25, 80);
        Console.WriteLine($"  {sensor.Describe()}");
        Console.WriteLine($"  平均: {sensor.Average():F1}，最高: {sensor.Peak():F1}");
        Console.WriteLine();

        // 舊寫法
        Console.WriteLine("  舊寫法要多寫一個建構式：");
        Console.WriteLine("    public class TemperatureSensor");
        Console.WriteLine("    {");
        Console.WriteLine("        public string Name { get; }");
        Console.WriteLine("        public TemperatureSensor(string name, double min, double max)");
        Console.WriteLine("        { Name = name; Min = min; Max = max; }   // 3 行重複");
        Console.WriteLine("        ... ");
        Console.WriteLine("    }");
        Console.WriteLine();

        // ⚠ 重要限制：primary constructor 的參數「不會」自動變成屬性
        Console.WriteLine("  ⚠ 重點：參數不會自動產生屬性，要用就明確宣告。");
        Console.WriteLine("    如果只寫 public string Name { get; } = name;");
        Console.WriteLine("    那 Name 是唯讀的初始值，不是「參數的儲存位置」。");
        Console.WriteLine();
        Console.WriteLine("  另一個限制：沒有 readonly 的隱含保證。");
        Console.WriteLine("    建構式執行完之後，參數仍可能被內部程式碼重新賦值。");
        Console.WriteLine("    這在 record 沒問題，在一般 class 要自己小心。");
        Console.WriteLine();
    }

    // ── 5. params 集合 ───────────────────────────────────────────────
    private static async Task Section5_ParamsCollection(CancellationToken ct)
    {
        Console.WriteLine("--- 5. params 集合（C# 13 / .NET 9+）---");

        // 舊寫法：只支援陣列
        Log("array", "第一則", "第二則", "第三則");

        // C# 13：params 可以是任何可_collection_expression_ 型別
        LogSequence("span", ["A", "B", "C"]);
        LogSequence("list", new List<string> { "X", "Y" });

        // 實際用途：params 搭配來源生成器、記錄參數
        var report = new SensorReport("Line1", "週報",
            new SensorSummary("Temp", 25.5),
            new SensorSummary("Flow", 120.0),
        );

        foreach (var item in report.Items)
        {
            Console.WriteLine($"    {item.Name} = {item.Value:F1}");
        }

        await Task.CompletedTask;
        Console.WriteLine();
        Console.WriteLine("  舊寫法：public static void Log(params string[] entries)");
        Console.WriteLine("  C# 13：public static void LogSequence(params ReadOnlySpan<string> entries)");
        Console.WriteLine("         → params ReadOnlySpan 避免為了少數幾個字串配置一個陣列");
        Console.WriteLine();
    }

    private static void Log(string source, params string[] entries)
    {
        Console.WriteLine($"    [{source}] {entries.Length} 則（string[]）");
    }

    private static void LogSequence(string source, params ReadOnlySpan<string> entries)
    {
        Console.WriteLine($"    [{source}] {entries.Length} 則（ReadOnlySpan，不配置陣列）");
    }

    // ── 6. field 關鍵字 ─────────────────────────────────────────────
    private static void Section6_BackingFieldKeyword()
    {
        Console.WriteLine("--- 6. field 關鍵字（C# 14 / .NET 10）---");

        var config = new DeviceConfig { Name = "  Line1.PLC  ", Port = 502 };
        Console.WriteLine($"  Name: 「{config.Name}」  ← setter 自動 Trim");
        Console.WriteLine($"  Port: {config.Port}     ← setter 擋掉不合法值");

        config.Port = -1;
        Console.WriteLine($"  設 Port = -1 後: {config.Port}   ← 被擋下來了");

        config.Port = 70000;
        Console.WriteLine($"  設 Port = 70000 後: {config.Port}  ← 一樣被擋下來");
        Console.WriteLine();

        Console.WriteLine("  舊寫法要自己宣告一個 private 欄位：");
        Console.WriteLine("    private string _name = \"\";");
        Console.WriteLine("    public string Name { get => _name; set => _name = value.Trim(); }");
        Console.WriteLine();
        Console.WriteLine("  C# 14 之後：");
        Console.WriteLine("    public string Name { get => field; set => field = value.Trim(); }");
        Console.WriteLine();
        Console.WriteLine("  ⚠ field 是 contextual keyword，只有在屬性存取器裡才是那個意思。");
        Console.WriteLine("    外面有個 method 叫 field() 的話那邊就是方法呼叫，不會衝突。");
        Console.WriteLine();
    }

    // ── 7. ?.= null-conditional 賦值 ─────────────────────────────────
    private static async Task Section7_NullConditionalAssignment(CancellationToken ct)
    {
        Console.WriteLine("--- 7. ?.= 賦值（C# 14 / .NET 10）---");

        var registry = new DeviceRegistry();

        // 舊寫法：先檢查再賦值
        if (registry.Primary is not null)
        {
            registry.Primary.Port = 502;
        }

        // C# 14：直接把 null 檢查和賦值合成一句
        registry.Primary?.Port = 1502;

        Console.WriteLine($"  registry.Primary?.Port = 1502 → 實際: {registry.Primary?.Port}");
        Console.WriteLine("  （Primary 是 null，所以 ?. 短路，什麼都沒做，也沒有例外）");
        Console.WriteLine();

        registry.Primary = new DeviceConfig { Name = "Main", Port = 502 };
        registry.Primary?.Port = 1502;
        Console.WriteLine($"  有實體時: {registry.Primary?.Port}");
        Console.WriteLine();

        // ??= 早就有，但 ?.= 是新的
        var fresh = new DeviceRegistry();
        fresh.Primary ??= new DeviceConfig { Name = "Fallback" };   // C# 2.0 就有
        fresh.Primary?.Name = "Renamed";                            // C# 14 新增
        Console.WriteLine($"  ??= + ?.= 結果: {fresh.Primary?.Name}");
        Console.WriteLine();
        Console.WriteLine("  ⚠ 讀取仍然要用 ?.：");
        Console.WriteLine("    int? port = registry.Primary?.Port;   ← 得到 int?");
        Console.WriteLine("    int port = registry.Primary.Port;    ← ⚠ 還是會有 CS8602");
        Console.WriteLine();

        await Task.CompletedTask;
    }

    // ── 8. Extension members ─────────────────────────────────────────
    private static void Section8_ExtensionMembers()
    {
        Console.WriteLine("--- 8. extension 成員（C# 14 / .NET 10）---");
        Console.WriteLine("  ⚠ 這是最新的語法，本 repo 無法編譯驗證。");
        Console.WriteLine("     若編譯失敗請對照 Microsoft 官方文件。");
        Console.WriteLine();

        int[] samples = [100, 102, 98, 101];
        Console.WriteLine($"  IsEmpty: {samples.IsEmpty()}");
        Console.WriteLine($"  Between(99, 102): {samples.Between(99, 102)}");
        Console.WriteLine();

        Console.WriteLine("  舊寫法：public static class X { public static bool IsEmpty<T>(this IEnumerable<T> s) ... }");
        Console.WriteLine("  C# 14：");
        Console.WriteLine("    public static class X");
        Console.WriteLine("    {");
        Console.WriteLine("        extension<T>(IEnumerable<T> source)");
        Console.WriteLine("        {");
        Console.WriteLine("            public bool IsEmpty() => !source.Any();");
        Console.WriteLine("            public bool Between(int lo, int hi) => ...");
        Console.WriteLine("        }");
        Console.WriteLine("    }");
        Console.WriteLine();
        Console.WriteLine("  好處：一個 extension 區塊可以有多個成員，而且能放進泛型介面實作。");
        Console.WriteLine("  （舊寫法一個 static class 只能對同一個型別做一種泛型實作）");
        Console.WriteLine();
    }
}

// ── Section 4 用的型別 ──────────────────────────────────────────────
internal sealed class TemperatureSensor(string name, double min, double max)
{
    public string Name { get; } = name;
    public double Min { get; } = min;
    public double Max { get; } = max;

    private readonly List<double> _readings = [];

    public void Add(double value) => _readings.Add(value);

    public double Average() => _readings.Count == 0 ? 0 : _readings.Average();

    public double Peak() => _readings.Count == 0 ? 0 : _readings.Max();

    public string Describe() => $"{Name} 量測範圍 {Min:F1}–{Max:F1}，目前 {Average():F1} 筆";
}

// ── Section 5 用的型別 ──────────────────────────────────────────────
internal readonly record struct SensorSummary(string Name, double Value);

internal sealed class SensorReport
{
    private readonly SensorSummary[] _items;

    public string Line { get; }

    public string Title { get; }

    public IReadOnlyList<SensorSummary> Items => _items;

    public SensorReport(string line, string title, params SensorSummary[] items)
    {
        Line = line;
        Title = title;
        _items = items;
    }
}

// ── Section 6 / 7 用的型別 ──────────────────────────────────────────
internal sealed class DeviceConfig
{
    private string _name = "";

    public string Name
    {
        get => field;                        // C# 14 backing field
        set => field = value.Trim();
    }

    private int _port = 502;

    public int Port
    {
        get => field;                        // C# 14 backing field
        set
        {
            // 1–65535，超過就忽略
            if (value is >= 1 and <= 65535)
            {
                field = value;
            }
        }
    }
}

internal sealed class DeviceRegistry
{
    public DeviceConfig? Primary { get; set; }
}

// ── Section 8 用的 extension members ────────────────────────────────
internal static class SequenceExtensions
{
    extension<T>(IEnumerable<T> source)
    {
        public bool IsEmpty()
        {
            return !source.Any();
        }

        public bool Between(T lo, T hi)
        {
            return source.All(x => Comparer<T>.Default.Compare(x, lo) >= 0
                                && Comparer<T>.Default.Compare(x, hi) <= 0);
        }
    }
}
