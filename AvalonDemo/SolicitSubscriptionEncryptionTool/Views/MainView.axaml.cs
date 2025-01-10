using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System;
using System.Threading.Tasks;
using System.Threading;
using MsBox.Avalonia;
using System.IO;
using System.Linq;
using System.IO.Compression;
using SolicitSubscriptionEncryptionTool.Models.Enums;
using SolicitSubscriptionEncryptionTool.Helpers;
using Avalonia.Platform.Storage;
using System.Diagnostics;

namespace SolicitSubscriptionEncryptionTool.Views;

public partial class MainView : UserControl
{
	private const string TmpDir = "tmp";
	private readonly string[] ImageFormats = [".jpg",".png",".jpeg",".gif"];
	public MainView()
	{
		InitializeComponent();
	}

	private async void SelectFileButton_Click(object sender, RoutedEventArgs e)
	{
		if(this.VisualRoot is Window window)
		{
			var result = await window.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
			{
				Title = "请选择待加密文件",
				FileTypeFilter = [new("Zip Files") { Patterns = ["*.zip", "*.rar", "*.7z"] }]
			});

			if (result != null && result.Count > 0)
			{
				fileSelectTextBox.Text = result[0].Path.LocalPath;
			}
		}
	}

	private async void EncryptClick(object sender, RoutedEventArgs e)
	{
		if (string.IsNullOrEmpty(fileSelectTextBox.Text))
		{
			// 弹出提示框
			var box = MessageBoxManager.GetMessageBoxStandard("提示", "请输入文件路径", MsBox.Avalonia.Enums.ButtonEnum.Ok);
			await box.ShowAsPopupAsync(this);
			return;
		}
		DateTime start = DateTime.Now;
		encryptProgressContentTexbBox.Text = "开始扫描文件...";
		await ScanFiles(fileSelectTextBox.Text);
		// 初始化进度条信息
		await InitProcessBar(fileSelectTextBox.Text);
		AppendValue(encryptProgressContentTexbBox, "开始加密...");
		var extractPath = await ProcessFiles(fileSelectTextBox.Text);
		AppendValue(encryptProgressContentTexbBox, "加密完成，耗时：" + (DateTime.Now - start).TotalSeconds + "秒");
		// 加密完成后打开文件夹
		OpenFolder(extractPath);
	}

	private void OpenFolder(string folderPath)
	{
		try
		{
			// 跨平台打开文件夹
			ProcessStartInfo startInfo = new()
			{
				Arguments = folderPath,
				FileName = GetSystemExplorerPath(),
				UseShellExecute = true
			};
			Process.Start(startInfo);
		}
		catch (Exception ex)
		{
			// 处理打开文件夹时可能出现的异常
			Console.WriteLine("无法打开文件夹: " + ex.Message);
		}
	}

	// 获取系统文件浏览器的路径（跨平台）
	private static string GetSystemExplorerPath()
	{
		if (OperatingSystem.IsWindows())
		{
			return "explorer";
		}
		else if (OperatingSystem.IsLinux())
		{
			return "xdg-open";
		}
		else if (OperatingSystem.IsMacOS())
		{
			return "open";
		}
		else
		{
			throw new NotSupportedException("Unsupported operating system");
		}
	}

	private async Task<string> ProcessFiles(string filePath)
	{
		int processedFiles = 0;
		string extractPath = Path.Combine(TmpDir, Path.GetFileNameWithoutExtension(filePath));
		await Task.Run(() =>
		{
			ProcessFiles(extractPath,extractPath+"_encrypted",ref processedFiles);
		});
		return extractPath + "_encrypted";
	}

	private void ProcessFiles(string sourceDirectory, string destinationDirectory, ref int processedFiles)
	{
		Directory.CreateDirectory(destinationDirectory);
		foreach (string fileName in Directory.GetFiles(sourceDirectory))
		{
			string destFileName = Path.Combine(destinationDirectory, Path.GetFileName(fileName));
			ProcessFile(fileName, destFileName);
			processedFiles++;
			UpdateProgressBar(processedFiles);
		}

		foreach (string subdir in Directory.GetDirectories(sourceDirectory))
		{
			string destSubdir = Path.Combine(destinationDirectory, Path.GetFileName(subdir));
			ProcessFiles(subdir, destSubdir, ref processedFiles);
		}
	}

	private void ProcessFile(string sourceFile, string destinationFile)
	{
		// 检查文件类型
		string extension = Path.GetExtension(sourceFile).ToLower();
		if(extension == ".mp4")
		{
			EncryptFile(sourceFile, destinationFile, FileFormatType.Video);
		}
		else if(extension == ".mp3" || extension == ".jpg")
		{
			EncryptFile(sourceFile, destinationFile, FileFormatType.Image);
		}
		else
		{
			// 非视频/音频/图片文件，直接复制
			File.Copy(sourceFile, destinationFile, true);
		}
	}

	private void EncryptFile(string sourceFile, string destinationFile, FileFormatType video)
	{
		switch (video)
		{
			case FileFormatType.Image:
			case FileFormatType.Audio:
				EncryptImageAndAudioFile(sourceFile,destinationFile);
				break;
			case FileFormatType.Video:
				EncryptVideoFile(sourceFile,destinationFile);
				break;
		}

		static void EncryptImageAndAudioFile(string sourceFile, string destinationFile)
		{
			var buffer = File.ReadAllBytes(sourceFile);
			var encryptBuffer = AesHelper.AesEncrypt(buffer);
			File.WriteAllBytes(destinationFile, encryptBuffer);
		}

		static void EncryptVideoFile(string sourceFile, string destinationFile)
		{
			var buffer = File.ReadAllBytes(sourceFile);
			var encryptBuffer = AesHelper.AesEncrypt(buffer[..16]);
			encryptBuffer = [.. encryptBuffer, .. buffer[16..]];
			File.WriteAllBytes(destinationFile, encryptBuffer);
		}
	}

	private void UpdateProgressBar(int processedFiles)
	{
		Dispatcher.UIThread.InvokeAsync(() =>
		{
			progressBar.Value = processedFiles;
		});
	}



	private async Task InitProcessBar(string filePath)
	{
		await Dispatcher.UIThread.InvokeAsync(() =>
		{
			AppendValue(encryptProgressContentTexbBox, "正在计算所有文件信息...");
		});
		// 获取所有文件和目录的总计数
		string fileName = Path.GetFileNameWithoutExtension(filePath);
		int totalFiles = Directory.GetFiles(Path.Combine(TmpDir, fileName), "*.*", SearchOption.AllDirectories).Length;
		progressBar.Maximum = totalFiles;
		AppendValue(encryptProgressContentTexbBox, "计算完毕，计算文件数：" + totalFiles);
	}

	private async Task<Exception?> ScanFiles(string filePath)
	{
		// 判断文件格式是否为压缩文件
		string[] fileFormats = [".zip", ".rar", ".7z"];
		string fileFormat = Path.GetExtension(filePath);
		if (!fileFormats.Contains(fileFormat))
			return new Exception("文件格式必须是 [zip,rar,7z] 压缩格式");
		// 创建临时目录
		TryCreateFolder();
		// 解压
	 	await UnzipFile(filePath);

		return null;
	}

	private async Task UnzipFile(string filePath)
	{
		await Dispatcher.UIThread.InvokeAsync(() =>
		{
			AppendValue(encryptProgressContentTexbBox, "开始解压文件...");
		});
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
		string extractPath = Path.Combine(TmpDir, fileNameWithoutExtension);
		// 创建解压目录
		Directory.CreateDirectory(extractPath);
		// 解压文件
		ZipFile.ExtractToDirectory(filePath, extractPath, true);
		await Dispatcher.UIThread.InvokeAsync(() =>
		{
			AppendValue(encryptProgressContentTexbBox, "解压完成！");
		});
	}

	private static bool TryCreateFolder()
	{
		if (!Directory.Exists(TmpDir))
			Directory.CreateDirectory(TmpDir);
		return true;
	}

	private static void AppendValue(TextBox textBox, string value) => textBox.Text += Environment.NewLine + value;
}
