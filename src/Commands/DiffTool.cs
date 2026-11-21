using System;
using System.Diagnostics;

namespace SourceGit.Commands
{
    public class DiffTool : Command
    {
        public DiffTool(string repo, Models.DiffOption option)
        {
            WorkingDirectory = repo;
            Context = repo;
            _option = option;
        }

        public void Open()
        {
            var tool = Native.OS.GetDiffMergeTool(true);
            if (tool == null)
            {
                RaiseException(App.Text("Diagnostics.InvalidDiffTool"));
                return;
            }

            if (string.IsNullOrEmpty(tool.Cmd))
            {
                if (!CheckGitConfiguration())
                    return;

                Args = $"difftool -g --no-prompt {_option}";
            }
            else
            {
                var cmd = $"{tool.Exec.Quoted()} {tool.Cmd}";
                Args = $"-c difftool.sourcegit.cmd={cmd.Quoted()} difftool --tool=sourcegit --no-prompt {_option}";
            }

            try
            {
                Process.Start(CreateGitStartInfo(false));
            }
            catch (Exception ex)
            {
                RaiseException(ex.Message);
            }
        }

        private bool CheckGitConfiguration()
        {
            var config = new Config(WorkingDirectory).ReadAll();
            if (config.TryGetValue("diff.guitool", out var guiTool))
                return CheckCLIBasedTool(guiTool);
            if (config.TryGetValue("merge.guitool", out var mergeGuiTool))
                return CheckCLIBasedTool(mergeGuiTool);
            if (config.TryGetValue("diff.tool", out var diffTool))
                return CheckCLIBasedTool(diffTool);
            if (config.TryGetValue("merge.tool", out var mergeTool))
                return CheckCLIBasedTool(mergeTool);

            RaiseException(App.Text("Diagnostics.MissingGitConfig"));
            return false;
        }

        private bool CheckCLIBasedTool(string tool)
        {
            if (tool.StartsWith("vimdiff", StringComparison.Ordinal) ||
                tool.StartsWith("nvimdiff", StringComparison.Ordinal))
            {
                RaiseException(App.Text("Diagnostics.UnsupportedDiffTool", tool));
                return false;
            }

            return true;
        }

        private Models.DiffOption _option;
    }
}
