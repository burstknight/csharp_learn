// MQTTnet 的命名空間不在 ImplicitUsings 裡，全部要自己 using。
//
// ⚠ 這支範例用的是 MQTTnet 5.x 的 API，跟網路上大多是 4.x 的教學不一樣：
//     v4：MqttFactory
//     v5：MqttClientFactory / MqttServerFactory（拆成兩個）
//     v4：MqttApplicationMessage.Payload 是 byte[]
//     v5：改成 ReadOnlySequence<byte>，請用 ConvertPayloadToString() 取值
//     搜尋教學時請先確認版本 —— 這是 MQTTnet 最容易踩到的坑。
//
// ⚠ 5.x 的命名空間也扁平化了。沒有 MQTTnet.Client / MQTTnet.Server.Options 這些子命名空間：
//     MQTTnet                 → MqttClientFactory、MqttClientOptionsBuilder、MqttApplicationMessageBuilder、
//                               MqttClientSubscribeOptionsBuilder、MqttTopicFilterBuilder、IMqttClient…
//     MQTTnet.Protocol        → MqttQualityOfServiceLevel、MqttRetainHandling…
//     MQTTnet.Formatter       → MqttProtocolVersion
//     MQTTnet.Server          → MqttServerFactory、MqttServerOptionsBuilder（在 MQTTnet.Server 套件裡）
//     寫 using MQTTnet.Client; 會得到 ⚠ CS0234「命名空間 'MQTTnet' 中沒有類型或命名空間名稱 'Client'」。
//     另外 QoS 不是直接設在 subscribe builder 上，而是設在「topic filter builder」裡，
//     所以要寫成 WithTopicFilter(f => f.WithTopic(...).WithAtLeastOnceQoS())。
using System.Globalization;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using MQTTnet.Server;

namespace CSharpLearn.Day3;

// Day 3 第三支範例：MQTT —— 內嵌 broker + 兩個 client 跑在同一個 process。
//
// 這樣你不需要先裝 Mosquitto 或架一台 broker，就能離線跑完整個發布／訂閱流程。
//
// ✅ 本檔案已用 .NET 10 SDK + MQTTnet 5.2.0.1603 編譯並實跑驗證過。
//    API 寫法參照 MQTTnet 官方的 Samples/Client 與 Samples/Server 目錄。
//    若編譯失敗，先確認 day3.csproj 的套件是 5.x，並到
//    https://www.nuget.org/packages/MQTTnet 查最新版本。

public static class MqttDemo
{
    private const int BrokerPort = 1883;
    private const string Topic = "csharp-learn/line1/temperature";
    private const int MessageCount = 3;

    public static async Task RunAsync(CancellationToken ct)
    {
        Console.WriteLine("--- MQTT：內嵌 broker + client（MQTTnet 5.x）---");
        Console.WriteLine();

        // ── 1. 開 broker ──────────────────────────────────────────────
        // v5 用 MqttServerFactory（v4 是 MqttFactory.CreateMqttServer）。
        var serverFactory = new MqttServerFactory();

        // WithDefaultEndpoint 預設開 1883，而且是「不加密」的明文連線。
        // 生產環境應該改用 WithEncryptedEndpoint + 憑證。
        var serverOptions = new MqttServerOptionsBuilder()
            .WithDefaultEndpoint()
            .WithDefaultEndpointPort(BrokerPort)
            .Build();

        using var server = serverFactory.CreateMqttServer(serverOptions);
        await server.StartAsync();
        Console.WriteLine($"  [broker] 已在 port {BrokerPort} 啟動（無加密，僅供本機練習）");

        try
        {
            // ── 2. 訂閱端 ──────────────────────────────────────────────
            // v5 用 MqttClientFactory（v4 是 MqttFactory）。
            var subscriberFactory = new MqttClientFactory();

            using var subscriber = subscriberFactory.CreateMqttClient();

            // 收集結果。用 TaskCompletionSource 讓主流程可以等「收滿 3 筆」。
            var received = new List<string>();
            var allReceived = new TaskCompletionSource();
            var count = 0;

            // ⚠ 事件處理器必須在 ConnectAsync「之前」掛上。
            //    MQTTnet 官方 sample 的註解寫得很清楚：沒有掛 handler 的話，
            //    在連線建立到 handler 掛上之間收到的訊息會直接被丟掉。
            subscriber.ApplicationMessageReceivedAsync += e =>
            {
                // v5 的 Payload 是 ReadOnlySequence<byte>，不是 byte[]。
                // 用擴充方法 ConvertPayloadToString() 取字串（它回傳 string?）。
                var payload = e.ApplicationMessage.ConvertPayloadToString() ?? "(空)";
                var entry = $"{e.ApplicationMessage.Topic} = {payload}";

                // 這個 handler 會在網路執行緒上被呼叫，可能同時有多筆進來。
                // 寫入共用集合一定要上鎖。
                lock (received)
                {
                    received.Add(entry);

                    if (++count == MessageCount)
                    {
                        allReceived.TrySetResult();
                    }
                }

                Console.WriteLine($"  [subscriber] 收到 {entry}");

                return Task.CompletedTask;
            };

            var subscriberOptions = new MqttClientOptionsBuilder()
                .WithTcpServer("localhost", BrokerPort)
                .WithClientId("csharp-learn-subscriber")
                .WithProtocolVersion(MqttProtocolVersion.V500)
                .Build();

            await subscriber.ConnectAsync(subscriberOptions, ct);
            Console.WriteLine("  [subscriber] 已連線（client id: csharp-learn-subscriber）");

// ⚠ 這裡的 QoS 要設在「topic filter builder」上，不是 subscribe builder 上。
//   直接寫 .WithAtLeastOnceQoS() 會得到 ⚠ CS1061（builder 上沒有這個成員）。
var subscribeOptions = subscriberFactory
    .CreateSubscribeOptionsBuilder()
    .WithTopicFilter(f => f
        .WithTopic(Topic)
        .WithAtLeastOnceQoS())    // QoS 1
    .Build();

            await subscriber.SubscribeAsync(subscribeOptions, ct);
            Console.WriteLine($"  [subscriber] 已訂閱 {Topic}（QoS 1）");

            // ── 3. 發布端 ──────────────────────────────────────────────
            var publisherFactory = new MqttClientFactory();

            using var publisher = publisherFactory.CreateMqttClient();

            var publisherOptions = new MqttClientOptionsBuilder()
                .WithTcpServer("localhost", BrokerPort)
                .WithClientId("csharp-learn-publisher")
                .Build();

            await publisher.ConnectAsync(publisherOptions, ct);
            Console.WriteLine("  [publisher] 已連線（client id: csharp-learn-publisher）");
            Console.WriteLine();

            // 發 3 筆，模擬感測器資料
            for (var i = 1; i <= MessageCount; i++)
            {
                var value = (20.0 + (i * 0.5)).ToString("F1", CultureInfo.InvariantCulture);

                var message = new MqttApplicationMessageBuilder()
                    .WithTopic(Topic)
                    .WithPayload(value)
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                    .Build();

                await publisher.PublishAsync(message, ct);
                Console.WriteLine($"  [publisher] 已發布 {Topic} = {value}");

                await Task.Delay(100, ct);
            }

            // 等訂閱端收完再斷線。
            // ⚠ 這很重要：QoS 1 的訊息是「非同步確認」的，
            //    publisher 立刻 Disconnect 的話，訊息可能還沒送到 broker。
            await allReceived.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);

            // ── 4. 收尾 ────────────────────────────────────────────────
            await publisher.DisconnectAsync();
            await subscriber.DisconnectAsync();

            Console.WriteLine();
            lock (received)
            {
                Console.WriteLine($"  共收到 {received.Count} 則:");
                foreach (var entry in received)
                {
                    Console.WriteLine($"    {entry}");
                }
            }

            Console.WriteLine();
            Console.WriteLine("  QoS 對照:");
            Console.WriteLine("    0 AtMostOnce     發了就忘，不重試     → 資料量大、可容忍遺失的遙測");
            Console.WriteLine("    1 AtLeastOnce    保證送達，可能重複     → 警報、控制指令");
            Console.WriteLine("    2 ExactlyOnce    保證送達且不重複     → 需要時用，握手成本最高");
        }
        finally
        {
            await server.StopAsync();
            Console.WriteLine();
            Console.WriteLine("  [broker] 已停止");
        }
    }
}