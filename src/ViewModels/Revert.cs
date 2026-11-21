using System.Threading.Tasks;

namespace SourceGit.ViewModels
{
    public class Revert : Popup
    {
        public Models.Commit Target
        {
            get;
        }

        public bool AutoCommit
        {
            get;
            set;
        }

        public Revert(Repository repo, Models.Commit target)
        {
            _repo = repo;
            Target = target;
            AutoCommit = true;
        }

        public override async Task<bool> Sure()
        {
            using var lockWatcher = _repo.LockWatcher();
            _repo.ClearCommitMessage();
            ProgressDescription = App.Text("Progress.Revert", Target.SHA);

            var log = _repo.CreateLog(App.Text("Operation.RevertCommit", Target.SHA));
            Use(log);

            await new Commands.Revert(_repo.FullPath, Target.SHA, AutoCommit)
                .Use(log)
                .ExecAsync();

            log.Complete();
            return true;
        }

        private readonly Repository _repo = null;
    }
}
