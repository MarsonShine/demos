using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.MediaFoundation;

namespace StreamTtsDemo
{
    /// <summary>
    /// 改进的流式TTS播放器 - 解决断断续续问题
    /// </summary>
    public class ImprovedStreamingTTSPlayer
    {
        private readonly ConcurrentQueue<byte[]> _audioChunks = new();
        private readonly ManualResetEventSlim _playbackResetEvent = new(false);
        private readonly CancellationTokenSource _cancellationTokenSource = new();

        private WaveOutEvent? _waveOut;
        private BufferedWaveProvider? _bufferedWaveProvider;
        private MemoryStream _mp3Stream = new();
        private bool _isPlaying = false;
        private bool _isReceivingComplete = false;
        private readonly object _lockObject = new();

        // 缓冲配置
        private const int MIN_BUFFER_SIZE = 1024 * 32; // 32KB 最小缓冲
        private const int SAFE_BUFFER_SIZE = 1024 * 64; // 64KB 安全缓冲
        private const double BUFFER_DURATION_SECONDS = 5.0; // 5秒缓冲时间

        /// <summary>
        /// 初始化音频播放器
        /// </summary>
        public void InitializePlayer()
        {
            try
            {
                // 初始化 MediaFoundation (用于MP3解码)
                MediaFoundationApi.Startup();

                // 设置音频格式 (PCM 24kHz, 16bit, 单声道)
                var waveFormat = new WaveFormat(24000, 16, 1);

                _bufferedWaveProvider = new BufferedWaveProvider(waveFormat)
                {
                    BufferDuration = TimeSpan.FromSeconds(BUFFER_DURATION_SECONDS),
                    DiscardOnBufferOverflow = false
                };

                _waveOut = new WaveOutEvent()
                {
                    DesiredLatency = 150, // 150ms 延迟
                    NumberOfBuffers = 4   // 使用4个缓冲区提高稳定性
                };

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

            // 异步处理音频数据，避免阻塞
            Task.Run(() => ProcessAudioChunk(audioData));
        }

        /// <summary>
        /// 处理单个音频数据块
        /// </summary>
        private void ProcessAudioChunk(byte[] audioData)
        {
            lock (_lockObject)
            {
                try
                {
                    // 将音频数据追加到MP3流
                    _mp3Stream.Write(audioData, 0, audioData.Length);
                    Console.WriteLine($"===> 添加音频块: {audioData.Length} 字节，总大小: {_mp3Stream.Length} 字节");

                    // 尝试解码新的音频数据
                    TryDecodeLatestChunk();

                    // 检查是否应该开始播放
                    CheckAndStartPlayback();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"处理音频块时出错: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 尝试解码最新的音频块
        /// </summary>
        private void TryDecodeLatestChunk()
        {
            try
            {
                // 创建当前MP3流的副本进行解码
                var mp3Data = _mp3Stream.ToArray();
                if (mp3Data.Length < 1024) // 太小的数据块可能无法解码
                {
                    return;
                }

                using var tempStream = new MemoryStream(mp3Data);

                // 尝试解码MP3
                if (TryDecodeMp3Stream(tempStream, out var pcmData))
                {
                    if (pcmData.Length > 0 && _bufferedWaveProvider != null)
                    {
                        // 清空之前的缓冲区，使用最新的完整PCM数据
                        _bufferedWaveProvider.ClearBuffer();
                        _bufferedWaveProvider.AddSamples(pcmData, 0, pcmData.Length);

                        Console.WriteLine($"===> 成功解码PCM数据: {pcmData.Length} 字节");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"解码音频时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 尝试解码MP3流
        /// </summary>
        private bool TryDecodeMp3Stream(Stream mp3Stream, out byte[] pcmData)
        {
            pcmData = Array.Empty<byte>();

            try
            {
                mp3Stream.Position = 0;

                using var mp3Reader = new Mp3FileReader(mp3Stream);
                using var pcmStream = new MemoryStream();

                // 将MP3解码为PCM
                var buffer = new byte[4096];
                int bytesRead;
                while ((bytesRead = mp3Reader.Read(buffer, 0, buffer.Length)) > 0)
                {
                    pcmStream.Write(buffer, 0, bytesRead);
                }

                pcmData = pcmStream.ToArray();
                return pcmData.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 检查并开始播放
        /// </summary>
        private void CheckAndStartPlayback()
        {
            if (!_isPlaying && _bufferedWaveProvider != null &&
                _bufferedWaveProvider.BufferedBytes >= MIN_BUFFER_SIZE)
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
            Console.WriteLine($"===> 开始播放音频，缓冲区大小: {_bufferedWaveProvider.BufferedBytes} 字节");

            _waveOut.Play();

            // 启动缓冲区监控任务
            Task.Run(MonitorBufferAsync, _cancellationTokenSource.Token);
        }

        /// <summary>
        /// 监控缓冲区状态
        /// </summary>
        private async Task MonitorBufferAsync()
        {
            try
            {
                while (!_cancellationTokenSource.Token.IsCancellationRequested && _isPlaying)
                {
                    if (_bufferedWaveProvider != null)
                    {
                        var bufferedBytes = _bufferedWaveProvider.BufferedBytes;

                        // 如果缓冲区过满，暂停接收数据
                        if (bufferedBytes >= SAFE_BUFFER_SIZE)
                        {
                            Console.WriteLine("===> 缓冲区已满，暂停接收数据");
                            await Task.Delay(500, _cancellationTokenSource.Token); // 等待缓冲区空余
                        }
                        else
                        {
                            // 继续接收数据
                            if (_audioChunks.Count > 0)
                            {
                                if (_audioChunks.TryDequeue(out var audioData))
                                    ProcessAudioChunk(audioData);
                            }
                        }

                        // 如果数据接收完成且缓冲区即将为空，准备结束
                        if (_isReceivingComplete && bufferedBytes < 1024)
                        {
                            Console.WriteLine("===> 播放即将完成");
                            break;
                        }
                        //var bufferedBytes = _bufferedWaveProvider.BufferedBytes;
                        //var bufferedDuration = _bufferedWaveProvider.BufferedDuration;

                        //// 如果数据接收完成且缓冲区即将为空，准备结束
                        //if (_isReceivingComplete && bufferedBytes < 1024)
                        //{
                        //    Console.WriteLine("===> 播放即将完成");
                        //    break;
                        //}

                        //// 如果缓冲区过低且还在接收数据，重新启动播放器
                        //if (bufferedBytes < MIN_BUFFER_SIZE / 4 && !_isReceivingComplete)
                        //{
                        //    Console.WriteLine("===> 缓冲区不足，重新处理音频数据");

                        //    // 停止当前播放
                        //    _waveOut?.Stop();

                        //    // 等待一小段时间让更多数据到达
                        //    await Task.Delay(300, _cancellationTokenSource.Token);

                        //    // 重新处理所有音频数据
                        //    lock (_lockObject)
                        //    {
                        //        TryDecodeLatestChunk();
                        //    }

                        //    // 如果有足够数据，重新开始播放
                        //    if (_bufferedWaveProvider.BufferedBytes >= MIN_BUFFER_SIZE / 2)
                        //    {
                        //        _waveOut?.Play();
                        //        Console.WriteLine("===> 重新开始播放");
                        //    }
                        //}
                    }

                    await Task.Delay(200, _cancellationTokenSource.Token); // 每200ms检查一次
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("===> 缓冲区监控已取消");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"监控缓冲区时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 标记接收完成
        /// </summary>
        public void MarkReceivingComplete()
        {
            lock (_lockObject)
            {
                _isReceivingComplete = true;
                Console.WriteLine("===> 数据接收完成");

                // 最后一次完整解码
                TryDecodeLatestChunk();

                // 如果还没开始播放但有数据，立即开始播放
                if (!_isPlaying && _bufferedWaveProvider?.BufferedBytes > 0)
                {
                    StartPlayback();
                }
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

            // 添加检查缓冲区的逻辑，以决定是否重新启动播放
            if (_bufferedWaveProvider?.BufferedBytes > MIN_BUFFER_SIZE)
            {
                Console.WriteLine("===> 缓冲区有足够数据，重新开始播放");
                _waveOut?.Play();
            }
            else
            {
                Console.WriteLine("===> 暂停播放，缓冲区数据不足");
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
                _mp3Stream?.Dispose();

                _playbackResetEvent.Set();
                _playbackResetEvent.Dispose();

                // 清理 MediaFoundation
                try
                {
                    MediaFoundationApi.Shutdown();
                }
                catch
                {
                    // 忽略关闭错误
                }

                Console.WriteLine("===> 音频播放器已停止");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"停止播放器时出错: {ex.Message}");
            }
        }
    }
}