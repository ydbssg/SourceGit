using System.Threading.Tasks;

namespace SourceGit.ViewModels
{
    public class ClearStashes : Popup
    {
        public ClearStashes(Repository repo)
        {
            _repo = repo;
        }

        public override async Task<bool> Sure()
        {
            using var lockWatcher = _repo.LockWatcher();
            ProgressDescription = App.Text("Progress.ClearStashes");

            var log = _repo.CreateLog(App.Text("Operation.ClearStashes"));
            Use(log);

            await new Commands.Stash(_repo.FullPath)
                .Use(log)
                .ClearAsync();

            log.Complete();
            _repo.MarkStashesDirtyManually();
            return true;
        }

        private readonly Repository _repo = null;
    }
}
