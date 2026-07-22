using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TrackBuilderGui;

public sealed class PathEntry : INotifyPropertyChanged
{
    private bool _isChecked;

    public PathEntry(string id)
    {
        Id = id;
    }

    public string Id { get; }

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value)
                return;
            _isChecked = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
