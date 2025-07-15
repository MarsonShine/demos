using System.Collections.Concurrent;
using NAudio.Wave;

namespace StreamTtsDemo
{
    /// <summary>
    /// 流式TTS播放器 - 支持边接收边播放
    /// </summary>
    public class StreamingTTSPlayer
    {
        private readonly ConcurrentQueue<byte[]> _audioChunks = new();
        private readonly ManualResetEventSlim _playbackResetEvent = new(false);
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private WaveOutEvent? _waveOut;
        private BufferedWaveProvider? _bufferedWaveProvider;
        private bool _isPlaying = false;
        private bool _isReceivingComplete = false;

        /// <summary>
        /// 初始化音频播放器
        /// </summary>
        public void InitializePlayer()
        {
            try
            {
                // 设置音频格式 (MP3 24kHz)
                var waveFormat = new WaveFormat(24000, 16, 1); // 24kHz, 16bit, 单声道

                _bufferedWaveProvider = new BufferedWaveProvider(waveFormat)
                {
                    BufferDuration = TimeSpan.FromSeconds(10), // 10秒缓冲
                    DiscardOnBufferOverflow = true
                };

                _waveOut = new WaveOutEvent();
                _waveOut.Init(_bufferedWaveProvider);
                _waveOut.PlaybackStopped += OnPlaybackStopped;

                Console.WriteLine("===> 音频播放器初始化完成");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"初始化音频播放器失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 添加音频数据块
        /// </summary>
        public void AddAudioChunk(byte[] audioData)
        {
            if (audioData == null || audioData.Length == 0)
                return;

            _audioChunks.Enqueue(audioData);

            // 如果还没开始播放，且有足够的数据，开始播放
            if (!_isPlaying && _audioChunks.Count >= 3) // 等待3个块后开始播放，减少卡顿
            {
                StartPlayback();
            }
        }

        /// <summary>
        /// 标记接收完成
        /// </summary>
        public void MarkReceivingComplete()
        {
            _isReceivingComplete = true;

            // 如果还没开始播放但有数据，立即开始播放剩余数据
            if (!_isPlaying && _audioChunks.Count > 0)
            {
                StartPlayback();
            }
        }

        /// <summary>
        /// 开始播放
        /// </summary>
        private void StartPlayback()
        {
            if (_isPlaying || _waveOut == null || _bufferedWaveProvider == null)
                return;

            _isPlaying = true;
            Console.WriteLine("===> 开始播放音频");

            // 启动音频数据处理任务
            Task.Run(async () => await ProcessAudioChunks(), _cancellationTokenSource.Token);

            // 开始播放
            _waveOut.Play();
        }

        /// <summary>
        /// 处理音频数据块
        /// </summary>
        private async Task ProcessAudioChunks()
        {
            try
            {
                while (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    if (_audioChunks.TryDequeue(out var audioChunk))
                    {
                        // 将MP3数据转换为PCM并添加到缓冲区
                        var pcmData = await ConvertMp3ToPcm(audioChunk);
                        if (pcmData?.Length > 0)
                        {
                            _bufferedWaveProvider?.AddSamples(pcmData, 0, pcmData.Length);
                        }
                    }
                    else if (_isReceivingComplete)
                    {
                        // 数据接收完成且队列为空，结束处理
                        break;
                    }
                    else
                    {
                        // 等待新数据
                        await Task.Delay(50, _cancellationTokenSource.Token);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("===> 音频处理已取消");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"处理音频数据时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 将MP3数据转换为PCM
        /// </summary>
        private async Task<byte[]?> ConvertMp3ToPcm(byte[] mp3Data)
        {
            try
            {
                using var mp3Stream = new MemoryStream(mp3Data);
                using var mp3Reader = new Mp3FileReader(mp3Stream);
                using var pcmStream = new MemoryStream();

                await mp3Reader.CopyToAsync(pcmStream);
                return pcmStream.ToArray();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"MP3转PCM失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 播放停止事件处理
        /// </summary>
        private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
        {
            if (e.Exception != null)
            {
                Console.WriteLine($"播放出错: {e.Exception.Message}");
            }
            else
            {
                Console.WriteLine("===> 音频播放完成");
            }

            _playbackResetEvent.Set();
        }

        /// <summary>
        /// 等待播放完成
        /// </summary>
        public void WaitForPlaybackComplete()
        {
            _playbackResetEvent.Wait();
        }

        /// <summary>
        /// 停止播放并释放资源
        /// </summary>
        public void Stop()
        {
            try
            {
                _cancellationTokenSource.Cancel();

                _waveOut?.Stop();
                _waveOut?.Dispose();

                _bufferedWaveProvider?.ClearBuffer();

                _playbackResetEvent.Set();
                _playbackResetEvent.Dispose();

                Console.WriteLine("===> 音频播放器已停止");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"停止播放器时出错: {ex.Message}");
            }
        }
    }
}