using System.ComponentModel;
using System.Runtime.CompilerServices;
using Autodesk.Revit.DB;

namespace YD_RevitTools.LicenseManager.Commands.FamilyParameterRename.Models
{
    public class ParameterRenameRow : INotifyPropertyChanged
    {
        private bool _isSelected = true;
        private string _newName = "";
        private string _status = "";
        private string _message = "";

        public FamilyParameter Parameter { get; set; }
        public string OldName { get; set; } = "";

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                OnPropertyChanged();
            }
        }

        public string NewName
        {
            get => _newName;
            set
            {
                if (_newName == value) return;
                _newName = value;
                OnPropertyChanged();
            }
        }

        public string Status
        {
            get => _status;
            set
            {
                if (_status == value) return;
                _status = value;
                OnPropertyChanged();
            }
        }

        public string Message
        {
            get => _message;
            set
            {
                if (_message == value) return;
                _message = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
