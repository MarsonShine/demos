using NAudio.Wave;

namespace audioAlignmentDemo
{
    public class SilenceBasedAlignment
    {
        public static List<AudioSegment> SplitByVoiceActivity(string audioPath, double silenceThreshold = 0.01, int minSilenceDuration = 500)
        {
            var segments = new List<AudioSegment>();

            using var reader = new AudioFileReader(audioPath);
            var buffer = new float[1024];
            var sampleRate = reader.WaveFormat.SampleRate;
            var currentTime = 0.0;
            var segmentStart = 0.0;
            var silenceStart = -1.0;
            var inSilence = false;

            while (reader.Read(buffer, 0, buffer.Length) > 0)
            {
                // 计算当前块的音量
                var rms = Math.Sqrt(buffer.Select(x => x * x).Average());
                var timeIncrement = (double)buffer.Length / sampleRate;

                if (rms < silenceThreshold)
                {
                    if (!inSilence)
                    {
                        silenceStart = currentTime;
                        inSilence = true;
                    }
                }
                else
                {
                    if (inSilence)
                    {
                        var silenceDuration = (currentTime - silenceStart) * 1000;
                        if (silenceDuration >= minSilenceDuration)
                        {
                            // 创建一个音频段
                            segments.Add(new AudioSegment
                            {
                                StartTime = segmentStart,
                                EndTime = silenceStart,
                                Duration = silenceStart - segmentStart
                            });
                            segmentStart = currentTime;
                        }
                        inSilence = false;
                    }
                }

                currentTime += timeIncrement;
            }

            // 添加最后一个段
            if (segmentStart < currentTime)
            {
                segments.Add(new AudioSegment
                {
                    StartTime = segmentStart,
                    EndTime = currentTime,
                    Duration = currentTime - segmentStart
                });
            }

            return segments;
        }
    }
}