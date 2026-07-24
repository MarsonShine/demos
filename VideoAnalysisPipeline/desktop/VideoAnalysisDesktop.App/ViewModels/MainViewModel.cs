using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using VideoAnalysisDesktop.Application.Services;
using VideoAnalysisDesktop.Infrastructure.Data;
using VideoAnalysisDesktop.Infrastructure.FileSystem;
using VideoAnalysisDesktop.Infrastructure.Python;
using VideoAnalysisDesktop.Infrastructure.Security;

namespace VideoAnalysisDesktop.App.ViewModels;

/// <summary>
/// Owns the UI-facing job lifecycle. A job may start only from a successful
/// preflight; a retry always revalidates the saved input snapshot before the
/// engine receives its <c>--resume</c> argument.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly JobRepository _jobRepo;
    private readonly PreflightService _preflightService;
    private readonly CommandBuilder _commandBuilder;
    private readonly SecretManager _secretManager;
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    private PreflightResult? _preflightResult;
    private string? _preflightInputRoot;
    private JobRecord? _resumeCandidate;
    private string? _currentJobId;
    private string? _currentAttemptId;
    private PythonProcessHost? _currentProcess;
    private JsonlTailReader? _eventReader;
    private bool _cancelRequested;
    private bool _suppressPathInvalidation;
    private int _engineReportedFailure;
    private string? _engineFailureSummary;

    public MainViewModel(
        JobRepository jobRepo,
        PreflightService preflightService,
        CommandBuilder commandBuilder,
        SecretManager secretManager)
    {
        _jobRepo = jobRepo;
        _preflightService = preflightService;
        _commandBuilder = commandBuilder;
        _secretManager = secretManager;
    }

    [ObservableProperty]
    private string _inputRoot = "";

    [ObservableProperty]
    private string _outputRoot = "";

    [ObservableProperty]
    private string _statusText = "Select input and output directories, then run preflight.";

    [ObservableProperty]
    private string _progressText = "";

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string _stageText = "";

    [ObservableProperty]
    private string _elapsedText = "";

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _isPreflightRunning;

    [ObservableProperty]
    private bool _canStart;

    [ObservableProperty]
    private bool _canCancel;

    [ObservableProperty]
    private bool _canResume;

    [ObservableProperty]
    private bool _canOpenOutput;

    [ObservableProperty]
    private bool _preflightPassed;

    [ObservableProperty]
    private int _preflightTotalItems;

    [ObservableProperty]
    private string _preflightSummary = "Run preflight before starting a job.";

    [ObservableProperty]
    private string _presetDescription = PresetProvider.Description;

    public bool CanChangePaths => !IsRunning && !IsPreflightRunning;

    public ObservableCollection<string> LogEntries { get; } = new();

    partial void OnInputRootChanged(string value) => InvalidatePreflightForPathChange();

    partial void OnOutputRootChanged(string value) => InvalidatePreflightForPathChange();

    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(CanChangePaths));

    partial void OnIsPreflightRunningChanged(bool value) => OnPropertyChanged(nameof(CanChangePaths));

    [RelayCommand]
    private void BrowseInput()
    {
        if (!CanChangePaths)
        {
            return;
        }

        var dialog = new OpenFolderDialog { Title = "Select Input Root Directory" };
        if (Directory.Exists(InputRoot))
        {
            dialog.InitialDirectory = InputRoot;
        }

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        InputRoot = dialog.FolderName;
        _jobRepo.SetAppState("last_input_root", InputRoot);
    }

    [RelayCommand]
    private void BrowseOutput()
    {
        if (!CanChangePaths)
        {
            return;
        }

        var dialog = new OpenFolderDialog { Title = "Select Output Root Directory" };
        if (Directory.Exists(OutputRoot))
        {
            dialog.InitialDirectory = OutputRoot;
        }

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        OutputRoot = dialog.FolderName;
        _jobRepo.SetAppState("last_output_root", OutputRoot);
    }

    [RelayCommand]
    private async Task RunPreflightAsync()
    {
        if (IsRunning || IsPreflightRunning)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(InputRoot))
        {
            StatusText = "Select an input directory before running preflight.";
            return;
        }

        var inputRoot = InputRoot;
        IsPreflightRunning = true;
        CanStart = false;
        PreflightPassed = false;
        StatusText = "Running preflight scan...";
        PreflightSummary = "Scanning input files and creating an input snapshot...";

        try
        {
            var resultFile = CreatePreflightResultPath();
            var result = await _preflightService.RunPreflightAsync(inputRoot, resultFile);

            // A user could only change a path programmatically while preflight is
            // running. Do not apply a result to a different path in that case.
            if (!PathsEqual(inputRoot, InputRoot))
            {
                Log("Discarded a preflight result because the input directory changed.");
                return;
            }

            _preflightResult = result;
            _preflightInputRoot = NormalizePath(inputRoot);
            PreflightTotalItems = result.TotalItems;
            PreflightPassed = result.Passed;

            if (result.Passed)
            {
                PreflightSummary = $"Found {result.TotalItems} item(s). Input snapshot is ready.";
                StatusText = "Preflight passed. Click Start to begin processing.";
                CanStart = true;
            }
            else
            {
                var errors = result.Errors.Select(error => error.Message).Where(message => !string.IsNullOrWhiteSpace(message));
                PreflightSummary = $"Preflight failed:{Environment.NewLine}{string.Join(Environment.NewLine, errors)}";
                StatusText = "Preflight failed. Fix the reported issues and try again.";
            }

            Log($"Preflight completed: {result.TotalItems} item(s), passed={result.Passed}.");
        }
        catch (Exception exception)
        {
            _preflightResult = null;
            _preflightInputRoot = null;
            PreflightPassed = false;
            PreflightTotalItems = 0;
            PreflightSummary = "Preflight could not be completed. See the log for details.";
            StatusText = "Preflight error.";
            Log($"Preflight error: {exception.Message}");
        }
        finally
        {
            IsPreflightRunning = false;
        }
    }

    [RelayCommand]
    private async Task StartJobAsync()
    {
        if (!await _operationGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            if (IsRunning || IsPreflightRunning)
            {
                return;
            }

            if (!HasUsablePreflight())
            {
                StatusText = "Run a successful preflight for the selected input directory before starting.";
                return;
            }

            if (string.IsNullOrWhiteSpace(OutputRoot))
            {
                StatusText = "Select an output directory before starting.";
                return;
            }

            if (_jobRepo.GetRunningJobId() is not null)
            {
                StatusText = "Another job is already running in this installation.";
                return;
            }

            var validation = DirectoryValidator.Validate(
                InputRoot,
                OutputRoot,
                _preflightResult!.TotalInputSizeBytes);
            if (!validation.IsValid)
            {
                StatusText = string.Join(Environment.NewLine, validation.Errors);
                return;
            }

            var secrets = LoadSecretsOrReportFailure();
            if (secrets is null)
            {
                return;
            }

            var job = new JobRecord
            {
                JobId = Guid.NewGuid().ToString("N"),
                CreatedByUser = Environment.UserName,
                InputRoot = InputRoot,
                OutputRoot = OutputRoot,
                Status = "Running",
                PresetVersion = PresetProvider.PresetVersion,
                PresetHash = GetCurrentDesktopContractHash(),
                InputSnapshotHash = _preflightResult.SnapshotHash,
                EngineVersion = RuntimePaths.GetEngineVersion(_commandBuilder.PythonExecutablePath),
                StartedAtUtc = DateTime.UtcNow.ToString("O"),
            };

            var attempt = CreateAttemptRecord(job.JobId, isResume: false, sequenceNumber: 1);
            if (!_jobRepo.TryCreateRunningJob(job, attempt, _preflightResult.SnapshotJson))
            {
                StatusText = "Another job acquired the processing lock before this job could start.";
                return;
            }

            _resumeCandidate = null;
            CanResume = false;

            await RunAttemptAsync(job, _preflightResult, secrets, attempt);
        }
        catch (Exception exception)
        {
            StatusText = "Could not create the job. See the log for details.";
            Log($"Job creation error: {exception.GetType().Name}.");
        }
        finally
        {
            _operationGate.Release();
        }
    }

    [RelayCommand]
    private void CancelJob()
    {
        if (!IsRunning || _currentJobId is null || _cancelRequested)
        {
            return;
        }

        _cancelRequested = true;
        CanCancel = false;
        StatusText = "Cancelling...";
        StageText = "cancelling";
        _jobRepo.UpdateJobStatus(_currentJobId, "Cancelling");
        _currentProcess?.Cancel();
    }

    [RelayCommand]
    private async Task ResumeJobAsync()
    {
        if (!await _operationGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            if (IsRunning || IsPreflightRunning)
            {
                return;
            }

            var job = _resumeCandidate ?? _jobRepo.GetLatestResumableJob();
            if (job is null)
            {
                CanResume = false;
                StatusText = "There is no failed or interrupted job to continue.";
                return;
            }

            if (!PathsEqual(InputRoot, job.InputRoot) || !PathsEqual(OutputRoot, job.OutputRoot))
            {
                StatusText = "The selected paths no longer match the failed job. Reload the failed job before continuing.";
                return;
            }

            var pathError = ValidateResumePaths(job, out var resumeValidationError);
            if (!pathError)
            {
                StatusText = resumeValidationError;
                return;
            }

            StatusText = "Re-running preflight to verify the failed job's input snapshot...";
            PreflightPassed = false;
            CanResume = false;
            var preflight = await _preflightService.RunPreflightAsync(job.InputRoot, CreatePreflightResultPath());
            ApplyPreflightResult(preflight, job.InputRoot);

            if (!preflight.Passed)
            {
                StatusText = "Resume is blocked because preflight failed.";
                CanResume = true;
                return;
            }

            var resumeValidation = DirectoryValidator.Validate(
                job.InputRoot,
                job.OutputRoot,
                preflight.TotalInputSizeBytes,
                allowExistingOutput: true);
            if (!resumeValidation.IsValid)
            {
                StatusText = string.Join(Environment.NewLine, resumeValidation.Errors);
                CanResume = true;
                return;
            }

            if (!ResumeContractMatches(job, preflight))
            {
                StatusText = "Resume is blocked because the input snapshot, preset, or engine has changed. Start a new job instead.";
                CanResume = true;
                return;
            }

            if (_jobRepo.GetRunningJobId() is not null)
            {
                StatusText = "Another job is already running in this installation.";
                CanResume = true;
                return;
            }

            var secrets = LoadSecretsOrReportFailure();
            if (secrets is null)
            {
                CanResume = true;
                return;
            }

            var attempt = CreateAttemptRecord(
                job.JobId,
                isResume: true,
                sequenceNumber: _jobRepo.GetNextAttemptSequence(job.JobId));
            if (!_jobRepo.TryBeginResumeAttempt(job.JobId, attempt))
            {
                StatusText = "The failed job can no longer be resumed because another job owns the processing lock.";
                CanResume = true;
                return;
            }

            await RunAttemptAsync(job, preflight, secrets, attempt);
        }
        catch (Exception exception)
        {
            StatusText = "Could not resume the failed job. See the log for details.";
            CanResume = _resumeCandidate is not null;
            Log($"Resume error: {exception.GetType().Name}.");
        }
        finally
        {
            _operationGate.Release();
        }
    }

    [RelayCommand]
    private void OpenOutputFolder()
    {
        if (!Directory.Exists(OutputRoot))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = OutputRoot,
            UseShellExecute = true,
        });
    }

    [RelayCommand]
    private void OpenAdminSettings()
    {
        var packagedExecutable = Path.Combine(AppContext.BaseDirectory, "VideoAnalysisDesktop.App.exe");
        var executable = File.Exists(packagedExecutable) ? packagedExecutable : Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
        {
            StatusText = "Could not locate the desktop application executable for administrator settings.";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = "--configure",
                UseShellExecute = true,
                Verb = "runas",
            });
            StatusText = "An elevated Administrator Settings window was opened. Save the credentials there, then return here.";
            Log("Opened the elevated Administrator Settings window.");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // This is the normal result when the UAC prompt is declined.
            StatusText = "Administrator settings were not opened. Approve the Windows elevation prompt to update credentials.";
        }
        catch (Exception exception)
        {
            StatusText = "Administrator settings could not be opened.";
            Log($"Could not open Administrator Settings: {exception.GetType().Name}.");
        }
    }

    /// <summary>
    /// MainWindow uses this to prevent a user from tearing down the visual host
    /// while the job record and child process are still being finalized.
    /// </summary>
    public bool CanCloseWindow()
    {
        if (!IsRunning)
        {
            return true;
        }

        MessageBox.Show(
            "A job is still running. Cancel it and wait for cancellation to finish before closing the application.",
            "Video Analysis Desktop",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        return false;
    }

    public async Task InitializeAsync()
    {
        var lastInput = _jobRepo.GetAppState("last_input_root");
        var lastOutput = _jobRepo.GetAppState("last_output_root");
        _suppressPathInvalidation = true;
        try
        {
            if (!string.IsNullOrWhiteSpace(lastInput))
            {
                InputRoot = lastInput;
            }

            if (!string.IsNullOrWhiteSpace(lastOutput))
            {
                OutputRoot = lastOutput;
            }
        }
        finally
        {
            _suppressPathInvalidation = false;
        }

        // The app uses an installation-wide single-instance mutex. Once this
        // process owns it, a Running row can only be left by a previous crashed
        // application.
        _jobRepo.MarkRunningJobsInterrupted("The desktop application was closed before the job finished.");
        var resumableJob = _jobRepo.GetLatestResumableJob();
        if (resumableJob is not null)
        {
            LoadResumeCandidate(resumableJob);
        }

        await Task.CompletedTask;
    }

    private async Task RunAttemptAsync(
        JobRecord job,
        PreflightResult preflight,
        AzureOpenAiSecrets secrets,
        AttemptRecord attempt)
    {
        var attemptId = attempt.AttemptId;
        var attemptDirectory = GetAttemptDir(job.JobId, attemptId);
        var eventFile = Path.Combine(attemptDirectory, "events.jsonl");
        var stdoutFile = Path.Combine(attemptDirectory, "stdout.txt");
        var stderrFile = Path.Combine(attemptDirectory, "stderr.txt");
        const bool attemptCreated = true;
        var exitCode = -1;
        CancellationTokenSource? pumpCancellation = null;
        Task? eventPumpTask = null;
        Task? elapsedTask = null;
        var stopwatch = Stopwatch.StartNew();

        _currentJobId = job.JobId;
        _currentAttemptId = attemptId;
        _cancelRequested = false;
        Interlocked.Exchange(ref _engineReportedFailure, 0);
        _engineFailureSummary = null;
        IsRunning = true;
        CanCancel = true;
        CanStart = false;
        CanOpenOutput = false;
        StatusText = attempt.IsResume ? "Continuing failed job..." : "Running...";
        ProgressText = $"0 / {preflight.TotalItems}";
        ProgressPercent = 0;
        StageText = "starting";
        ElapsedText = "Elapsed: 00:00:00";

        try
        {
            Directory.CreateDirectory(attemptDirectory);
            var configFile = Path.Combine(attemptDirectory, "pipeline_config.json");
            await CopyPipelineConfigTemplateAsync(configFile);
            ThrowIfCancellationRequested();

            var environment = BuildEngineEnvironment(secrets);
            var stdoutLock = new object();
            var stderrLock = new object();
            await using var stdoutWriter = new StreamWriter(stdoutFile, append: false, Encoding.UTF8) { AutoFlush = true };
            await using var stderrWriter = new StreamWriter(stderrFile, append: false, Encoding.UTF8) { AutoFlush = true };

            _currentProcess = new PythonProcessHost();
            _currentProcess.OutputDataReceived += (_, eventArgs) =>
            {
                if (eventArgs.Data is null)
                {
                    return;
                }

                lock (stdoutLock)
                {
                    stdoutWriter.WriteLine(eventArgs.Data);
                }
                PostToUi(() => Log(RedactApiKey(eventArgs.Data, secrets.ApiKey)));
            };
            _currentProcess.ErrorDataReceived += (_, eventArgs) =>
            {
                if (eventArgs.Data is null)
                {
                    return;
                }

                lock (stderrLock)
                {
                    stderrWriter.WriteLine(eventArgs.Data);
                }
                PostToUi(() => Log($"[stderr] {RedactApiKey(eventArgs.Data, secrets.ApiKey)}"));
            };

            var arguments = _commandBuilder.BuildBatchArgs(
                configFile,
                eventFile,
                attemptId,
                job.InputRoot,
                job.OutputRoot,
                resume: attempt.IsResume);
            ThrowIfCancellationRequested();
            _currentProcess.Start(
                _commandBuilder.PythonExecutablePath,
                arguments,
                _commandBuilder.WorkingDirectory,
                environment);
            _jobRepo.UpdateAttemptProcess(attemptId, _currentProcess.ProcessId);
            if (_cancelRequested)
            {
                // Cancel may have landed in the tiny window between the
                // pre-start check and Process.Start. The earlier Cancel call
                // could not kill a process that did not exist yet.
                _currentProcess.Cancel();
            }

            _eventReader = new JsonlTailReader(eventFile);
            pumpCancellation = new CancellationTokenSource();
            eventPumpTask = Task.Run(() => PumpEventsAsync(_eventReader, pumpCancellation.Token));
            elapsedTask = Task.Run(() => PumpElapsedTimeAsync(stopwatch, pumpCancellation.Token));

            exitCode = await _currentProcess.WaitForExitAsync();
            stopwatch.Stop();
            pumpCancellation.Cancel();
            await AwaitPumpTaskAsync(eventPumpTask);
            await AwaitPumpTaskAsync(elapsedTask);
            DrainFinalEvents(_eventReader, secrets.ApiKey);
            ElapsedText = $"Elapsed: {stopwatch.Elapsed:hh\\:mm\\:ss}";

            if (_cancelRequested)
            {
                _jobRepo.UpdateJobStatus(
                    job.JobId,
                    "Cancelled",
                    errorSummary: "Cancelled by the operator.",
                    endedAtUtc: DateTime.UtcNow.ToString("O"));
                StatusText = "Cancelled.";
                StageText = "cancelled";
                SetResumeCandidate(job);
            }
            else if (exitCode != 0 || Volatile.Read(ref _engineReportedFailure) != 0)
            {
                var engineFailureSummary = Volatile.Read(ref _engineFailureSummary);
                var failureSummary = Volatile.Read(ref _engineReportedFailure) != 0 &&
                    !string.IsNullOrWhiteSpace(engineFailureSummary)
                    ? RedactApiKey(engineFailureSummary, secrets.ApiKey)
                    : $"Python engine exited with code {exitCode}.";
                _jobRepo.UpdateJobStatus(
                    job.JobId,
                    "Failed",
                    errorSummary: failureSummary,
                    endedAtUtc: DateTime.UtcNow.ToString("O"));
                StatusText = exitCode != 0
                    ? $"Failed with exit code {exitCode}."
                    : "Failed because the Python engine reported a terminal error.";
                StageText = "failed";
                SetResumeCandidate(job);
            }
            else
            {
                var outputValidation = ModOutputValidator.Validate(job.OutputRoot, preflight.TotalItems);
                if (!outputValidation.IsValid)
                {
                    var outputErrors = string.Join(Environment.NewLine, outputValidation.Errors);
                    _jobRepo.UpdateJobStatus(
                        job.JobId,
                        "Failed",
                        errorSummary: $"MOD output validation failed:{Environment.NewLine}{outputErrors}",
                        endedAtUtc: DateTime.UtcNow.ToString("O"));
                    StatusText = "Processing finished, but output validation failed.";
                    StageText = "validation failed";
                    Log($"MOD output validation failed: {outputErrors}");
                    SetResumeCandidate(job);
                }
                else
                {
                    _jobRepo.UpdateJobStatus(
                        job.JobId,
                        "Succeeded",
                        endedAtUtc: DateTime.UtcNow.ToString("O"),
                        resultPath: job.OutputRoot);
                    StatusText = "Completed successfully.";
                    StageText = "completed";
                    CanOpenOutput = true;
                    _resumeCandidate = null;
                    CanResume = false;
                }
            }
        }
        catch (Exception exception)
        {
            var safeMessage = RedactApiKey(exception.Message, secrets.ApiKey);
            var status = _cancelRequested ? "Cancelled" : "Failed";
            _jobRepo.UpdateJobStatus(
                job.JobId,
                status,
                errorSummary: _cancelRequested ? "Cancelled by the operator." : safeMessage,
                endedAtUtc: DateTime.UtcNow.ToString("O"));
            StatusText = _cancelRequested ? "Cancelled." : "The job could not be started or completed.";
            StageText = _cancelRequested ? "cancelled" : "failed";
            Log($"Job error: {safeMessage}");
            SetResumeCandidate(job);
        }
        finally
        {
            stopwatch.Stop();
            pumpCancellation?.Cancel();
            await AwaitPumpTaskAsync(eventPumpTask);
            await AwaitPumpTaskAsync(elapsedTask);
            pumpCancellation?.Dispose();

            if (attemptCreated)
            {
                _jobRepo.UpdateAttemptResult(attemptId, exitCode, DateTime.UtcNow.ToString("O"));
            }

            _eventReader?.Dispose();
            _eventReader = null;
            _currentProcess?.Dispose();
            _currentProcess = null;
            _currentJobId = null;
            _currentAttemptId = null;
            IsRunning = false;
            CanCancel = false;
            CanStart = false;
        }
    }

    private AzureOpenAiSecrets? LoadSecretsOrReportFailure()
    {
        try
        {
            var secrets = _secretManager.Load();
            if (secrets is not null &&
                !string.IsNullOrWhiteSpace(secrets.Endpoint) &&
                !string.IsNullOrWhiteSpace(secrets.ApiKey) &&
                !string.IsNullOrWhiteSpace(secrets.Deployment))
            {
                return secrets;
            }
        }
        catch
        {
            // Do not surface potentially sensitive decryption details in the UI.
        }

        StatusText = "Azure OpenAI credentials are not configured. Open Admin Settings to configure them.";
        return null;
    }

    private void ThrowIfCancellationRequested()
    {
        if (_cancelRequested)
        {
            throw new OperationCanceledException("The operator cancelled the job before the Python engine started.");
        }
    }

    private Dictionary<string, string> BuildEngineEnvironment(AzureOpenAiSecrets secrets)
    {
        var ffmpegDirectory = RuntimePaths.TryFindFfmpegDirectory(_commandBuilder.PythonExecutablePath);
        if (ffmpegDirectory is null)
        {
            throw new FileNotFoundException("Bundled FFmpeg was not found next to the Python engine.");
        }

        var ffmpegValidation = FfmpegRuntimeValidator.Validate(ffmpegDirectory);
        if (!ffmpegValidation.IsValid)
        {
            throw new InvalidOperationException(
                "Bundled FFmpeg is not runnable. Re-stage the engine with real FFmpeg binaries (not package-manager shims). " +
                string.Join(" ", ffmpegValidation.Errors));
        }

        var dataRoot = DesktopDataPaths.GetDataRoot();
        var cacheDirectory = Path.Combine(dataRoot, "cache", "bgm");
        Directory.CreateDirectory(cacheDirectory);

        return new Dictionary<string, string>
        {
            ["PYTHONUTF8"] = "1",
            ["AZURE_OPENAI_ENDPOINT"] = secrets.Endpoint,
            ["AZURE_OPENAI_API_KEY"] = secrets.ApiKey,
            ["AZURE_OPENAI_DEPLOYMENT"] = secrets.Deployment,
            ["FASTER_WHISPER_DOWNLOAD_ROOT"] = Path.Combine(dataRoot, "models", "faster-whisper"),
            ["TORCH_HOME"] = Path.Combine(dataRoot, "models", "torch"),
            ["AUDIO_CACHE_DIR"] = cacheDirectory,
            ["PATH"] = string.Join(
                Path.PathSeparator,
                new[] { ffmpegDirectory, Environment.GetEnvironmentVariable("PATH") ?? string.Empty }
                    .Where(value => !string.IsNullOrWhiteSpace(value))),
        };
    }

    private async Task CopyPipelineConfigTemplateAsync(string destinationPath)
    {
        var templatePath = Path.Combine(AppContext.BaseDirectory, "Assets", "pipeline_config.json");
        if (!File.Exists(templatePath))
        {
            throw new FileNotFoundException("The bundled pipeline configuration template is missing.", templatePath);
        }

        await using var source = File.OpenRead(templatePath);
        await using var destination = File.Create(destinationPath);
        await source.CopyToAsync(destination);
    }

    private static string GetCurrentDesktopContractHash()
    {
        var templatePath = Path.Combine(AppContext.BaseDirectory, "Assets", "pipeline_config.json");
        if (!File.Exists(templatePath))
        {
            throw new FileNotFoundException("The bundled pipeline configuration template is missing.", templatePath);
        }

        return PresetProvider.ComputeDesktopContractHash(File.ReadAllText(templatePath, Encoding.UTF8));
    }

    private async Task PumpEventsAsync(JsonlTailReader eventReader, CancellationToken cancellationToken)
    {
        var readFailureReported = false;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                foreach (var pipelineEvent in eventReader.ReadNewLines())
                {
                    ObserveTerminalEvent(pipelineEvent);
                    PersistAttemptEvent(pipelineEvent);
                    PostToUi(() => HandleEvent(pipelineEvent));
                }

                readFailureReported = false;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                if (!readFailureReported)
                {
                    readFailureReported = true;
                    PostToUi(() => Log("The progress event file is temporarily unavailable; retrying."));
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task PumpElapsedTimeAsync(Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            PostToUi(() => ElapsedText = $"Elapsed: {stopwatch.Elapsed:hh\\:mm\\:ss}");
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static async Task AwaitPumpTaskAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            // Normal during job completion/cancellation.
        }
        catch
        {
            // Progress reporting is observational. It must never turn a finished
            // engine run into a failed job record.
        }
    }

    private void DrainFinalEvents(JsonlTailReader eventReader, string apiKey)
    {
        try
        {
            foreach (var pipelineEvent in eventReader.ReadNewLines())
            {
                ObserveTerminalEvent(pipelineEvent);
                PersistAttemptEvent(pipelineEvent);
                HandleEvent(pipelineEvent, apiKey);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log($"Could not read final progress events: {RedactApiKey(exception.Message, apiKey)}");
        }
    }

    private void HandleEvent(JsonlEvent pipelineEvent, string? apiKey = null)
    {
        var totalItems = pipelineEvent.TotalItems > 0 ? pipelineEvent.TotalItems : PreflightTotalItems;
        var completedItems = Math.Max(0, pipelineEvent.CompletedItems);
        if (totalItems > 0)
        {
            ProgressText = $"{completedItems} / {totalItems}";
            ProgressPercent = Math.Clamp((double)completedItems / totalItems * 100, 0, 100);
        }

        switch (pipelineEvent.Event)
        {
            case "run_started":
                StageText = "starting";
                break;
            case "item_started":
                StageText = $"Processing item {pipelineEvent.ItemIndex}: {pipelineEvent.RelativeDir}";
                break;
            case "stage_changed":
                StageText = pipelineEvent.Stage ?? "processing";
                if (string.Equals(pipelineEvent.Status, "failed", StringComparison.OrdinalIgnoreCase))
                {
                    Log($"WARNING: Stage '{pipelineEvent.Stage}' failed for item {pipelineEvent.ItemIndex}.");
                }
                break;
            case "item_completed":
                StageText = $"Completed item {pipelineEvent.ItemIndex}";
                break;
            case "run_completed":
                StageText = "completed";
                break;
            case "run_failed":
                StageText = "failed";
                if (!string.IsNullOrWhiteSpace(pipelineEvent.ErrorSummary))
                {
                    Log($"FAILED: {RedactApiKey(pipelineEvent.ErrorSummary, apiKey)}");
                }
                break;
        }
    }

    private void PersistAttemptEvent(JsonlEvent pipelineEvent)
    {
        var attemptId = _currentAttemptId;
        if (string.IsNullOrWhiteSpace(attemptId))
        {
            return;
        }

        try
        {
            // Keep the raw contract payload in the ledger so a support
            // investigation can identify the last durable pipeline stage even
            // after the desktop host crashes.
            _jobRepo.UpdateAttemptLastEvent(attemptId, JsonSerializer.Serialize(pipelineEvent));
        }
        catch
        {
            // Event persistence is diagnostic. It must not interrupt media
            // processing or turn a successful run into a failed one.
        }
    }

    private void ObserveTerminalEvent(JsonlEvent pipelineEvent)
    {
        if (!string.Equals(pipelineEvent.Event, "run_failed", StringComparison.Ordinal))
        {
            return;
        }

        _engineFailureSummary = pipelineEvent.ErrorSummary;
        Interlocked.Exchange(ref _engineReportedFailure, 1);
    }

    private void ApplyPreflightResult(PreflightResult result, string inputRoot)
    {
        _preflightResult = result;
        _preflightInputRoot = NormalizePath(inputRoot);
        PreflightTotalItems = result.TotalItems;
        PreflightPassed = result.Passed;
        PreflightSummary = result.Passed
            ? $"Found {result.TotalItems} item(s). Input snapshot is ready."
            : $"Preflight failed:{Environment.NewLine}{string.Join(Environment.NewLine, result.Errors.Select(error => error.Message))}";
    }

    private void LoadResumeCandidate(JobRecord job)
    {
        _resumeCandidate = job;
        _suppressPathInvalidation = true;
        try
        {
            InputRoot = job.InputRoot;
            OutputRoot = job.OutputRoot;
        }
        finally
        {
            _suppressPathInvalidation = false;
        }

        PreflightPassed = false;
        PreflightTotalItems = 0;
        _preflightResult = null;
        _preflightInputRoot = null;
        CanStart = false;
        CanResume = true;
        CanOpenOutput = false;
        StatusText = "A failed or interrupted job is ready to continue. Click Continue Failed to revalidate its input snapshot.";
        Log($"Loaded resumable job {job.JobId}.");
    }

    private void SetResumeCandidate(JobRecord job)
    {
        _resumeCandidate = job;
        CanResume = true;
    }

    private bool ResumeContractMatches(JobRecord job, PreflightResult preflight)
    {
        var storedSnapshot = _jobRepo.GetInputSnapshot(job.JobId);
        var snapshotMatches = !string.IsNullOrWhiteSpace(storedSnapshot) &&
            string.Equals(storedSnapshot, preflight.SnapshotJson, StringComparison.Ordinal) &&
            string.Equals(job.InputSnapshotHash, preflight.SnapshotHash, StringComparison.OrdinalIgnoreCase);
        var presetMatches = string.Equals(job.PresetVersion, PresetProvider.PresetVersion, StringComparison.Ordinal) &&
            string.Equals(job.PresetHash, GetCurrentDesktopContractHash(), StringComparison.OrdinalIgnoreCase);
        var engineMatches = string.Equals(
            job.EngineVersion,
            RuntimePaths.GetEngineVersion(_commandBuilder.PythonExecutablePath),
            StringComparison.Ordinal);

        return snapshotMatches && presetMatches && engineMatches;
    }

    private static bool ValidateResumePaths(JobRecord job, out string error)
    {
        if (!Directory.Exists(job.InputRoot))
        {
            error = "The original input directory no longer exists.";
            return false;
        }

        if (!Directory.Exists(job.OutputRoot))
        {
            error = "The original output directory no longer exists, so it cannot be resumed safely.";
            return false;
        }

        var inputRoot = NormalizePath(job.InputRoot);
        var outputRoot = NormalizePath(job.OutputRoot);
        if (string.Equals(inputRoot, outputRoot, StringComparison.OrdinalIgnoreCase) ||
            inputRoot.StartsWith(outputRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            outputRoot.StartsWith(inputRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            error = "The saved input and output directories overlap, so the job cannot be resumed safely.";
            return false;
        }

        error = "";
        return true;
    }

    private bool HasUsablePreflight()
    {
        return PreflightPassed &&
            _preflightResult is not null &&
            !string.IsNullOrWhiteSpace(_preflightInputRoot) &&
            PathsEqual(_preflightInputRoot, InputRoot);
    }

    private void InvalidatePreflightForPathChange()
    {
        if (_suppressPathInvalidation)
        {
            return;
        }

        _preflightResult = null;
        _preflightInputRoot = null;
        PreflightPassed = false;
        PreflightTotalItems = 0;
        PreflightSummary = "Path changed. Run preflight before starting a new job.";
        CanStart = false;
        _resumeCandidate = null;
        CanResume = false;
        CanOpenOutput = false;
    }

    private static string CreatePreflightResultPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "VideoAnalysisDesktop", "preflight");
        Directory.CreateDirectory(root);
        return Path.Combine(root, $"preflight_{Guid.NewGuid():N}.json");
    }

    private static string GetAttemptDir(string jobId, string attemptId)
    {
        return Path.Combine(
            DesktopDataPaths.GetDataRoot(),
            "jobs",
            jobId,
            attemptId);
    }

    private static AttemptRecord CreateAttemptRecord(string jobId, bool isResume, int sequenceNumber)
    {
        var attemptId = Guid.NewGuid().ToString("N");
        var attemptDirectory = GetAttemptDir(jobId, attemptId);
        return new AttemptRecord
        {
            AttemptId = attemptId,
            JobId = jobId,
            SequenceNumber = sequenceNumber,
            IsResume = isResume,
            EventFilePath = Path.Combine(attemptDirectory, "events.jsonl"),
            StdoutFilePath = Path.Combine(attemptDirectory, "stdout.txt"),
            StderrFilePath = Path.Combine(attemptDirectory, "stderr.txt"),
            StartedAtUtc = DateTime.UtcNow.ToString("O"),
        };
    }

    private static string NormalizePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath) ?? fullPath;
        return fullPath.Length <= root.Length
            ? root
            : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool PathsEqual(string first, string second)
    {
        try
        {
            return string.Equals(NormalizePath(first), NormalizePath(second), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception) when (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
        {
            return false;
        }
    }

    private static string RedactApiKey(string value, string? apiKey)
    {
        return string.IsNullOrEmpty(apiKey) ? value : value.Replace(apiKey, "***", StringComparison.Ordinal);
    }

    private static void PostToUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            return;
        }

        try
        {
            dispatcher.BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
            // The application is closing; there is no UI left to update.
        }
    }

    private void Log(string message)
    {
        LogEntries.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
        while (LogEntries.Count > 500)
        {
            LogEntries.RemoveAt(0);
        }
    }
}
