// See https://aka.ms/new-console-template for more information
using NAudio.Wave.SampleProviders;
using NAudio.Wave;
using System.Collections.Generic;
using System;

Console.WriteLine("Hello, World!");

string[] inputFiles = ["1.wav","2.wav"];
MixAudioFiles(inputFiles, "output.wav");

static ISampleProvider ConvertToStereo(ISampleProvider input)
{
	if (input.WaveFormat.Channels == 2)
	{
		return input;
	}
	return new MonoToStereoSampleProvider(input);
}

static void MixAudioFiles(string[] inputFiles, string outputFile)
{
	var audioFileReaders = inputFiles.Select(file => new AudioFileReader(file)).ToList();
	var sampleProviders = audioFileReaders.Select(reader => ConvertToStereo(reader)).ToList();

	// 创建一个混音器，设置为立体声（2通道）
	var mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(24000, 2)); // 设为2个通道

	// 将每个音频文件添加到混音器
	foreach (var sampleProvider in sampleProviders)
	{
		mixer.AddMixerInput(sampleProvider);
	}

	// 创建输出文件并保存合成的音频
	using var waveFileWriter = new WaveFileWriter(outputFile, mixer.WaveFormat);
	var buffer = new float[mixer.WaveFormat.SampleRate];
	int samplesRead;
	while ((samplesRead = mixer.Read(buffer, 0, buffer.Length)) > 0)
	{
		waveFileWriter.WriteSamples(buffer, 0, samplesRead);
	}
	Console.WriteLine($"混音完成！文件已保存到 {outputFile}");
}