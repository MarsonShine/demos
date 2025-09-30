// See https://aka.ms/new-console-template for more information
using AudioAlignmentDemo.Configuration;
using AudioAlignmentDemo.Library;
using Fz.Platform.Office;
using System.Net.Http;
using UserCase;

Console.WriteLine("=== 音频分割处理程序 ===");
Console.WriteLine("开始处理音频分割任务...\n");

try
{
    // 检查输入文件是否存在
    string inputFile = "input_data.xlsx";
    if (!File.Exists(inputFile))
    {
        Console.WriteLine($"错误: 未找到输入文件 '{inputFile}'");
        Console.WriteLine($"当前工作目录: {Directory.GetCurrentDirectory()}");
        Console.WriteLine("请确保 input_data.xlsx 文件存在于程序目录中。");
        return;
    }

    Console.WriteLine($"✓ 找到输入文件: {Path.GetFullPath(inputFile)}");

    // 1. 读取 input_data.xlsx
    Console.WriteLine("正在读取 input_data.xlsx...");
    var list = new ExcelHelper().InitSheetIndex(0)
        .InitStartReadRowIndex(0, 1)
        .Import<InputData>("x.xlsx", File.ReadAllBytes(inputFile));

    if (list == null || !list.Any())
    {
        Console.WriteLine("未找到有效数据，程序退出。");
        return;
    }

    Console.WriteLine($"✓ 成功读取 {list.Count} 条数据");

    // 2. 数据验证和清理
    var validData = list.Where(x =>
        !string.IsNullOrWhiteSpace(x.AudioUrl) &&
        !string.IsNullOrWhiteSpace(x.Content)).ToList();

    if (validData.Count < list.Count)
    {
        Console.WriteLine($"⚠ 过滤掉 {list.Count - validData.Count} 条无效数据（AudioUrl或Content为空）");
    }

    if (!validData.Any())
    {
        Console.WriteLine("没有有效的数据可供处理，程序退出。");
        return;
    }

    // 3. 按 CatalogueId 分组
    var groupedData = validData.GroupBy(x => x.CatalogueId).ToList();
    Console.WriteLine($"✓ 数据已按 CatalogueId 分组，共 {groupedData.Count} 个组\n");

    // 创建基础输出目录
    string baseOutputDir = "output";
    Directory.CreateDirectory(baseOutputDir);
    Console.WriteLine($"✓ 输出目录: {Path.GetFullPath(baseOutputDir)}");

    // 初始化音频分割器和HTTP客户端
    var config = ConfigurationManager.Presets.Balanced;
    config.Language = "en";
    AudioSplitterLibrary audioSplitter = new(config);
    HttpClient httpClient = new();
    httpClient.Timeout = TimeSpan.FromMinutes(10); // 设置下载超时

    // 统计变量
    int processedGroups = 0;
    int totalAudioProcessed = 0;
    int totalAudioFailed = 0;
    var startTime = DateTime.Now;

    // 4. 依次处理每个组
    foreach (var group in groupedData)
    {
        var catalogueId = group.Key;
        var items = group.ToList();

        Console.WriteLine($"[{processedGroups + 1}/{groupedData.Count}] 正在处理 CatalogueId: {catalogueId}，包含 {items.Count} 个音频文件");

        // 5. 为每个 CatalogueId 创建文件夹
        string outputDirectory = Path.Combine(baseOutputDir, $"Catalogue_{catalogueId}");
        Directory.CreateDirectory(outputDirectory);
        Console.WriteLine($"  📁 已创建目录: {outputDirectory}");

        int groupAudioProcessed = 0;
        int groupAudioFailed = 0;
        var groupStartTime = DateTime.Now;

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            Console.WriteLine($"  [{i + 1}/{items.Count}] 处理 ID {item.Id}");

            try
            {
                // 6. 下载音频文件
                Console.WriteLine($"    🔗 下载: {item.AudioUrl}");

                // 从URL中提取文件扩展名
                var uri = new Uri(item.AudioUrl);
                var originalFileName = Path.GetFileName(uri.LocalPath);
                var extension = Path.GetExtension(originalFileName);
                if (string.IsNullOrEmpty(extension))
                {
                    extension = ".mp3"; // 默认扩展名
                }

                string audioFileName = $"audio_{item.Id}_{DateTime.Now.Ticks}{extension}";
                string tempAudioPath = Path.Combine(Path.GetTempPath(), audioFileName);

                using (var response = await httpClient.GetAsync(item.AudioUrl))
                {
                    response.EnsureSuccessStatusCode();
                    await using var fileStream = File.Create(tempAudioPath);
                    await response.Content.CopyToAsync(fileStream);
                }

                var fileInfo = new FileInfo(tempAudioPath);
                Console.WriteLine($"    ✓ 下载完成，文件大小: {fileInfo.Length / 1024}KB");

                // 验证下载的文件
                if (!AudioSplitterLibrary.IsAudioFileSupported(tempAudioPath))
                {
                    Console.WriteLine($"    ⚠ 不支持的音频格式，跳过处理");
                    File.Delete(tempAudioPath);
                    groupAudioFailed++;
                    continue;
                }

                // 7. 使用 AudioSplitterLibrary 进行分割
                string itemOutputDirectory = Path.Combine(outputDirectory, $"ID_{item.Id}");
                Directory.CreateDirectory(itemOutputDirectory);

                Console.WriteLine($"    🔧 正在执行音频分割...");
                var result = await audioSplitter.ProcessAudioFileAsync(tempAudioPath, item.Content!, itemOutputDirectory);

                if (result.Success)
                {
                    Console.WriteLine($"    ✅ 分割成功! 生成 {result.SegmentCount} 个片段，用时 {result.ProcessingTime.TotalSeconds:F1}秒");

                    // 保存原始内容到文本文件
                    string contentFilePath = Path.Combine(itemOutputDirectory, "original_content.txt");
                    await File.WriteAllTextAsync(contentFilePath, item.Content ?? "");

                    groupAudioProcessed++;
                }
                else
                {
                    Console.WriteLine($"    ❌ 分割失败: {result.Error}");
                    groupAudioFailed++;
                }

                // 清理临时文件
                if (File.Exists(tempAudioPath))
                {
                    File.Delete(tempAudioPath);
                }
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"    ❌ 下载失败: {ex.Message}");
                groupAudioFailed++;
            }
            catch (UriFormatException ex)
            {
                Console.WriteLine($"    ❌ 无效的URL格式: {ex.Message}");
                groupAudioFailed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    ❌ 处理失败: {ex.Message}");
                groupAudioFailed++;
            }
        }

        totalAudioProcessed += groupAudioProcessed;
        totalAudioFailed += groupAudioFailed;
        processedGroups++;

        var groupTime = DateTime.Now - groupStartTime;
        Console.WriteLine($"  📊 CatalogueId {catalogueId} 处理完成: {groupAudioProcessed} 成功, {groupAudioFailed} 失败，用时 {groupTime.TotalMinutes:F1} 分钟\n");
    }

    // 最终统计
    var totalTime = DateTime.Now - startTime;
    Console.WriteLine("=== 任务完成 ===");
    Console.WriteLine($"📁 处理的分组数: {processedGroups}");
    Console.WriteLine($"✅ 成功处理的音频: {totalAudioProcessed}");
    Console.WriteLine($"❌ 失败的音频: {totalAudioFailed}");
    Console.WriteLine($"📊 总计音频: {totalAudioProcessed + totalAudioFailed}");
    Console.WriteLine($"⏱ 总处理时间: {totalTime.TotalMinutes:F1} 分钟");
    Console.WriteLine($"📂 输出目录: {Path.GetFullPath(baseOutputDir)}");

    httpClient.Dispose();
}
catch (FileNotFoundException ex)
{
    Console.WriteLine($"文件未找到错误: {ex.Message}");
}
catch (UnauthorizedAccessException ex)
{
    Console.WriteLine($"文件访问权限错误: {ex.Message}");
}
catch (Exception ex)
{
    Console.WriteLine($"程序执行出错: {ex.Message}");
    Console.WriteLine($"详细错误信息:");
    Console.WriteLine(ex.ToString());
}

Console.WriteLine("\n按任意键退出...");
Console.ReadKey();

