using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace StreamTtsDemo
{
    public class VolcaneTtsService
    {
        private readonly HttpClient _httpClient;

        public VolcaneTtsService()
        {
            _httpClient = new HttpClient();
        }

        public async Task<TtsResponse> StreamTtsAsync(TtsRequest request)
        {
            try
            {
                // 生成连接ID
                var connectId = Guid.NewGuid().ToString();

                // 构建 WebSocket URI
                var wsUri = $"wss://openspeech.bytedance.com/api/v3/tts/bidirection";

                using var client = new ClientWebSocket();

                // 设置请求头
                client.Options.SetRequestHeader("User-Agent", "VolcaneTTS-CSharp/1.0");
                client.Options.SetRequestHeader("X-Api-App-Key", request.ApiAppKey);
                client.Options.SetRequestHeader("X-Api-Access-Key", request.ApiAccessKey);
                client.Options.SetRequestHeader("X-Api-Resource-Id", request.ApiResourceId);
                client.Options.SetRequestHeader("X-Api-Connect-Id", connectId);

                // 连接到 WebSocket
                await client.ConnectAsync(new Uri(wsUri), CancellationToken.None);

                // 构建发送消息
                var sendMessage = new
                {
                    user = new { uid = "12345" },
                    @event = 100,
                    req_params = new
                    {
                        text = request.Text,
                        speaker = request.Speaker,
                        audio_params = new
                        {
                            format = request.Format,
                            sample_rate = request.SampleRate
                        }
                    }
                };

                // 序列化消息
                var jsonMessage = JsonSerializer.Serialize(sendMessage, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                var messageBytes = Encoding.UTF8.GetBytes(jsonMessage);

                // 发送消息
                await client.SendAsync(
                    new ArraySegment<byte>(messageBytes),
                    WebSocketMessageType.Text,
                    true,
                    CancellationToken.None
                );

                // 接收响应
                var audioDataList = new List<byte>();
                var buffer = new byte[8192];

                while (client.State == WebSocketState.Open)
                {
                    var result = await client.ReceiveAsync(
                        new ArraySegment<byte>(buffer),
                        CancellationToken.None
                    );

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        // 处理文本消息（状态信息等）
                        var textMessage = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        Console.WriteLine($"收到文本消息: {textMessage}");

                        // 检查是否是结束消息
                        if (textMessage.Contains("\"event\":200") || textMessage.Contains("\"event\":300"))
                        {
                            break;
                        }
                    }
                    else if (result.MessageType == WebSocketMessageType.Binary)
                    {
                        // 处理音频数据
                        var audioChunk = new byte[result.Count];
                        Array.Copy(buffer, audioChunk, result.Count);
                        audioDataList.AddRange(audioChunk);
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }
                }

                // 关闭连接
                if (client.State == WebSocketState.Open)
                {
                    await client.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "完成",
                        CancellationToken.None
                    );
                }

                var audioData = audioDataList.ToArray();

                return new TtsResponse(
                    Success: true,
                    AudioData: audioData,
                    AudioBase64: Convert.ToBase64String(audioData)
                );
            }
            catch (Exception ex)
            {
                return new TtsResponse(
                    Success: false,
                    Message: $"TTS 转换失败: {ex.Message}"
                );
            }
        }
    }
}
