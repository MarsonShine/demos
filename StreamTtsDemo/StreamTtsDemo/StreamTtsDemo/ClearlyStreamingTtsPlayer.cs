using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.IO.Pipelines;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;

namespace StreamTtsDemo
{

    // --- Main Application Class ---
    public static class StreamingTtsPlayer
    {
        // --- 配置你的火山引擎凭据 ---
        private const string AppId = ""; // 填写您的 App ID
        private const string Token = ""; // 填写您的 Access Token
                                                                         // ---------------------------------

        private const string WssUrl = "wss://openspeech.bytedance.com/api/v3/tts/bidirection";
        private const string Speaker = "zh_female_shuangkuaisisi_moon_bigtts";

        // 音频格式必须与你在 StartTTSSessionAsync 中请求的格式一致
        private static readonly WaveFormat AudioWaveFormat = new(24000, 16, 1);

        public static async Task Main(string[] args)
        {
            Console.WriteLine("--- 火山引擎TTS流式播放Demo (Pipeline & NAudio 版本) ---");

            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) =>
            {
                Console.WriteLine("正在取消...");
                cts.Cancel();
                e.Cancel = true;
            };

            var audioPipe = new Pipe();

            using var client = new ClientWebSocket();
            await ConnectToTtsServiceAsync(client);

            // 启动音频播放消费者
            var playerTask = AudioPlayerConsumer(audioPipe.Reader, cts.Token);
            // 启动WebSocket生产者
            var producerTask = WebSocketProducer(client, audioPipe.Writer, cts.Token);

            Console.WriteLine("\n连接成功！现在您可以输入要合成的文本，按 Enter 发送。");
            Console.WriteLine("输入 'exit' 或按 Ctrl+C 退出程序。\n");

            // 主线程处理用户输入
            try
            {
                // 向生产者发送要合成的文本
                await TtsProtocol.SendMessageAsync(client, Speaker, "明朝开国皇帝朱元璋也称这本书为,万物之根");
                //while (!cts.IsCancellationRequested)
                //{
                //    var text = await Task.Run(Console.ReadLine, cts.Token);
                //    if (string.IsNullOrWhiteSpace(text) || text.Equals("exit", StringComparison.OrdinalIgnoreCase))
                //    {
                //        break;
                //    }

                //    // 向生产者发送要合成的文本
                //    await TtsProtocol.SendMessageAsync(client, Speaker, text);
                //}
            }
            catch (OperationCanceledException) { /* 正常退出 */ }

            await Task.WhenAll(producerTask, playerTask);
            // 发送结束会话的信令
            await TtsProtocol.FinishSessionAsync(client);

            // 等待任务完成
            Console.WriteLine("正在等待任务结束...");
            if (!cts.IsCancellationRequested)
            {
                cts.Cancel();
            }


            Console.WriteLine("程序已退出。");
        }

        /// <summary>
        /// 连接到火山引擎服务
        /// </summary>
        private static async Task ConnectToTtsServiceAsync(ClientWebSocket client)
        {
            var sessionId = Guid.NewGuid().ToString("N");
            client.Options.SetRequestHeader("X-Api-App-Key", AppId);
            client.Options.SetRequestHeader("X-Api-Access-Key", Token);
            client.Options.SetRequestHeader("X-Api-Resource-Id", "volc.service_type.10029");
            client.Options.SetRequestHeader("X-Api-Connect-Id", sessionId);
            TtsProtocol.SessionId = sessionId; // 保存SessionId供后续使用

            await client.ConnectAsync(new Uri(WssUrl), CancellationToken.None);
        }

        /// <summary>
        /// 生产者: 负责所有WebSocket通信，并将接收到的音频数据写入Pipe
        /// </summary>
        private static async Task WebSocketProducer(ClientWebSocket client, PipeWriter audioWriter, CancellationToken ct)
        {
            Console.WriteLine("[生产者] 启动。");
            try
            {
                // 1. 发送建连请求
                await TtsProtocol.StartConnectionAsync(client);

                var buffer = new ArrayBufferWriter<byte>();
                while (client.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    // 使用ValueTask以获得更好的性能
                    ValueWebSocketReceiveResult result;
                    do
                    {
                        var memory = buffer.GetMemory();
                        result = await client.ReceiveAsync(memory, ct);
                        buffer.Advance(result.Count);
                    } while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close) break;

                    // 解析完整的消息
                    var response = TtsProtocol.ParseResponse(new ReadOnlySequence<byte>(buffer.WrittenSpan.ToArray()));
                    buffer.Clear();

                    if (response == null) continue;

                    // 处理服务器事件
                    switch (response.Event)
                    {
                        case TtsProtocol.EVENT_ConnectionStarted:
                            Console.WriteLine("[生产者] 连接成功，正在启动TTS会话...");
                            await TtsProtocol.StartTTSSessionAsync(client, Speaker);
                            break;

                        case TtsProtocol.EVENT_SessionStarted:
                            Console.WriteLine("[生产者] TTS会话已就绪。");
                            break;

                        case TtsProtocol.EVENT_TTSResponse when response.AudioPayload.Length > 0:
                            // 核心：将音频数据写入Pipe
                            await audioWriter.WriteAsync(response.AudioPayload, ct);
                            break;

                        case TtsProtocol.EVENT_SessionFinished:
                            Console.WriteLine("[生产者] 会话已结束。");
                            break;

                        case TtsProtocol.EVENT_ConnectionFinished:
                            Console.WriteLine("[生产者] 连接已关闭。");
                            return; // 结束生产者

                        case TtsProtocol.EVENT_ConnectionFailed or TtsProtocol.EVENT_SessionFailed:
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"[生产者] 错误！事件: {response.Event}, 元数据: {response.MetadataJson}");
                            Console.ResetColor();
                            return;
                    }
                }
            }
            catch (OperationCanceledException) { /* 正常关闭 */ }
            catch (Exception ex)
            {
                Console.WriteLine($"[生产者] 发生异常: {ex.Message}");
            }
            finally
            {
                await audioWriter.CompleteAsync(); // 通知消费者数据已写完
                if (client.State == WebSocketState.Open)
                {
                    await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client closing", CancellationToken.None);
                }
                Console.WriteLine("[生产者] 已停止。");
            }
        }

        /// <summary>
        /// 消费者: 从Pipe读取音频数据，并通过NAudio播放
        /// </summary>
        private static async Task AudioPlayerConsumer(PipeReader audioReader, CancellationToken ct)
        {
            Console.WriteLine("[消费者] 启动。");
            try
            {
                using var waveOut = new WaveOutEvent();
                var bufferedWaveProvider = new BufferedWaveProvider(AudioWaveFormat)
                {
                    BufferDuration = TimeSpan.FromSeconds(5), // 5秒的缓冲以应对网络抖动
                    DiscardOnBufferOverflow = true
                };

                waveOut.Init(bufferedWaveProvider);
                waveOut.Play();

                while (!ct.IsCancellationRequested)
                {
                    ReadResult result = await audioReader.ReadAsync(ct);
                    ReadOnlySequence<byte> buffer = result.Buffer;

                    if (buffer.IsEmpty && result.IsCompleted) break;

                    // 将数据块添加到NAudio的播放缓冲区
                    foreach (var segment in buffer)
                    {
                        bufferedWaveProvider.AddSamples(segment.Span.ToArray(), 0, segment.Span.Length);
                    }

                    // 确保播放器在有数据时处于播放状态
                    if (bufferedWaveProvider.BufferedBytes > 0 && waveOut.PlaybackState != PlaybackState.Playing)
                    {
                        waveOut.Play();
                    }

                    // 告知Pipe我们处理了多少数据
                    audioReader.AdvanceTo(buffer.End);

                    if (result.IsCompleted) break;
                }
            }
            catch (OperationCanceledException) { /* 正常关闭 */ }
            catch (Exception ex)
            {
                Console.WriteLine($"[消费者] 发生异常: {ex.Message}");
            }
            finally
            {
                await audioReader.CompleteAsync();
                Console.WriteLine("[消费者] 已停止。");
            }
        }
    }


    /// <summary>
    /// 负责火山引擎TTS WebSocket协议的封装、解析和构建
    /// (Refactored and Simplified)
    /// </summary>
    public static class TtsProtocol
    {
        // 公开 SessionId 以便主流程可以访问和设置
        public static string SessionId { get; set; } = "";

        #region 事件常量 (只保留必要的)
        public const int EVENT_Start_Connection = 1;
        public const int EVENT_FinishConnection = 2;
        public const int EVENT_ConnectionStarted = 50;
        public const int EVENT_ConnectionFailed = 51;
        public const int EVENT_ConnectionFinished = 52;
        public const int EVENT_StartSession = 100;
        public const int EVENT_FinishSession = 102;
        public const int EVENT_SessionStarted = 150;
        public const int EVENT_SessionFinished = 152;
        public const int EVENT_SessionFailed = 153;
        public const int EVENT_TaskRequest = 200;
        public const int EVENT_TTSResponse = 352;
        #endregion

        #region 协议常量
        private const int PROTOCOL_VERSION = 1;
        private const int HEADER_SIZE = 1;
        private const int MSG_TYPE_FULL_CLIENT_REQUEST = 1;
        private const int MSG_TYPE_AUDIO_ONLY_RESPONSE = 11;
        private const int MSG_TYPE_FULL_SERVER_RESPONSE = 9;
        private const int MSG_TYPE_FLAG_WITH_EVENT = 4;
        private const int SERIALIZATION_JSON = 1;
        private const int COMPRESSION_NO = 0;
        #endregion

        // 响应的数据结构
        public record TtsResponse(int Event, ReadOnlyMemory<byte> AudioPayload, string? MetadataJson);

        /// <summary>
        /// 解析从服务器收到的二进制消息
        /// </summary>
        public static TtsResponse? ParseResponse(ReadOnlySequence<byte> buffer)
        {
            var reader = new SequenceReader<byte>(buffer);

            // 读取4字节头部
            if (!reader.TryRead(out byte b0) || !reader.TryRead(out byte b1) ||
                !reader.TryRead(out byte b2) || !reader.TryRead(out byte b3))
                return null;

            int messageType = b1 >> 4 & 0b1111;

            int eventType = 0;
            // 尝试读取事件 (4字节)
            if (reader.TryReadBigEndian(out int readEvent))
            {
                eventType = readEvent;
            }

            string? sessionId = null;
            string? metadataJson = null;
            ReadOnlyMemory<byte> audioPayload = ReadOnlyMemory<byte>.Empty;

            // 根据消息类型解析剩余部分
            if (messageType is MSG_TYPE_FULL_SERVER_RESPONSE or MSG_TYPE_AUDIO_ONLY_RESPONSE)
            {
                switch (eventType)
                {
                    case EVENT_ConnectionStarted or EVENT_ConnectionFailed or EVENT_SessionStarted or EVENT_SessionFailed or EVENT_SessionFinished:
                        TryReadLVString(ref reader, out _); // 读取并忽略 ConnectionId/SessionId
                        TryReadLVString(ref reader, out metadataJson); // 读取元数据
                        break;
                    case EVENT_TTSResponse:
                        TryReadLVString(ref reader, out sessionId); // 读取并验证 SessionId
                        TryReadLVBytes(ref reader, out audioPayload); // 读取音频负载
                        break;
                }
            }

            return new TtsResponse(eventType, audioPayload, metadataJson);
        }

        #region 消息发送 (简化和优化)

        public static Task StartConnectionAsync(WebSocket client) =>
            SendEventAsync(client, EVENT_Start_Connection, null, "{}");

        public static Task FinishSessionAsync(WebSocket client) =>
            SendEventAsync(client, EVENT_FinishSession, SessionId, "{}");

        public static Task StartTTSSessionAsync(WebSocket client, string speaker)
        {
            var payloadObj = new
            {
                @event = EVENT_StartSession,
                @namespace = "BidirectionalTTS",
                req_params = new
                {
                    speaker,
                    audio_params = new { format = "pcm", sample_rate = 24000 }
                }
            };
            return SendEventAsync(client, EVENT_StartSession, SessionId, JsonSerializer.Serialize(payloadObj));
        }

        public static Task SendMessageAsync(WebSocket client, string speaker, string text)
        {
            var payloadObj = new
            {
                @event = EVENT_TaskRequest,
                @namespace = "BidirectionalTTS",
                req_params = new { text, speaker },
                audio_params = new
                {
                    format = "pcm",
                    sample_rate = 24000
                }
            };
            return SendEventAsync(client, EVENT_TaskRequest, SessionId, JsonSerializer.Serialize(payloadObj));
        }

        private static Task SendEventAsync(WebSocket client, int eventType, string? sessionId, string payloadJson)
        {
            var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
            var sessionIdBytes = string.IsNullOrEmpty(sessionId) ? ReadOnlySpan<byte>.Empty : Encoding.UTF8.GetBytes(sessionId);

            // 使用 ArrayBufferWriter 来高效构建消息，避免多次数组拷贝
            var writer = new ArrayBufferWriter<byte>();

            // 1. 写入头部 (4 bytes)
            writer.GetSpan(4)[0] = PROTOCOL_VERSION << 4 | HEADER_SIZE;
            writer.GetSpan(4)[1] = MSG_TYPE_FULL_CLIENT_REQUEST << 4 | MSG_TYPE_FLAG_WITH_EVENT;
            writer.GetSpan(4)[2] = SERIALIZATION_JSON << 4 | COMPRESSION_NO;
            writer.GetSpan(4)[3] = 0;
            writer.Advance(4);

            // 2. 写入事件 (4 bytes)
            BinaryPrimitives.WriteInt32BigEndian(writer.GetSpan(4), eventType);
            writer.Advance(4);

            // 3. 写入 Session ID (LV格式: 4字节长度 + 字符串)
            if (!sessionIdBytes.IsEmpty)
            {
                BinaryPrimitives.WriteInt32BigEndian(writer.GetSpan(4), sessionIdBytes.Length);
                writer.Advance(4);
                writer.Write(sessionIdBytes);
            }

            // 4. 写入 Payload (LV格式: 4字节长度 + JSON)
            BinaryPrimitives.WriteInt32BigEndian(writer.GetSpan(4), payloadBytes.Length);
            writer.Advance(4);
            writer.Write(payloadBytes);

            // 发送构建好的消息
            return client.SendAsync(writer.WrittenMemory, WebSocketMessageType.Binary, true, CancellationToken.None).AsTask();
        }
        #endregion

        #region 二进制读取帮助方法
        // LV = Length-Value
        private static bool TryReadLVString(ref SequenceReader<byte> reader, out string? value)
        {
            if (TryReadLVBytes(ref reader, out var bytes))
            {
                value = Encoding.UTF8.GetString(bytes.Span);
                return true;
            }
            value = null;
            return false;
        }

        private static bool TryReadLVBytes(ref SequenceReader<byte> reader, out ReadOnlyMemory<byte> value)
        {
            if (reader.TryReadBigEndian(out int length) && reader.Remaining >= length)
            {
                value = reader.Sequence.Slice(reader.Position, length).ToArray();
                reader.Advance(length);
                return true;
            }
            value = ReadOnlyMemory<byte>.Empty;
            return false;
        }
        #endregion
    }
}