using System;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public partial class SSHKeyGenerator : ObservableValidator
    {
        public Models.SSHKeyType Type
        {
            get => _type;
            set => SetProperty(ref _type, value);
        }

        [Required(ErrorMessageResourceType = typeof(ValidationMessages), ErrorMessageResourceName = nameof(ValidationMessages.NameRequired))]
        [RegularExpression(@"^[a-zA-Z0-9_\-]+$", ErrorMessageResourceType = typeof(ValidationMessages), ErrorMessageResourceName = nameof(ValidationMessages.SSHNameFormat))]
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value, true);
        }

        [Required(ErrorMessageResourceType = typeof(ValidationMessages), ErrorMessageResourceName = nameof(ValidationMessages.EmailRequired))]
        [EmailAddress(ErrorMessageResourceType = typeof(ValidationMessages), ErrorMessageResourceName = nameof(ValidationMessages.EmailFormat))]
        public string Email
        {
            get => _email;
            set => SetProperty(ref _email, value, true);
        }

        public bool UsePassphrase
        {
            get => _usePassphrase;
            set
            {
                if (SetProperty(ref _usePassphrase, value))
                {
                    ValidateProperty(_passphrase, nameof(Passphrase));
                    ValidateProperty(_confirmedPassphrase, nameof(ConfirmedPassphrase));
                }
            }
        }

        [CustomValidation(typeof(SSHKeyGenerator), nameof(ValidatePassphrase))]
        public string Passphrase
        {
            get => _passphrase;
            set
            {
                if (SetProperty(ref _passphrase, value, true))
                    ValidateProperty(_confirmedPassphrase, nameof(ConfirmedPassphrase));
            }
        }

        [CustomValidation(typeof(SSHKeyGenerator), nameof(ValidateConfirmedPassphrase))]
        public string ConfirmedPassphrase
        {
            get => _confirmedPassphrase;
            set => SetProperty(ref _confirmedPassphrase, value, true);
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        public static ValidationResult ValidatePassphrase(string password, ValidationContext context)
        {
            var instance = (SSHKeyGenerator)context.ObjectInstance;
            if (!instance.UsePassphrase)
                return ValidationResult.Success;

            if (string.IsNullOrEmpty(password))
                return new ValidationResult(App.Text("Validation.PassphraseRequired"));

            if (!REG_PSWD_FORMAT().IsMatch(password))
                return new ValidationResult(App.Text("Validation.PassphraseCharacters"));

            return ValidationResult.Success;
        }

        public static ValidationResult ValidateConfirmedPassphrase(string confirmedPassword, ValidationContext context)
        {
            var instance = (SSHKeyGenerator)context.ObjectInstance;
            if (!instance.UsePassphrase)
                return ValidationResult.Success;

            if (confirmedPassword != instance.Passphrase)
                return new ValidationResult(App.Text("Validation.PassphraseMismatch"));

            return ValidationResult.Success;
        }

        public Models.SSHKeyPair Run()
        {
            ErrorMessage = string.Empty;

            ValidateAllProperties();
            if (HasErrors)
                return null;

            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
            if (!Directory.Exists(dir))
            {
                try
                {
                    Directory.CreateDirectory(dir);
                }
                catch (Exception e)
                {
                    ErrorMessage = App.Text("Diagnostics.CreateSSHDirectory", e.Message);
                    return null;
                }
            }

            var type = _type?.Cmdline ?? "-t ed25519";
            var passphrase = _usePassphrase ? _passphrase : string.Empty;
            var keyFile = Path.Combine(dir, _name);
            var start = new ProcessStartInfo();
            start.FileName = "ssh-keygen";
            start.Arguments = $"-q {type} -N {passphrase.Quoted()} -C {_email.Quoted()} -f {keyFile.Quoted()}";
            start.UseShellExecute = false;
            start.CreateNoWindow = true;

            try
            {
                var proc = Process.Start(start);
                proc.WaitForExit();
                proc.Close();
            }
            catch (Exception e)
            {
                ErrorMessage = App.Text("Diagnostics.GenerateSSHKey", e.Message);
                return null;
            }

            var publicKeyFile = keyFile + ".pub";
            if (File.Exists(keyFile) && File.Exists(publicKeyFile))
                return new Models.SSHKeyPair(keyFile, publicKeyFile);

            ErrorMessage = App.Text("Diagnostics.SSHKeyFilesMissing");
            return null;
        }

        [GeneratedRegex(@"^[0-9a-zA-Z_\-\@\#\$\%\!\&\+\=]+$")]
        private static partial Regex REG_PSWD_FORMAT();

        private Models.SSHKeyType _type = Models.SSHKeyType.Supported[0];
        private string _name;
        private string _email;
        private bool _usePassphrase;
        private string _passphrase;
        private string _confirmedPassphrase;
        private string _errorMessage;
    }
}
