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

        public Pull(string repo, Models.Remote remote, Models.Branch remoteBranch, string upstream, bool useRebase, bool onlySelectedRemoteBranch)
        {
            WorkingDirectory = repo;
            Context = repo;
            SSHKey = remote.PrivateSSHKey;

            var isUpstream = remoteBranch.FullName == upstream;
            var builder = new StringBuilder(512);
            builder
                .Append("pull --verbose --progress --rebase=")
                .Append(useRebase ? "true" : "false");

            if (!onlySelectedRemoteBranch && isUpstream)
                builder.Append(" --prune");

            builder.Append(' ').Append(remote.Name);

            if (onlySelectedRemoteBranch)
                builder.Append(' ').Append($"refs/heads/{remoteBranch.Name}".Quoted());
            else if (!isUpstream)
                builder.Append(' ').Append(remoteBranch.Name.Quoted());

            Args = builder.ToString();
        }
    }
}
