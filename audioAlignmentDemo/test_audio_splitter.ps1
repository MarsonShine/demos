#!/usr/bin/env pwsh

# 音频切割工具测试脚本

Write-Host "?? 音频句子自动切割工具 - 测试脚本" -ForegroundColor Green
Write-Host "=================================" -ForegroundColor Green

# 检查示例音频文件
$sampleAudio = "be64c3b9-662c-47cf-8faa-3b663e8aaa0e.mp3"

if (-not (Test-Path $sampleAudio)) {
    Write-Host "? 找不到示例音频文件: $sampleAudio" -ForegroundColor Red
    Write-Host "?? 请将音频文件放在当前目录中" -ForegroundColor Yellow
    exit 1
}

Write-Host "?? 找到示例音频文件: $sampleAudio" -ForegroundColor Green

# 测试1: 显示帮助
Write-Host "`n?? 测试1: 显示帮助信息" -ForegroundColor Cyan
dotnet run -- --help

# 测试2: 处理单个文件 (使用默认配置)
Write-Host "`n?? 测试2: 处理单个文件" -ForegroundColor Cyan
dotnet run -- --input $sampleAudio --output "test_output_single"

# 测试3: 使用调试模式
Write-Host "`n?? 测试3: 调试模式处理" -ForegroundColor Cyan
dotnet run -- --input $sampleAudio --output "test_output_debug" --debug

# 测试4: 使用不同参数
Write-Host "`n?? 测试4: 自定义参数处理" -ForegroundColor Cyan
dotnet run -- --input $sampleAudio --output "test_output_custom" --language "en" --model "tiny" --padding 0.5

# 生成示例配置文件
Write-Host "`n?? 测试5: 生成示例配置文件" -ForegroundColor Cyan
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
Write-Host "?? 已生成测试配置文件: test_config.json" -ForegroundColor Green

# 测试6: 使用配置文件
Write-Host "`n?? 测试6: 使用配置文件处理" -ForegroundColor Cyan
dotnet run -- --input $sampleAudio --config "test_config.json"

Write-Host "`n? 所有测试完成！" -ForegroundColor Green
Write-Host "?? 请查看以下输出目录中的结果:" -ForegroundColor Yellow
Write-Host "   - test_output_single/" -ForegroundColor White
Write-Host "   - test_output_debug/" -ForegroundColor White
Write-Host "   - test_output_custom/" -ForegroundColor White
Write-Host "   - test_output_config/" -ForegroundColor White

# 显示输出目录统计
$outputDirs = @("test_output_single", "test_output_debug", "test_output_custom", "test_output_config")

Write-Host "`n?? 输出统计:" -ForegroundColor Cyan
foreach ($dir in $outputDirs) {
    if (Test-Path $dir) {
        $files = Get-ChildItem -Path $dir -Filter "sentence_*.*" | Where-Object { $_.Extension -ne ".json" -and $_.Extension -ne ".txt" -and $_.Extension -ne ".csv" }
        Write-Host "   $dir`: $($files.Count) 个音频文件" -ForegroundColor White
        
        # 显示第一个文件作为示例
        if ($files.Count -gt 0) {
            Write-Host "     示例: $($files[0].Name)" -ForegroundColor Gray
        }
    }
}

Write-Host "`n?? 接下来你可以:" -ForegroundColor Yellow
Write-Host "   - 播放生成的音频文件验证效果" -ForegroundColor White
Write-Host "   - 查看 JSON 报告了解处理详情" -ForegroundColor White
Write-Host "   - 调整配置参数优化切割效果" -ForegroundColor White
Write-Host "   - 尝试批量处理多个文件" -ForegroundColor White