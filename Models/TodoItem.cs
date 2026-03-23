using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ToDoDo.Models;

public sealed class TodoItem : INotifyPropertyChanged
{
    private string _text = string.Empty;
    private bool _isDone;
    private string _groupId = string.Empty;
    private int _sortOrder;
    private TodoPriority _priority = TodoPriority.Medium;
    private DateTime? _dueDate;
    private TodoRepeatPattern _repeatPattern;
    private DateTime? _completedAt;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Text
    {
        get => _text;
        set
        {
            if (_text == value) return;
            _text = value;
            OnPropertyChanged();
        }
    }

    public bool IsDone
    {
        get => _isDone;
        set
        {
            if (_isDone == value) return;
            _isDone = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusLabel));
        }
    }

    public string GroupId
    {
        get => _groupId;
        set
        {
            if (_groupId == value) return;
            _groupId = value;
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

    public TodoPriority Priority
    {
        get => _priority;
        set
        {
            if (_priority == value) return;
            _priority = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PriorityLabel));
        }
    }

    public DateTime? DueDate
    {
        get => _dueDate;
        set
        {
            if (_dueDate == value) return;
            _dueDate = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DueDateLabel));
            OnPropertyChanged(nameof(IsOverdue));
        }
    }

    public TodoRepeatPattern RepeatPattern
    {
        get => _repeatPattern;
        set
        {
            if (_repeatPattern == value) return;
            _repeatPattern = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RepeatLabel));
        }
    }

    public DateTime? CompletedAt
    {
        get => _completedAt;
        set
        {
            if (_completedAt == value) return;
            _completedAt = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusLabel));
        }
    }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string PriorityLabel => Priority switch
    {
        TodoPriority.High => "높음",
        TodoPriority.Medium => "보통",
        _ => "낮음"
    };

    public string DueDateLabel => DueDate switch
    {
        null => "마감 없음",
        var date when date.Value.Date == DateTime.Today => "오늘",
        var date when date.Value.Date == DateTime.Today.AddDays(1) => "내일",
        var date => $"{date.Value:MM.dd}"
    };

    public string RepeatLabel => RepeatPattern switch
    {
        TodoRepeatPattern.Daily => "매일",
        TodoRepeatPattern.Weekly => "매주",
        TodoRepeatPattern.Monthly => "매월",
        _ => "반복 없음"
    };

    public string StatusLabel => IsDone ? "완료" : "진행 중";

    public bool IsOverdue => !IsDone && DueDate is DateTime due && due.Date < DateTime.Today;

    public event PropertyChangedEventHandler? PropertyChanged;

    public TodoItem CreateNextOccurrence()
    {
        var baseDate = DueDate?.Date ?? DateTime.Today;
        var nextDate = RepeatPattern switch
        {
            TodoRepeatPattern.Daily => baseDate.AddDays(1),
            TodoRepeatPattern.Weekly => baseDate.AddDays(7),
            TodoRepeatPattern.Monthly => baseDate.AddMonths(1),
            _ => baseDate
        };

        return new TodoItem
        {
            Text = Text,
            GroupId = GroupId,
            SortOrder = SortOrder + 1,
            Priority = Priority,
            DueDate = nextDate,
            RepeatPattern = RepeatPattern,
            CreatedAt = DateTime.Now
        };
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
