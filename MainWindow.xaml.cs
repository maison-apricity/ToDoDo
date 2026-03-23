using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using FileOpenDialog = Microsoft.Win32.OpenFileDialog;
using FileSaveDialog = Microsoft.Win32.SaveFileDialog;
using WpfButton = System.Windows.Controls.Button;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfDragDropEffects = System.Windows.DragDropEffects;
using WpfDragEventArgs = System.Windows.DragEventArgs;
using WpfMessageBox = System.Windows.MessageBox;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfPoint = System.Windows.Point;
using WpfTextBox = System.Windows.Controls.TextBox;
using ToDoDo.Models;
using ToDoDo.Services;

namespace ToDoDo;

internal enum SmartFilterKind
{
    All,
    Today,
    ThisWeek,
    Later
}

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private const double ResizeBorderThickness = 8;

    private readonly TodoStore _todoStore = new();
    private readonly SettingsStore _settingsStore = new();
    private readonly BackupService _backupService = new();
    private readonly BottomMostService _bottomMostService = new();
    private readonly TrayIconService _trayIconService = new();
    private readonly HotKeyService _hotKeyService = new();

    private readonly ObservableCollection<TaskGroup> _groups;
    private readonly ObservableCollection<TodoItem> _todoItems;
    private readonly ObservableCollection<TodoItem> _visibleActiveTodoItems = new();
    private readonly ObservableCollection<TodoItem> _visibleCompletedTodoItems = new();

    private WidgetSettings _settings;
    private bool _allowClose;
    private bool _isRestoring;
    private bool _isSettingsPanelOpen;
    private string _summaryText = string.Empty;
    private string _completedHeaderText = "완료한 작업";
    private TaskGroup? _selectedGroup;
    private HwndSource? _hwndSource;
    private SmartFilterKind _currentFilter = SmartFilterKind.All;
    private WpfPoint _dragStartPoint;
    private TodoItem? _draggedTodoItem;

    public ObservableCollection<TaskGroup> Groups => _groups;
    public ObservableCollection<TodoItem> VisibleActiveTodoItems => _visibleActiveTodoItems;
    public ObservableCollection<TodoItem> VisibleCompletedTodoItems => _visibleCompletedTodoItems;

    public TaskGroup? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (ReferenceEquals(_selectedGroup, value)) return;
            _selectedGroup = value;
            _settings.SelectedGroupId = _selectedGroup?.Id ?? string.Empty;

            if (GroupNameTextBox is not null)
            {
                GroupNameTextBox.Text = _selectedGroup?.Name ?? string.Empty;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedGroupName));
            RefreshVisibleTodos();
            SaveSettings();
        }
    }

    public string SelectedGroupName => SelectedGroup?.Name ?? "목록 없음";

    public string SummaryText
    {
        get => _summaryText;
        private set
        {
            if (_summaryText == value) return;
            _summaryText = value;
            OnPropertyChanged();
        }
    }

    public string CompletedHeaderText
    {
        get => _completedHeaderText;
        private set
        {
            if (_completedHeaderText == value) return;
            _completedHeaderText = value;
            OnPropertyChanged();
        }
    }

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        _settings = _settingsStore.Load();
        _currentFilter = ParseFilter(_settings.SelectedFilter);

        var data = _todoStore.Load();
        if (data.Groups.Count == 0)
        {
            data.Groups.Add(new TaskGroup { Name = "기본", SortOrder = 0 });
        }

        _groups = new ObservableCollection<TaskGroup>(data.Groups.OrderBy(group => group.SortOrder));
        _todoItems = new ObservableCollection<TodoItem>(data.Items.OrderBy(item => item.GroupId).ThenBy(item => item.SortOrder));

        NormalizeData();
        AttachHandlers();
        ApplyWindowSettings();

        SelectedGroup = _groups.FirstOrDefault(group => group.Id == _settings.SelectedGroupId) ?? _groups.FirstOrDefault();
        GroupListBox.SelectedItem = SelectedGroup;

        UpdateLockButton();
        UpdateSidebarVisibility();
        UpdateCompletedFoldButton();
        UpdateFilterButtons();
        UpdateSettingsPanel();
        RefreshVisibleTodos();
        UpdateSummary();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void AttachHandlers()
    {
        _groups.CollectionChanged += Groups_CollectionChanged;
        _todoItems.CollectionChanged += TodoItems_CollectionChanged;

        foreach (var group in _groups)
        {
            group.PropertyChanged += Group_PropertyChanged;
        }

        foreach (var item in _todoItems)
        {
            item.PropertyChanged += TodoItem_PropertyChanged;
        }

        _trayIconService.ShowRequested += (_, _) => RestoreFromTray(false);
        _trayIconService.HideRequested += (_, _) => HideToTray();
        _trayIconService.ExitRequested += (_, _) => ExitApplication();
        _trayIconService.ToggleLockRequested += (_, _) => ToggleLock();
        _hotKeyService.QuickAddRequested += (_, _) => ActivateQuickAdd();

        SourceInitialized += MainWindow_SourceInitialized;
        LocationChanged += (_, _) => SaveSettings();
        SizeChanged += (_, _) => SaveSettings();
        Closing += MainWindow_Closing;
        StateChanged += MainWindow_StateChanged;
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        _bottomMostService.Initialize(hwnd);
        _hotKeyService.Initialize(hwnd, _settings.EnableGlobalQuickAdd);

        _hwndSource = HwndSource.FromHwnd(hwnd);
        _hwndSource?.AddHook(WndProc);

        if (_settings.KeepBottomMost)
        {
            _bottomMostService.Start();
        }
        else
        {
            _bottomMostService.Stop();
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            _bottomMostService.Dispose();
            _trayIconService.Dispose();
            _hotKeyService.Dispose();
            return;
        }

        e.Cancel = true;
        HideToTray();
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (_isRestoring)
        {
            return;
        }

        if (WindowState == WindowState.Minimized)
        {
            HideToTray();
        }
    }

    private void ApplyWindowSettings()
    {
        Width = Clamp(_settings.Width, 640, 1480);
        Height = Clamp(_settings.Height, 420, 1180);

        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var virtualWidth = SystemParameters.VirtualScreenWidth;
        var virtualHeight = SystemParameters.VirtualScreenHeight;

        Left = Clamp(_settings.Left, virtualLeft, virtualLeft + Math.Max(0, virtualWidth - Width));
        Top = Clamp(_settings.Top, virtualTop, virtualTop + Math.Max(0, virtualHeight - Height));
    }

    private static double Clamp(double value, double min, double max)
        => Math.Max(min, Math.Min(max, value));

    private void NormalizeData()
    {
        if (_groups.Count == 0)
        {
            _groups.Add(new TaskGroup { Name = "기본", SortOrder = 0 });
        }

        for (var i = 0; i < _groups.Count; i++)
        {
            _groups[i].SortOrder = i;
        }

        var existingGroupIds = _groups.Select(group => group.Id).ToHashSet(StringComparer.Ordinal);
        var fallbackGroupId = _groups.First().Id;

        foreach (var item in _todoItems)
        {
            if (!existingGroupIds.Contains(item.GroupId))
            {
                item.GroupId = fallbackGroupId;
            }
        }

        foreach (var group in _groups)
        {
            NormalizeTaskOrder(group.Id);
        }
    }

    private void NormalizeTaskOrder(string groupId)
    {
        var index = 0;
        foreach (var item in _todoItems.Where(item => item.GroupId == groupId).OrderBy(item => item.IsDone).ThenBy(item => item.SortOrder).ThenBy(item => item.CreatedAt))
        {
            item.SortOrder = index++;
        }
    }

    private void Groups_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (TaskGroup group in e.OldItems)
            {
                group.PropertyChanged -= Group_PropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (TaskGroup group in e.NewItems)
            {
                group.PropertyChanged += Group_PropertyChanged;
            }
        }

        NormalizeData();
        SaveData();
        UpdateSummary();
    }

    private void TodoItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (TodoItem item in e.OldItems)
            {
                item.PropertyChanged -= TodoItem_PropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (TodoItem item in e.NewItems)
            {
                item.PropertyChanged += TodoItem_PropertyChanged;
            }
        }

        SaveData();
        RefreshVisibleTodos();
        UpdateSummary();
    }

    private void Group_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        SaveData();
        OnPropertyChanged(nameof(SelectedGroupName));
        UpdateSummary();
    }

    private void TodoItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is TodoItem item && e.PropertyName == nameof(TodoItem.IsDone))
        {
            if (item.IsDone)
            {
                if (item.CompletedAt is null)
                {
                    item.CompletedAt = DateTime.Now;
                    if (item.RepeatPattern != TodoRepeatPattern.None)
                    {
                        var nextItem = item.CreateNextOccurrence();
                        nextItem.GroupId = item.GroupId;
                        nextItem.SortOrder = item.SortOrder + 1;
                        _todoItems.Add(nextItem);
                    }
                }
            }
            else
            {
                item.CompletedAt = null;
            }

            NormalizeTaskOrder(item.GroupId);
        }

        SaveData();
        RefreshVisibleTodos();
        UpdateSummary();
    }

    private void SaveData()
    {
        _todoStore.Save(_groups, _todoItems);
    }

    private void SaveSettings()
    {
        if (!IsLoaded)
        {
            return;
        }

        _settings.Left = Left;
        _settings.Top = Top;
        _settings.Width = Width;
        _settings.Height = Height;
        _settings.SelectedFilter = _currentFilter.ToString();
        _settings.SelectedGroupId = SelectedGroup?.Id ?? string.Empty;
        _settingsStore.Save(_settings);
    }

    private void RefreshVisibleTodos()
    {
        _visibleActiveTodoItems.Clear();
        _visibleCompletedTodoItems.Clear();

        if (SelectedGroup is null)
        {
            CompletedHeaderText = "완료한 작업";
            CompletedExpander.Visibility = Visibility.Collapsed;
            return;
        }

        var filteredItems = _todoItems
            .Where(item => item.GroupId == SelectedGroup.Id)
            .Where(MatchesCurrentFilter)
            .OrderBy(item => item.IsDone)
            .ThenBy(item => item.SortOrder)
            .ThenBy(item => item.CreatedAt)
            .ToList();

        foreach (var item in filteredItems.Where(item => !item.IsDone))
        {
            _visibleActiveTodoItems.Add(item);
        }

        foreach (var item in filteredItems.Where(item => item.IsDone))
        {
            _visibleCompletedTodoItems.Add(item);
        }

        CompletedHeaderText = _visibleCompletedTodoItems.Count == 0
            ? "완료한 작업이 없습니다"
            : $"완료한 작업 {_visibleCompletedTodoItems.Count}개";

        CompletedExpander.Visibility = _settings.ShowCompletedSection && _visibleCompletedTodoItems.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        CompletedExpander.IsExpanded = !_settings.AutoCollapseCompleted && _visibleCompletedTodoItems.Count > 0;
    }

    private bool MatchesCurrentFilter(TodoItem item)
    {
        var endOfWeek = GetEndOfWeek(DateTime.Today);
        var dueDate = item.DueDate?.Date;

        return _currentFilter switch
        {
            SmartFilterKind.Today => dueDate is not null && dueDate.Value <= DateTime.Today,
            SmartFilterKind.ThisWeek => dueDate is not null && dueDate.Value > DateTime.Today && dueDate.Value <= endOfWeek,
            SmartFilterKind.Later => dueDate is null || dueDate.Value > endOfWeek,
            _ => true
        };
    }

    private static DateTime GetEndOfWeek(DateTime date)
    {
        var current = date.Date;
        var diff = DayOfWeek.Saturday - current.DayOfWeek;
        if (diff < 0)
        {
            diff += 7;
        }
        return current.AddDays(diff);
    }

    private void UpdateSummary()
    {
        var total = _todoItems.Count;
        var done = _todoItems.Count(item => item.IsDone);
        var remaining = total - done;
        var overdue = _todoItems.Count(item => item.IsOverdue);

        SummaryText = total == 0
            ? "할 일을 추가해 주세요"
            : $"전체 {total}개 · 남은 일 {remaining}개 · 완료 {done}개 · 지연 {overdue}개";
    }

    private void UpdateLockButton()
    {
        LockButton.ToolTip = _settings.IsLocked ? "위치 고정 해제" : "위치 고정";
        LockButton.Background = _settings.IsLocked ? BrushFromHex("#26386AFF") : BrushFromHex("#0EFFFFFF");
        LockButton.BorderBrush = _settings.IsLocked ? BrushFromHex("#6F85FF") : BrushFromHex("#22FFFFFF");
    }

    private void UpdateSidebarVisibility()
    {
        var collapsed = _settings.IsSidebarCollapsed;
        SidebarPanel.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        SidebarColumn.Width = collapsed ? new GridLength(0) : new GridLength(220);
        if (SidebarVisibleCheckBox is not null)
        {
            SidebarVisibleCheckBox.IsChecked = !collapsed;
        }
    }

    private void UpdateCompletedFoldButton()
    {
        CompletedFoldToggleButton.Content = _settings.AutoCollapseCompleted ? "완료 자동 접기" : "완료 항목 펼쳐 보기";
        if (AutoCollapseCheckBox is not null)
        {
            AutoCollapseCheckBox.IsChecked = _settings.AutoCollapseCompleted;
        }
    }

    private void UpdateSettingsPanel()
    {
        SettingsPanel.Visibility = _isSettingsPanelOpen ? Visibility.Visible : Visibility.Collapsed;
        if (BottomMostCheckBox is not null)
        {
            BottomMostCheckBox.IsChecked = _settings.KeepBottomMost;
        }
        if (SidebarVisibleCheckBox is not null)
        {
            SidebarVisibleCheckBox.IsChecked = !_settings.IsSidebarCollapsed;
        }
        if (QuickAddHotkeyCheckBox is not null)
        {
            QuickAddHotkeyCheckBox.IsChecked = _settings.EnableGlobalQuickAdd;
        }
        if (AutoCollapseCheckBox is not null)
        {
            AutoCollapseCheckBox.IsChecked = _settings.AutoCollapseCompleted;
        }
    }

    private void UpdateFilterButtons()
    {
        ApplyFilterVisual(FilterAllButton, _currentFilter == SmartFilterKind.All);
        ApplyFilterVisual(FilterTodayButton, _currentFilter == SmartFilterKind.Today);
        ApplyFilterVisual(FilterThisWeekButton, _currentFilter == SmartFilterKind.ThisWeek);
        ApplyFilterVisual(FilterLaterButton, _currentFilter == SmartFilterKind.Later);
    }

    private void ApplyFilterVisual(WpfButton button, bool active)
    {
        button.Background = active ? BrushFromHex("#22386AFF") : BrushFromHex("#0DFFFFFF");
        button.BorderBrush = active ? BrushFromHex("#6F85FF") : BrushFromHex("#22FFFFFF");
        button.Foreground = active ? BrushFromHex("#F7FAFF") : BrushFromHex("#D7DEEB");
    }

    private static SolidColorBrush BrushFromHex(string hex)
        => new((WpfColor)WpfColorConverter.ConvertFromString(hex)!);

    private SmartFilterKind ParseFilter(string? key)
        => Enum.TryParse<SmartFilterKind>(key, out var filter) ? filter : SmartFilterKind.All;

    private void SetFilter(SmartFilterKind filter)
    {
        _currentFilter = filter;
        UpdateFilterButtons();
        RefreshVisibleTodos();
        SaveSettings();
    }

    private void AddTodo_Click(object sender, RoutedEventArgs e)
    {
        AddTodoFromInput();
    }

    private void NewTodoTextBox_KeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AddTodoFromInput();
            e.Handled = true;
        }
    }

    private void AddTodoFromInput()
    {
        var text = (NewTodoTextBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text) || SelectedGroup is null)
        {
            return;
        }

        var nextOrder = _todoItems
            .Where(item => item.GroupId == SelectedGroup.Id)
            .Select(item => item.SortOrder)
            .DefaultIfEmpty(-1)
            .Max() + 1;

        var priority = TodoPriority.Medium;
        if (NewTodoPriorityComboBox.SelectedItem is ComboBoxItem priorityItem && priorityItem.Tag is string priorityTag)
        {
            Enum.TryParse(priorityTag, out priority);
        }

        var repeatPattern = TodoRepeatPattern.None;
        if (NewTodoRepeatComboBox.SelectedItem is ComboBoxItem repeatItem && repeatItem.Tag is string repeatTag)
        {
            Enum.TryParse(repeatTag, out repeatPattern);
        }

        _todoItems.Add(new TodoItem
        {
            Text = text,
            GroupId = SelectedGroup.Id,
            SortOrder = nextOrder,
            Priority = priority,
            DueDate = NewTodoDatePicker.SelectedDate?.Date,
            RepeatPattern = repeatPattern,
            CreatedAt = DateTime.Now
        });

        NewTodoTextBox.Clear();
        NewTodoDatePicker.SelectedDate = null;
        NewTodoPriorityComboBox.SelectedIndex = 1;
        NewTodoRepeatComboBox.SelectedIndex = 0;
        NewTodoTextBox.Focus();
    }

    private void DeleteTodo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is TodoItem item)
        {
            _todoItems.Remove(item);
            NormalizeTaskOrder(item.GroupId);
            RefreshVisibleTodos();
            SaveData();
        }
    }

    private void PriorityButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not TodoItem item)
        {
            return;
        }

        item.Priority = item.Priority switch
        {
            TodoPriority.Low => TodoPriority.Medium,
            TodoPriority.Medium => TodoPriority.High,
            _ => TodoPriority.Low
        };
    }

    private void DueDateButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not TodoItem item)
        {
            return;
        }

        var endOfWeek = GetEndOfWeek(DateTime.Today);
        item.DueDate = item.DueDate?.Date switch
        {
            null => DateTime.Today,
            var due when due == DateTime.Today => endOfWeek,
            var due when due == endOfWeek => null,
            _ => null
        };
    }

    private void RepeatButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not TodoItem item)
        {
            return;
        }

        item.RepeatPattern = item.RepeatPattern switch
        {
            TodoRepeatPattern.None => TodoRepeatPattern.Daily,
            TodoRepeatPattern.Daily => TodoRepeatPattern.Weekly,
            TodoRepeatPattern.Weekly => TodoRepeatPattern.Monthly,
            _ => TodoRepeatPattern.None
        };
    }

    private void MoveUpButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is TodoItem item)
        {
            MoveItem(item, -1);
        }
    }

    private void MoveDownButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is TodoItem item)
        {
            MoveItem(item, 1);
        }
    }

    private void MoveItem(TodoItem item, int direction)
    {
        var items = _todoItems
            .Where(todo => todo.GroupId == item.GroupId && !todo.IsDone)
            .OrderBy(todo => todo.SortOrder)
            .ToList();

        var index = items.IndexOf(item);
        var targetIndex = index + direction;
        if (index < 0 || targetIndex < 0 || targetIndex >= items.Count)
        {
            return;
        }

        MoveItemToTarget(item, items[targetIndex]);
    }

    private void MoveItemToTarget(TodoItem draggedItem, TodoItem targetItem)
    {
        if (draggedItem.GroupId != targetItem.GroupId)
        {
            return;
        }

        var activeItems = _todoItems
            .Where(item => item.GroupId == draggedItem.GroupId && !item.IsDone)
            .OrderBy(item => item.SortOrder)
            .ToList();
        var completedItems = _todoItems
            .Where(item => item.GroupId == draggedItem.GroupId && item.IsDone)
            .OrderBy(item => item.SortOrder)
            .ToList();

        activeItems.Remove(draggedItem);
        var targetIndex = activeItems.IndexOf(targetItem);
        if (targetIndex < 0)
        {
            activeItems.Add(draggedItem);
        }
        else
        {
            activeItems.Insert(targetIndex, draggedItem);
        }

        var index = 0;
        foreach (var item in activeItems)
        {
            item.SortOrder = index++;
        }

        foreach (var item in completedItems)
        {
            item.SortOrder = index++;
        }

        RefreshVisibleTodos();
        SaveData();
    }

    private void CompletedFoldToggleButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.AutoCollapseCompleted = !_settings.AutoCollapseCompleted;
        UpdateCompletedFoldButton();
        RefreshVisibleTodos();
        SaveSettings();
    }

    private void RePinButton_Click(object sender, RoutedEventArgs e)
    {
        _bottomMostService.ApplyBottomMost();
    }

    private void AddGroupButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var group in _groups)
        {
            group.IsEditing = false;
        }

        var newGroup = new TaskGroup
        {
            Name = "새 목록",
            SortOrder = _groups.Count,
            IsEditing = true
        };

        _groups.Add(newGroup);
        SelectedGroup = newGroup;
        GroupListBox.SelectedItem = newGroup;
        SaveData();
        SaveSettings();
    }

    private void RenameGroupButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedGroup is not null)
        {
            BeginGroupRename(SelectedGroup);
        }
    }

    private void DeleteGroupButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedGroup is null)
        {
            return;
        }

        if (_groups.Count <= 1)
        {
            WpfMessageBox.Show("최소 한 개의 목록은 유지되어야 합니다.", "ToDoDo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var targetGroup = SelectedGroup;
        var fallbackGroup = _groups.First(group => !ReferenceEquals(group, targetGroup));

        foreach (var item in _todoItems.Where(item => item.GroupId == targetGroup.Id))
        {
            item.GroupId = fallbackGroup.Id;
        }

        _groups.Remove(targetGroup);
        NormalizeTaskOrder(fallbackGroup.Id);
        SelectedGroup = fallbackGroup;
        GroupListBox.SelectedItem = fallbackGroup;
        SaveData();
    }

    private void ExportButton_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new FileSaveDialog
        {
            Title = "백업 파일 저장",
            FileName = $"ToDoDo_backup_{DateTime.Now:yyyyMMdd_HHmmss}.json",
            Filter = "JSON 파일 (*.json)|*.json"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _backupService.Export(dialog.FileName, _groups, _todoItems, _settings);
        WpfMessageBox.Show("백업 파일을 저장했습니다.", "ToDoDo", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ImportButton_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new FileOpenDialog
        {
            Title = "백업 파일 불러오기",
            Filter = "JSON 파일 (*.json)|*.json"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var bundle = _backupService.Import(dialog.FileName);
        if (bundle is null)
        {
            WpfMessageBox.Show("백업 파일을 불러오지 못했습니다.", "ToDoDo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _groups.Clear();
        foreach (var group in bundle.Groups.OrderBy(group => group.SortOrder))
        {
            group.IsEditing = false;
            _groups.Add(group);
        }

        _todoItems.Clear();
        foreach (var item in bundle.Items.OrderBy(item => item.GroupId).ThenBy(item => item.SortOrder))
        {
            _todoItems.Add(item);
        }

        _settings = bundle.Settings ?? new WidgetSettings();
        NormalizeData();
        ApplyWindowSettings();
        _currentFilter = ParseFilter(_settings.SelectedFilter);
        SelectedGroup = _groups.FirstOrDefault(group => group.Id == _settings.SelectedGroupId) ?? _groups.FirstOrDefault();
        GroupListBox.SelectedItem = SelectedGroup;

        UpdateLockButton();
        UpdateSidebarVisibility();
        UpdateCompletedFoldButton();
        UpdateFilterButtons();
        UpdateSettingsPanel();
        RefreshVisibleTodos();
        UpdateSummary();
        SaveSettings();
        SaveData();

        WpfMessageBox.Show("백업 파일을 불러왔습니다.", "ToDoDo", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ToggleSidebarButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.IsSidebarCollapsed = !_settings.IsSidebarCollapsed;
        UpdateSidebarVisibility();
        SaveSettings();
    }

    private void LockButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleLock();
    }

    private void ToggleLock()
    {
        _settings.IsLocked = !_settings.IsLocked;
        UpdateLockButton();
        SaveSettings();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_settings.IsLocked || e.ClickCount == 2)
        {
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void HideButton_Click(object? sender, RoutedEventArgs e)
    {
        HideToTray();
    }

    private void ExitButton_Click(object? sender, RoutedEventArgs e)
    {
        ExitApplication();
    }

    private void HideToTray()
    {
        WindowState = WindowState.Minimized;
        Hide();
    }

    private void RestoreFromTray(bool focusInput)
    {
        _isRestoring = true;
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;

            Show();
            Visibility = Visibility.Visible;
            WindowState = WindowState.Normal;
            Win32.ShowWindow(hwnd, Win32.SW_RESTORE);
            Win32.ShowWindow(hwnd, Win32.SW_SHOW);
            Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, 0, 0, 0, 0,
                Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_SHOWWINDOW);

            Topmost = true;
            Activate();
            Focus();
            Win32.SetForegroundWindow(hwnd);

            Dispatcher.InvokeAsync(() =>
            {
                Topmost = false;
                if (_settings.KeepBottomMost)
                {
                    _bottomMostService.ApplyBottomMost();
                }

                if (focusInput)
                {
                    NewTodoTextBox.Focus();
                    NewTodoTextBox.SelectAll();
                }
            }, DispatcherPriority.ApplicationIdle);
        }
        finally
        {
            Dispatcher.InvokeAsync(() => _isRestoring = false, DispatcherPriority.Background);
        }
    }

    private void ActivateQuickAdd()
    {
        RestoreFromTray(true);
    }

    private void ExitApplication()
    {
        SaveData();
        SaveSettings();
        _allowClose = true;
        Close();
    }

    private void FilterAllButton_Click(object sender, RoutedEventArgs e) => SetFilter(SmartFilterKind.All);
    private void FilterTodayButton_Click(object sender, RoutedEventArgs e) => SetFilter(SmartFilterKind.Today);
    private void FilterThisWeekButton_Click(object sender, RoutedEventArgs e) => SetFilter(SmartFilterKind.ThisWeek);
    private void FilterLaterButton_Click(object sender, RoutedEventArgs e) => SetFilter(SmartFilterKind.Later);

    private void QuickAddContextMenu_Click(object sender, RoutedEventArgs e) => ActivateQuickAdd();
    private void ContextFilterAllMenuItem_Click(object sender, RoutedEventArgs e) => SetFilter(SmartFilterKind.All);
    private void ContextFilterTodayMenuItem_Click(object sender, RoutedEventArgs e) => SetFilter(SmartFilterKind.Today);
    private void ContextFilterWeekMenuItem_Click(object sender, RoutedEventArgs e) => SetFilter(SmartFilterKind.ThisWeek);
    private void ContextFilterLaterMenuItem_Click(object sender, RoutedEventArgs e) => SetFilter(SmartFilterKind.Later);

    private void ContextAutoCollapseMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _settings.AutoCollapseCompleted = !_settings.AutoCollapseCompleted;
        UpdateCompletedFoldButton();
        RefreshVisibleTodos();
        SaveSettings();
    }

    private void ContextSidebarMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _settings.IsSidebarCollapsed = !_settings.IsSidebarCollapsed;
        UpdateSidebarVisibility();
        SaveSettings();
    }

    private void ContextBottomMostMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _bottomMostService.ApplyBottomMost();
    }

    private void ContextHotkeyMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _settings.EnableGlobalQuickAdd = !_settings.EnableGlobalQuickAdd;
        _hotKeyService.UpdateRegistration(_settings.EnableGlobalQuickAdd);
        UpdateSettingsPanel();
        SaveSettings();
    }

    private void SettingsButton_Click(object? sender, RoutedEventArgs e)
    {
        _isSettingsPanelOpen = !_isSettingsPanelOpen;
        UpdateSettingsPanel();
    }

    private void SettingsCloseButton_Click(object? sender, RoutedEventArgs e)
    {
        _isSettingsPanelOpen = false;
        UpdateSettingsPanel();
    }

    private void BottomMostCheckBox_Click(object sender, RoutedEventArgs e)
    {
        _settings.KeepBottomMost = BottomMostCheckBox.IsChecked == true;
        if (_settings.KeepBottomMost)
        {
            _bottomMostService.Start();
            _bottomMostService.ApplyBottomMost();
        }
        else
        {
            _bottomMostService.Stop();
        }

        SaveSettings();
    }

    private void SidebarVisibilityCheckBox_Click(object sender, RoutedEventArgs e)
    {
        _settings.IsSidebarCollapsed = SidebarVisibleCheckBox.IsChecked != true;
        UpdateSidebarVisibility();
        SaveSettings();
    }

    private void QuickAddHotkeyCheckBox_Click(object sender, RoutedEventArgs e)
    {
        _settings.EnableGlobalQuickAdd = QuickAddHotkeyCheckBox.IsChecked == true;
        _hotKeyService.UpdateRegistration(_settings.EnableGlobalQuickAdd);
        SaveSettings();
    }

    private void AutoCollapseCheckBox_Click(object sender, RoutedEventArgs e)
    {
        _settings.AutoCollapseCompleted = AutoCollapseCheckBox.IsChecked == true;
        UpdateCompletedFoldButton();
        RefreshVisibleTodos();
        SaveSettings();
    }

    private void RestoreFromSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        RestoreFromTray(false);
    }

    private void GroupNameTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        CommitSelectedGroupName();
    }

    private void GroupNameTextBox_KeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitSelectedGroupName();
            e.Handled = true;
        }
    }

    private void CommitSelectedGroupName()
    {
        if (SelectedGroup is null)
        {
            return;
        }

        var value = (GroupNameTextBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            value = "이름 없는 목록";
        }

        SelectedGroup.Name = value;
        GroupNameTextBox.Text = value;
        SaveData();
    }

    private void GroupNameDisplay_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not TaskGroup group)
        {
            return;
        }

        if (ReferenceEquals(SelectedGroup, group))
        {
            BeginGroupRename(group);
            e.Handled = true;
        }
        else
        {
            SelectedGroup = group;
        }
    }

    private void BeginGroupRename(TaskGroup group)
    {
        foreach (var entry in _groups)
        {
            entry.IsEditing = false;
        }

        group.IsEditing = true;
        SelectedGroup = group;
        GroupListBox.SelectedItem = group;
    }

    private void GroupRenameTextBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is WpfTextBox textBox && textBox.DataContext is TaskGroup group && group.IsEditing)
        {
            textBox.Focus();
            textBox.SelectAll();
        }
    }

    private void GroupRenameTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is WpfTextBox textBox && textBox.DataContext is TaskGroup group)
        {
            CommitGroupRename(group, textBox.Text);
        }
    }

    private void GroupRenameTextBox_KeyDown(object sender, WpfKeyEventArgs e)
    {
        if (sender is not WpfTextBox textBox || textBox.DataContext is not TaskGroup group)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            CommitGroupRename(group, textBox.Text);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            group.IsEditing = false;
            e.Handled = true;
        }
    }

    private void CommitGroupRename(TaskGroup group, string? rawName)
    {
        var value = (rawName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            value = "새 목록";
        }

        group.Name = value;
        group.IsEditing = false;
        if (ReferenceEquals(SelectedGroup, group))
        {
            GroupNameTextBox.Text = value;
        }
        SaveData();
    }

    private void MainWindow_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        ContextFilterAllMenuItem.IsChecked = _currentFilter == SmartFilterKind.All;
        ContextFilterTodayMenuItem.IsChecked = _currentFilter == SmartFilterKind.Today;
        ContextFilterWeekMenuItem.IsChecked = _currentFilter == SmartFilterKind.ThisWeek;
        ContextFilterLaterMenuItem.IsChecked = _currentFilter == SmartFilterKind.Later;
        ContextAutoCollapseMenuItem.IsChecked = _settings.AutoCollapseCompleted;
        ContextSidebarMenuItem.IsChecked = !_settings.IsSidebarCollapsed;
        ContextHotkeyMenuItem.IsChecked = _settings.EnableGlobalQuickAdd;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32.WM_NCHITTEST && !_settings.IsLocked)
        {
            var hit = HitTestResizeBorder(lParam);
            if (hit != Win32.HTCLIENT)
            {
                handled = true;
                return new IntPtr(hit);
            }
        }

        if (msg == Win32.WM_HOTKEY && _hotKeyService.TryHandleHotKey(wParam))
        {
            handled = true;
            return IntPtr.Zero;
        }

        return IntPtr.Zero;
    }

    private int HitTestResizeBorder(IntPtr lParam)
    {
        if (WindowState != WindowState.Normal)
        {
            return Win32.HTCLIENT;
        }

        var x = (short)((long)lParam & 0xFFFF);
        var y = (short)(((long)lParam >> 16) & 0xFFFF);
        var point = PointFromScreen(new WpfPoint(x, y));

        var onLeft = point.X >= 0 && point.X <= ResizeBorderThickness;
        var onRight = point.X <= ActualWidth && point.X >= ActualWidth - ResizeBorderThickness;
        var onTop = point.Y >= 0 && point.Y <= ResizeBorderThickness;
        var onBottom = point.Y <= ActualHeight && point.Y >= ActualHeight - ResizeBorderThickness;

        if (onLeft && onTop) return Win32.HTTOPLEFT;
        if (onRight && onTop) return Win32.HTTOPRIGHT;
        if (onLeft && onBottom) return Win32.HTBOTTOMLEFT;
        if (onRight && onBottom) return Win32.HTBOTTOMRIGHT;
        if (onLeft) return Win32.HTLEFT;
        if (onRight) return Win32.HTRIGHT;
        if (onTop) return Win32.HTTOP;
        if (onBottom) return Win32.HTBOTTOM;

        return Win32.HTCLIENT;
    }

    private void ActiveTodoListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        _draggedTodoItem = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource)?.DataContext as TodoItem;
    }

    private void ActiveTodoListBox_MouseMove(object sender, WpfMouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _draggedTodoItem is null)
        {
            return;
        }

        var position = e.GetPosition(null);
        if (Math.Abs(position.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        DragDrop.DoDragDrop(ActiveTodoListBox, _draggedTodoItem, WpfDragDropEffects.Move);
    }

    private void ActiveTodoListBox_DragOver(object sender, WpfDragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(TodoItem)) ? WpfDragDropEffects.Move : WpfDragDropEffects.None;
        e.Handled = true;
    }

    private void ActiveTodoListBox_Drop(object sender, WpfDragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(TodoItem)))
        {
            return;
        }

        var draggedItem = e.Data.GetData(typeof(TodoItem)) as TodoItem;
        if (draggedItem is null)
        {
            return;
        }

        var targetContainer = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
        var targetItem = targetContainer?.DataContext as TodoItem;
        if (targetItem is not null && !ReferenceEquals(draggedItem, targetItem))
        {
            MoveItemToTarget(draggedItem, targetItem);
        }
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T typed)
            {
                return typed;
            }
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
