using System.Threading.Tasks;

namespace SourceGit.ViewModels
{
    public class DeleteRemote : Popup
    {
        public Models.Remote Remote
        {
            get;
            private set;
        }

        public DeleteRemote(Repository repo, Models.Remote remote)
        {
            _repo = repo;
            Remote = remote;
        }

        public override async Task<bool> Sure()
        {
            using var lockWatcher = _repo.LockWatcher();
            ProgressDescription = App.Text("Progress.DeleteRemote");

            var log = _repo.CreateLog(App.Text("Operation.DeleteRemote"));
            Use(log);

            var succ = await new Commands.Remote(_repo.FullPath)
                .Use(log)
                .DeleteAsync(Remote.Name);

            log.Complete();
            _repo.MarkBranchesDirtyManually();
            return succ;
        }

        private readonly Repository _repo = null;
    }
}
