namespace ToDoDo.Models;

public sealed class TodoItem : ObservableObject
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _groupId = string.Empty;
    private string _text = "새 할 일";
    private bool _isDone;
    private bool _isArchived;
    private TodoPriority _priority = TodoPriority.Normal;
    private TodoRepeat _repeat = TodoRepeat.None;
    private DateTime? _dueDate;
    private DateTime? _completedAt;
    private int _sortOrder;
    private bool _isEditing;
    private bool _isExpanded = true;
    private DateTime _createdAt = DateTime.Now;
    private bool _isCompletingFeedback;
    private string _archivedGroupName = string.Empty;

    private string _editText = string.Empty;
    private TodoPriority _editPriority = TodoPriority.Normal;
    private TodoRepeat _editRepeat = TodoRepeat.None;
    private DateTime? _editDueDate;
    private bool _editUseDueDate;

    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string GroupId
    {
        get => _groupId;
        set => SetProperty(ref _groupId, value);
    }

    public string Text
    {
        get => _text;
        set => SetText(value);
    }

    public bool IsDone
    {
        get => _isDone;
        set
        {
            if (SetProperty(ref _isDone, value))
            {
                OnPropertyChanged(nameof(ShowMeta));
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(CompleteActionText));
            }
        }
    }

    public bool IsArchived
    {
        get => _isArchived;
        set => SetProperty(ref _isArchived, value);
    }

    public TodoPriority Priority
    {
        get => _priority;
        set
        {
            if (SetProperty(ref _priority, value))
            {
                OnPropertyChanged(nameof(PriorityText));
            }
        }
    }

    public TodoRepeat Repeat
    {
        get => _repeat;
        set
        {
            if (SetProperty(ref _repeat, value))
            {
                OnPropertyChanged(nameof(RepeatText));
                OnPropertyChanged(nameof(HasRepeat));
            }
        }
    }

    public DateTime? DueDate
    {
        get => _dueDate;
        set
        {
            if (SetProperty(ref _dueDate, value))
            {
                OnPropertyChanged(nameof(DueText));
                OnPropertyChanged(nameof(HasDueDate));
            }
        }
    }

    public DateTime? CompletedAt
    {
        get => _completedAt;
        set => SetProperty(ref _completedAt, value);
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

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value))
            {
                OnPropertyChanged(nameof(ShowMeta));
            }
        }
    }

    public DateTime CreatedAt
    {
        get => _createdAt;
        set => SetProperty(ref _createdAt, value);
    }

    public bool IsCompletingFeedback
    {
        get => _isCompletingFeedback;
        set => SetProperty(ref _isCompletingFeedback, value);
    }

    public string ArchivedGroupName
    {
        get => _archivedGroupName;
        set
        {
            if (SetProperty(ref _archivedGroupName, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(ArchivedGroupCaption));
            }
        }
    }

    public string EditText
    {
        get => _editText;
        set => SetProperty(ref _editText, value);
    }

    public TodoPriority EditPriority
    {
        get => _editPriority;
        set
        {
            if (SetProperty(ref _editPriority, value))
            {
                OnPropertyChanged(nameof(EditPriorityText));
            }
        }
    }

    public TodoRepeat EditRepeat
    {
        get => _editRepeat;
        set
        {
            if (SetProperty(ref _editRepeat, value))
            {
                OnPropertyChanged(nameof(EditRepeatText));
            }
        }
    }

    public DateTime? EditDueDate
    {
        get => _editDueDate;
        set
        {
            if (SetProperty(ref _editDueDate, value))
            {
                OnPropertyChanged(nameof(EditDueText));
            }
        }
    }

    public bool EditUseDueDate
    {
        get => _editUseDueDate;
        set
        {
            if (SetProperty(ref _editUseDueDate, value))
            {
                OnPropertyChanged(nameof(EditDueText));
            }
        }
    }

    public bool HasDueDate => DueDate.HasValue;
    public bool HasRepeat => Repeat != TodoRepeat.None;
    public bool ShowMeta => !IsDone && IsExpanded;
    public string StatusText => IsDone ? "완료" : "진행 중";
    public string CompleteActionText => IsDone ? "복원" : "완료";

    public string DueText
        => DueDate.HasValue ? $"마감 {DueDate.Value:yyyy.MM.dd}" : string.Empty;

    public string RepeatText
        => Repeat switch
        {
            TodoRepeat.Daily => "매일",
            TodoRepeat.Weekly => "매주",
            TodoRepeat.Monthly => "매월",
            _ => string.Empty
        };

    public string PriorityText
        => Priority switch
        {
            TodoPriority.Low => "낮음",
            TodoPriority.High => "높음",
            _ => "보통"
        };

    public string EditDueText => EditUseDueDate && EditDueDate.HasValue ? EditDueDate.Value.ToString("yyyy.MM.dd") : "날짜 선택";
    public string ArchivedGroupCaption => string.IsNullOrWhiteSpace(ArchivedGroupName) ? "이전 그룹 정보 없음" : $"이전 그룹 · {ArchivedGroupName}";
    public string EditRepeatText
        => EditRepeat switch
        {
            TodoRepeat.Daily => "매일",
            TodoRepeat.Weekly => "매주",
            TodoRepeat.Monthly => "매월",
            _ => "반복 없음"
        };
    public string EditPriorityText
        => EditPriority switch
        {
            TodoPriority.Low => "낮음",
            TodoPriority.High => "높음",
            _ => "보통"
        };

    public void BeginEdit()
    {
        EditText = Text;
        EditPriority = Priority;
        EditRepeat = Repeat;
        EditUseDueDate = DueDate.HasValue;
        EditDueDate = DueDate ?? DateTime.Today;
        IsEditing = true;
    }

    public void CommitEdit()
    {
        Text = string.IsNullOrWhiteSpace(EditText) ? Text : EditText.Trim();
        Priority = EditPriority;
        Repeat = EditRepeat;
        DueDate = EditUseDueDate ? EditDueDate : null;
        IsEditing = false;
    }

    public void CancelEdit()
    {
        IsEditing = false;
    }

    private bool SetText(string value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "새 할 일" : value.Trim();
        return SetProperty(ref _text, normalized);
    }
}
