using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Serialization;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var opt = ParseArgs(args);
        if (string.IsNullOrWhiteSpace(opt.Audio) || (string.IsNullOrWhiteSpace(opt.Text) && string.IsNullOrWhiteSpace(opt.TextFile)))
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  dotnet run -- --audio <audio_path> --text \"Full text here.\" [--precise] [--pad-ms 120] [--task <aeneas_task_string>]");
            Console.WriteLine("If --task is omitted a default task string is used.");
            return 1;
        }
        if (!File.Exists(opt.Audio))
        {
            Console.Error.WriteLine("Audio not found: " + opt.Audio);
            return 1;
        }

        string text = opt.Text ?? await File.ReadAllTextAsync(opt.TextFile!);
        text = Normalize(text);
        var segments = SplitIntoSentences(text, opt.MaxChars, opt.MinChars);
        if (segments.Count == 0)
        {
            Console.Error.WriteLine("No segments after splitting.");
            return 1;
        }

        Console.WriteLine("Segments:");
        for (int i = 0; i < segments.Count; i++)
            Console.WriteLine($"{i + 1:D2}: {segments[i]}");

        var outDir = opt.OutDir ?? Path.Combine(Directory.GetCurrentDirectory(), "out");
        Directory.CreateDirectory(outDir);
        await File.WriteAllLinesAsync(Path.Combine(outDir, "split.txt"), segments);

        var tempDir = Path.Combine(outDir, ".tmp_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var textFile = Path.Combine(tempDir, "text.txt");
        await File.WriteAllLinesAsync(textFile, segments);

        var alignJson = Path.Combine(tempDir, "aeneas.json");
        var taskStr = opt.TaskSpec ?? $"task_language={opt.Language}|is_text_type=plain|os_task_file_format=json";
        Console.WriteLine("Task string: " + taskStr);

        Console.WriteLine("\nRunning aeneas...");
        bool ok = await RunProcess("py", $"-m aeneas.tools.execute_task \"{opt.Audio}\" \"{textFile}\" \"{taskStr}\" \"{alignJson}\"");
        if (!ok || !File.Exists(alignJson))
        {
            Console.Error.WriteLine("aeneas failed. Check installation.");
            return 1;
        }

        var frags = await LoadFragments(alignJson);
        if (frags.Count == 0)
        {
            Console.Error.WriteLine("No fragments from aeneas.");
            return 1;
        }
        if (frags.Count != segments.Count)
            Console.WriteLine($"[WARN] fragment count {frags.Count} != segments {segments.Count}; using min count.");
        int count = Math.Min(frags.Count, segments.Count);

        var segDir = Path.Combine(outDir, "segments");
        Directory.CreateDirectory(segDir);

        double padSec = opt.PadMs / 1000.0;

        var manifest = new List<object>();
        for (int i = 0; i < count; i++)
        {
            var f = frags[i];
            double begin = Math.Max(0, f.Begin - padSec);
            double end = f.End <= f.Begin ? f.Begin + 0.25 : f.End;
            end += padSec;
            if (end <= begin) end = begin + 0.25; // safety

            string safeName = SanitizeFileName(segments[i], 24);
            string outFile = Path.Combine(segDir, $"{i + 1:000}_{safeName}.wav");

            string argsFfmpeg;
            if (opt.Precise)
            {
                // Accurate seek: decode then trim; -ss placed after -i. Additional atrim/asetpts to be extra precise.
                argsFfmpeg = $"-y -i \"{opt.Audio}\" -ss {begin.ToString(CultureInfo.InvariantCulture)} -to {end.ToString(CultureInfo.InvariantCulture)} -af \"atrim=start={begin.ToString(CultureInfo.InvariantCulture)}:end={end.ToString(CultureInfo.InvariantCulture)},asetpts=PTS-STARTPTS\" -ac 1 -ar 16000 -c:a pcm_s16le \"{outFile}\"";
            }
            else
            {
                // Fast (may be up to ~hundreds ms off, depending on keyframe/timebase)
                argsFfmpeg = $"-y -ss {begin.ToString(CultureInfo.InvariantCulture)} -to {end.ToString(CultureInfo.InvariantCulture)} -i \"{opt.Audio}\" -ac 1 -ar 16000 -c:a pcm_s16le \"{outFile}\"";
            }

            bool cutOk = await RunProcess("ffmpeg", argsFfmpeg, capture: true);
            if (!cutOk)
                Console.Error.WriteLine($"[WARN] ffmpeg failed for segment {i + 1}");

            manifest.Add(new
            {
                index = i + 1,
                text = f.Text,
                begin,
                end,
                file = outFile
            });
            Console.WriteLine($"Segment {i + 1} => {outFile}");
        }

        string alignmentOut = Path.Combine(outDir, "alignment.json");
        await File.WriteAllTextAsync(alignmentOut,
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine($"\nDone.\nOutput dir: {outDir}");
        Console.WriteLine($"Segments: {segDir}");
        Console.WriteLine($"Alignment JSON: {alignmentOut}");

        try { Directory.Delete(tempDir, true); } catch { }

        return 0;
    }

    class Options
    {
        public string? Audio { get; set; }
        public string? Text { get; set; }
        public string? TextFile { get; set; }
        public string? OutDir { get; set; }
        public string Language { get; set; } = "eng";
        public int MaxChars { get; set; } = 80;
        public int MinChars { get; set; } = 12; // unused
        public bool Precise { get; set; } = false;
        public int PadMs { get; set; } = 0; // extra context padding at start/end
        public string? TaskSpec { get; set; } // custom aeneas task string
    }

    static Options ParseArgs(string[] args)
    {
        var o = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string Next() => (++i < args.Length) ? args[i] : "";
            switch (a)
            {
                case "--audio": o.Audio = Next(); break;
                case "--text": o.Text = Next(); break;
                case "--text-file": o.TextFile = Next(); break;
                case "--out": o.OutDir = Next(); break;
                case "--language": o.Language = Next(); break;
                case "--max": if (int.TryParse(Next(), out var mx)) o.MaxChars = mx; break;
                case "--min": if (int.TryParse(Next(), out var mn)) o.MinChars = mn; break; // legacy
                case "--precise": o.Precise = true; break;
                case "--pad-ms": if (int.TryParse(Next(), out var pad)) o.PadMs = Math.Max(0, pad); break;
                case "--task": o.TaskSpec = Next(); break;
            }
        }
        return o;
    }

    static string Normalize(string s) => Regex.Replace(s.Trim(), "\\s+", " ");

    static List<string> SplitIntoSentences(string text, int maxChars, int _unused)
    {
        var primary = new List<string>();
        var rx = new Regex(".+?(?:[.!?]+|$)", RegexOptions.Singleline);
        foreach (Match m in rx.Matches(text))
        {
            var val = m.Value.Trim();
            if (!string.IsNullOrEmpty(val)) primary.Add(val);
        }

        var result = new List<string>();
        foreach (var seg in primary)
        {
            if (seg.Length <= maxChars)
            {
                result.Add(Normalize(seg));
                continue;
            }
            foreach (var s in Secondary(seg, maxChars))
                result.Add(Normalize(s));
        }
        return result;
    }

    static IEnumerable<string> Secondary(string seg, int maxChars)
    {
        var pieces = new List<string>();
        int start = 0;
        var rx = new Regex("[,:;]+");
        foreach (Match m in rx.Matches(seg))
        {
            int endIndex = m.Index + m.Length;
            var chunk = seg.Substring(start, endIndex - start).Trim();
            if (!string.IsNullOrEmpty(chunk)) pieces.Add(chunk);
            start = endIndex;
        }
        var tail = seg.Substring(start).Trim();
        if (!string.IsNullOrEmpty(tail)) pieces.Add(tail);

        foreach (var p in pieces)
        {
            if (p.Length <= maxChars)
            {
                yield return p;
            }
            else
            {
                var words = p.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var buf = new List<string>();
                int curLen = 0;
                foreach (var w in words)
                {
                    int add = (buf.Count > 0 ? 1 : 0) + w.Length;
                    if (curLen + add > maxChars)
                    {
                        if (buf.Count > 0) yield return string.Join(' ', buf);
                        buf.Clear();
                        curLen = 0;
                    }
                    buf.Add(w);
                    curLen += add;
                }
                if (buf.Count > 0) yield return string.Join(' ', buf);
            }
        }
    }

    class RawAeneas { [JsonPropertyName("fragments")] public List<Frag>? Fragments { get; set; } }
    class Frag { [JsonPropertyName("begin")] public string? Begin { get; set; } [JsonPropertyName("end")] public string? End { get; set; } [JsonPropertyName("lines")] public List<string>? Lines { get; set; } }
    class Fragment { public double Begin { get; set; } public double End { get; set; } public string Text { get; set; } = string.Empty; }

    static async Task<List<Fragment>> LoadFragments(string jsonPath)
    {
        var json = await File.ReadAllTextAsync(jsonPath);
        var data = JsonSerializer.Deserialize<RawAeneas>(json) ?? new RawAeneas();
        var list = new List<Fragment>();
        if (data.Fragments == null) return list;
        foreach (var f in data.Fragments)
        {
            if (f == null) continue;
            double b = ParseDouble(f.Begin);
            double e = ParseDouble(f.End);
            string txt = (f.Lines != null && f.Lines.Count > 0) ? f.Lines[0] : "";
            list.Add(new Fragment { Begin = b, End = e, Text = txt });
        }
        return list;
    }

    static double ParseDouble(string? s) => double.TryParse(s ?? "0", NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;

    static async Task<bool> RunProcess(string file, string args, bool capture = false)
    {
        try
        {
            Console.WriteLine($"[EXEC] {file} {args}");
            var psi = new ProcessStartInfo(file, args)
            {
                UseShellExecute = !capture,
                RedirectStandardError = capture,
                RedirectStandardOutput = capture,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            if (capture)
            {
                var stderrTask = p.StandardError.ReadToEndAsync();
                var stdoutTask = p.StandardOutput.ReadToEndAsync();
                await Task.WhenAll(stderrTask, stdoutTask);
                if (!string.IsNullOrWhiteSpace(stdoutTask.Result)) Console.WriteLine(stdoutTask.Result);
                if (!string.IsNullOrWhiteSpace(stderrTask.Result)) Console.WriteLine(stderrTask.Result);
            }
            await p.WaitForExitAsync();
            if (p.ExitCode != 0) Console.Error.WriteLine($"[EXIT {p.ExitCode}] {file}");
            return p.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return false;
        }
    }

    static string SanitizeFileName(string raw, int maxLen)
    {
        string noPunct = Regex.Replace(raw, "[^a-zA-Z0-9 _-]", "");
        string trimmed = noPunct.Trim();
        if (trimmed.Length > maxLen) trimmed = trimmed.Substring(0, maxLen).Trim();
        if (string.IsNullOrEmpty(trimmed)) trimmed = "segment";
        return trimmed.Replace(' ', '_');
    }
}