using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Threading.Tasks;

namespace SourceGit.ViewModels
{
    public class AddWorktree : Popup
    {
        [CustomValidation(typeof(AddWorktree), nameof(ValidateWorktreePath))]
        public string WorktreePath
        {
            get => _worktreePath;
            set => SetProperty(ref _worktreePath, value, true);
        }

        [CustomValidation(typeof(AddWorktree), nameof(ValidateBranchName))]
        public string BranchName
        {
            get => _branchName;
            set => SetProperty(ref _branchName, value, true);
        }

        public AddWorktree(Repository repo)
        {
            _repo = repo;
            _worktreePath = Path.Combine(repo.GetRecommandedWorktreeDir(), "new-worktree");
        }

        public static ValidationResult ValidateWorktreePath(string path, ValidationContext ctx)
        {
            if (ctx.ObjectInstance is not AddWorktree creator)
                return new ValidationResult(App.Text("Validation.AddWorktreeContext"));

            if (string.IsNullOrEmpty(path))
                return new ValidationResult(App.Text("Validation.WorktreePath"));

            var fullPath = Path.IsPathRooted(path) ? path : Path.Combine(creator._repo.FullPath, path);
            var info = new DirectoryInfo(fullPath);
            if (info.Exists)
            {
                var files = info.GetFiles();
                if (files.Length > 0)
                    return new ValidationResult(App.Text("Validation.NonEmptyPath"));

                var folders = info.GetDirectories();
                if (folders.Length > 0)
                    return new ValidationResult(App.Text("Validation.NonEmptyPath"));
            }

            return ValidationResult.Success;
        }

        public static ValidationResult ValidateBranchName(string name, ValidationContext ctx)
        {
            if (ctx.ObjectInstance is not AddWorktree creator)
                return new ValidationResult(App.Text("Validation.AddWorktreeContext"));

            var test = !string.IsNullOrEmpty(name) ? name : Path.GetFileName(creator._worktreePath.TrimEnd('/').TrimEnd('\\'));
            if (!Models.RefName.IsValidBranchName(test))
                return new ValidationResult(App.Text("Validation.InvalidBranchName", test));

            foreach (var b in creator._repo.Branches)
            {
                if (b.IsLocal && b.Name.Equals(test, StringComparison.Ordinal))
                    return new ValidationResult(App.Text("Validation.ExistingBranchNamed", test));
            }

            return ValidationResult.Success;
        }

        public override async Task<bool> Sure()
        {
            using var lockWatcher = _repo.LockWatcher();
            ProgressDescription = App.Text("Progress.AddWorktree");

            var log = _repo.CreateLog(App.Text("Operation.AddWorktree"));
            Use(log);

            var succ = await new Commands.Worktree(_repo.FullPath)
                .Use(log)
                .AddAsync(_worktreePath, _branchName);

            log.Complete();
            return succ;
        }

        private readonly Repository _repo = null;
        private string _worktreePath = string.Empty;
        private string _branchName = string.Empty;
    }
}
