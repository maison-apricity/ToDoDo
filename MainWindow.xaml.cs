using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ToDoDo.Models;
using ToDoDo.Services;
using WpfButton = System.Windows.Controls.Button;
using WpfDragEventArgs = System.Windows.DragEventArgs;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfPoint = System.Windows.Point;
using WpfSize = System.Windows.Size;

namespace ToDoDo;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly AppState _state;
    private readonly DesktopPinService _pinService;
    private readonly TrayIconService _trayIconService;

    private TaskGroup? _selectedGroup;
    private FilterMode _currentFilter = FilterMode.All;
    private SortMode _currentSortMode = SortMode.Created;
    private bool _isSidebarVisible = true;
    private bool _isComposerVisible;
    private bool _isDeleteOverlayVisible;
    private bool _isSettingsOpen;
    private bool _isPinnedToDesktop = true;
    private bool _areCompletedDetailsCollapsed = true;
    private bool _draftUseDueDate;
    private string _draftTitle = string.Empty;
    private DateTime? _draftDueDate = DateTime.Today;
    private TodoPriority _draftPriority = TodoPriority.Normal;
    private TodoRepeat _draftRepeat = TodoRepeat.None;
    private string _summaryText = "전체 0 · 진행 0 · 완료 0";
    private string _deleteOverlayMessage = string.Empty;
    private bool _isEmptyVisible = true;
    private bool _isInitialized;

    private WpfPoint _dragStartPoint;
    private TodoItem? _draggedTodoItem;
    private TaskGroup? _draggedGroup;
    private WpfPoint _groupDragStartPoint;
    private TodoItem? _editingTodo;
    private List<DeletedTodoInfo> _lastDeletedTodos = new();
    private double _lastVisibleSidebarWidth = 220;
    private TodoItem? _inlineDueDateTodo;
    private const double HiddenMainMinWidth = 620;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<TaskGroup> Groups { get; } = new();
    public ObservableCollection<TodoItem> AllTodos { get; } = new();
    public ObservableCollection<TodoItem> VisibleTodos { get; } = new();


    private sealed class DeletedTodoInfo
    {
        public required TodoItem Item { get; init; }
        public required int CollectionIndex { get; init; }
        public required int SortOrder { get; init; }
        public required string GroupId { get; init; }
    }

    public TaskGroup? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (_selectedGroup == value)
            {
                return;
            }

            PersistGroupUiState(_selectedGroup);
            _selectedGroup = value;

            if (_selectedGroup is not null)
            {
                _currentFilter = FromInt(_selectedGroup.SavedFilterMode, FilterMode.All);
                _currentSortMode = FromInt(_selectedGroup.SavedSortMode, SortMode.Created);
                _areCompletedDetailsCollapsed = _selectedGroup.SavedCompletedCollapsed;
            }

            OnPropertyChanged(nameof(SelectedGroup));
            OnPropertyChanged(nameof(MainTitleText));
            UpdateFilterButtonLabel();
            UpdateActionButtonLabels();
            RefreshVisibleTodos();
            SaveState();
        }
    }

    public bool IsSidebarVisible
    {
        get => _isSidebarVisible;
        set
        {
            if (_isSidebarVisible == value)
            {
                return;
            }

            _isSidebarVisible = value;
            OnPropertyChanged(nameof(IsSidebarVisible));
            UpdateSidebarLayout();
            SaveState();
        }
    }

    public bool IsComposerVisible
    {
        get => _isComposerVisible;
        set
        {
            if (_isComposerVisible == value)
            {
                return;
            }

            _isComposerVisible = value;
            OnPropertyChanged(nameof(IsComposerVisible));
        }
    }

    public bool IsDeleteOverlayVisible
    {
        get => _isDeleteOverlayVisible;
        set
        {
            if (_isDeleteOverlayVisible == value)
            {
                return;
            }

            _isDeleteOverlayVisible = value;
            OnPropertyChanged(nameof(IsDeleteOverlayVisible));
        }
    }

    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        set
        {
            if (_isSettingsOpen == value)
            {
                return;
            }

            _isSettingsOpen = value;
            OnPropertyChanged(nameof(IsSettingsOpen));
        }
    }

    public string DeleteOverlayMessage
    {
        get => _deleteOverlayMessage;
        set
        {
            if (_deleteOverlayMessage == value)
            {
                return;
            }

            _deleteOverlayMessage = value;
            OnPropertyChanged(nameof(DeleteOverlayMessage));
        }
    }

    public bool IsEmptyVisible
    {
        get => _isEmptyVisible;
        set
        {
            if (_isEmptyVisible == value)
            {
                return;
            }

            _isEmptyVisible = value;
            OnPropertyChanged(nameof(IsEmptyVisible));
        }
    }

    public bool IsPinnedToDesktop
    {
        get => _isPinnedToDesktop;
        set
        {
            if (_isPinnedToDesktop == value)
            {
                return;
            }

            _isPinnedToDesktop = value;
            OnPropertyChanged(nameof(IsPinnedToDesktop));
            UpdatePinState();
        }
    }

    public bool AreCompletedDetailsCollapsed
    {
        get => _areCompletedDetailsCollapsed;
        set
        {
            if (_areCompletedDetailsCollapsed == value)
            {
                return;
            }

            _areCompletedDetailsCollapsed = value;
            OnPropertyChanged(nameof(AreCompletedDetailsCollapsed));
            UpdateActionButtonLabels();
        }
    }

    public string DraftTitle
    {
        get => _draftTitle;
        set
        {
            if (_draftTitle == value)
            {
                return;
            }

            _draftTitle = value;
            OnPropertyChanged(nameof(DraftTitle));
            OnPropertyChanged(nameof(IsDraftTitleEmpty));
        }
    }

    public bool IsDraftTitleEmpty => string.IsNullOrWhiteSpace(_draftTitle);

    public bool DraftUseDueDate
    {
        get => _draftUseDueDate;
        set
        {
            if (_draftUseDueDate == value)
            {
                return;
            }

            _draftUseDueDate = value;
            if (_draftUseDueDate && _draftDueDate is null)
            {
                _draftDueDate = DateTime.Today;
                OnPropertyChanged(nameof(DraftDueDate));
            }

            OnPropertyChanged(nameof(DraftUseDueDate));
            OnPropertyChanged(nameof(DraftDueDateText));
        }
    }

    public DateTime? DraftDueDate
    {
        get => _draftDueDate;
        set
        {
            if (_draftDueDate == value)
            {
                return;
            }

            _draftDueDate = value;
            OnPropertyChanged(nameof(DraftDueDate));
            OnPropertyChanged(nameof(DraftDueDateText));
        }
    }

    public string DraftDueDateText => DraftUseDueDate && DraftDueDate.HasValue ? DraftDueDate.Value.ToString("yyyy.MM.dd") : "날짜 선택";
    public string DraftPriorityText => $"우선순위 {PriorityToText(_draftPriority)}";
    public string DraftRepeatText => RepeatToText(_draftRepeat);

    public string SummaryText
    {
        get => _summaryText;
        set
        {
            if (_summaryText == value)
            {
                return;
            }

            _summaryText = value;
            OnPropertyChanged(nameof(SummaryText));
        }
    }

    public string MainTitleText => SelectedGroup is null ? "ToDo List" : $"ToDo List - {SelectedGroup.Name}";

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        ApplyResolvedWindowIcon();

        _state = StorageService.Load();

        foreach (var group in _state.Groups.OrderBy(g => g.SortOrder))
        {
            Groups.Add(group);
        }

        foreach (var todo in _state.Todos.OrderBy(t => t.SortOrder))
        {
            AllTodos.Add(todo);
        }

        if (Groups.Count == 0)
        {
            Groups.Add(new TaskGroup { Name = "기본", SortOrder = 0 });
        }

        _isSidebarVisible = _state.Settings.IsSidebarVisible;
        _isPinnedToDesktop = _state.Settings.IsPinnedToDesktop;
        _lastVisibleSidebarWidth = Math.Clamp(SafeFinite(_state.Settings.SidebarWidth, 220), 180, 360);

        Width = Math.Max(SafeFinite(_state.Settings.Width, 900), MinWidth);
        Height = Math.Max(SafeFinite(_state.Settings.Height, 760), MinHeight);
        Left = SafeFinite(_state.Settings.Left, 70);
        Top = SafeFinite(_state.Settings.Top, 50);

        _pinService = new DesktopPinService(this);
        _trayIconService = new TrayIconService(ShowFromTray, ShowComposerFromTray, TogglePinnedFromTray, CloseFromTray, () => IsPinnedToDesktop, AssetResolverService.ResolveTrayIcon());

        Loaded += MainWindow_Loaded;
        LocationChanged += (_, _) => SaveState();
        SizeChanged += (_, _) => { UpdateToolbarLayout(); UpdateMinimumWindowWidth(); SaveState(); };

        SelectedGroup = Groups.FirstOrDefault();
        UpdateSidebarLayout();
        UpdateFilterButtonLabel();
        UpdateActionButtonLabels();
        RefreshVisibleTodos();

        _isInitialized = true;
    }

    private void ApplyResolvedWindowIcon()
    {
        var iconSource = AssetResolverService.ResolveWindowIcon();
        if (iconSource is not null)
        {
            Icon = iconSource;
        }
    }

    private void PersistGroupUiState(TaskGroup? group)
    {
        if (group is null)
        {
            return;
        }

        group.SavedFilterMode = (int)_currentFilter;
        group.SavedSortMode = (int)_currentSortMode;
        group.SavedCompletedCollapsed = _areCompletedDetailsCollapsed;
    }

    private static TEnum FromInt<TEnum>(int value, TEnum fallback) where TEnum : struct, Enum
    {
        return Enum.IsDefined(typeof(TEnum), value) ? (TEnum)(object)value : fallback;
    }

    public void DisposeServices() => _trayIconService.Dispose();

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdatePinState();
        UpdateToolbarLayout();
        UpdateMinimumWindowWidth();
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(WndProc);
        }
    }

    private void HeaderDragArea_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            return;
        }

        if (HasInteractiveAncestor(e.OriginalSource as DependencyObject))
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch
        {
        }
    }

    private void SidebarToggleButton_Click(object sender, RoutedEventArgs e)
    {
        var sidebarWidth = Math.Clamp(SidebarColumn.ActualWidth > 0 ? SidebarColumn.ActualWidth : _lastVisibleSidebarWidth, 180, 360);
        var delta = sidebarWidth + 8;
        var oldWidth = Width;
        var oldLeft = Left;

        if (IsSidebarVisible)
        {
            _lastVisibleSidebarWidth = sidebarWidth;
            IsSidebarVisible = false;
            UpdateMinimumWindowWidth();
            Width = Math.Max(MinWidth, oldWidth - delta);
            Left = oldLeft + (oldWidth - Width);
        }
        else
        {
            IsSidebarVisible = true;
            UpdateMinimumWindowWidth();
            Width = Math.Max(MinWidth, oldWidth + delta);
            Left = Math.Max(0, oldLeft - (Width - oldWidth));
        }

        SaveState();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        IsSettingsOpen = !IsSettingsOpen;
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source)
        {
            var editingGroup = Groups.FirstOrDefault(g => g.IsEditing);
            if (editingGroup is not null && GetAncestor<TextBox>(source) is null)
            {
                CancelRenameGroup(editingGroup);
            }
        }

        if (!IsSettingsOpen)
        {
            return;
        }

        if (e.OriginalSource is not DependencyObject settingsSource)
        {
            IsSettingsOpen = false;
            return;
        }

        if (IsVisualDescendantOf(settingsSource, SettingsPanel) || IsVisualDescendantOf(settingsSource, SettingsToggleButton))
        {
            return;
        }

        if (DueDatePopup.IsOpen && IsVisualDescendantOf(settingsSource, DueDateButton))
        {
            return;
        }

        IsSettingsOpen = false;
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        IsPinnedToDesktop = !IsPinnedToDesktop;
    }

    private void PinnedToggleButton_Click(object sender, RoutedEventArgs e)
    {
        UpdatePinState();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void UpdatePinState()
    {
        _pinService.SetPinned(IsPinnedToDesktop);
        UpdatePinnedVisuals();
        SaveState();
    }

    private void UpdatePinnedVisuals()
    {
        if (HeaderPinButton is not null)
        {
            HeaderPinButton.BorderBrush = IsPinnedToDesktop ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B7C3FF")!) : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#42526C")!);
            HeaderPinButton.BorderThickness = IsPinnedToDesktop ? new Thickness(2.4) : new Thickness(1);
            HeaderPinButton.Background = IsPinnedToDesktop ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3A568F")!) : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#263246")!);
            HeaderPinButton.ToolTip = IsPinnedToDesktop ? "바탕화면 고정 중" : "바탕화면에 고정";
        }

        if (SettingsPinButton is not null)
        {
            SettingsPinButton.BorderBrush = IsPinnedToDesktop ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B7C3FF")!) : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#42526C")!);
            SettingsPinButton.BorderThickness = IsPinnedToDesktop ? new Thickness(2.4) : new Thickness(1);
            SettingsPinButton.Background = IsPinnedToDesktop ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3A568F")!) : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#263246")!);
            SettingsPinButton.Content = IsPinnedToDesktop ? "바탕화면 고정 중" : "바탕화면 고정";
        }
    }

    private void UpdateSidebarLayout()
    {
        if (SidebarColumn is null || SidebarSpacerColumn is null)
        {
            return;
        }

        var sidebarWidth = Math.Clamp(SafeFinite(_state.Settings.SidebarWidth, _lastVisibleSidebarWidth), 180, 360);
        if (IsSidebarVisible)
        {
            _lastVisibleSidebarWidth = sidebarWidth;
        }

        SidebarColumn.Width = IsSidebarVisible ? new GridLength(sidebarWidth) : new GridLength(0);
        SidebarSpacerColumn.Width = IsSidebarVisible ? new GridLength(8) : new GridLength(0);
        if (SidebarSplitter is not null)
        {
            SidebarSplitter.Width = IsSidebarVisible ? 8 : 0;
        }

        UpdateMinimumWindowWidth();
    }

    private void UpdateToolbarLayout()
    {
        if (ToolbarCol0 is null || ToolbarCol2 is null || ToolbarCol4 is null || ToolbarCol6 is null)
        {
            return;
        }

        var buttons = new FrameworkElement?[] { FilterCycleButton, SortButton, CompletedToggleButton, DeleteSelectedButton };
        var widths = new double[4];
        for (var i = 0; i < buttons.Length; i++)
        {
            var btn = buttons[i];
            if (btn is null) continue;
            btn.Measure(new WpfSize(double.PositiveInfinity, double.PositiveInfinity));
            widths[i] = Math.Max(btn.DesiredSize.Width, btn.ActualWidth > 0 ? btn.ActualWidth : 0);
            if (widths[i] < 78) widths[i] = 78;
        }

        var sum = widths.Sum();
        if (sum <= 0) sum = 4;
        ToolbarCol0.Width = new GridLength(widths[0], GridUnitType.Star);
        ToolbarCol2.Width = new GridLength(widths[1], GridUnitType.Star);
        ToolbarCol4.Width = new GridLength(widths[2], GridUnitType.Star);
        ToolbarCol6.Width = new GridLength(widths[3], GridUnitType.Star);
    }

    private void UpdateMinimumWindowWidth()
    {
        var sidebarWidth = IsSidebarVisible ? Math.Clamp(SidebarColumn.ActualWidth > 0 ? SidebarColumn.ActualWidth : _lastVisibleSidebarWidth, 180, 360) + 8 : 0;
        double toolbarRequired = 0;
        foreach (var btn in new FrameworkElement?[] { FilterCycleButton, SortButton, CompletedToggleButton, DeleteSelectedButton })
        {
            if (btn is null) continue;
            btn.Measure(new WpfSize(double.PositiveInfinity, double.PositiveInfinity));
            toolbarRequired += Math.Max(btn.ActualWidth, btn.DesiredSize.Width);
        }
        toolbarRequired += 24;

        var quickAddRequired = 0d;
        if (QuickAddLauncherButton is not null)
        {
            QuickAddLauncherButton.Measure(new WpfSize(double.PositiveInfinity, double.PositiveInfinity));
            quickAddRequired = Math.Max(QuickAddLauncherButton.ActualWidth, QuickAddLauncherButton.DesiredSize.Width);
        }

        var computedMain = Math.Max(HiddenMainMinWidth, Math.Max(toolbarRequired, quickAddRequired) + 28);
        UpdateToolbarLayout();
        MinWidth = computedMain + sidebarWidth + 28;
    }

    private void SidebarSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        _state.Settings.SidebarWidth = Math.Clamp(SidebarColumn.ActualWidth, 180, 360);
        _lastVisibleSidebarWidth = _state.Settings.SidebarWidth;
        SaveState();
    }

    private void AddGroupButton_Click(object sender, RoutedEventArgs e)
    {
        var group = new TaskGroup
        {
            Name = string.Empty,
            SortOrder = Groups.Count == 0 ? 0 : Groups.Max(g => g.SortOrder) + 1,
            IsEditing = true
        };

        Groups.Add(group);
        SelectedGroup = group;
        SaveState();

        Dispatcher.BeginInvoke(() =>
        {
            GroupListBox.SelectedItem = group;
            GroupListBox.ScrollIntoView(group);
            StartRenameGroup(group);
        });
    }

    private void DeleteGroupButton_Click(object sender, RoutedEventArgs e)
    {
        ShowDeleteGroupOverlay();
    }

    private void ShowDeleteGroupOverlay()
    {
        if (SelectedGroup is null)
        {
            return;
        }

        if (Groups.Count <= 1)
        {
            System.Windows.MessageBox.Show("최소 한 개의 그룹은 남아 있어야 합니다.", "ToDoDo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DeleteOverlayMessage = $"\"{SelectedGroup.Name}\" 그룹을 삭제할까요? 해당 그룹의 할 일도 함께 제거됩니다.";
        IsDeleteOverlayVisible = true;
    }

    private void ConfirmDeleteOverlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedGroup is null || Groups.Count <= 1)
        {
            IsDeleteOverlayVisible = false;
            return;
        }

        var groupToDelete = SelectedGroup;
        foreach (var todo in AllTodos.Where(t => t.GroupId == groupToDelete.Id).ToList())
        {
            AllTodos.Remove(todo);
        }

        Groups.Remove(groupToDelete);
        SelectedGroup = Groups.OrderBy(g => g.SortOrder).FirstOrDefault();
        IsDeleteOverlayVisible = false;
        RefreshVisibleTodos();
        SaveState();
    }

    private void CancelDeleteOverlayButton_Click(object sender, RoutedEventArgs e)
    {
        IsDeleteOverlayVisible = false;
    }

    private void GroupItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && GetAncestor<TextBox>(source) is not null)
        {
            return;
        }

        if (GetDataContext<TaskGroup>(sender) is not { } group)
        {
            return;
        }

        if (group.IsEditing)
        {
            return;
        }

        SelectedGroup = group;

        if (e.ClickCount >= 2 && !HasInteractiveAncestor(e.OriginalSource as DependencyObject))
        {
            StartRenameGroup(group);
            e.Handled = true;
        }
    }

    private void RenameGroupMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetDataContextFromMenu<TaskGroup>(sender) is { } group)
        {
            SelectedGroup = group;
            StartRenameGroup(group);
        }
    }

    private void DeleteGroupMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetDataContextFromMenu<TaskGroup>(sender) is { } group)
        {
            SelectedGroup = group;
            ShowDeleteGroupOverlay();
        }
    }

    private void StartRenameGroup(TaskGroup group)
    {
        foreach (var item in Groups)
        {
            if (item != group)
            {
                item.CancelEdit();
            }
        }

        group.BeginEdit();
    }

    private void GroupRenameTextBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is WpfTextBox textBox && textBox.DataContext is TaskGroup group && group.IsEditing)
        {
            FocusRenameTextBox(textBox);
        }
    }

    private void GroupRenameTextBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is WpfTextBox textBox && textBox.IsVisible)
        {
            FocusRenameTextBox(textBox);
        }
    }

    private void GroupRenameTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is WpfTextBox textBox && textBox.DataContext is TaskGroup group)
        {
            CancelRenameGroup(group);
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
            EndRenameGroup(group, textBox.Text);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelRenameGroup(group);
            e.Handled = true;
        }
        else if (e.Key is Key.Delete or Key.Back or Key.Left or Key.Right or Key.Home or Key.End)
        {
            e.Handled = false;
        }
    }

    private void EndRenameGroup(TaskGroup group, string text)
    {
        group.EditName = text;
        group.CommitEdit();
        OnPropertyChanged(nameof(MainTitleText));
        SaveState();
        RefreshVisibleTodos();
    }

    private void CancelRenameGroup(TaskGroup group)
    {
        group.CancelEdit();
        OnPropertyChanged(nameof(MainTitleText));
        SaveState();
        RefreshVisibleTodos();
    }

    private void ShowComposerButton_Click(object sender, RoutedEventArgs e) => ShowComposer();
    private void PlaceholderAddTodoButton_Click(object sender, RoutedEventArgs e) => ShowComposer();

    private void ShowComposer()
    {
        if (!IsComposerVisible)
        {
            PrepareDraftForNewTodo();
            IsComposerVisible = true;
        }

        FocusComposer();
    }

    private void ShowComposerFromTray()
    {
        Dispatcher.Invoke(() =>
        {
            ShowFromTray();
            PrepareDraftForNewTodo();
            FocusComposer();
        });
    }

    private void PrepareDraftForNewTodo()
    {
        if (SelectedGroup is null)
        {
            return;
        }

        _editingTodo = null;
        IsComposerVisible = true;
        DraftTitle = string.Empty;
        DraftUseDueDate = false;
        DraftDueDate = DateTime.Today;
        _draftPriority = TodoPriority.Normal;
        _draftRepeat = TodoRepeat.None;
        OnPropertyChanged(nameof(DraftPriorityText));
        OnPropertyChanged(nameof(DraftRepeatText));
        OnPropertyChanged(nameof(DraftDueDateText));

        RefreshVisibleTodos();
    }

    private void PrepareDraftForEdit(TodoItem todo)
    {
        _editingTodo = todo;
        IsComposerVisible = true;
        DraftTitle = todo.Text;
        DraftUseDueDate = todo.DueDate.HasValue;
        DraftDueDate = todo.DueDate ?? DateTime.Today;
        _draftPriority = todo.Priority;
        _draftRepeat = todo.Repeat;
        OnPropertyChanged(nameof(DraftPriorityText));
        OnPropertyChanged(nameof(DraftRepeatText));
        OnPropertyChanged(nameof(DraftDueDateText));

        RefreshVisibleTodos();
    }

    private void FocusComposer()
    {
        Dispatcher.BeginInvoke(() =>
        {
            ComposerTitleTextBox.Focus();
            ComposerTitleTextBox.SelectAll();
        });
    }

    private void CancelComposerButton_Click(object sender, RoutedEventArgs e)
    {
        DueDatePopup.IsOpen = false;
        PrepareDraftForNewTodo();
        IsComposerVisible = false;
    }

    private void SaveComposerButton_Click(object sender, RoutedEventArgs e)
    {
        CreateTodoFromDraft();
    }

    private void ComposerTitleTextBox_KeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CreateTodoFromDraft();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            PrepareDraftForNewTodo();
            IsComposerVisible = false;
            e.Handled = true;
        }
    }

    private void DueDateButton_Click(object sender, RoutedEventArgs e)
    {
        if (!DraftUseDueDate)
        {
            return;
        }

        _inlineDueDateTodo = null;
        DueDatePopup.PlacementTarget = sender as UIElement;
        DueDateCalendar.SelectedDate = DraftDueDate ?? DateTime.Today;
        DueDatePopup.IsOpen = true;
    }

    private void DueDateCalendar_SelectedDatesChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DueDateCalendar.SelectedDate.HasValue)
        {
            if (_inlineDueDateTodo is not null)
            {
                _inlineDueDateTodo.EditDueDate = DueDateCalendar.SelectedDate.Value;
            }
            else
            {
                DraftDueDate = DueDateCalendar.SelectedDate.Value;
            }
        }

        _inlineDueDateTodo = null;
        DueDatePopup.IsOpen = false;
    }

    private void DraftPriorityButton_Click(object sender, RoutedEventArgs e)
    {
        _draftPriority = _draftPriority switch
        {
            TodoPriority.Low => TodoPriority.Normal,
            TodoPriority.Normal => TodoPriority.High,
            _ => TodoPriority.Low
        };

        OnPropertyChanged(nameof(DraftPriorityText));
    }

    private void DraftRepeatButton_Click(object sender, RoutedEventArgs e)
    {
        _draftRepeat = _draftRepeat switch
        {
            TodoRepeat.None => TodoRepeat.Daily,
            TodoRepeat.Daily => TodoRepeat.Weekly,
            TodoRepeat.Weekly => TodoRepeat.Monthly,
            _ => TodoRepeat.None
        };

        OnPropertyChanged(nameof(DraftRepeatText));
    }

    private void CreateTodoFromDraft()
    {
        if (SelectedGroup is null)
        {
            return;
        }

        var title = string.IsNullOrWhiteSpace(DraftTitle) ? "새 할 일" : DraftTitle.Trim();
        var due = DraftUseDueDate ? DraftDueDate : null;
        TodoItem todo;

        if (_editingTodo is not null)
        {
            todo = _editingTodo;
            todo.Text = title;
            todo.Priority = _draftPriority;
            todo.Repeat = _draftRepeat;
            todo.DueDate = due;
            todo.IsExpanded = true;
            todo.CancelEdit();
            _editingTodo = null;
        }
        else
        {
            var nextSort = AllTodos.Where(t => t.GroupId == SelectedGroup.Id).Select(t => t.SortOrder).DefaultIfEmpty(-1).Max() + 1;
            todo = new TodoItem
            {
                GroupId = SelectedGroup.Id,
                Text = title,
                Priority = _draftPriority,
                Repeat = _draftRepeat,
                DueDate = due,
                SortOrder = nextSort,
                CreatedAt = DateTime.Now,
                IsExpanded = true
            };

            AllTodos.Add(todo);
        }

        DueDatePopup.IsOpen = false;
        PrepareDraftForNewTodo();
        IsComposerVisible = false;
        RefreshVisibleTodos();
        SaveState();

        Dispatcher.BeginInvoke(() =>
        {
            ActiveTodoListBox.SelectedItem = todo;
            ActiveTodoListBox.ScrollIntoView(todo);
        });
    }

    private void CycleFilterButton_Click(object sender, RoutedEventArgs e)
    {
        _currentFilter = _currentFilter switch
        {
            FilterMode.All => FilterMode.Today,
            FilterMode.Today => FilterMode.ThisWeek,
            FilterMode.ThisWeek => FilterMode.NoDueDate,
            _ => FilterMode.All
        };

        UpdateFilterButtonLabel();
        RefreshVisibleTodos();
        SaveState();
    }

    private void UpdateFilterButtonLabel()
    {
        if (FilterCycleButton is not null)
        {
            FilterCycleButton.Content = FilterModeToText(_currentFilter);
        }
    }

    private void SortButton_Click(object sender, RoutedEventArgs e)
    {
        _currentSortMode = _currentSortMode switch
        {
            SortMode.DueDate => SortMode.Created,
            SortMode.Created => SortMode.Name,
            SortMode.Name => SortMode.Priority,
            SortMode.Priority => SortMode.Manual,
            _ => SortMode.DueDate
        };

        UpdateActionButtonLabels();
        RefreshVisibleTodos();
        SaveState();
    }

    private void ToggleCompletedDetailsButton_Click(object sender, RoutedEventArgs e)
    {
        AreCompletedDetailsCollapsed = !AreCompletedDetailsCollapsed;
        SaveState();
        RefreshVisibleTodos();
    }

    private void UpdateActionButtonLabels()
    {
        if (SortButton is not null)
        {
            SortButton.Content = $"정렬: {SortModeToText(_currentSortMode)}";
        }

        if (CompletedToggleButton is not null)
        {
            CompletedToggleButton.Content = AreCompletedDetailsCollapsed ? "완료 펼치기" : "완료 접기";
        }
    }

    private void DeleteSelectedTodosButton_Click(object sender, RoutedEventArgs e)
    {
        DeleteSelectedTodos();
    }

    private void DeleteSelectedTodos()
    {
        var selected = ActiveTodoListBox.SelectedItems.Cast<TodoItem>().ToList();
        if (selected.Count == 0)
        {
            return;
        }

        RememberDeletedTodos(selected);

        foreach (var todo in selected)
        {
            AllTodos.Remove(todo);
        }

        RefreshVisibleTodos();
        SaveState();
    }

    private void RememberDeletedTodos(IEnumerable<TodoItem> todos)
    {
        _lastDeletedTodos = todos
            .Select(todo => new DeletedTodoInfo
            {
                Item = todo,
                CollectionIndex = AllTodos.IndexOf(todo),
                SortOrder = todo.SortOrder,
                GroupId = todo.GroupId
            })
            .OrderBy(info => info.CollectionIndex)
            .ToList();
    }

    private void RestoreLastDeletedTodos()
    {
        if (_lastDeletedTodos.Count == 0)
        {
            return;
        }

        foreach (var deleted in _lastDeletedTodos.OrderBy(info => info.CollectionIndex))
        {
            if (!Groups.Any(g => g.Id == deleted.GroupId))
            {
                continue;
            }

            deleted.Item.SortOrder = deleted.SortOrder;
            var insertAt = Math.Clamp(deleted.CollectionIndex, 0, AllTodos.Count);
            AllTodos.Insert(insertAt, deleted.Item);
        }

        NormalizeSortOrders();
        _lastDeletedTodos.Clear();
        RefreshVisibleTodos();
        SaveState();
    }

    private void NormalizeSortOrders()
    {
        foreach (var group in Groups)
        {
            var groupTodos = AllTodos.Where(t => t.GroupId == group.Id).OrderBy(t => t.SortOrder).ThenBy(t => t.CreatedAt).ToList();
            for (var i = 0; i < groupTodos.Count; i++)
            {
                groupTodos[i].SortOrder = i;
            }
        }
    }

    private void TodoCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2)
        {
            return;
        }

        if (HasInteractiveAncestor(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (GetDataContext<TodoItem>(sender) is { } todo)
        {
            StartInlineEdit(todo);
            e.Handled = true;
        }
    }

    private void RenameTodoMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetDataContextFromMenu<TodoItem>(sender) is { } todo)
        {
            StartInlineEdit(todo);
        }
    }

    private void DeleteTodoMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetDataContextFromMenu<TodoItem>(sender) is { } todo)
        {
            RememberDeletedTodos(new[] { todo });
            AllTodos.Remove(todo);
            RefreshVisibleTodos();
            SaveState();
        }
    }

    private void StartInlineEdit(TodoItem todo)
    {
        foreach (var item in AllTodos)
        {
            item.CancelEdit();
        }

        todo.BeginEdit();
        todo.IsExpanded = true;
        ActiveTodoListBox.SelectedItem = todo;
    }

    private void TodoRenameTextBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is WpfTextBox textBox && textBox.DataContext is TodoItem todo && todo.IsEditing)
        {
            FocusRenameTextBox(textBox);
        }
    }

    private void TodoRenameTextBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is WpfTextBox textBox && textBox.IsVisible)
        {
            FocusRenameTextBox(textBox);
        }
    }

    private void TodoRenameTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is WpfTextBox textBox && textBox.DataContext is TodoItem todo)
        {
            SaveInlineEdit(todo);
        }
    }

    private void TodoRenameTextBox_KeyDown(object sender, WpfKeyEventArgs e)
    {
        if (sender is not WpfTextBox textBox || textBox.DataContext is not TodoItem todo)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            SaveInlineEdit(todo);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            todo.CancelEdit();
            e.Handled = true;
        }
    }

    private void SaveInlineEdit(TodoItem todo)
    {
        todo.CommitEdit();
        SaveState();
        RefreshVisibleTodos();
    }

    private void EditTodoButton_Click(object sender, RoutedEventArgs e)
    {
        if (GetDataContext<TodoItem>(sender) is { } todo)
        {
            StartInlineEdit(todo);
        }
    }

    private void SaveInlineEditButton_Click(object sender, RoutedEventArgs e)
    {
        if (GetDataContext<TodoItem>(sender) is { } todo)
        {
            SaveInlineEdit(todo);
        }
    }

    private void CancelInlineEditButton_Click(object sender, RoutedEventArgs e)
    {
        if (GetDataContext<TodoItem>(sender) is { } todo)
        {
            todo.CancelEdit();
        }
    }

    private void InlineEditTextBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is WpfTextBox textBox && textBox.DataContext is TodoItem todo && todo.IsEditing)
        {
            FocusRenameTextBox(textBox);
        }
    }

    private void InlineEditTextBox_KeyDown(object sender, WpfKeyEventArgs e)
    {
        if (sender is not WpfTextBox textBox || textBox.DataContext is not TodoItem todo)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            SaveInlineEdit(todo);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            todo.CancelEdit();
            e.Handled = true;
        }
    }

    private void CycleInlinePriorityButton_Click(object sender, RoutedEventArgs e)
    {
        if (GetDataContext<TodoItem>(sender) is not { } todo)
        {
            return;
        }

        todo.EditPriority = todo.EditPriority switch
        {
            TodoPriority.Low => TodoPriority.Normal,
            TodoPriority.Normal => TodoPriority.High,
            _ => TodoPriority.Low
        };
    }

    private void CycleInlineRepeatButton_Click(object sender, RoutedEventArgs e)
    {
        if (GetDataContext<TodoItem>(sender) is not { } todo)
        {
            return;
        }

        todo.EditRepeat = todo.EditRepeat switch
        {
            TodoRepeat.None => TodoRepeat.Daily,
            TodoRepeat.Daily => TodoRepeat.Weekly,
            TodoRepeat.Weekly => TodoRepeat.Monthly,
            _ => TodoRepeat.None
        };
    }

    private void InlineDueDateButton_Click(object sender, RoutedEventArgs e)
    {
        if (GetDataContext<TodoItem>(sender) is not { } todo)
        {
            return;
        }

        if (!todo.EditUseDueDate)
        {
            return;
        }

        _inlineDueDateTodo = todo;
        DueDatePopup.PlacementTarget = sender as UIElement;
        DueDateCalendar.SelectedDate = todo.EditDueDate ?? DateTime.Today;
        DueDatePopup.IsOpen = true;
    }

    private void EndRenameTodo(TodoItem todo, string text)
    {
        todo.Text = string.IsNullOrWhiteSpace(text) ? "새 할 일" : text.Trim();
        todo.CancelEdit();
        SaveState();
    }

    private async void CompleteTodoButton_Click(object sender, RoutedEventArgs e)
    {
        if (GetDataContext<TodoItem>(sender) is not { } todo)
        {
            return;
        }

        if (!todo.IsDone)
        {
            todo.IsCompletingFeedback = true;
            await Task.Delay(180);
            todo.IsCompletingFeedback = false;
            todo.IsDone = true;
            todo.IsExpanded = false;
        }
        else
        {
            todo.IsDone = false;
            todo.IsExpanded = true;
        }

        RefreshVisibleTodos();
        SaveState();
    }

    private void HandleRepeatOnCompletion(TodoItem sourceTodo)
    {
        if (sourceTodo.Repeat == TodoRepeat.None)
        {
            return;
        }

        var nextDate = sourceTodo.DueDate ?? DateTime.Today;
        nextDate = sourceTodo.Repeat switch
        {
            TodoRepeat.Daily => nextDate.AddDays(1),
            TodoRepeat.Weekly => nextDate.AddDays(7),
            TodoRepeat.Monthly => nextDate.AddMonths(1),
            _ => nextDate
        };

        var clone = new TodoItem
        {
            GroupId = sourceTodo.GroupId,
            Text = sourceTodo.Text,
            Priority = sourceTodo.Priority,
            Repeat = sourceTodo.Repeat,
            DueDate = nextDate,
            SortOrder = AllTodos.Where(t => t.GroupId == sourceTodo.GroupId).Select(t => t.SortOrder).DefaultIfEmpty(-1).Max() + 1,
            CreatedAt = DateTime.Now,
            IsExpanded = true
        };

        AllTodos.Add(clone);
        sourceTodo.Repeat = TodoRepeat.None;
    }


    private void ActiveTodoListBox_DragOver(object sender, WpfDragEventArgs e)
    {
        if (_currentSortMode != SortMode.Manual || !e.Data.GetDataPresent(typeof(TodoItem)))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;

        if (_draggedTodoItem is not null)
        {
        }
    }

    private void ActiveTodoListBox_Drop(object sender, WpfDragEventArgs e)
    {
        try
        {
            if (_currentSortMode != SortMode.Manual || !e.Data.GetDataPresent(typeof(TodoItem)))
            {
                return;
            }

            var source = e.Data.GetData(typeof(TodoItem)) as TodoItem;
            if (source is null)
            {
                return;
            }

            var groupItems = AllTodos.Where(t => t.GroupId == source.GroupId).OrderBy(t => t.SortOrder).ToList();
            if (groupItems.Count == 0)
            {
                return;
            }

            var targetItem = GetItemFromPoint<TodoItem>(e.GetPosition(ActiveTodoListBox));
            groupItems.Remove(source);

            var insertIndex = groupItems.Count;
            if (targetItem is not null && targetItem.GroupId == source.GroupId)
            {
                insertIndex = groupItems.IndexOf(targetItem);
                if (insertIndex < 0)
                {
                    insertIndex = groupItems.Count;
                }
                else
                {
                    var container = ActiveTodoListBox.ItemContainerGenerator.ContainerFromItem(targetItem) as ListBoxItem;
                    if (container is not null)
                    {
                        var position = e.GetPosition(container);
                        if (position.Y > container.ActualHeight / 2)
                        {
                            insertIndex++;
                        }
                    }
                }
            }

            if (insertIndex > groupItems.Count)
            {
                insertIndex = groupItems.Count;
            }

            groupItems.Insert(insertIndex, source);
            for (var i = 0; i < groupItems.Count; i++)
            {
                groupItems[i].SortOrder = i;
            }

            RefreshVisibleTodos();
            ActiveTodoListBox.SelectedItem = source;
            ActiveTodoListBox.ScrollIntoView(source);
            SaveState();
        }
        finally
        {
            _draggedTodoItem = null;
            Mouse.Capture(null);
        }
    }

    private void TodoDragHandle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (GetDataContext<TodoItem>(sender) is not { } todo)
        {
            return;
        }

        _currentSortMode = SortMode.Manual;
        UpdateActionButtonLabels();
        PersistGroupUiState(SelectedGroup);
        _draggedTodoItem = todo;
        _dragStartPoint = e.GetPosition(ActiveTodoListBox);
        Mouse.Capture(sender as IInputElement);
        ActiveTodoListBox.SelectedItem = todo;
        e.Handled = true;
    }

    private void TodoDragHandle_PreviewMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (_draggedTodoItem is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var position = e.GetPosition(ActiveTodoListBox);
        if (Math.Abs(position.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        try
        {
            var data = new System.Windows.DataObject(typeof(TodoItem), _draggedTodoItem);
            DragDrop.DoDragDrop(sender as DependencyObject ?? ActiveTodoListBox, data, DragDropEffects.Move);
        }
        finally
        {
            Mouse.Capture(null);
        }
    }


    private static bool IsDragHandle(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is TextBlock tb && Equals(tb.Text, "⋮⋮"))
            {
                return true;
            }
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }


    private void GroupItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && GetAncestor<TextBox>(source) is not null)
        {
            return;
        }

        if (GetDataContext<TaskGroup>(sender) is not { } group)
        {
            return;
        }

        if (group.IsEditing)
        {
            return;
        }

        _draggedGroup = group;
        _groupDragStartPoint = e.GetPosition(GroupListBox);
    }

    private void GroupItem_PreviewMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (_draggedGroup is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var position = e.GetPosition(GroupListBox);
        if (Math.Abs(position.X - _groupDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _groupDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        try
        {
            var data = new System.Windows.DataObject(typeof(TaskGroup), _draggedGroup);
            DragDrop.DoDragDrop(GroupListBox, data, DragDropEffects.Move);
        }
        finally
        {
            _draggedGroup = null;
        }
    }

    private void GroupListBox_DragOver(object sender, WpfDragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(TaskGroup)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void GroupListBox_Drop(object sender, WpfDragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(TaskGroup)))
        {
            return;
        }

        var source = e.Data.GetData(typeof(TaskGroup)) as TaskGroup;
        if (source is null)
        {
            return;
        }

        var ordered = Groups.OrderBy(g => g.SortOrder).ToList();
        ordered.Remove(source);

        var target = GetItemFromPoint<TaskGroup>(e.GetPosition(GroupListBox), GroupListBox);
        var insertIndex = ordered.Count;
        if (target is not null)
        {
            insertIndex = ordered.IndexOf(target);
            if (insertIndex < 0) insertIndex = ordered.Count;
            else
            {
                var container = GroupListBox.ItemContainerGenerator.ContainerFromItem(target) as ListBoxItem;
                if (container is not null)
                {
                    var pos = e.GetPosition(container);
                    if (pos.Y > container.ActualHeight / 2) insertIndex++;
                }
            }
        }

        if (insertIndex > ordered.Count) insertIndex = ordered.Count;
        ordered.Insert(insertIndex, source);
        for (var i = 0; i < ordered.Count; i++) ordered[i].SortOrder = i;

        Groups.Clear();
        foreach (var g in ordered) Groups.Add(g);
        SelectedGroup = source;
        SaveState();
    }

    private void GroupListBox_KeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.F2 && SelectedGroup is not null)
        {
            StartRenameGroup(SelectedGroup);
            e.Handled = true;
        }
    }

    private void Window_PreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox)
        {
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z)
        {
            RestoreLastDeletedTodos();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.N)
        {
            ShowComposer();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.A)
        {
            ActiveTodoListBox.SelectAll();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.C)
        {
            CopySelectedTodos(false);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.X)
        {
            CopySelectedTodos(true);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.V)
        {
            PasteTodosFromClipboard();
            e.Handled = true;
            return;
        }

        if (IsDeleteOverlayVisible && e.Key == Key.Enter)
        {
            ConfirmDeleteOverlayButton_Click(ConfirmDeleteButton, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete)
        {
            if (ActiveTodoListBox.SelectedItems.Count > 0)
            {
                DeleteSelectedTodos();
                e.Handled = true;
                return;
            }

            if (GroupListBox.IsKeyboardFocusWithin)
            {
                ShowDeleteGroupOverlay();
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.Escape)
        {
            foreach (var todo in AllTodos.Where(t => t.IsEditing))
            {
                todo.CancelEdit();
            }
            if (SelectedGroup is not null && SelectedGroup.IsEditing)
            {
                CancelRenameGroup(SelectedGroup);
            }
            e.Handled = false;
        }

        if (e.Key == Key.F2)
        {
            if (GroupListBox.IsKeyboardFocusWithin && SelectedGroup is not null)
            {
                StartRenameGroup(SelectedGroup);
                e.Handled = true;
            }
            else if (ActiveTodoListBox.SelectedItem is TodoItem todo)
            {
                StartInlineEdit(todo);
                e.Handled = true;
            }
        }
    }

    private void CopySelectedTodos(bool cutAfterCopy)
    {
        var selected = ActiveTodoListBox.SelectedItems.Cast<TodoItem>().ToList();
        if (selected.Count == 0)
        {
            return;
        }

        System.Windows.Clipboard.SetText(string.Join(Environment.NewLine, selected.Select(t => t.Text)));

        if (cutAfterCopy)
        {
            foreach (var todo in selected)
            {
                AllTodos.Remove(todo);
            }

            RefreshVisibleTodos();
            SaveState();
        }
    }

    private void PasteTodosFromClipboard()
    {
        if (SelectedGroup is null || !System.Windows.Clipboard.ContainsText())
        {
            return;
        }

        var lines = System.Windows.Clipboard.GetText()
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        if (lines.Count == 0)
        {
            return;
        }

        var nextSort = AllTodos.Where(t => t.GroupId == SelectedGroup.Id).Select(t => t.SortOrder).DefaultIfEmpty(-1).Max() + 1;
        foreach (var line in lines)
        {
            AllTodos.Add(new TodoItem
            {
                GroupId = SelectedGroup.Id,
                Text = line,
                SortOrder = nextSort++,
                Priority = TodoPriority.Normal,
                CreatedAt = DateTime.Now
            });
        }

        RefreshVisibleTodos();
        SaveState();
    }

    private void ShowFromTrayButton_Click(object sender, RoutedEventArgs e) => ShowFromTray();

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        _pinService.Reapply();
    }

    private void CloseFromTray()
    {
        Dispatcher.Invoke(Close);
    }

    private void TogglePinnedFromTray()
    {
        Dispatcher.Invoke(() => IsPinnedToDesktop = !IsPinnedToDesktop);
    }

    private void ExportBackupButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "ToDoDo Backup (*.json)|*.json",
            FileName = $"ToDoDo-backup-{DateTime.Now:yyyyMMdd-HHmm}.json"
        };

        if (dialog.ShowDialog(this) == true)
        {
            var json = JsonSerializer.Serialize(_state, new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() }
            });

            File.WriteAllText(dialog.FileName, json);
        }
    }

    private void ImportBackupButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "ToDoDo Backup (*.json)|*.json"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var json = File.ReadAllText(dialog.FileName);
        var imported = JsonSerializer.Deserialize<AppState>(json, new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter() }
        });

        if (imported is null)
        {
            return;
        }

        Groups.Clear();
        AllTodos.Clear();

        foreach (var group in imported.Groups.OrderBy(g => g.SortOrder))
        {
            Groups.Add(group);
        }

        foreach (var todo in imported.Todos.OrderBy(t => t.SortOrder))
        {
            AllTodos.Add(todo);
        }

        IsSidebarVisible = imported.Settings.IsSidebarVisible;
        IsPinnedToDesktop = imported.Settings.IsPinnedToDesktop;
        SelectedGroup = Groups.FirstOrDefault();
        UpdateSidebarLayout();
        UpdateMinimumWindowWidth();
        RefreshVisibleTodos();
        SaveState();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        SaveState();
    }

    private void RefreshVisibleTodos()
    {
        VisibleTodos.Clear();

        if (SelectedGroup is null)
        {
            SummaryText = "전체 0 · 진행 0 · 완료 0";
            IsEmptyVisible = true;
            return;
        }

        var groupTodos = AllTodos.Where(t => t.GroupId == SelectedGroup.Id).ToList();
        IEnumerable<TodoItem> ordered = _currentSortMode switch
        {
            SortMode.Created => groupTodos.OrderBy(t => t.IsDone).ThenBy(t => t.CreatedAt).ThenBy(t => t.SortOrder),
            SortMode.Name => groupTodos.OrderBy(t => t.IsDone).ThenBy(t => t.Text, StringComparer.CurrentCultureIgnoreCase).ThenBy(t => t.CreatedAt),
            SortMode.Priority => groupTodos.OrderBy(t => t.IsDone).ThenBy(t => PriorityRank(t.Priority)).ThenBy(t => t.DueDate ?? DateTime.MaxValue).ThenBy(t => t.CreatedAt),
            SortMode.Manual => groupTodos.OrderBy(t => t.IsDone).ThenBy(t => t.SortOrder),
            _ => groupTodos.OrderBy(t => t.IsDone).ThenBy(t => t.DueDate ?? DateTime.MaxValue).ThenBy(t => t.CreatedAt)
        };

        foreach (var todo in ordered.Where(MatchesCurrentFilter))
        {
            VisibleTodos.Add(todo);
        }

        var total = groupTodos.Count;
        var completed = groupTodos.Count(t => t.IsDone);
        var remaining = total - completed;
        SummaryText = $"전체 {total} · 진행 {remaining} · 완료 {completed}";
        IsEmptyVisible = false;
    }

    private bool MatchesCurrentFilter(TodoItem todo)
    {
        var today = DateTime.Today;
        var endOfWeek = today.AddDays(6 - (int)today.DayOfWeek);

        return _currentFilter switch
        {
            FilterMode.Today => todo.DueDate.HasValue && todo.DueDate.Value.Date == today,
            FilterMode.ThisWeek => todo.DueDate.HasValue && todo.DueDate.Value.Date > today && todo.DueDate.Value.Date <= endOfWeek,
            FilterMode.NoDueDate => !todo.DueDate.HasValue,
            _ => true
        };
    }

    private void SaveState()
    {
        if (!_isInitialized)
        {
            return;
        }

        PersistGroupUiState(SelectedGroup);
        _state.Groups = Groups.OrderBy(g => g.SortOrder).ToList();
        _state.Todos = AllTodos.OrderBy(t => t.GroupId).ThenBy(t => t.SortOrder).ToList();
        _state.Settings.Width = SafeFinite(Width, 900);
        _state.Settings.Height = SafeFinite(Height, 760);
        _state.Settings.Left = SafeFinite(Left, 70);
        _state.Settings.Top = SafeFinite(Top, 50);
        _state.Settings.IsSidebarVisible = IsSidebarVisible;
        _state.Settings.IsPinnedToDesktop = IsPinnedToDesktop;
        _state.Settings.SidebarWidth = Math.Clamp(SidebarColumn.ActualWidth > 0 ? SidebarColumn.ActualWidth : _state.Settings.SidebarWidth, 180, 360);
        StorageService.Save(_state);
    }

    private static void FocusRenameTextBox(WpfTextBox textBox)
    {
        textBox.Focus();
        textBox.SelectAll();
    }

    private static string PriorityToText(TodoPriority priority)
        => priority switch
        {
            TodoPriority.Low => "낮음",
            TodoPriority.High => "높음",
            _ => "보통"
        };

    private static string RepeatToText(TodoRepeat repeat)
        => repeat switch
        {
            TodoRepeat.Daily => "매일",
            TodoRepeat.Weekly => "매주",
            TodoRepeat.Monthly => "매월",
            _ => "반복 없음"
        };

    private static int PriorityRank(TodoPriority priority)
        => priority switch
        {
            TodoPriority.High => 0,
            TodoPriority.Normal => 1,
            _ => 2
        };

    private static string FilterModeToText(FilterMode filterMode)
        => filterMode switch
        {
            FilterMode.Today => "오늘",
            FilterMode.ThisWeek => "이번 주",
            FilterMode.NoDueDate => "기한 없음",
            _ => "전체"
        };

    private static string SortModeToText(SortMode sortMode)
        => sortMode switch
        {
            SortMode.Created => "추가순",
            SortMode.Name => "이름순",
            SortMode.Priority => "우선순위순",
            SortMode.Manual => "직접정렬",
            _ => "날짜순"
        };

    private static double SafeFinite(double value, double fallback)
        => double.IsFinite(value) ? value : fallback;

    private static T? GetDataContext<T>(object sender) where T : class
        => sender is FrameworkElement element ? element.DataContext as T : null;

    private static T? GetDataContextFromMenu<T>(object sender) where T : class
    {
        if (sender is MenuItem item && item.Parent is ContextMenu menu && menu.PlacementTarget is FrameworkElement element)
        {
            return element.DataContext as T;
        }

        return null;
    }

    private static TAncestor? GetAncestor<TAncestor>(DependencyObject? source) where TAncestor : DependencyObject
    {
        while (source is not null)
        {
            if (source is TAncestor match)
            {
                return match;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private T? GetItemFromPoint<T>(Point point, ItemsControl itemsControl) where T : class
    {
        var hit = itemsControl.InputHitTest(point) as DependencyObject;
        return GetItemFromElement<T>(hit);
    }

    private static T? GetItemFromElement<T>(DependencyObject? source) where T : class
    {
        while (source is not null)
        {
            if (source is FrameworkElement element && element.DataContext is T item)
            {
                return item;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private T? GetItemFromPoint<T>(Point point) where T : class
    {
        var hit = ActiveTodoListBox.InputHitTest(point) as DependencyObject;
        return GetItemFromElement<T>(hit);
    }

    private static bool IsVisualDescendantOf(DependencyObject? source, DependencyObject? ancestor)
    {
        while (source is not null)
        {
            if (ReferenceEquals(source, ancestor))
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private static bool HasInteractiveAncestor(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Button || source is TextBox || source is ToggleButton || source is Calendar)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void OnPropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_NCHITTEST = 0x0084;
        const int HTLEFT = 10;
        const int HTRIGHT = 11;
        const int HTTOP = 12;
        const int HTTOPLEFT = 13;
        const int HTTOPRIGHT = 14;
        const int HTBOTTOM = 15;
        const int HTBOTTOMLEFT = 16;
        const int HTBOTTOMRIGHT = 17;

        if (msg == WM_NCHITTEST)
        {
            handled = true;
            return (IntPtr)HitTestNca(lParam, HTLEFT, HTRIGHT, HTTOP, HTTOPLEFT, HTTOPRIGHT, HTBOTTOM, HTBOTTOMLEFT, HTBOTTOMRIGHT);
        }

        return IntPtr.Zero;
    }

    private int HitTestNca(IntPtr lParam, int htLeft, int htRight, int htTop, int htTopLeft, int htTopRight, int htBottom, int htBottomLeft, int htBottomRight)
    {
        if (WindowState == WindowState.Maximized)
        {
            return 1;
        }

        var resizeBorder = 8;
        var raw = lParam.ToInt64();
        var screenPoint = new WpfPoint((short)(raw & 0xFFFF), (short)((raw >> 16) & 0xFFFF));
        var windowPoint = PointFromScreen(screenPoint);

        var onLeft = windowPoint.X >= 0 && windowPoint.X < resizeBorder;
        var onRight = windowPoint.X <= ActualWidth && windowPoint.X > ActualWidth - resizeBorder;
        var onTop = windowPoint.Y >= 0 && windowPoint.Y < resizeBorder;
        var onBottom = windowPoint.Y <= ActualHeight && windowPoint.Y > ActualHeight - resizeBorder;

        if (onLeft && onTop) return htTopLeft;
        if (onLeft && onBottom) return htBottomLeft;
        if (onRight && onTop) return htTopRight;
        if (onRight && onBottom) return htBottomRight;
        if (onLeft) return htLeft;
        if (onRight) return htRight;
        if (onTop) return htTop;
        if (onBottom) return htBottom;
        return 1;
    }

    private enum FilterMode
    {
        All,
        Today,
        ThisWeek,
        NoDueDate
    }

    private enum SortMode
    {
        DueDate,
        Created,
        Name,
        Priority,
        Manual
    }
}