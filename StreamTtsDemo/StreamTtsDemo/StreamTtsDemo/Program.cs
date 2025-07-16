using StreamTtsDemo;


await StreamingTtsPlayer.Main([]);

var builder = WebApplication.CreateBuilder(args);

// ��ӷ���
builder.Services.AddEndpointsApiExplorer();

// ���� CORS�������Ҫǰ�˵��ã�
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});
// ע�� TTS ����
builder.Services.AddScoped<VolcaneTtsService>();

var app = builder.Build();

app.UseCors();

// API �˵�
app.MapGet("/api/tts", async (VolcaneTtsService ttsService) =>
{
    //await TTSWebSocketDemo.Main([]);
    //await StreamingTTSClient.Main([]);
    //await OptimizedStreamingTTSClient.Main([]);
    await StreamingTtsPlayer.Main([]);
    //var request = new TtsRequest("c");
    //if (string.IsNullOrEmpty(request.Text))
    //{
    //    return Results.BadRequest("�ı����ݲ���Ϊ��");
    //}

    //if (string.IsNullOrEmpty(request.ApiAccessKey))
    //{
    //    return Results.BadRequest("API Access Key ����Ϊ��");
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

// ������Ƶ�ļ��Ķ˵�
app.MapPost("/api/tts/download", async (TtsRequest request, VolcaneTtsService ttsService) =>
{
    if (string.IsNullOrEmpty(request.Text))
    {
        return Results.BadRequest("�ı����ݲ���Ϊ��");
    }

    if (string.IsNullOrEmpty(request.ApiAccessKey))
    {
        return Results.BadRequest("API Access Key ����Ϊ��");
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

// �������˵�
app.MapGet("/", () => "��ɽ���� TTS ����������")
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

// TTS ��Ӧģ��
public record TtsResponse(
    bool Success,
    string? Message = null,
    byte[]? AudioData = null,
    string? AudioBase64 = null
);