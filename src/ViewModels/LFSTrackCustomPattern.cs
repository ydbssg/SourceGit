using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;

namespace SourceGit.ViewModels
{
    public class LFSTrackCustomPattern : Popup
    {
        [Required(ErrorMessageResourceType = typeof(ValidationMessages), ErrorMessageResourceName = nameof(ValidationMessages.LFSPatternRequired))]
        public string Pattern
        {
            get => _pattern;
            set => SetProperty(ref _pattern, value, true);
        }

        public bool IsFilename
        {
            get;
            set;
        } = false;

        public LFSTrackCustomPattern(Repository repo)
        {
            _repo = repo;
        }

        public override async Task<bool> Sure()
        {
            using var lockWatcher = _repo.LockWatcher();
            ProgressDescription = App.Text("Progress.LFSTrackCustom");

            var log = _repo.CreateLog(App.Text("Operation.LFSTrackCustom"));
            Use(log);

            var succ = await new Commands.LFS(_repo.FullPath)
                .Use(log)
                .TrackAsync(_pattern, IsFilename);

            log.Complete();
            return succ;
        }

        private readonly Repository _repo = null;
        private string _pattern = string.Empty;
    }
}
