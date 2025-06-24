using System.Diagnostics;

namespace Publishment
{
    public class Startup
    {
        public async ValueTask<int> Deploy(string[] args)
        {
            var opt = CliOptions.Parse(args);
            string tempDir = Path.Combine(Path.GetTempPath(), "deploy_" + Guid.NewGuid());

            try
            {
                // 1. 本地发布
                Publish(opt.ProjectFile, opt.Configuration, tempDir);

                // 2. 停止远程进程
                await StopRemoteProcess(opt);

                // 3. 复制文件
                await CopyFiles(tempDir, opt);

                // 4. 启动远程进程
                await StartRemoteProcess(opt);

                Success("✔ 部署完成");
                return 0;
            }
            catch (Exception ex)
            {
                Fail("✘ 部署失败: " + ex.Message);
                return 1;
            }
            finally
            {
                Try(() => Directory.Delete(tempDir, true));
            }
        }

        void Publish(string proj, string cfg, string outDir)
        {
            Info($"→ dotnet publish {Path.GetFileName(proj)}");
            Directory.CreateDirectory(outDir);
            var p = Process.Start(new ProcessStartInfo("dotnet",
                            $"publish \"{proj}\" -c {cfg} -o \"{outDir}\"")
            { RedirectStandardOutput = true })!;
            p.WaitForExit();
            if (p.ExitCode != 0) throw new($"dotnet publish 失败，代码 {p.ExitCode}");
        }

        async Task StopRemoteProcess(CliOptions opt)
        {
            Info("→ 停止远程进程");

            // 使用 PsExec 或者 WMI 远程执行
            var stopScript = $@"
Get-Process -Name dotnet -ErrorAction SilentlyContinue | 
Where-Object {{ $_.Path -like '*{opt.Match}*' }} | 
Stop-Process -Force
";

            await ExecuteRemoteCommand(opt.Host, stopScript);
        }

        async Task CopyFiles(string sourceDir, CliOptions opt)
        {
            Info("→ 复制文件到远程服务器");

            string targetUNC = $@"\\{opt.Host}\{opt.PublishPath.Replace(':', '$')}";

            if (!Directory.Exists(targetUNC))
            {
                throw new DirectoryNotFoundException($"无法访问目标路径: {targetUNC}\n请确保:\n1. 目标服务器的 C$ 共享可访问\n2. 当前用户有权限访问");
            }

            var process = Process.Start(new ProcessStartInfo("robocopy",
                            $"\"{sourceDir}\" \"{targetUNC}\" /MIR /NFL /NDL /NJH /NJS /NP")
            { RedirectStandardOutput = true })!;

            await process.WaitForExitAsync();
            if (process.ExitCode >= 8)
                throw new($"文件复制失败: {process.ExitCode}");
        }

        async Task StartRemoteProcess(CliOptions opt)
        {
            Info("→ 启动远程进程");

            var startScript = $@"
Start-Process powershell -WindowStyle Hidden -ArgumentList '-NoLogo -ExecutionPolicy Bypass -File ""{opt.RunScript}""'
";

            await ExecuteRemoteCommand(opt.Host, startScript);
        }

        async Task ExecuteRemoteCommand(string host, string script)
        {
            // 方法1: 使用 Invoke-Command (需要 PowerShell Remoting，但比 WinRM 简单)
            try
            {
                var psScript = $@"
Invoke-Command -ComputerName {host} -ScriptBlock {{
    {script}
}} -ErrorAction Stop
";

                var process = Process.Start(new ProcessStartInfo("powershell", $"-Command \"{psScript}\"")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                })!;

                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    var error = await process.StandardError.ReadToEndAsync();
                    throw new Exception($"远程命令执行失败: {error}");
                }
            }
            catch (Exception ex)
            {
                // 如果 Invoke-Command 失败，尝试其他方法
                Info($"Invoke-Command 失败: {ex.Message}");
                await ExecuteWithWMI(host, script);
            }
        }

        async Task ExecuteWithWMI(string host, string script)
        {
            // 使用 WMI 执行远程命令（更底层，但兼容性更好）
            var wmiScript = $@"
$computer = '{host}'
$scriptBlock = @'
{script}
'@

$credential = Get-Credential -Message '请输入远程服务器凭据'
Invoke-WmiMethod -Class Win32_Process -Name Create -ArgumentList ""powershell -Command `""$scriptBlock`"""" -ComputerName $computer -Credential $credential
";

            var process = Process.Start(new ProcessStartInfo("powershell", $"-Command \"{wmiScript}\"")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false
            })!;

            await process.WaitForExitAsync();
        }

        static void Info(string m) => Console.WriteLine(m);
        static void Success(string m) { Console.ForegroundColor = ConsoleColor.Green; Console.WriteLine(m); Console.ResetColor(); }
        static void Fail(string m) { Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine(m); Console.ResetColor(); }
        static void Try(Action act) { try { act(); } catch { } }
    }
}