using System;
using System.Threading.Tasks;

namespace SourceGit.ViewModels
{
    public class SetSubmoduleBranch : Popup
    {
        public Models.Submodule Submodule
        {
            get;
        }

        public string ChangeTo
        {
            get => _changeTo;
            set => SetProperty(ref _changeTo, value);
        }

        public SetSubmoduleBranch(Repository repo, Models.Submodule submodule)
        {
            _repo = repo;
            _changeTo = submodule.Branch;
            Submodule = submodule;
        }

        public override async Task<bool> Sure()
        {
            ProgressDescription = App.Text("Progress.SubmoduleBranch");

            if (_changeTo.Equals(Submodule.Branch, StringComparison.Ordinal))
                return true;

            using var lockWatcher = _repo.LockWatcher();
            var log = _repo.CreateLog(App.Text("Operation.SetSubmoduleBranch"));
            Use(log);

            var succ = await new Commands.Submodule(_repo.FullPath)
                .Use(log)
                .SetBranchAsync(Submodule.Path, _changeTo);

            log.Complete();
            return succ;
        }

        private readonly Repository _repo;
        private string _changeTo;
    }
}
