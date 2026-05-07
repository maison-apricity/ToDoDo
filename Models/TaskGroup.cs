namespace ToDoDo.Models;

public sealed class TaskGroup : ObservableObject
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _name = string.Empty;
    private int _sortOrder;
    private bool _isEditing;
    private int _savedFilterMode;
    private int _savedSortMode;
    private bool _savedCompletedCollapsed = true;
    private string _editName = string.Empty;

    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value?.Trim() ?? string.Empty);
    }

    public int SortOrder
    {
        get => _sortOrder;
        set => SetProperty(ref _sortOrder, value);
    }

    public bool IsEditing
    {
        get => _isEditing;
        set => SetProperty(ref _isEditing, value);
    }

    public int SavedFilterMode
    {
        get => _savedFilterMode;
        set => SetProperty(ref _savedFilterMode, value);
    }

    public int SavedSortMode
    {
        get => _savedSortMode;
        set => SetProperty(ref _savedSortMode, value);
    }

    public bool SavedCompletedCollapsed
    {
        get => _savedCompletedCollapsed;
        set => SetProperty(ref _savedCompletedCollapsed, value);
    }

    public string EditName
    {
        get => _editName;
        set => SetProperty(ref _editName, value ?? string.Empty);
    }

    public void BeginEdit()
    {
        EditName = Name;
        IsEditing = true;
    }

    public void CommitEdit()
    {
        Name = string.IsNullOrWhiteSpace(EditName) ? "새 그룹" : EditName.Trim();
        IsEditing = false;
    }

    public void CancelEdit()
    {
        EditName = Name;
        if (string.IsNullOrWhiteSpace(Name))
        {
            Name = "새 그룹";
        }
        IsEditing = false;
    }

    public override string ToString() => Name;
}

