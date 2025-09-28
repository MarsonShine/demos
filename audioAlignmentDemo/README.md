# ��Ƶ�����Զ��и�� - ʹ��ָ��

## ?? ����

����һ��ǿ�����Ƶ�����ߣ����Խ�����������ӵ���Ƶ�ļ��Զ��и�ɶ����ľ�����Ƶ�ļ���֧�ֶ���ʹ�÷�ʽ�������в���������ģʽ�������������Ϊ�����á�

## ?? ���ٿ�ʼ

### 1. ������ʹ��

#### �������ļ�
```bash
# ʹ��Ĭ�����ô�����Ƶ�ļ�
dotnet run -- --input audio.mp3

# ָ�����Ŀ¼
dotnet run -- --input audio.mp3 --output my_output

# ʹ����������ģ��
dotnet run -- --input chinese_audio.wav --language zh --model small
```

#### ��������
```bash
# �������ļ�
dotnet run -- --input file1.mp3 file2.wav file3.m4a

# ��������Ŀ¼
dotnet run -- --input-dir ./audio_files/ --batch

# ʹ�������ļ�
dotnet run -- --input-dir ./audio/ --config my_settings.json
```

#### �߼�����
```bash
# �߾��ȴ���
dotnet run -- --input audio.mp3 --model base --padding 0.5 --threshold 0.05 --debug

# ������������
dotnet run -- --input-dir ./batch/ --model tiny --quality Balanced --batch
```

### 2. ����ģʽ

ֱ�����г��򲻴��κβ���������������ģʽ��

```bash
dotnet run
```

Ȼ������ʾѡ�������
- ��������Ƶ�ļ�
- ����������Ƶ�ļ�  
- ʹ������Ԥ��
- �����Զ�������
- ����ʾ�������ļ�

### 3. �����ļ�

���� JSON �����ļ������泣�����ã�

```json
{
  "Language": "en",
  "ModelSize": "base", 
  "AudioQualityStrategy": "HighQuality",
  "SentenceBoundaryPadding": 0.4,
  "EnableTimeCorrection": true,
  "TimeCorrectionThreshold": 0.1,
  "DebugMode": true
}
```

ʹ�������ļ���
```bash
dotnet run -- --input audio.mp3 --config settings.json
```

## ?? ��Ϊ���ʹ��

����� C# ��Ŀ����������⣺

### ����ʹ��

```csharp
using AudioAlignmentDemo.Library;

// �����ָ���ʵ��
var splitter = new AudioSplitterLibrary();

// �������ļ� (ʹ��Ĭ������)
var result = await splitter.ProcessAudioFileAsync("input.mp3", "output_dir");

if (result.Success)
{
    Console.WriteLine($"�ɹ����� {result.SegmentCount} ����ƵƬ��");
    foreach (var file in result.GeneratedFiles)
    {
        Console.WriteLine($"�����ļ�: {file}");
    }
}
else
{
    Console.WriteLine($"����ʧ��: {result.Error}");
}
```

### �Զ�������

```csharp
using AudioAlignmentDemo.Library;
using AudioAlignmentDemo.Configuration;

// ʹ��Ԥ������
var config = ConfigurationManager.Presets.HighPrecision;
config.InputAudioPath = "my_audio.mp3";
config.OutputDirectory = "my_output";

var splitter = new AudioSplitterLibrary();
var result = await splitter.ProcessAudioFileAsync(config);
```

### ��������

```csharp
using AudioAlignmentDemo.Library;

var splitter = new AudioSplitterLibrary();

// ��Ŀ¼������Ƶ�ļ�
var audioFiles = AudioSplitterLibrary.FindAudioFiles("./audio_directory/");

// ��������
var batchResult = await splitter.ProcessAudioFilesAsync(
    audioFiles, 
    "batch_output",
    "balanced" // ʹ��ƽ��Ԥ��
);

Console.WriteLine($"������ {batchResult.InputFiles.Count} ���ļ�");
Console.WriteLine($"�ɹ�: {batchResult.SuccessfulFiles}, ʧ��: {batchResult.FailedFiles}");
Console.WriteLine($"�ܹ����� {batchResult.TotalSegments} ����ƵƬ��");
```

### ��֤�Ͳ����ļ�

```csharp
// ����ļ��Ƿ�֧��
if (AudioSplitterLibrary.IsAudioFileSupported("test.mp3"))
{
    Console.WriteLine("�ļ���ʽ֧��");
}

// ��ȡ����Ԥ��
var presets = AudioSplitterLibrary.GetAvailablePresets();
Console.WriteLine("����Ԥ��: " + string.Join(", ", presets));

// ������Ƶ�ļ�
var audioFiles = AudioSplitterLibrary.FindAudioFiles("./music/", includeSubdirectories: true);
Console.WriteLine($"�ҵ� {audioFiles.Count} ����Ƶ�ļ�");
```

## ?? ����Ԥ��

���������˶���Ԥ�����ã���Ӧ��ͬʹ�ó�����

### 1. high-precision (�߾���ģʽ)
- �ʺϣ���Ҫ���ݵľ�ϸ�и�
- �ص㣺ʹ�� base ģ�ͣ��߽߱���䣬��ϸ������Ϣ

### 2. fast-batch (��������ģʽ)  
- �ʺϣ������ļ��Ŀ��ٴ���
- �ص㣺ʹ�� tiny ģ�ͣ�������䣬�رյ���

### 3. balanced (ƽ��ģʽ)
- �ʺϣ��ճ�ʹ�ã��ھ��Ⱥ��ٶȼ�ƽ��
- �ص㣺ʹ�� small ģ�ͣ����в���

### 4. chinese (�����Ż�ģʽ)
- �ʺϣ�������������
- �ص㣺������������ص��Ż�����

### 5. english-dialogue (Ӣ�ĶԻ�ģʽ)
- �ʺϣ�Ӣ�ĶԻ��ͷ�̸
- �ص㣺��ԶԻ��ص��Ż�������������

## ??? ��Ҫ����˵��

| ���� | ���� | Ĭ��ֵ | ˵�� |
|------|------|--------|------|
| `--input` | string[] | - | ������Ƶ�ļ�·�� (����) |
| `--input-dir` | string | - | ����Ŀ¼·�� |
| `--output` | string | output_sentences | ���Ŀ¼·�� |
| `--language` | string | en | ���Դ��� (en/zh/ja��) |
| `--model` | string | tiny | Whisperģ�� (tiny/base/small/medium/large) |
| `--quality` | string | HighQuality | ��ƵƷ�ʲ��� |
| `--padding` | double | 0.4 | ���ӱ߽����ʱ��(��) |
| `--threshold` | double | 0.1 | ʱ��У����ֵ(��) |
| `--config` | string | - | �����ļ�·�� |
| `--batch` | bool | false | ���ò�������ģʽ |
| `--debug` | bool | false | ���õ���ģʽ |

## ?? ֧�ֵ���Ƶ��ʽ

- WAV (.wav) - ������Ƶ��ʽ
- MP3 (.mp3) - ����ѹ����ʽ  
- M4A (.m4a) - Apple ��Ƶ��ʽ
- WMA (.wma) - Windows Media ��Ƶ
- AAC (.aac) - �߼���Ƶ����
- FLAC (.flac) - ����ѹ����ʽ
- OGG (.ogg) - ��Դ��Ƶ��ʽ
- MP4 (.mp4) - ��Ƶ�����е���Ƶ

## ?? ʹ�ý���

### 1. ѡ����ʵ�ģ��
- `tiny`: ��죬�ʺϿ�����������
- `base`: ƽ��ѡ���ճ�ʹ���Ƽ�
- `small`: ���þ��ȣ��ʺ���Ҫ����
- `medium/large`: ��߾��ȣ�����ʱ��ϳ�

### 2. �����߽����
- ������ֵ��ʱ��жϣ����� `SentenceBoundaryPadding`
- �������ٽϿ�����ݣ�����ʹ�� 0.5-0.8 ��
- �������ٽ��������ݣ�0.2-0.4 ��ͨ���㹻

### 3. ʱ��У��
- Ĭ������ʱ��У�����ܣ����ʶ��ʱ����׼ȷ����
- ������ֽ�β���ضϣ����Խ��� `TimeCorrectionThreshold`
- �������� `MaxExtensionTime` �����������չ

### 4. ���������Ż�
- ʹ�� `--batch` �������ò��д���
- �����ļ�ʱ����ʹ�� `fast-batch` Ԥ��
- ���Ը��� CPU ����������������

## ?? ��������

### 1. ģ������ʧ��
- ȷ��������������
- �����ֶ�����ģ���ļ��� `models` Ŀ¼
- �״�ʹ����Ҫ���ض�Ӧ�� Whisper ģ��

### 2. FFmpeg ��ش���
- ȷ��ϵͳ�Ѱ�װ FFmpeg
- �� FFmpeg ��ӵ�ϵͳ PATH ��������
- ���� WAV �ļ���������ʹ�� NAudio ����

### 3. ��Ƶ��ʽ��֧��
- ����ļ���չ���Ƿ���֧���б���
- ����������������ת��Ϊ֧�ֵĸ�ʽ
- ʹ�� `IsAudioFileSupported` ������֤

### 4. �ڴ治��
- ������ļ�ʱ������Ҫ�����ڴ�
- ���Գ���ʹ�ø�С��ģ�� (tiny/base)
- ��������ʱ������������

## ?? ����ļ�

������ɺ�����������ļ���
- `sentence_XX_time_text.{format}` - �и����ƵƬ��
- `sentence_split_report.json` - ��ϸ������
- `sentence_list.txt` - �����嵥
- `performance_report.json` - ���ܷ������� (�������)

## ?? ���º���չ

������߲���ģ�黯��ƣ�����������չ��
- ����µ���Ƶ��ʽ֧��
- ��չ����ʶ������
- �����µķ����㷨
- ������������ʶ������

## ?? ����֧��

��������������Ҫ�¹��ܣ�
1. ��鱾�ĵ��ĳ������ⲿ��
2. ʹ�� `--debug` ������ȡ��ϸ��־
3. ȷ�������ļ���ʽ��·����ȷ
4. ���ϵͳ���� (FFmpeg, .NET 9) �Ƿ���ȷ��װ