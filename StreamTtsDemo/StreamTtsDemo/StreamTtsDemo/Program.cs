using StreamTtsDemo;


await StreamingTtsPlayer.Main([]);

var builder = WebApplication.CreateBuilder(args);

// 添加服务
builder.Services.AddEndpointsApiExplorer();

// 配置 CORS（如果需要前端调用）
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});
// 注册 TTS 服务
builder.Services.AddScoped<VolcaneTtsService>();

var app = builder.Build();

app.UseCors();

// API 端点
app.MapGet("/api/tts", async (VolcaneTtsService ttsService) =>
{
    //await TTSWebSocketDemo.Main([]);
    //await StreamingTTSClient.Main([]);
    //await OptimizedStreamingTTSClient.Main([]);
    await StreamingTtsPlayer.Main([]);
    //var request = new TtsRequest("c");
    //if (string.IsNullOrEmpty(request.Text))
    //{
    //    return Results.BadRequest("文本内容不能为空");
    //}

    //if (string.IsNullOrEmpty(request.ApiAccessKey))
    //{
    //    return Results.BadRequest("API Access Key 不能为空");
    //}

    //var result = await ttsService.StreamTtsAsync(request);

    //if (!result.Success)
    //{
    //    return Results.BadRequest(result.Message);
    //}

    return Results.Ok(null);
})
.WithName("TextToSpeech")
.WithOpenApi();

// 下载音频文件的端点
app.MapPost("/api/tts/download", async (TtsRequest request, VolcaneTtsService ttsService) =>
{
    if (string.IsNullOrEmpty(request.Text))
    {
        return Results.BadRequest("文本内容不能为空");
    }

    if (string.IsNullOrEmpty(request.ApiAccessKey))
    {
        return Results.BadRequest("API Access Key 不能为空");
    }

    var result = await ttsService.StreamTtsAsync(request);

    if (!result.Success)
    {
        return Results.BadRequest(result.Message);
    }

    var contentType = request.Format.ToLower() switch
    {
        "mp3" => "audio/mpeg",
        "wav" => "audio/wav",
        "pcm" => "audio/pcm",
        _ => "application/octet-stream"
    };

    var fileName = $"tts_output_{DateTime.Now:yyyyMMdd_HHmmss}.{request.Format}";

    return Results.File(result.AudioData!, contentType, fileName);
})
.WithName("DownloadAudio")
.WithOpenApi();

// 健康检查端点
app.MapGet("/", () => "火山引擎 TTS 服务运行中")
.WithName("HealthCheck")
.WithOpenApi();

app.Run();




public record TtsRequest(
    string Text,
    string Speaker = "zh_female_shuangkuaisisi_moon_bigtts",
    string Format = "mp3",
    int SampleRate = 24000,
    string ApiAppKey = "",
    string ApiAccessKey = "",
    string ApiResourceId = "volc.service_type.10029"
);

// TTS 响应模型
public record TtsResponse(
    bool Success,
    string? Message = null,
    byte[]? AudioData = null,
    string? AudioBase64 = null
);