using System.Diagnostics;

namespace audioAlignmentDemo
{
    public class FFMpegAlignment
    {
        public static async Task<List<AudioSegment>> DetectSpeechSegments(string inputPath)
        {
            var segments = new List<AudioSegment>();
            var tempDir = Path.GetTempPath();
            var silenceFile = Path.Combine(tempDir, "silence_detection.txt");

            try
            {
                // 使用FFMpeg检测静音
                var arguments = $"-i \"{inputPath}\" -af silencedetect=noise=-30dB:duration=0.5 -f null - 2> \"{silenceFile}\"";

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "ffmpeg",
                        Arguments = arguments,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };

                process.Start();
                await process.WaitForExitAsync();

                // 解析静音检测结果
                var lines = await File.ReadAllLinesAsync(silenceFile);
                var silenceEvents = ParseSilenceEvents(lines);

                // 根据静音事件创建音频段
                segments = CreateSegmentsFromSilence(silenceEvents);
            }
            finally
            {
                if (File.Exists(silenceFile))
                    File.Delete(silenceFile);
            }

            return segments;
        }

        private static List<SilenceEvent> ParseSilenceEvents(string[] lines)
        {
            var events = new List<SilenceEvent>();

            foreach (var line in lines)
            {
                if (line.Contains("silence_start"))
                {
                    var start = ExtractTimeFromLine(line, "silence_start:");
                    events.Add(new SilenceEvent { Type = "start", Time = start });
                }
                else if (line.Contains("silence_end"))
                {
                    var end = ExtractTimeFromLine(line, "silence_end:");
                    events.Add(new SilenceEvent { Type = "end", Time = end });
                }
            }

            return events;
        }

        private static double ExtractTimeFromLine(string line, string prefix)
        {
            var startIndex = line.IndexOf(prefix) + prefix.Length;
            var endIndex = line.IndexOf(' ', startIndex);
            if (endIndex == -1) endIndex = line.Length;

            var timeStr = line.Substring(startIndex, endIndex - startIndex).Trim();
            return double.TryParse(timeStr, out var time) ? time : 0;
        }

        private static List<AudioSegment> CreateSegmentsFromSilence(List<SilenceEvent> events)
        {
            var segments = new List<AudioSegment>();
            double lastEnd = 0;

            for (int i = 0; i < events.Count; i += 2)
            {
                if (i + 1 < events.Count)
                {
                    var silenceStart = events[i].Time;
                    var silenceEnd = events[i + 1].Time;

                    // 添加语音段（静音之前的部分）
                    if (silenceStart > lastEnd)
                    {
                        segments.Add(new AudioSegment
                        {
                            StartTime = lastEnd,
                            EndTime = silenceStart,
                            Duration = silenceStart - lastEnd
                        });
                    }

                    lastEnd = silenceEnd;
                }
            }

            return segments;
        }
    }

    public class SilenceEvent
    {
        public string Type { get; set; } = "";
        public double Time { get; set; }
    }
}