using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace Iconic
{
    public abstract class Observable : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnChanged(name);
            return true;
        }

        protected void OnChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>One icon in the launcher (a shortcut, program, file or folder).</summary>
    public class AppItem : Observable
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        private string _name = "";
        public string Name { get => _name; set => Set(ref _name, value); }

        /// <summary>The file Iconic opens when the icon is clicked.</summary>
        public string Path { get; set; } = "";

        /// <summary>Where the item lived on the desktop before Iconic moved it (null if never moved).</summary>
        public string OriginalPath { get; set; }

        private string _customIcon;
        public string CustomIcon { get => _customIcon; set => Set(ref _customIcon, value); }

        private ImageSource _icon;
        [JsonIgnore] public ImageSource Icon { get => _icon; set => Set(ref _icon, value); }

        private bool _isShown = true;
        [JsonIgnore] public bool IsShown { get => _isShown; set => Set(ref _isShown, value); }

        private bool _isEditing;
        [JsonIgnore] public bool IsEditing { get => _isEditing; set => Set(ref _isEditing, value); }

        [JsonIgnore] public string EditOriginal { get; set; }
    }

    /// <summary>A labeled, colored group of icons.</summary>
    public class AppGroup : Observable
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        private string _name = "New Group";
        public string Name { get => _name; set => Set(ref _name, value); }

        private string _accent = "#0A84FF";
        public string Accent
        {
            get => _accent;
            set { if (Set(ref _accent, value)) OnChanged(nameof(AccentBrush)); }
        }

        private bool _isCollapsed;
        public bool IsCollapsed
        {
            get => _isCollapsed;
            set { if (Set(ref _isCollapsed, value)) OnChanged(nameof(ShowItems)); }
        }

        public ObservableCollection<AppItem> Items { get; set; } = new ObservableCollection<AppItem>();

        [JsonIgnore]
        public Brush AccentBrush
        {
            get
            {
                try
                {
                    var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(Accent));
                    brush.Freeze();
                    return brush;
                }
                catch { return Brushes.DodgerBlue; }
            }
        }

        private bool _forceOpen;
        /// <summary>True while searching, so matches inside collapsed groups still show.</summary>
        [JsonIgnore]
        public bool ForceOpen
        {
            get => _forceOpen;
            set { if (Set(ref _forceOpen, value)) OnChanged(nameof(ShowItems)); }
        }

        [JsonIgnore] public bool ShowItems => !IsCollapsed || ForceOpen;

        private bool _isShown = true;
        [JsonIgnore] public bool IsShown { get => _isShown; set => Set(ref _isShown, value); }

        private bool _isEditing;
        [JsonIgnore] public bool IsEditing { get => _isEditing; set => Set(ref _isEditing, value); }

        [JsonIgnore] public string EditOriginal { get; set; }

        private string _dropHint;
        /// <summary>"Top" or "Bottom" while another group is dragged over this one.</summary>
        [JsonIgnore] public string DropHint { get => _dropHint; set => Set(ref _dropHint, value); }
    }

    public class Library
    {
        public int Version { get; set; } = 1;
        public ObservableCollection<AppGroup> Groups { get; set; } = new ObservableCollection<AppGroup>();
        public double WindowWidth { get; set; } = 1000;
        public double WindowHeight { get; set; } = 660;
        public double? WindowLeft { get; set; }
        public double? WindowTop { get; set; }
        public bool DesktopOffered { get; set; }
        public bool KeepDesktopTidy { get; set; }
        public bool IsPinned { get; set; }

        /// <summary>Desktop items the user chose to leave where they are.</summary>
        public List<string> IgnoredDesktopItems { get; set; } = new List<string>();
    }
}
