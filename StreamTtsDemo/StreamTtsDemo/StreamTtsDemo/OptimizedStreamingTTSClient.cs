using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace StreamTtsDemo
{
    /// <summary>
    /// 优化的流式TTS客户端
    /// </summary>
    public class OptimizedStreamingTTSClient
    {
        #region 常量定义

        private const int PROTOCOL_VERSION = 0b0001;
        private const int DEFAULT_HEADER_SIZE = 0b0001;

        // Message Type:
        private const int FULL_CLIENT_REQUEST = 0b0001;
        private const int AUDIO_ONLY_RESPONSE = 0b1011;
        private const int FULL_SERVER_RESPONSE = 0b1001;
        private const int ERROR_INFORMATION = 0b1111;

        // Message Type Specific Flags
        private const int MsgTypeFlagNoSeq = 0b0000; // Non-terminal packet with no sequence
        private const int MsgTypeFlagPositiveSeq = 0b1; // Non-terminal packet with sequence > 0
        private const int MsgTypeFlagLastNoSeq = 0b10; // last packet with no sequence
        private const int MsgTypeFlagNegativeSeq = 0b11; // Payload contains event number (int32)
        private const int MsgTypeFlagWithEvent = 0b100;

        // Message Serialization
        private const int NO_SERIALIZATION = 0b0000;
        private const int JSON = 0b0001;

        // Message Compression
        private const int COMPRESSION_NO = 0b0000;
        private const int COMPRESSION_GZIP = 0b0001;

        #endregion

        #region 事件常量

        // 默认事件
        public const int EVENT_NONE = 0;
        public const int EVENT_Start_Connection = 1;
        public const int EVENT_FinishConnection = 2;

        // 下行Connection事件
        public const int EVENT_ConnectionStarted = 50; // 成功建连
        public const int EVENT_ConnectionFailed = 51; // 建连失败
        public const int EVENT_ConnectionFinished = 52; // 连接结束

        // 上行Session事件
        public const int EVENT_StartSession = 100;
        public const int EVENT_FinishSession = 102;

        // 下行Session事件
        public const int EVENT_SessionStarted = 150;
        public const int EVENT_SessionFinished = 152;
        public const int EVENT_SessionFailed = 153;

        // 上行通用事件
        public const int EVENT_TaskRequest = 200;

        // 下行TTS事件
        public const int EVENT_TTSSentenceStart = 350;
        public const int EVENT_TTSSentenceEnd = 351;
        public const int EVENT_TTSResponse = 352;

        #endregion

        #region 数据结构

        /// <summary>
        /// 消息头结构
        /// </summary>
        public class Header
        {
            public int ProtocolVersion { get; set; } = PROTOCOL_VERSION;
            public int HeaderSize { get; set; } = DEFAULT_HEADER_SIZE;
            public int MessageType { get; set; }
            public int MessageTypeSpecificFlags { get; set; } = MsgTypeFlagWithEvent;
            public int SerializationMethod { get; set; } = NO_SERIALIZATION;
            public int MessageCompression { get; set; } = COMPRESSION_NO;
            public int Reserved { get; set; } = 0;

            public Header() { }

            public Header(int protocolVersion, int headerSize, int messageType,
                         int messageTypeSpecificFlags, int serializationMethod,
                         int messageCompression, int reserved)
            {
                ProtocolVersion = protocolVersion;
                HeaderSize = headerSize;
                MessageType = messageType;
                MessageTypeSpecificFlags = messageTypeSpecificFlags;
                SerializationMethod = serializationMethod;
                MessageCompression = messageCompression;
                Reserved = reserved;
            }

            /// <summary>
            /// 转换为字节数组
            /// </summary>
            public byte[] GetBytes()
            {
                return new byte[]
                {
                    (byte)(ProtocolVersion << 4 | HeaderSize),
                    (byte)(MessageType << 4 | MessageTypeSpecificFlags),
                    (byte)(SerializationMethod << 4 | MessageCompression),
                    (byte)Reserved
                };
            }
        }

        /// <summary>
        /// 可选字段结构
        /// </summary>
        public class Optional
        {
            public int Event { get; set; } = EVENT_NONE;
            public string? SessionId { get; set; }
            public int ErrorCode { get; set; }
            public string? ConnectionId { get; set; }
            public string? ResponseMetaJson { get; set; }

            public Optional() { }

            public Optional(int eventType, string? sessionId)
            {
                Event = eventType;
                SessionId = sessionId;
            }

            public byte[] GetBytes()
            {
                var bytes = new byte[0];

                if (Event != EVENT_NONE)
                {
                    bytes = IntToBytes(Event);
                }

                if (!string.IsNullOrEmpty(SessionId))
                {
                    var sessionIdBytes = Encoding.UTF8.GetBytes(SessionId);
                    var sessionIdSizeBytes = IntToBytes(sessionIdBytes.Length);

                    var temp = bytes;
                    bytes = new byte[temp.Length + sessionIdSizeBytes.Length + sessionIdBytes.Length];
                    var destPos = 0;

                    Array.Copy(temp, 0, bytes, destPos, temp.Length);
                    destPos += temp.Length;
                    Array.Copy(sessionIdSizeBytes, 0, bytes, destPos, sessionIdSizeBytes.Length);
                    destPos += sessionIdSizeBytes.Length;
                    Array.Copy(sessionIdBytes, 0, bytes, destPos, sessionIdBytes.Length);
                }

                return bytes;
            }
        }

        /// <summary>
        /// TTS 响应结构
        /// </summary>
        public class TTSResponse
        {
            public Header? Header { get; set; }
            public Optional? Optional { get; set; }
            public int PayloadSize { get; set; }
            public byte[]? Payload { get; set; }
            public string? PayloadJson { get; set; }

            public override string ToString()
            {
                return JsonSerializer.Serialize(this, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
            }
        }

        /// <summary>
        /// 泛型键值对
        /// </summary>
        public class Pair<TFirst, TSecond>
        {
            public TFirst First { get; set; }
            public TSecond Second { get; set; }

            public Pair(TFirst first, TSecond second)
            {
                First = first;
                Second = second;
            }
        }

        #endregion

        #region 工具方法

        /// <summary>
        /// 字节数组转整数
        /// </summary>
        private static int BytesToInt(byte[] src)
        {
            if (src == null || src.Length != 4)
                throw new ArgumentException("字节数组必须为4个字节");

            return (src[0] & 0xFF) << 24 |
                   (src[1] & 0xFF) << 16 |
                   (src[2] & 0xFF) << 8 |
                   src[3] & 0xFF;
        }

        /// <summary>
        /// 整数转字节数组
        /// </summary>
        private static byte[] IntToBytes(int value)
        {
            return new byte[]
            {
                (byte)(value >> 24 & 0xFF),
                (byte)(value >> 16 & 0xFF),
                (byte)(value >> 8 & 0xFF),
                (byte)(value & 0xFF)
            };
        }

        #endregion

        #region 响应解析

        /// <summary>
        /// 解析响应包
        /// </summary>
        private static TTSResponse? ParseResponse(byte[] responseBytes)
        {
            if (responseBytes == null || responseBytes.Length == 0)
                return null;

            var response = new TTSResponse();
            var header = new Header();
            response.Header = header;

            const byte mask = 0b00001111;

            // 解析头部（4字节）
            header.ProtocolVersion = responseBytes[0] >> 4 & mask;
            header.HeaderSize = responseBytes[0] & 0x0f;
            header.MessageType = responseBytes[1] >> 4 & mask;
            header.MessageTypeSpecificFlags = responseBytes[1] & 0x0f;
            header.SerializationMethod = responseBytes[2] >> 4;
            header.MessageCompression = responseBytes[2] & 0x0f;
            header.Reserved = responseBytes[3];

            var offset = 4;
            response.Optional = new Optional();

            // 解析正常响应
            if (header.MessageType == FULL_SERVER_RESPONSE || header.MessageType == AUDIO_ONLY_RESPONSE)
            {
                offset = ReadEvent(responseBytes, header.MessageTypeSpecificFlags, response, offset);
                var eventType = response.Optional.Event;

                switch (eventType)
                {
                    case EVENT_ConnectionStarted:
                        ReadConnectionStarted(responseBytes, response, offset);
                        break;
                    case EVENT_ConnectionFailed:
                        ReadConnectionFailed(responseBytes, response, offset);
                        break;
                    case EVENT_SessionStarted:
                        offset = ReadSessionId(responseBytes, response, offset);
                        break;
                    case EVENT_TTSResponse:
                        offset = ReadSessionId(responseBytes, response, offset);
                        offset = ReadPayload(responseBytes, response, offset);
                        break;
                    case EVENT_TTSSentenceStart:
                    case EVENT_TTSSentenceEnd:
                        offset = ReadSessionId(responseBytes, response, offset);
                        offset = ReadPayloadJson(responseBytes, response, offset);
                        break;
                    case EVENT_SessionFailed:
                    case EVENT_SessionFinished:
                        offset = ReadSessionId(responseBytes, response, offset);
                        ReadMetaJson(responseBytes, response, offset);
                        break;
                }
            }
            // 解析错误响应
            else if (header.MessageType == ERROR_INFORMATION)
            {
                offset = ReadErrorCode(responseBytes, response, offset);
                ReadPayload(responseBytes, response, offset);
            }

            return response;
        }

        private static void ReadConnectionStarted(byte[] responseBytes, TTSResponse response, int start)
        {
            ReadConnectionId(responseBytes, response, start);
        }

        private static void ReadConnectionFailed(byte[] responseBytes, TTSResponse response, int start)
        {
            start = ReadConnectionId(responseBytes, response, start);
            ReadMetaJson(responseBytes, response, start);
        }

        private static int ReadConnectionId(byte[] responseBytes, TTSResponse response, int start)
        {
            var result = ReadText(responseBytes, start);
            response.Optional!.ConnectionId = result.Second;
            return result.First;
        }

        private static int ReadMetaJson(byte[] responseBytes, TTSResponse response, int start)
        {
            var result = ReadText(responseBytes, start);
            response.Optional!.ResponseMetaJson = result.Second;
            return result.First;
        }

        private static int ReadPayloadJson(byte[] responseBytes, TTSResponse response, int start)
        {
            var result = ReadText(responseBytes, start);
            response.PayloadJson = result.Second;
            return result.First;
        }

        private static Pair<int, string> ReadText(byte[] responseBytes, int start)
        {
            var sizeBytes = new byte[4];
            Array.Copy(responseBytes, start, sizeBytes, 0, 4);
            start += 4;

            var size = BytesToInt(sizeBytes);
            var textBytes = new byte[size];
            Array.Copy(responseBytes, start, textBytes, 0, size);

            var text = Encoding.UTF8.GetString(textBytes);
            start += size;

            return new Pair<int, string>(start, text);
        }

        private static int ReadPayload(byte[] responseBytes, TTSResponse response, int start)
        {
            var sizeBytes = new byte[4];
            Array.Copy(responseBytes, start, sizeBytes, 0, 4);
            start += 4;

            var size = BytesToInt(sizeBytes);
            response.PayloadSize += size;

            var payloadBytes = new byte[size];
            Array.Copy(responseBytes, start, payloadBytes, 0, size);
            response.Payload = payloadBytes;
            start += size;

            if (response.Optional?.Event == FULL_SERVER_RESPONSE)
            {
                Console.WriteLine($"===> payload: {Encoding.UTF8.GetString(payloadBytes)}");
            }

            return start;
        }

        private static int ReadErrorCode(byte[] responseBytes, TTSResponse response, int start)
        {
            var errorCodeBytes = new byte[4];
            Array.Copy(responseBytes, start, errorCodeBytes, 0, 4);
            start += 4;
            response.Optional!.ErrorCode = BytesToInt(errorCodeBytes);
            return start;
        }

        private static int ReadEvent(byte[] responseBytes, int msgTypeFlag, TTSResponse response, int start)
        {
            if (msgTypeFlag == MsgTypeFlagWithEvent)
            {
                var eventBytes = new byte[4];
                Array.Copy(responseBytes, start, eventBytes, 0, 4);
                response.Optional!.Event = BytesToInt(eventBytes);
                start += 4;
            }
            return start;
        }

        private static int ReadSessionId(byte[] responseBytes, TTSResponse response, int start)
        {
            var result = ReadText(responseBytes, start);
            response.Optional!.SessionId = result.Second;
            return result.First;
        }

        #endregion

        #region 消息发送

        /// <summary>
        /// 开始连接
        /// </summary>
        private static async Task<bool> StartConnectionAsync(ClientWebSocket webSocket)
        {
            var header = new Header(
                PROTOCOL_VERSION,
                DEFAULT_HEADER_SIZE,
                FULL_CLIENT_REQUEST,
                MsgTypeFlagWithEvent,
                JSON,
                COMPRESSION_NO,
                0).GetBytes();

            var optional = new Optional(EVENT_Start_Connection, null).GetBytes();
            var payload = Encoding.UTF8.GetBytes("{}");

            return await SendEventAsync(webSocket, header, optional, payload);
        }

        /// <summary>
        /// 结束连接
        /// </summary>
        private static async Task<bool> FinishConnectionAsync(ClientWebSocket webSocket)
        {
            var header = new Header(
                PROTOCOL_VERSION,
                DEFAULT_HEADER_SIZE,
                FULL_CLIENT_REQUEST,
                MsgTypeFlagWithEvent,
                JSON,
                COMPRESSION_NO,
                0).GetBytes();

            var optional = new Optional(EVENT_FinishConnection, null).GetBytes();
            var payload = Encoding.UTF8.GetBytes("{}");

            return await SendEventAsync(webSocket, header, optional, payload);
        }

        /// <summary>
        /// 结束会话
        /// </summary>
        private static async Task<bool> FinishSessionAsync(ClientWebSocket webSocket, string sessionId)
        {
            var header = new Header(
                PROTOCOL_VERSION,
                DEFAULT_HEADER_SIZE,
                FULL_CLIENT_REQUEST,
                MsgTypeFlagWithEvent,
                JSON,
                COMPRESSION_NO,
                0).GetBytes();

            var optional = new Optional(EVENT_FinishSession, sessionId).GetBytes();
            var payload = Encoding.UTF8.GetBytes("{}");

            return await SendEventAsync(webSocket, header, optional, payload);
        }

        /// <summary>
        /// 开始TTS会话
        /// </summary>
        private static async Task<bool> StartTTSSessionAsync(ClientWebSocket webSocket, string sessionId, string speaker)
        {
            var header = new Header(
                PROTOCOL_VERSION,
                DEFAULT_HEADER_SIZE,
                FULL_CLIENT_REQUEST,
                MsgTypeFlagWithEvent,
                JSON,
                COMPRESSION_NO,
                0).GetBytes();

            var optional = new Optional(EVENT_StartSession, sessionId).GetBytes();

            var payloadObj = new
            {
                user = new { uid = "123456" },
                @event = EVENT_StartSession,
                @namespace = "BidirectionalTTS",
                req_params = new
                {
                    speaker,
                    audio_params = new
                    {
                        format = "mp3",
                        sample_rate = 24000,
                        enable_timestamp = true
                    }
                }
            };

            var payloadJson = JsonSerializer.Serialize(payloadObj);
            var payload = Encoding.UTF8.GetBytes(payloadJson);

            return await SendEventAsync(webSocket, header, optional, payload);
        }

        /// <summary>
        /// 发送文本消息
        /// </summary>
        private static async Task<bool> SendMessageAsync(ClientWebSocket webSocket, string speaker, string sessionId, string text)
        {
            var header = new Header(
                PROTOCOL_VERSION,
                DEFAULT_HEADER_SIZE,
                FULL_CLIENT_REQUEST,
                MsgTypeFlagWithEvent,
                JSON,
                COMPRESSION_NO,
                0).GetBytes();

            var optional = new Optional(EVENT_TaskRequest, sessionId).GetBytes();

            var payloadObj = new
            {
                user = new { uid = "123456" },
                @event = EVENT_TaskRequest,
                @namespace = "BidirectionalTTS",
                req_params = new
                {
                    text,
                    speaker,
                    audio_params = new
                    {
                        format = "mp3",
                        sample_rate = 24000
                    }
                }
            };

            var payloadJson = JsonSerializer.Serialize(payloadObj);
            var payload = Encoding.UTF8.GetBytes(payloadJson);

            return await SendEventAsync(webSocket, header, optional, payload);
        }

        /// <summary>
        /// 发送事件
        /// </summary>
        private static async Task<bool> SendEventAsync(ClientWebSocket webSocket, byte[] header, byte[] optional, byte[] payload)
        {
            if (webSocket?.State != WebSocketState.Open)
                return false;

            var payloadSizeBytes = IntToBytes(payload.Length);
            var requestBytes = new byte[
                header.Length +
                (optional?.Length ?? 0) +
                payloadSizeBytes.Length +
                payload.Length];

            var destPos = 0;
            Array.Copy(header, 0, requestBytes, destPos, header.Length);
            destPos += header.Length;

            if (optional != null)
            {
                Array.Copy(optional, 0, requestBytes, destPos, optional.Length);
                destPos += optional.Length;
            }

            Array.Copy(payloadSizeBytes, 0, requestBytes, destPos, payloadSizeBytes.Length);
            destPos += payloadSizeBytes.Length;
            Array.Copy(payload, 0, requestBytes, destPos, payload.Length);

            try
            {
                await webSocket.SendAsync(
                    new ArraySegment<byte>(requestBytes),
                    WebSocketMessageType.Binary,
                    true,
                    CancellationToken.None);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"发送消息失败: {ex.Message}");
                return false;
            }
        }

        #endregion

        public static async Task Main(string[] args)
        {
            const string appId = ""; // 您的 App ID
            const string token = ""; // 填写您的 Access Token
            const string url = "wss://openspeech.bytedance.com/api/v3/tts/bidirection";
            const string testText = "落霞与孤鹜齐飞，秋水共长天一色。东隅已逝，桑榆非晚，关山难越，谁悲失路之人？这是一段较长的文本，用来测试流式播放的效果。希望能够听到流畅连续的语音输出。";
            const string speaker = "zh_female_shuangkuaisisi_moon_bigtts";

            // 创建改进的流式播放器
            var streamingPlayer = new ImprovedStreamingTTSPlayer();
            streamingPlayer.InitializePlayer();

            // 创建输出文件
            var outputFile = new FileInfo("output_audio_streaming.mp3");
            if (outputFile.Exists)
                outputFile.Delete();

            await using var fileStream = outputFile.Create();

            // 创建 WebSocket 客户端
            using var client = new ClientWebSocket();

            // 设置请求头
            client.Options.SetRequestHeader("X-Api-App-Key", appId);
            client.Options.SetRequestHeader("X-Api-Access-Key", token);
            client.Options.SetRequestHeader("X-Api-Resource-Id", "volc.service_type.10029");
            client.Options.SetRequestHeader("X-Api-Connect-Id", Guid.NewGuid().ToString());

            // 设置更大的接收缓冲区
            client.Options.SetBuffer(1024 * 64, 1024 * 64); // 64KB 缓冲区

            var sessionId = Guid.NewGuid().ToString("N");

            try
            {
                // 连接到 WebSocket
                var uri = new Uri(url);
                await client.ConnectAsync(uri, CancellationToken.None);
                Console.WriteLine("===> WebSocket 连接成功");

                // 开始连接
                await StartConnectionAsync(client);

                // 处理消息循环
                var buffer = new byte[1024 * 32]; // 增大接收缓冲区到32KB
                var receivedAudioChunks = 0;

                while (client.State == WebSocketState.Open)
                {
                    var result = await client.ReceiveAsync(
                        new ArraySegment<byte>(buffer),
                        CancellationToken.None);

                    if (result.MessageType == WebSocketMessageType.Binary)
                    {
                        var responseBytes = new byte[result.Count];
                        Array.Copy(buffer, responseBytes, result.Count);

                        var response = ParseResponse(responseBytes);
                        if (response?.Optional != null)
                        {
                            switch (response.Optional.Event)
                            {
                                case EVENT_ConnectionFailed:
                                case EVENT_SessionFailed:
                                    Console.WriteLine("连接或会话失败");
                                    streamingPlayer.Stop();
                                    return;

                                case EVENT_ConnectionStarted:
                                    Console.WriteLine("===> 连接已建立，开始TTS会话");
                                    await StartTTSSessionAsync(client, sessionId, speaker);
                                    break;

                                case EVENT_SessionStarted:
                                    Console.WriteLine("===> TTS会话已开始，发送文本");
                                    await SendMessageAsync(client, speaker, sessionId, testText);
                                    await FinishSessionAsync(client, sessionId);
                                    break;

                                case EVENT_TTSSentenceStart:
                                    Console.WriteLine("===> 开始合成语音");
                                    break;

                                case EVENT_TTSSentenceEnd:
                                    Console.WriteLine("===> 语音合成完成");
                                    break;

                                case EVENT_TTSResponse:
                                    if (response.Payload != null && response.Header?.MessageType == AUDIO_ONLY_RESPONSE)
                                    {
                                        receivedAudioChunks++;

                                        // 同时保存到文件和添加到播放器
                                        await fileStream.WriteAsync(response.Payload);
                                        await fileStream.FlushAsync(); // 立即刷新到磁盘

                                        streamingPlayer.AddAudioChunk(response.Payload);

                                        Console.WriteLine($"===> 接收音频块 #{receivedAudioChunks}: {response.Payload.Length} 字节");
                                    }
                                    break;

                                case EVENT_SessionFinished:
                                    Console.WriteLine("===> 会话结束");
                                    await FinishConnectionAsync(client);
                                    break;

                                case EVENT_ConnectionFinished:
                                    Console.WriteLine($"===> 连接结束，共接收 {receivedAudioChunks} 个音频块");
                                    streamingPlayer.MarkReceivingComplete();

                                    // 等待播放完成
                                    Console.WriteLine("===> 等待音频播放完成...");
                                    streamingPlayer.WaitForPlaybackComplete();
                                    streamingPlayer.Stop();

                                    Console.WriteLine("===> 程序完成，退出");
                                    return;
                            }
                        }
                    }
                    else if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        Console.WriteLine($"===> 收到文本消息: {text}");
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Console.WriteLine($"===> WebSocket 关闭: {result.CloseStatus}");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"发生错误: {ex.Message}");
                Console.WriteLine($"堆栈跟踪: {ex.StackTrace}");
                streamingPlayer.Stop();
            }
            finally
            {
                if (client.State == WebSocketState.Open)
                {
                    await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "完成", CancellationToken.None);
                }
            }
        }

        // ... 其他方法保持不变 ...
    }
}