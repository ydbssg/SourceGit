using System.Collections.Generic;
using System.Threading.Tasks;

namespace SourceGit.ViewModels
{
    public class LFSFetch : Popup
    {
        public List<Models.Remote> Remotes => _repo.Remotes;

        public Models.Remote SelectedRemote
        {
            get;
            set;
        }

        public LFSFetch(Repository repo)
        {
            _repo = repo;
            SelectedRemote = _repo.Remotes[0];
        }

        public override async Task<bool> Sure()
        {
            using var lockWatcher = _repo.LockWatcher();
            ProgressDescription = App.Text("Progress.LFSFetch");

            var log = _repo.CreateLog(App.Text("Operation.LFSFetch"));
            Use(log);

            await new Commands.LFS(_repo.FullPath)
                .Use(log)
                .FetchAsync(SelectedRemote.Name);

            log.Complete();
            return true;
        }

        private readonly Repository _repo = null;
    }
}
