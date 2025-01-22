using NAudio.Wave;

namespace AudioDemo
{
	internal class Combination
	{
		/// <summary>
		/// 将多个音频文件合并为一个音频文件
		/// </summary>
		/// <param name="inputFiles">输入的音频文件</param>
		/// <param name="outputFile">输出的音频文件</param>
		public static void MixAudioFiles()
		{
			// 扫描当前目录sound文件夹下的所有mp3文件
			var inputFiles = System.IO.Directory.GetFiles("sound", "*.mp3");
			string outputFile = "sound/combination/output.wav";
			// 按文件创建时间排序
			inputFiles = [.. inputFiles.OrderBy(file => new System.IO.FileInfo(file).Name)];

			WaveFormat targetFormat = new(44100, 16, 2); // 目标格式
			using var outputStream = new WaveFileWriter(outputFile, targetFormat);
			foreach (var file in inputFiles)
			{
				using var reader = new WaveFileReader(file);
				if (!reader.WaveFormat.Equals(targetFormat))
				{
					// 使用 MediaFoundationResampler 进行格式转换，确保所有文件的采样率一致
					using var resampler = new MediaFoundationResampler(reader, targetFormat);
					resampler.ResamplerQuality = 60;
					WriteToOutput(resampler, outputStream);
				}
				else
				{
					WriteToOutput(reader, outputStream);
				}
				// 添加1秒的静音
				WriteSilence(outputStream, targetFormat, 1.0f);
			}

			// 下面这个方法可能会导致合并的文件的声音不正常
			//using var outputStream = new WaveFileWriter(outputFile, new WaveFormat(24000,16,1));
			//foreach (var file in inputFiles)
			//{
			//	using var reader = new WaveFileReader(file);
			//	if (!AreWaveFormatsEqual(outputStream.WaveFormat, reader.WaveFormat))
			//	{
			//		throw new InvalidOperationException("所有的 WAV 文件格式必须一致（采样率、通道数等）。");
			//	}

			//	byte[] buffer = new byte[1024];
			//	int bytesRead;
			//	while ((bytesRead = reader.Read(buffer, 0, buffer.Length)) > 0)
			//	{
			//		outputStream.Write(buffer, 0, bytesRead);
			//	}
			//}
		}

		static void WriteToOutput(IWaveProvider source, WaveFileWriter output)
		{
			byte[] buffer = new byte[1024];
			int bytesRead;
			while ((bytesRead = source.Read(buffer, 0, buffer.Length)) > 0)
			{
				output.Write(buffer, 0, bytesRead);
			}
		}

		static bool AreWaveFormatsEqual(WaveFormat format1, WaveFormat format2)
		{
			return format1.SampleRate == format2.SampleRate &&
				   format1.Channels == format2.Channels &&
				   format1.BitsPerSample == format2.BitsPerSample &&
				   format1.BlockAlign == format2.BlockAlign &&
				   format1.AverageBytesPerSecond == format2.AverageBytesPerSecond;
		}

		static void WriteSilence(WaveFileWriter output, WaveFormat format, float durationSeconds)
		{
			int bytesPerSample = format.BitsPerSample / 8; // 每个采样点的字节数
			int samplesPerSecond = format.SampleRate * format.Channels; // 每秒采样点数
			int totalSamples = (int)(samplesPerSecond * durationSeconds); // 总采样点数

			byte[] silenceBuffer = new byte[totalSamples * bytesPerSample]; // 静音数据缓冲区（默认为 0）

			// 将静音数据写入输出流
			output.Write(silenceBuffer, 0, silenceBuffer.Length);
		}
	}
}
