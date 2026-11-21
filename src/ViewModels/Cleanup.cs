using System.Threading.Tasks;

namespace SourceGit.ViewModels
{
    public class Cleanup : Popup
    {
        public Cleanup(Repository repo)
        {
            _repo = repo;
        }

        public override async Task<bool> Sure()
        {
            using var lockWatcher = _repo.LockWatcher();
            ProgressDescription = App.Text("Progress.Cleanup");

            var log = _repo.CreateLog(App.Text("Operation.Cleanup"));
            Use(log);

            await new Commands.GC(_repo.FullPath)
                .Use(log)
                .ExecAsync();

            log.Complete();
            return true;
        }

        private readonly Repository _repo = null;
    }
}
