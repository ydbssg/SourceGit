using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    internal sealed partial class MergeRequestGit : Command
    {
        public MergeRequestGit(string workingDirectory, string context, Models.Remote remote, params string[] arguments)
        {
            WorkingDirectory = workingDirectory;
            Context = context;
            SSHKey = remote.PrivateSSHKey;
            Editor = EditorType.None;
            Args = string.Join(' ', arguments.Select(QuoteArgument));
        }

        public Task<Result> RunAsync()
        {
            return ReadToEndAsync();
        }

        public Task<Result> WriteCommitAsync(string name, string email, string date, string message)
        {
            var start = CreateGitStartInfo(true);
            start.Environment["GIT_AUTHOR_NAME"] = name;
            start.Environment["GIT_AUTHOR_EMAIL"] = email;
            start.Environment["GIT_AUTHOR_DATE"] = date;
            return ReadToEndAsync(start, message);
        }

        public static Dictionary<string, string> ParseRemoteRefs(string output)
        {
            var refs = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = line.IndexOf('\t');
                if (separator > 0)
                    refs[line[(separator + 1)..]] = line[..separator];
            }
            return refs;
        }

        private static string QuoteArgument(string value)
        {
            return "\"" + ArgumentEscapes().Replace(value, static match =>
            {
                var slashes = match.Groups[1].Value;
                return slashes + slashes + (match.Groups[2].Length > 0 ? "\\\"" : string.Empty);
            }) + "\"";
        }

        [GeneratedRegex("""(\\*)("|$)""")]
        private static partial Regex ArgumentEscapes();
    }
}
