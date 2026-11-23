using System.Text;

namespace SourceGit.Commands
{
    public class Fetch : Command
    {
        public Fetch(string repo, Models.Remote remote, bool noTags, bool force)
        {
            WorkingDirectory = repo;
            Context = repo;
            SSHKey = remote.PrivateSSHKey;

            var builder = new StringBuilder(512);
            builder.Append("fetch --progress --verbose ");
            builder.Append(noTags ? "--no-tags " : "--tags ");
            if (force)
                builder.Append("--force ");
            builder.Append(remote.Name);

            Args = builder.ToString();
        }

        public Fetch(string repo, Models.Remote remote, bool prune = false)
        {
            WorkingDirectory = repo;
            Context = repo;
            SSHKey = remote.PrivateSSHKey;
            RaiseError = false;
            var pruneOption = prune ? "--prune " : string.Empty;
            Args = $"fetch --progress --verbose {pruneOption}{remote.Name}";
        }

        public Fetch(string repo, Models.Remote remote, Models.Branch remoteBranch, Models.Branch local)
        {
            WorkingDirectory = repo;
            Context = repo;
            SSHKey = remote.PrivateSSHKey;
            Args = $"fetch --progress --verbose {remote.Name} {remoteBranch.Name}:{local.Name}";
        }
    }
}
