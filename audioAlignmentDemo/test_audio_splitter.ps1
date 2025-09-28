#!/usr/bin/env pwsh

# ��Ƶ�и�߲��Խű�

Write-Host "?? ��Ƶ�����Զ��и�� - ���Խű�" -ForegroundColor Green
Write-Host "=================================" -ForegroundColor Green

# ���ʾ����Ƶ�ļ�
$sampleAudio = "be64c3b9-662c-47cf-8faa-3b663e8aaa0e.mp3"

if (-not (Test-Path $sampleAudio)) {
    Write-Host "? �Ҳ���ʾ����Ƶ�ļ�: $sampleAudio" -ForegroundColor Red
    Write-Host "?? �뽫��Ƶ�ļ����ڵ�ǰĿ¼��" -ForegroundColor Yellow
    exit 1
}

Write-Host "?? �ҵ�ʾ����Ƶ�ļ�: $sampleAudio" -ForegroundColor Green

# ����1: ��ʾ����
Write-Host "`n?? ����1: ��ʾ������Ϣ" -ForegroundColor Cyan
dotnet run -- --help

# ����2: �������ļ� (ʹ��Ĭ������)
Write-Host "`n?? ����2: �������ļ�" -ForegroundColor Cyan
dotnet run -- --input $sampleAudio --output "test_output_single"

# ����3: ʹ�õ���ģʽ
Write-Host "`n?? ����3: ����ģʽ����" -ForegroundColor Cyan
dotnet run -- --input $sampleAudio --output "test_output_debug" --debug

# ����4: ʹ�ò�ͬ����
Write-Host "`n?? ����4: �Զ����������" -ForegroundColor Cyan
dotnet run -- --input $sampleAudio --output "test_output_custom" --language "en" --model "tiny" --padding 0.5

# ����ʾ�������ļ�
Write-Host "`n?? ����5: ����ʾ�������ļ�" -ForegroundColor Cyan
$configContent = @"
{
  "Language": "en",
  "ModelSize": "tiny",
  "AudioQualityStrategy": "HighQuality",
  "SentenceBoundaryPadding": 0.4,
  "TimeCorrectionThreshold": 0.1,
  "EnableTimeCorrection": true,
  "DebugMode": true,
  "OutputDirectory": "test_output_config"
}
"@

$configContent | Out-File -FilePath "test_config.json" -Encoding UTF8
Write-Host "?? �����ɲ��������ļ�: test_config.json" -ForegroundColor Green

# ����6: ʹ�������ļ�
Write-Host "`n?? ����6: ʹ�������ļ�����" -ForegroundColor Cyan
dotnet run -- --input $sampleAudio --config "test_config.json"

Write-Host "`n? ���в�����ɣ�" -ForegroundColor Green
Write-Host "?? ��鿴�������Ŀ¼�еĽ��:" -ForegroundColor Yellow
Write-Host "   - test_output_single/" -ForegroundColor White
Write-Host "   - test_output_debug/" -ForegroundColor White
Write-Host "   - test_output_custom/" -ForegroundColor White
Write-Host "   - test_output_config/" -ForegroundColor White

# ��ʾ���Ŀ¼ͳ��
$outputDirs = @("test_output_single", "test_output_debug", "test_output_custom", "test_output_config")

Write-Host "`n?? ���ͳ��:" -ForegroundColor Cyan
foreach ($dir in $outputDirs) {
    if (Test-Path $dir) {
        $files = Get-ChildItem -Path $dir -Filter "sentence_*.*" | Where-Object { $_.Extension -ne ".json" -and $_.Extension -ne ".txt" -and $_.Extension -ne ".csv" }
        Write-Host "   $dir`: $($files.Count) ����Ƶ�ļ�" -ForegroundColor White
        
        # ��ʾ��һ���ļ���Ϊʾ��
        if ($files.Count -gt 0) {
            Write-Host "     ʾ��: $($files[0].Name)" -ForegroundColor Gray
        }
    }
}

Write-Host "`n?? �����������:" -ForegroundColor Yellow
Write-Host "   - �������ɵ���Ƶ�ļ���֤Ч��" -ForegroundColor White
Write-Host "   - �鿴 JSON �����˽⴦������" -ForegroundColor White
Write-Host "   - �������ò����Ż��и�Ч��" -ForegroundColor White
Write-Host "   - ���������������ļ�" -ForegroundColor White