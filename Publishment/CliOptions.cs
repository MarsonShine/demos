namespace Publishment
{
    internal record CliOptions(
        string ProjectFile,
        string Host,
        string PublishPath,
        string RunScript,
        string User,
        string? Password,
        string Configuration,
        string Match,
        int Port)
    {
        public static CliOptions Parse(string[] a)
        {
            string Get(string k, string def = "") => Arg(a, k) ?? def;
            string? Opt(string k) => Arg(a, k);
            return new(
                ProjectFile: Get("-project"),
                Host: Get("-host"),
                PublishPath: Get("-publishPath"),
                RunScript: Get("-runScript"),
                User: Get("-user", Environment.UserName),
                Password: Opt("-password"),          // 允许为 null
                Configuration: Get("-configuration", "Release"),
                Match: Get("-match", "mydemo.dll"),
                Port: int.TryParse(Get("-port", "5985"), out var p) ? p : 5985
            );
        }
        static string? Arg(string[] a, string key)
        {
            int i = Array.IndexOf(a, key);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }
    }
}
