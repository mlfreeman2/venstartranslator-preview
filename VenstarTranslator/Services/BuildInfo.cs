using System;
using System.Linq;
using System.Reflection;

namespace VenstarTranslator.Services;

// "What build are you on?" — answerable in every issue report (§12b). Reads the version and
// commit stamped at build time (see VenstarTranslator.csproj / Dockerfile). Cached once,
// never throws: an unstamped local build reports dev/local.
public static class BuildInfo
{
    private static readonly Lazy<string> _version = new(ReadVersion);
    private static readonly Lazy<string> _commit = new(ReadCommit);

    public static string Version => _version.Value;

    public static string Commit => _commit.Value;

    private static string ReadVersion()
    {
        try
        {
            var informational = typeof(BuildInfo).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;

            if (string.IsNullOrWhiteSpace(informational))
            {
                return "dev";
            }

            // SourceLink may append "+<sha>" to the informational version on a git checkout;
            // trim it so the reported version equals the intended build/image tag.
            var plus = informational.IndexOf('+');
            return plus >= 0 ? informational[..plus] : informational;
        }
        catch
        {
            return "dev";
        }
    }

    private static string ReadCommit()
    {
        try
        {
            var gitSha = typeof(BuildInfo).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "GitSha")
                ?.Value;

            return string.IsNullOrWhiteSpace(gitSha) ? "local" : gitSha;
        }
        catch
        {
            return "local";
        }
    }
}
