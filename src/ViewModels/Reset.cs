using System.Threading.Tasks;

namespace SourceGit.ViewModels
{
    public class Reset : Popup
    {
        public Models.Branch Current
        {
            get;
        }

        public Models.Commit To
        {
            get;
        }

        public Models.ResetMode SelectedMode
        {
            get;
            set;
        }

        public Reset(Repository repo, Models.Branch current, Models.Commit to)
        {
            _repo = repo;
            Current = current;
            To = to;
            SelectedMode = Models.ResetMode.Supported[1];
        }

        public override async Task<bool> Sure()
        {
            using var lockWatcher = _repo.LockWatcher();
            ProgressDescription = App.Text("Progress.Reset", To.SHA);

            var log = _repo.CreateLog(App.Text("Operation.ResetHead", To.SHA));
            Use(log);

            var succ = await new Commands.Reset(_repo.FullPath, To.SHA, SelectedMode.Arg)
                .Use(log)
                .ExecAsync();

            await _repo.AutoUpdateSubmodulesAsync(log);

            log.Complete();
            return succ;
        }

        private readonly Repository _repo = null;
    }
}
