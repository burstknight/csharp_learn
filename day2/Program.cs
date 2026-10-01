// Day 2 的進入點。範例主題：非同步、取消、Channels。

using CSharpLearn.Day2;

var demos = new (string Key, string Description, Func<CancellationToken, Task> Run)[]
{
    ("async", "async/await 與取消", AsyncDemo.RunAsync),
    ("collector", "Channels 生產者/消費者", CollectorDemo.RunAsync),
};

if (!TryResolve(args, demos, out var selected))
{
    return;
}

using var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

try
{
    await selected.Run(cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("已取消。");
}

static bool TryResolve(
    string[] args,
    (string Key, string Description, Func<CancellationToken, Task> Run)[] demos,
    out (string Key, string Description, Func<CancellationToken, Task> Run) selected)
{
    if (args.Length == 0)
    {
        PrintUsage(demos);
        selected = default;
        return false;
    }

    selected = demos.FirstOrDefault(d => d.Key.Equals(args[0], StringComparison.OrdinalIgnoreCase));

    if (selected.Run is null)
    {
        Console.Error.WriteLine($"找不到 demo: {args[0]}");
        Console.Error.WriteLine();
        PrintUsage(demos);
        return false;
    }

    return true;
}

static void PrintUsage(
    (string Key, string Description, Func<CancellationToken, Task> Run)[] demos)
{
    Console.WriteLine("用法: dotnet run --project day2 -- <demo>");
    Console.WriteLine();
    Console.WriteLine("可用 demo:");
    foreach (var demo in demos)
    {
        Console.WriteLine($"  {demo.Key,-10} {demo.Description}");
    }
}