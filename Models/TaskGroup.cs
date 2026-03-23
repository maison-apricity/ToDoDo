using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ToDoDo.Models;

public sealed class TaskGroup : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private int _sortOrder;
    private bool _isEditing;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name
    {
        get => _name;
        set
        {
            if (_name == value) return;
            _name = value;
            OnPropertyChanged();
        }
    }

    public int SortOrder
    {
        get => _sortOrder;
        set
        {
            if (_sortOrder == value) return;
            _sortOrder = value;
            OnPropertyChanged();
        }
    }

    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            if (_isEditing == value) return;
            _isEditing = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
