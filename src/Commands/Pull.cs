using System.Text;

namespace SourceGit.Commands
{
    public class Pull : Command
    {
        public Pull(string repo, Models.Remote remote, Models.Branch remoteBranch, bool useRebase, bool automatic = false)
        {
            WorkingDirectory = repo;
            Context = repo;
            SSHKey = remote.PrivateSSHKey;

            var builder = new StringBuilder(512);
            if (automatic)
            {
                Editor = EditorType.None;
                // Git versions before merge autostash only accept --no-autostash with rebase.
                builder.Append("-c pull.autoStash=false -c merge.autoStash=false -c rebase.autoStash=false ");
            }

            builder
                .Append("pull --verbose --progress --rebase=")
                .Append(useRebase ? "true" : "false");

            if (automatic)
                builder.Append(" --no-recurse-submodules --no-edit --commit --no-squash");

            builder.Append(' ').Append(remote.Name);

            if (remoteBranch != null)
                builder.Append(' ').Append(remoteBranch.Name);

            Args = builder.ToString();
        }
    }
}
