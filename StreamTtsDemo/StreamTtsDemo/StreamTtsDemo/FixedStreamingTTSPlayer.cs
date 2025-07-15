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
    /// 修复缓冲区满问题的流式TTS播放器
    /// </summary>
    public class FixedStreamingTTSPlayer
    {
        private readonly ConcurrentQueue<byte[]> _audioChunks = new();
        private readonly ManualResetEventSlim _playbackResetEvent = new(false);
        private readonly CancellationTokenSource _cancellationTokenSource = new();

        private WaveOutEvent? _waveOut;
        private BufferedWaveProvider? _bufferedWaveProvider;
        private MemoryStream _completeMP3Stream = new();
        private long _lastDecodedPosition = 0;
        private bool _isPlaying = false;
        private bool _isReceivingComplete = false;
        private readonly object _lockObject = new();

        // 缓冲配置
        private const int TARGET_BUFFER_SIZE = 1024 * 48; // 48KB 目标缓冲区大小
        private const int MAX_BUFFER_SIZE = 1024 * 96;    // 96KB 最大缓冲区大小
        private const double BUFFER_DURATION_SECONDS = 3.0; // 3秒缓冲时间

        /// <summary>
        /// 初始化音频播放器
        /// </summary>
        public void InitializePlayer()
        {
            try
            {
                // 初始化 MediaFoundation
                MediaFoundationApi.Startup();

                // 设置音频格式
                var waveFormat = new WaveFormat(24000, 16, 1);

                _bufferedWaveProvider = new BufferedWaveProvider(waveFormat)
                {
                    BufferDuration = TimeSpan.FromSeconds(BUFFER_DURATION_SECONDS),
                    DiscardOnBufferOverflow = true, // 启用溢出丢弃，防止缓冲区满
                    ReadFully = false // 允许部分读取
                };

                _waveOut = new WaveOutEvent()
                {
                    DesiredLatency = 100, // 降低延迟到100ms
                    NumberOfBuffers = 3
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

            Console.WriteLine($"===> 接收音频块: {audioData.Length} 字节");

            lock (_lockObject)
            {
                // 将新数据追加到完整的MP3流
                _completeMP3Stream.Write(audioData, 0, audioData.Length);
                Console.WriteLine($"===> MP3流总大小: {_completeMP3Stream.Length} 字节");
            }

            // 异步处理新的音频数据
            Task.Run(() => ProcessIncrementalAudio());
        }

        /// <summary>
        /// 增量处理音频数据
        /// </summary>
        private void ProcessIncrementalAudio()
        {
            lock (_lockObject)
            {
                try
                {
                    // 检查缓冲区状态
                    if (_bufferedWaveProvider == null)
                        return;

                    var currentBufferedBytes = _bufferedWaveProvider.BufferedBytes;
                    Console.WriteLine($"===> 当前缓冲区: {currentBufferedBytes} 字节");

                    // 如果缓冲区太满，等待消耗一些数据
                    if (currentBufferedBytes > MAX_BUFFER_SIZE)
                    {
                        Console.WriteLine("===> 缓冲区过满，跳过此次处理");
                        return;
                    }

                    // 只有当缓冲区有空间时才处理新数据
                    if (currentBufferedBytes < TARGET_BUFFER_SIZE)
                    {
                        ProcessNewAudioData();
                    }

                    // 检查是否应该开始播放
                    if (!_isPlaying && currentBufferedBytes >= TARGET_BUFFER_SIZE / 2)
                    {
                        StartPlayback();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"处理增量音频时出错: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 处理新的音频数据
        /// </summary>
        private void ProcessNewAudioData()
        {
            try
            {
                // 从上次解码的位置开始处理
                if (_lastDecodedPosition >= _completeMP3Stream.Length)
                {
                    return; // 没有新数据
                }

                // 创建包含所有数据的流，但只解码新部分
                var allData = _completeMP3Stream.ToArray();
                using var mp3Stream = new MemoryStream(allData);

                // 尝试解码整个MP3流
                if (TryDecodeMP3Incrementally(mp3Stream, out var newPcmData))
                {
                    if (newPcmData.Length > 0)
                    {
                        // 安全地添加PCM数据到缓冲区
                        AddPCMDataSafely(newPcmData);
                        _lastDecodedPosition = _completeMP3Stream.Length;
                        Console.WriteLine($"===> 增量解码PCM: {newPcmData.Length} 字节");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"处理新音频数据时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 安全地添加PCM数据到缓冲区
        /// </summary>
        private void AddPCMDataSafely(byte[] pcmData)
        {
            if (_bufferedWaveProvider == null || pcmData.Length == 0)
                return;

            try
            {
                // 分批添加数据，避免一次性添加太多
                const int chunkSize = 4096; // 4KB 块
                for (int i = 0; i < pcmData.Length; i += chunkSize)
                {
                    var remainingBytes = Math.Min(chunkSize, pcmData.Length - i);

                    // 检查缓冲区空间
                    var availableSpace = (int)(_bufferedWaveProvider.BufferDuration.TotalSeconds *
                                             _bufferedWaveProvider.WaveFormat.AverageBytesPerSecond) -
                                       _bufferedWaveProvider.BufferedBytes;

                    if (availableSpace < remainingBytes)
                    {
                        Console.WriteLine($"===> 缓冲区空间不足，跳过剩余 {pcmData.Length - i} 字节");
                        break;
                    }

                    _bufferedWaveProvider.AddSamples(pcmData, i, remainingBytes);
                }
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Buffer full"))
            {
                Console.WriteLine("===> 缓冲区已满，等待消耗数据");
                // 缓冲区满时不做任何操作，等待下次处理
            }
            catch (Exception ex)
            {
                Console.WriteLine($"添加PCM数据时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 增量解码MP3
        /// </summary>
        private bool TryDecodeMP3Incrementally(Stream mp3Stream, out byte[] pcmData)
        {
            pcmData = Array.Empty<byte>();

            try
            {
                mp3Stream.Position = 0;
                using var mp3Reader = new Mp3FileReader(mp3Stream);

                // 跳过已经解码的部分
                var bytesToSkip = _lastDecodedPosition * mp3Reader.WaveFormat.AverageBytesPerSecond /
                                       (_completeMP3Stream.Length > 0 ? _completeMP3Stream.Length : 1);

                if (bytesToSkip > 0 && bytesToSkip < mp3Reader.Length)
                {
                    mp3Reader.Position = Math.Min(bytesToSkip, mp3Reader.Length - 1024);
                }

                using var pcmStream = new MemoryStream();
                var buffer = new byte[4096];
                int bytesRead;

                while ((bytesRead = mp3Reader.Read(buffer, 0, buffer.Length)) > 0)
                {
                    pcmStream.Write(buffer, 0, bytesRead);
                }

                pcmData = pcmStream.ToArray();
                return pcmData.Length > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"增量解码MP3失败: {ex.Message}");
                return false;
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
            var bufferedBytes = _bufferedWaveProvider.BufferedBytes;
            Console.WriteLine($"===> 开始播放音频，缓冲区: {bufferedBytes} 字节");

            _waveOut.Play();

            // 启动缓冲区监控
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

                        // 如果缓冲区过低，尝试处理更多数据
                        if (bufferedBytes < TARGET_BUFFER_SIZE / 4 && !_isReceivingComplete)
                        {
                            Console.WriteLine($"===> 缓冲区低 ({bufferedBytes} 字节)，处理更多数据");
                            ProcessIncrementalAudio();
                        }

                        // 如果接收完成且缓冲区为空，准备结束
                        if (_isReceivingComplete && bufferedBytes < 1024)
                        {
                            Console.WriteLine("===> 播放即将完成");
                            await Task.Delay(1000); // 等待1秒确保播放完成
                            break;
                        }
                    }

                    await Task.Delay(500, _cancellationTokenSource.Token);
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

                // 处理剩余的所有数据
                ProcessIncrementalAudio();

                // 如果还没开始播放，立即开始
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
                _completeMP3Stream?.Dispose();

                _playbackResetEvent.Set();
                _playbackResetEvent.Dispose();

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