# 音频分割处理程序使用说明

## 功能概述
此程序用于批量处理音频分割任务，主要功能包括：
1. 读取 Excel 文件中的音频URL和内容数据
2. 按 CatalogueId 分组处理
3. 自动下载音频文件
4. 使用 AudioSplitterLibrary 进行音频分割
5. 生成分割结果和报告

## 输入文件格式

程序需要一个名为 `input_data.xlsx` 的Excel文件，包含以下列：

| 列名 | 类型 | 说明 | 示例 |
|------|------|------|------|
| Id | int | 数据唯一标识 | 1, 2, 3... |
| CatalogueId | int | 分类ID（用于分组） | 100, 101, 102... |
| Content | string | 文本内容（用于语音对齐） | "Hello world, this is a test." |
| AudioUrl | string | 音频文件URL | "https://example.com/audio.mp3" |

## 输出目录结构

程序会在当前目录下创建以下结构：
```
output/
├── Catalogue_100/
│   ├── ID_1/
│   │   ├── sentence_1.wav
│   │   ├── sentence_2.wav
│   │   ├── original_content.txt
│   │   └── sentence_split_report.json
│   └── ID_2/
│       ├── sentence_1.wav
│       └── ...
└── Catalogue_101/
    └── ...
```

## 运行条件

1. 确保 `input_data.xlsx` 文件存在于程序目录中
2. 网络连接正常（用于下载音频文件）
3. 音频URL指向有效的音频文件（支持格式：WAV, MP3, M4A, WMA, AAC, FLAC, OGG, MP4）
4. Content 字段不为空（用于语音对齐）

## 错误处理

程序会自动跳过以下情况：
- AudioUrl 为空的记录
- Content 为空的记录
- 下载失败的音频文件
- 不支持的音频格式
- 分割处理失败的文件

## 性能建议

- 大批量处理时建议确保网络稳定
- 音频文件较大时可能需要较长时间
- 程序会显示详细的处理进度和统计信息