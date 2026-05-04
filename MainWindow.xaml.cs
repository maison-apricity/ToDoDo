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
using System.Windows.Threading;
using System.Windows.Data;
using ToDoDo.Models;
using ToDoDo.Services;
using WpfButton = System.Windows.Controls.Button;
using WpfDragEventArgs = System.Windows.DragEventArgs;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfPoint = System.Windows.Point;
using WpfSize = System.Windows.Size;
using WpfGiveFeedbackEventArgs = System.Windows.GiveFeedbackEventArgs;
using WpfQueryContinueDragEventArgs = System.Windows.QueryContinueDragEventArgs;

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
    private bool _isAllGroupsView;
    private bool _areCompletedDetailsCollapsed = true;
    private bool _didApplyStartupHide;
    private bool _allowRealClose;
    private readonly bool _launchToTrayRequested = Environment.GetCommandLineArgs().Any(arg => string.Equals(arg, "--tray", StringComparison.OrdinalIgnoreCase));
    private bool _draftUseDueDate;
    private string _draftTitle = string.Empty;
    private DateTime? _draftDueDate = DateTime.Today;
    private TodoPriority _draftPriority = TodoPriority.Normal;
    private TodoRepeat _draftRepeat = TodoRepeat.None;
    private string _summaryText = "전체 0 · 진행 0 · 완료 0";
    private string _deleteOverlayMessage = string.Empty;
    private bool _isEmptyVisible = true;
    private bool _isInitialized;
    private ImageSource? _headerIconSource;
    private SettingsWindow? _settingsWindow;
    private ArchiveWindow? _archiveWindow;

    private WpfPoint _dragStartPoint;
    private TodoItem? _draggedTodoItem;
    private TaskGroup? _draggedGroup;
    private WpfPoint _groupDragStartPoint;
    private TodoItem? _editingTodo;
    private List<DeletedTodoInfo> _lastDeletedTodos = new();
    private double _lastVisibleSidebarWidth = 220;
    private TodoItem? _inlineDueDateTodo;
    private bool _isSynchronizingDueDateCalendar;
    private const double HiddenMainMinWidth = 620;
    private readonly DispatcherTimer _dragPreviewTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly DispatcherTimer _windowStateSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private bool _isDragPreviewVisible;
    private ListBoxItem? _lastInsertionTargetContainer;
    private bool _lastInsertionAfter;
    private ListBoxItem? _lastGroupInsertionTargetContainer;
    private bool _lastGroupInsertionAfter;
    private bool _isRubberBandCandidate;
    private bool _isRubberBandSelecting;
    private WpfPoint _rubberBandStartPoint;
    private bool _rubberBandStartedOnBlank;
    private HashSet<TodoItem> _rubberBandCurrentSelection = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<TaskGroup> Groups { get; } = new();
    public ObservableCollection<TodoItem> AllTodos { get; } = new();
    public ImageSource? HeaderIconSource
    {
        get => _headerIconSource;
        private set
        {
            if (_headerIconSource == value)
            {
                return;
            }

            _headerIconSource = value;
            OnPropertyChanged(nameof(HeaderIconSource));
        }
    }
    public ObservableCollection<TodoItem> VisibleTodos { get; } = new();
    public ObservableCollection<TodoItem> CompletedVisibleTodos { get; } = new();
    public ObservableCollection<TodoItem> ArchivedVisibleTodos { get; } = new();
    public ICollectionView VisibleTodosView { get; }

    public bool HasCompletedTodos => CompletedVisibleTodos.Count > 0;
    public string CompletedCountText => CompletedVisibleTodos.Count.ToString();
    public bool HasArchivedTodos => AllTodos.Any(t => t.IsArchived);
    public string ArchivedCountText => AllTodos.Count(t => t.IsArchived).ToString();
    public string ArchivedSummaryText => $"보관된 항목 {ArchivedCountText}개";
    public string ArchiveLauncherButtonText => HasArchivedTodos ? $"보관함 ({ArchivedCountText})" : "보관함";
    public bool ShowCompletedStrikethroughSetting
    {
        get => _state.Settings.ShowCompletedStrikethrough;
        set
        {
            if (_state.Settings.ShowCompletedStrikethrough == value)
            {
                return;
            }

            _state.Settings.ShowCompletedStrikethrough = value;
            OnPropertyChanged(nameof(ShowCompletedStrikethroughSetting));
            OnPropertyChanged(nameof(CompletedStrikeThroughSettingText));
            RefreshVisibleTodos();
            SaveState();
        }
    }

    public string AutoArchiveSettingText => $"완료 자동 보관: {AutoArchiveDaysToText(_state.Settings.AutoArchiveDays)}";
    public string CloseBehaviorSettingText => _state.Settings.HideToTrayOnClose ? "닫기 버튼: 트레이로 숨김" : "닫기 버튼: 즉시 종료";
    public string StartMinimizedSettingText => _state.Settings.StartMinimizedToTray ? "시작 시 트레이 최소화: 켜짐" : "시작 시 트레이 최소화: 꺼짐";
    public string DefaultPrioritySettingText => $"기본 우선순위: {PriorityToText(_state.Settings.DefaultPriority)}";
    public string DefaultRepeatSettingText => $"기본 반복: {RepeatToText(_state.Settings.DefaultRepeat)}";
    public string DefaultDueDateSettingText => _state.Settings.DefaultUseDueDate ? "새 할 일 기본 기한: 사용" : "새 할 일 기본 기한: 사용 안 함";
    public string CompletedStrikeThroughSettingText => ShowCompletedStrikethroughSetting ? "완료 취소선 표시: 켜짐" : "완료 취소선 표시: 꺼짐";


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

            if (value is not null && _isAllGroupsView)
            {
                _isAllGroupsView = false;
                OnPropertyChanged(nameof(IsAllGroupsView));
                UpdateAllGroupsViewVisuals();
            }

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

    public bool IsAllGroupsView
    {
        get => _isAllGroupsView;
        private set
        {
            if (_isAllGroupsView == value)
            {
                return;
            }

            _isAllGroupsView = value;
            OnPropertyChanged(nameof(IsAllGroupsView));
            OnPropertyChanged(nameof(MainTitleText));
            UpdateAllGroupsViewVisuals();
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

    public string MainTitleText => IsAllGroupsView ? "ToDo List - 모든 목록" : SelectedGroup is null ? "ToDo List" : $"ToDo List - {SelectedGroup.Name}";

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        VisibleTodosView = CollectionViewSource.GetDefaultView(VisibleTodos);
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
        EnsureWindowVisibleOnScreen();

        _pinService = new DesktopPinService(this);
        _trayIconService = new TrayIconService(ShowFromTray, ShowComposerFromTray, TogglePinnedFromTray, CloseFromTray, () => IsPinnedToDesktop, AssetResolverService.ResolveTrayIcon());
        _dragPreviewTimer.Tick += DragPreviewTimer_Tick;
        _windowStateSaveTimer.Tick += WindowStateSaveTimer_Tick;

        ApplyAutoArchivePolicy();

        Loaded += MainWindow_Loaded;
        LocationChanged += (_, _) => QueueWindowStateSave();
        SizeChanged += (_, _) =>
        {
            QueueWindowStateSave();
        };

        SelectedGroup = Groups.FirstOrDefault();
        UpdateSidebarLayout();
        UpdateFilterButtonLabel();
        UpdateActionButtonLabels();
        RefreshVisibleTodos();

        _isInitialized = true;
    }

    private void ApplyResolvedWindowIcon()
    {
        var headerSource = AssetResolverService.ResolveHeaderImage();
        if (headerSource is not null)
        {
            HeaderIconSource = headerSource;
        }

        var iconSource = AssetResolverService.ResolveWindowIcon();
        if (iconSource is not null)
        {
            Icon = iconSource;
            HeaderIconSource ??= iconSource;
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

    private void WindowStateSaveTimer_Tick(object? sender, EventArgs e)
    {
        _windowStateSaveTimer.Stop();
        SaveState();
    }

    private void QueueWindowStateSave()
    {
        if (!_isInitialized)
        {
            return;
        }

        _windowStateSaveTimer.Stop();
        _windowStateSaveTimer.Start();
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdatePinState();
        UpdateAllGroupsViewVisuals();
        UpdateToolbarLayout();
        UpdateMinimumWindowWidth();

        EnsureWindowVisibleOnScreen();

        if (_state.Settings.StartMinimizedToTray && _launchToTrayRequested && !_didApplyStartupHide)
        {
            _didApplyStartupHide = true;
            Dispatcher.BeginInvoke(HideToTray, DispatcherPriority.Background);
        }
    }

    private void EnsureWindowVisibleOnScreen()
    {
        var primaryArea = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea ?? new System.Drawing.Rectangle(0, 0, 1600, 900);
        var width = Math.Min(Math.Max(Width, MinWidth), Math.Max(640, primaryArea.Width - 40));
        var height = Math.Min(Math.Max(Height, MinHeight), Math.Max(520, primaryArea.Height - 40));
        Width = width;
        Height = height;

        var windowRect = new System.Drawing.Rectangle((int)Math.Round(Left), (int)Math.Round(Top), (int)Math.Round(width), (int)Math.Round(height));
        var isVisibleOnAnyScreen = System.Windows.Forms.Screen.AllScreens
            .Select(screen => screen.WorkingArea)
            .Any(area => windowRect.Right > area.Left + 80 &&
                         windowRect.Left < area.Right - 80 &&
                         windowRect.Bottom > area.Top + 80 &&
                         windowRect.Top < area.Bottom - 80);

        if (isVisibleOnAnyScreen)
        {
            return;
        }

        Left = primaryArea.Left + Math.Max(20, (primaryArea.Width - width) / 2.0);
        Top = primaryArea.Top + Math.Max(20, (primaryArea.Height - height) / 4.0);
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
        OpenSettingsWindow();
    }

    public void OpenSettingsWindow()
    {
        if (_settingsWindow is not null)
        {
            if (!_settingsWindow.IsVisible)
            {
                _settingsWindow.Show();
            }

            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(this)
        {
            Owner = this
        };
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    public void OpenArchiveWindow()
    {
        if (_archiveWindow is not null)
        {
            if (!_archiveWindow.IsVisible)
            {
                _archiveWindow.Show();
            }

            _archiveWindow.Activate();
            return;
        }

        _archiveWindow = new ArchiveWindow(this)
        {
            Owner = this
        };
        _archiveWindow.Closed += (_, _) => _archiveWindow = null;
        _archiveWindow.Show();
        _archiveWindow.Activate();
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
    }

    public void TogglePinState()
    {
        IsPinnedToDesktop = !IsPinnedToDesktop;
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        TogglePinState();
    }


    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_state.Settings.HideToTrayOnClose)
        {
            HideToTray();
            return;
        }

        _allowRealClose = true;
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

        if (HeaderPinIconVertical is not null)
        {
            HeaderPinIconVertical.Visibility = IsPinnedToDesktop ? Visibility.Collapsed : Visibility.Visible;
        }

        if (HeaderPinIconTilted is not null)
        {
            HeaderPinIconTilted.Visibility = IsPinnedToDesktop ? Visibility.Visible : Visibility.Collapsed;
            HeaderPinIconTilted.Opacity = IsPinnedToDesktop ? 1.0 : 0.82;
        }

        if (SettingsPinButton is not null)
        {
            SettingsPinButton.BorderBrush = IsPinnedToDesktop ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B7C3FF")!) : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#42526C")!);
            SettingsPinButton.BorderThickness = IsPinnedToDesktop ? new Thickness(2.4) : new Thickness(1);
            SettingsPinButton.Background = IsPinnedToDesktop ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3A568F")!) : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#263246")!);
            SettingsPinButton.Content = IsPinnedToDesktop ? "바탕화면 고정 중" : "바탕화면 고정";
        }
    }

    private void UpdateAllGroupsViewVisuals()
    {
        if (AllGroupsViewButton is null)
        {
            return;
        }

        AllGroupsViewButton.Background = IsAllGroupsView ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3A568F")!) : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#263246")!);
        AllGroupsViewButton.BorderBrush = IsAllGroupsView ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B7C3FF")!) : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#42526C")!);
        AllGroupsViewButton.BorderThickness = IsAllGroupsView ? new Thickness(2.2) : new Thickness(1);
        AllGroupsViewButton.Content = IsAllGroupsView ? "모든 목록 보기 중" : "모든 목록";
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
            widths[i] = Math.Max(btn.DesiredSize.Width, 78);
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
            toolbarRequired += Math.Max(btn.DesiredSize.Width, 78);
        }
        toolbarRequired += 24;

        var quickAddRequired = 0d;
        if (QuickAddLauncherButton is not null)
        {
            QuickAddLauncherButton.Measure(new WpfSize(double.PositiveInfinity, double.PositiveInfinity));
            quickAddRequired = QuickAddLauncherButton.DesiredSize.Width;
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

    private void OpenArchiveWindowButton_Click(object sender, RoutedEventArgs e)
    {
        OpenArchiveWindow();
    }

    private void AllGroupsViewButton_Click(object sender, RoutedEventArgs e)
    {
        ShowAllGroupsView();
    }

    private void ShowAllGroupsView()
    {
        PersistGroupUiState(_selectedGroup);
        _selectedGroup = null;

        if (GroupListBox is not null)
        {
            GroupListBox.SelectedItem = null;
        }

        IsAllGroupsView = true;
        if (_currentSortMode == SortMode.Manual)
        {
            _currentSortMode = SortMode.DueDate;
        }

        OnPropertyChanged(nameof(SelectedGroup));
        OnPropertyChanged(nameof(MainTitleText));
        UpdateFilterButtonLabel();
        UpdateActionButtonLabels();
        RefreshVisibleTodos();
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

        DeleteOverlayMessage = $"\"{SelectedGroup.Name}\" 그룹을 삭제할까요? 진행 중/완료 항목은 삭제되고, 이미 보관된 항목은 보관함에 유지됩니다.";
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
        foreach (var archived in AllTodos.Where(t => t.GroupId == groupToDelete.Id && t.IsArchived))
        {
            archived.ArchivedGroupName = string.IsNullOrWhiteSpace(archived.ArchivedGroupName) ? groupToDelete.Name : archived.ArchivedGroupName;
        }

        foreach (var todo in AllTodos.Where(t => t.GroupId == groupToDelete.Id && !t.IsArchived).ToList())
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

    private void ShowComposer()
    {
        if (SelectedGroup is null)
        {
            SelectedGroup = Groups.OrderBy(g => g.SortOrder).FirstOrDefault();
        }

        if (SelectedGroup is null)
        {
            return;
        }

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
        DraftUseDueDate = _state.Settings.DefaultUseDueDate;
        DraftDueDate = DateTime.Today;
        _draftPriority = _state.Settings.DefaultPriority;
        _draftRepeat = _state.Settings.DefaultRepeat;
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
        OpenDueDatePopup(sender as UIElement, DraftDueDate ?? DateTime.Today);
    }

    private void OpenDueDatePopup(UIElement? placementTarget, DateTime selectedDate)
    {
        DueDatePopup.PlacementTarget = placementTarget;

        try
        {
            _isSynchronizingDueDateCalendar = true;
            DueDateCalendar.DisplayDate = selectedDate.Date;
            DueDateCalendar.SelectedDate = selectedDate.Date;
        }
        finally
        {
            _isSynchronizingDueDateCalendar = false;
        }

        DueDatePopup.IsOpen = true;
    }

    private void DueDateCalendar_SelectedDatesChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSynchronizingDueDateCalendar)
        {
            return;
        }

        if (DueDateCalendar.SelectedDate.HasValue)
        {
            if (_inlineDueDateTodo is not null)
            {
                _inlineDueDateTodo.EditUseDueDate = true;
                _inlineDueDateTodo.EditDueDate = DueDateCalendar.SelectedDate.Value.Date;
            }
            else
            {
                DraftDueDate = DueDateCalendar.SelectedDate.Value.Date;
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
            SortMode.Priority when !IsAllGroupsView => SortMode.Manual,
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
        var selected = ActiveTodoListBox.SelectedItems.Cast<TodoItem>()
            .Concat(CompletedTodoListBox.SelectedItems.Cast<TodoItem>())
            .Distinct()
            .ToList();
        if (selected.Count == 0)
        {
            return;
        }

        RememberDeletedTodos(selected);

        foreach (var todo in selected)
        {
            AllTodos.Remove(todo);
        }

        var affectedGroups = selected.Select(t => t.GroupId).Distinct().ToList();
        foreach (var groupId in affectedGroups)
        {
            ApplyGroupPartitionOrder(groupId);
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
            ApplyGroupPartitionOrder(group.Id);
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
        if (GetTodoFromMenuSender(sender) is { } todo)
        {
            StartInlineEdit(todo);
        }
    }

    private void DuplicateTodoMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetTodoFromMenuSender(sender) is not { } todo)
        {
            return;
        }

        var clone = new TodoItem
        {
            GroupId = todo.GroupId,
            Text = $"{todo.Text} - 복제됨",
            Priority = todo.Priority,
            Repeat = todo.Repeat,
            DueDate = todo.DueDate,
            SortOrder = 0,
            CreatedAt = DateTime.Now,
            IsExpanded = true,
            IsDone = false,
            CompletedAt = null,
            IsArchived = false
        };

        EnsureManualSortForGroup(todo.GroupId);
        var active = GetOrderedGroupPartition(todo.GroupId, false);
        active.Add(clone);
        var completed = GetOrderedGroupPartition(todo.GroupId, true);
        AllTodos.Add(clone);
        ApplyGroupPartitionOrder(todo.GroupId, active, completed);
        RefreshVisibleTodos();
        SaveState();
        StartInlineEdit(clone);
    }

    private void MoveTodoToTopMenuItem_Click(object sender, RoutedEventArgs e)
    {
        MoveTodoWithinPartition(GetTodoFromMenuSender(sender), true);
    }

    private void MoveTodoToBottomMenuItem_Click(object sender, RoutedEventArgs e)
    {
        MoveTodoWithinPartition(GetTodoFromMenuSender(sender), false);
    }

    private void MoveTodoWithinPartition(TodoItem? todo, bool toTop)
    {
        if (todo is null)
        {
            return;
        }

        EnsureManualSortForGroup(todo.GroupId);
        var partition = GetOrderedGroupPartition(todo.GroupId, todo.IsDone);
        if (!partition.Remove(todo))
        {
            return;
        }

        partition.Insert(toTop ? 0 : partition.Count, todo);
        if (todo.IsDone)
        {
            ApplyGroupPartitionOrder(todo.GroupId, completedOrder: partition);
        }
        else
        {
            ApplyGroupPartitionOrder(todo.GroupId, activeOrder: partition);
        }

        RefreshVisibleTodos();
        SaveState();
    }

    private void OpenMoveTodoToGroupDialogMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetTodoFromMenuSender(sender) is not { } todo)
        {
            return;
        }

        var dialog = new MoveTodoToGroupWindow(Groups.OrderBy(g => g.SortOrder).ToList(), todo.GroupId)
        {
            Owner = this,
            Icon = Icon,
            FontFamily = FontFamily
        };

        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.SelectedGroupId))
        {
            return;
        }

        MoveTodoToGroup(todo, dialog.SelectedGroupId);
    }

    public void MoveTodoToGroup(TodoItem todo, string groupId)
    {
        if (todo.GroupId == groupId || !Groups.Any(g => g.Id == groupId))
        {
            return;
        }

        var previousGroupId = todo.GroupId;
        todo.GroupId = groupId;
        todo.SortOrder = AllTodos.Where(t => t.GroupId == groupId && !t.IsArchived)
            .Select(t => t.SortOrder)
            .DefaultIfEmpty(-1)
            .Max() + 1;
        ApplyGroupPartitionOrder(previousGroupId);
        ApplyGroupPartitionOrder(groupId);
        RefreshVisibleTodos();
        SaveState();
    }

    private void SetTodoDueDateQuickMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || GetTodoFromMenuSender(item) is not { } todo)
        {
            return;
        }

        var today = DateTime.Today;
        todo.DueDate = (item.Tag as string) switch
        {
            "today" => today,
            "tomorrow" => today.AddDays(1),
            "thisweek" => today.AddDays(6 - (int)today.DayOfWeek),
            _ => null
        };

        RefreshVisibleTodos();
        SaveState();
    }

    private void SetTodoRepeatQuickMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || GetTodoFromMenuSender(item) is not { } todo)
        {
            return;
        }

        todo.Repeat = (item.Tag as string) switch
        {
            "Daily" => TodoRepeat.Daily,
            "Weekly" => TodoRepeat.Weekly,
            "Monthly" => TodoRepeat.Monthly,
            _ => TodoRepeat.None
        };

        RefreshVisibleTodos();
        SaveState();
    }

    private void MarkTodoDoneMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetTodoFromMenuSender(sender) is not { } todo)
        {
            return;
        }

        SetTodoCompletionState(todo, true, createRepeatClone: true);
        RefreshVisibleTodos();
        SaveState();
    }

    private void RestoreTodoMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetTodoFromMenuSender(sender) is not { } todo)
        {
            return;
        }

        SetTodoCompletionState(todo, false, createRepeatClone: false);
        RefreshVisibleTodos();
        SaveState();
    }

    private void ArchiveTodoMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetTodoFromMenuSender(sender) is not { } todo)
        {
            return;
        }

        ArchiveTodos(new[] { todo });
    }

    private void DeleteTodoMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetTodoFromMenuSender(sender) is { } todo)
        {
            DeleteTodo(todo);
        }
    }

    private void DeleteTodoButton_Click(object sender, RoutedEventArgs e)
    {
        if (GetDataContext<TodoItem>(sender) is { } todo)
        {
            DeleteTodo(todo);
        }
    }

    private void DeleteTodo(TodoItem todo)
    {
        RememberDeletedTodos(new[] { todo });
        var groupId = todo.GroupId;
        AllTodos.Remove(todo);
        ApplyGroupPartitionOrder(groupId);
        RefreshVisibleTodos();
        SaveState();
    }

    private void StartInlineEdit(TodoItem todo)
    {
        foreach (var item in AllTodos)
        {
            item.CancelEdit();
        }

        todo.BeginEdit();
        todo.IsExpanded = true;

        var ownerListBox = GetTodoListBoxForItem(todo);
        if (ownerListBox is not null)
        {
            ownerListBox.SelectedItem = todo;
            ownerListBox.ScrollIntoView(todo);
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
        OpenDueDatePopup(sender as UIElement, todo.EditDueDate ?? DateTime.Today);
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
            SetTodoCompletionState(todo, true, createRepeatClone: true);
        }
        else
        {
            SetTodoCompletionState(todo, false, createRepeatClone: false);
        }

        RefreshVisibleTodos();
        SaveState();
    }

    private void SetTodoCompletionState(TodoItem todo, bool isDone, bool createRepeatClone)
    {
        if (todo.IsDone == isDone && !todo.IsArchived)
        {
            return;
        }

        if (isDone)
        {
            todo.IsDone = true;
            todo.IsArchived = false;
            todo.IsExpanded = false;
            todo.CompletedAt = DateTime.Now;
            if (createRepeatClone)
            {
                HandleRepeatOnCompletion(todo);
            }
        }
        else
        {
            todo.IsDone = false;
            todo.IsArchived = false;
            todo.IsExpanded = true;
            todo.CompletedAt = null;
        }

        ApplyGroupPartitionOrder(todo.GroupId);
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
        ApplyGroupPartitionOrder(sourceTodo.GroupId);
    }


    private void SelectionHostGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_draggedTodoItem is not null)
        {
            return;
        }

        var startPoint = e.GetPosition(SelectionHostGrid);
        var source = ResolveRubberBandSource(startPoint, e.OriginalSource as DependencyObject);
        if (!CanStartRubberBandSelection(source))
        {
            return;
        }

        _isRubberBandCandidate = true;
        _isRubberBandSelecting = false;
        _rubberBandStartPoint = startPoint;
        _rubberBandStartedOnBlank = FindTodoContainerAtPoint(startPoint) is null;
        _rubberBandCurrentSelection.Clear();
    }

    private void SelectionHostGrid_PreviewMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (!_isRubberBandCandidate || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var currentPoint = e.GetPosition(SelectionHostGrid);
        if (!_isRubberBandSelecting)
        {
            if (Math.Abs(currentPoint.X - _rubberBandStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(currentPoint.Y - _rubberBandStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            _isRubberBandSelecting = true;
            foreach (var selected in ActiveTodoListBox.SelectedItems.Cast<TodoItem>().ToList())
            {
                if (ActiveTodoListBox.ItemContainerGenerator.ContainerFromItem(selected) is ListBoxItem selectedContainer)
                {
                    selectedContainer.IsSelected = false;
                }
            }
            _rubberBandCurrentSelection.Clear();
            Mouse.Capture(SelectionHostGrid, CaptureMode.Element);
        }

        UpdateRubberBandVisual(currentPoint);
        UpdateRubberBandSelection(currentPoint);
        e.Handled = true;
    }

    private void SelectionHostGrid_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isRubberBandSelecting)
        {
            EndRubberBandSelection();
            e.Handled = true;
            return;
        }

        if (_isRubberBandCandidate && _rubberBandStartedOnBlank)
        {
            ActiveTodoListBox.SelectedItems.Clear();
        }

        _isRubberBandCandidate = false;
        _rubberBandStartedOnBlank = false;
    }

    private DependencyObject? ResolveRubberBandSource(WpfPoint point, DependencyObject? originalSource)
    {
        var hit = SelectionHostGrid.InputHitTest(point) as DependencyObject;
        return hit ?? originalSource;
    }

    private ListBoxItem? FindTodoContainerAtPoint(WpfPoint point)
    {
        var hit = SelectionHostGrid.InputHitTest(point) as DependencyObject;
        while (hit is not null)
        {
            if (hit is ListBoxItem item)
            {
                return item;
            }
            hit = VisualTreeHelper.GetParent(hit);
        }

        foreach (var todo in VisibleTodos)
        {
            if (ActiveTodoListBox.ItemContainerGenerator.ContainerFromItem(todo) is not ListBoxItem container)
            {
                continue;
            }
            try
            {
                var topLeft = container.TransformToAncestor(SelectionHostGrid).Transform(new WpfPoint(0,0));
                var rect = new Rect(topLeft, new WpfSize(container.ActualWidth, container.ActualHeight));
                if (rect.Contains(point)) return container;
            }
            catch { }
        }
        return null;
    }

    private bool CanStartRubberBandSelection(DependencyObject? source)
    {
        if (source is null)
        {
            return false;
        }

        var current = source;
        while (current is not null)
        {
            if (current is FrameworkElement fe && fe.Tag is string tag && tag == "DragHandle")
            {
                return false;
            }

            if (current is Button || current is TextBox || current is ToggleButton || current is Calendar || current is System.Windows.Controls.Primitives.ScrollBar || current is System.Windows.Controls.Primitives.Thumb)
            {
                return false;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return true;
    }

    private void UpdateRubberBandVisual(WpfPoint currentPoint)
    {
        var left = Math.Min(_rubberBandStartPoint.X, currentPoint.X);
        var top = Math.Min(_rubberBandStartPoint.Y, currentPoint.Y);
        var width = Math.Abs(currentPoint.X - _rubberBandStartPoint.X);
        var height = Math.Abs(currentPoint.Y - _rubberBandStartPoint.Y);

        Canvas.SetLeft(SelectionRubberBand, left);
        Canvas.SetTop(SelectionRubberBand, top);
        SelectionRubberBand.Width = width;
        SelectionRubberBand.Height = height;
        SelectionRubberBand.Visibility = Visibility.Visible;
    }

    private void UpdateRubberBandSelection(WpfPoint currentPoint)
    {
        var left = Math.Min(_rubberBandStartPoint.X, currentPoint.X);
        var top = Math.Min(_rubberBandStartPoint.Y, currentPoint.Y);
        var width = Math.Abs(currentPoint.X - _rubberBandStartPoint.X);
        var height = Math.Abs(currentPoint.Y - _rubberBandStartPoint.Y);
        var selectionRect = new Rect(left, top, width, height);

        var nextSelection = new HashSet<TodoItem>();

        foreach (var todo in VisibleTodos)
        {
            if (ActiveTodoListBox.ItemContainerGenerator.ContainerFromItem(todo) is not ListBoxItem container)
            {
                continue;
            }

            var topLeft = container.TransformToAncestor(SelectionHostGrid).Transform(new WpfPoint(0, 0));
            var itemRect = new Rect(topLeft, new WpfSize(container.ActualWidth, container.ActualHeight));

            if (selectionRect.IntersectsWith(itemRect))
            {
                nextSelection.Add(todo);
            }
        }

        if (_rubberBandCurrentSelection.SetEquals(nextSelection))
        {
            return;
        }

        foreach (var todo in _rubberBandCurrentSelection.Except(nextSelection).ToList())
        {
            if (ActiveTodoListBox.ItemContainerGenerator.ContainerFromItem(todo) is ListBoxItem container)
            {
                container.IsSelected = false;
            }
        }

        foreach (var todo in nextSelection.Except(_rubberBandCurrentSelection).ToList())
        {
            if (ActiveTodoListBox.ItemContainerGenerator.ContainerFromItem(todo) is ListBoxItem container)
            {
                container.IsSelected = true;
            }
        }

        _rubberBandCurrentSelection = nextSelection;
    }

    private void EndRubberBandSelection()
    {
        _isRubberBandCandidate = false;
        _isRubberBandSelecting = false;
        _rubberBandStartedOnBlank = false;
        _rubberBandCurrentSelection.Clear();
        SelectionRubberBand.Visibility = Visibility.Collapsed;
        SelectionRubberBand.Width = 0;
        SelectionRubberBand.Height = 0;
        Mouse.Capture(null);
    }

    private ListBox? GetTodoListBoxForItem(TodoItem todo)
        => todo.IsDone ? CompletedTodoListBox : ActiveTodoListBox;

    private bool CanDropTodoOnListBox(TodoItem todo, ListBox targetListBox)
    {
        if (!ReferenceEquals(targetListBox, ActiveTodoListBox) && !ReferenceEquals(targetListBox, CompletedTodoListBox))
        {
            return false;
        }

        return todo.IsDone == ReferenceEquals(targetListBox, CompletedTodoListBox);
    }

    private void TodoListBox_DragOver(object sender, WpfDragEventArgs e)
    {
        if (IsAllGroupsView || _currentSortMode != SortMode.Manual || !e.Data.GetDataPresent(typeof(TodoItem)) || sender is not ListBox listBox)
        {
            ClearInsertionFeedback();
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var source = e.Data.GetData(typeof(TodoItem)) as TodoItem;
        if (source is null || !CanDropTodoOnListBox(source, listBox))
        {
            ClearInsertionFeedback();
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
        UpdateDropInsertionFeedback(listBox, e.GetPosition(listBox), e.OriginalSource as DependencyObject);
        UpdateDragPreviewPosition();
    }

    private void TodoListBox_Drop(object sender, WpfDragEventArgs e)
    {
        try
        {
            if (IsAllGroupsView || _currentSortMode != SortMode.Manual || !e.Data.GetDataPresent(typeof(TodoItem)) || sender is not ListBox listBox)
            {
                return;
            }

            var source = e.Data.GetData(typeof(TodoItem)) as TodoItem;
            if (source is null || !CanDropTodoOnListBox(source, listBox))
            {
                return;
            }

            var groupItems = GetOrderedGroupPartition(source.GroupId, source.IsDone);
            if (groupItems.Count == 0)
            {
                return;
            }

            var targetContainer = GetAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
            var targetItem = targetContainer?.DataContext as TodoItem ?? GetItemFromPoint<TodoItem>(e.GetPosition(listBox), listBox);
            if (targetItem is not null && targetItem.IsDone != source.IsDone)
            {
                targetItem = null;
                targetContainer = null;
            }

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
                    var container = targetContainer ?? (listBox.ItemContainerGenerator.ContainerFromItem(targetItem) as ListBoxItem);
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
            if (source.IsDone)
            {
                ApplyGroupPartitionOrder(source.GroupId, completedOrder: groupItems);
            }
            else
            {
                ApplyGroupPartitionOrder(source.GroupId, activeOrder: groupItems);
            }

            RefreshVisibleTodos();
            listBox.SelectedItem = source;
            listBox.ScrollIntoView(source);
            SaveState();
        }
        finally
        {
            ClearInsertionFeedback();
            HideDragPreview();
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

        var ownerListBox = GetTodoListBoxForItem(todo);
        if (ownerListBox is null)
        {
            return;
        }

        if (_currentSortMode != SortMode.Manual)
        {
            SnapCurrentOrderToManual(todo.GroupId);
            _currentSortMode = SortMode.Manual;
            PersistGroupUiState(SelectedGroup);
            UpdateActionButtonLabels();
            RefreshVisibleTodos();
        }

        _draggedTodoItem = todo;
        _dragStartPoint = e.GetPosition(ownerListBox);
        Mouse.Capture(sender as IInputElement);
        ownerListBox.SelectedItem = todo;
        e.Handled = true;
    }

    private void TodoDragHandle_PreviewMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (_draggedTodoItem is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var ownerListBox = GetTodoListBoxForItem(_draggedTodoItem);
        if (ownerListBox is null)
        {
            return;
        }

        var position = e.GetPosition(ownerListBox);
        if (Math.Abs(position.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        ShowDragPreview(_draggedTodoItem);
        System.Windows.GiveFeedbackEventHandler feedbackHandler = DragPreviewGiveFeedback;
        System.Windows.QueryContinueDragEventHandler continueHandler = DragPreviewQueryContinueDrag;
        ownerListBox.GiveFeedback += feedbackHandler;
        ownerListBox.QueryContinueDrag += continueHandler;
        try
        {
            var data = new System.Windows.DataObject(typeof(TodoItem), _draggedTodoItem);
            DragDrop.DoDragDrop(sender as DependencyObject ?? ownerListBox, data, DragDropEffects.Move);
        }
        finally
        {
            ownerListBox.GiveFeedback -= feedbackHandler;
            ownerListBox.QueryContinueDrag -= continueHandler;
            HideDragPreview();
            ClearInsertionFeedback();
            Mouse.Capture(null);
        }
    }


    private void ShowDragPreview(TodoItem todo)
    {
        DragPreviewTitleText.Text = todo.Text;
        DragPreviewMetaText.Text = $"{todo.PriorityText} · {(todo.IsDone ? "완료됨" : "진행 중")}";

        var ownerListBox = GetTodoListBoxForItem(todo);
        if (ownerListBox?.ItemContainerGenerator.ContainerFromItem(todo) is ListBoxItem container)
        {
            DragPreviewCard.Width = Math.Max(220, container.ActualWidth * 0.88);
            DragPreviewCard.Height = Math.Max(86, container.ActualHeight * 0.88);
        }
        else
        {
            DragPreviewCard.Width = 300;
            DragPreviewCard.Height = 98;
        }

        UpdateDragPreviewPosition();
        DragPreviewPopup.IsOpen = true;
        _isDragPreviewVisible = true;
        if (!_dragPreviewTimer.IsEnabled)
        {
            _dragPreviewTimer.Start();
        }
    }

    private void HideDragPreview()
    {
        _dragPreviewTimer.Stop();
        DragPreviewPopup.IsOpen = false;
        _isDragPreviewVisible = false;
    }

    private void DragPreviewGiveFeedback(object? sender, WpfGiveFeedbackEventArgs e)
    {
        if (_isDragPreviewVisible)
        {
            UpdateDragPreviewPosition();
        }

        e.UseDefaultCursors = true;
        e.Handled = true;
    }

    private void DragPreviewQueryContinueDrag(object? sender, WpfQueryContinueDragEventArgs e)
    {
        if (_isDragPreviewVisible)
        {
            UpdateDragPreviewPosition();
        }
    }

    private void DragPreviewTimer_Tick(object? sender, EventArgs e)
    {
        if (_isDragPreviewVisible)
        {
            UpdateDragPreviewPosition();
        }
    }

    private void UpdateDragPreviewPosition()
    {
        if (!TryGetCursorPosition(out var pt))
        {
            return;
        }

        var local = PointFromScreen(new WpfPoint(pt.X, pt.Y));
        DragPreviewPopup.HorizontalOffset = local.X + 20;
        DragPreviewPopup.VerticalOffset = local.Y + 20;
    }

    private void UpdateDropInsertionFeedback(ListBox listBox, WpfPoint point, DependencyObject? originalSource)
    {
        if (_draggedTodoItem is null)
        {
            ClearInsertionFeedback();
            return;
        }

        var container = GetAncestor<ListBoxItem>(originalSource);
        var targetItem = container?.DataContext as TodoItem ?? GetItemFromPoint<TodoItem>(point, listBox);

        if (targetItem is null)
        {
            if (TryGetLastInsertionContainer(listBox, out var lastContainer) && lastContainer is not null)
            {
                var lastTop = lastContainer.TransformToAncestor(listBox).Transform(new WpfPoint(0, 0));
                if (point.Y >= lastTop.Y + (lastContainer.ActualHeight * 0.5))
                {
                    _lastInsertionTargetContainer = lastContainer;
                    _lastInsertionAfter = true;
                    UpdateInsertionPreviewPosition(lastContainer, true);
                    return;
                }
            }

            ClearInsertionFeedback();
            return;
        }

        if (targetItem == _draggedTodoItem || targetItem.GroupId != _draggedTodoItem.GroupId || targetItem.IsDone != _draggedTodoItem.IsDone)
        {
            ClearInsertionFeedback();
            return;
        }

        container ??= listBox.ItemContainerGenerator.ContainerFromItem(targetItem) as ListBoxItem;
        if (container is null)
        {
            ClearInsertionFeedback();
            return;
        }

        var itemPos = Mouse.GetPosition(container);
        var after = itemPos.Y > container.ActualHeight / 2;

        if (ReferenceEquals(_lastInsertionTargetContainer, container) && _lastInsertionAfter == after)
        {
            UpdateInsertionPreviewPosition(container, after);
            return;
        }

        _lastInsertionTargetContainer = container;
        _lastInsertionAfter = after;
        UpdateInsertionPreviewPosition(container, after);
    }

    private void UpdateInsertionPreviewPosition(ListBoxItem container, bool after)
    {
        if (RootChrome is null)
        {
            return;
        }

        const double visualGapHalf = 5.0; // ListBoxItem bottom margin(10)의 중앙
        var topLeft = container.TransformToAncestor(RootChrome).Transform(new WpfPoint(0, 0));
        var markerWidth = Math.Max(160, container.ActualWidth - 52);
        var markerHalfHeight = InsertionPreviewMarker.Height / 2.0;
        var boundaryY = after ? topLeft.Y + container.ActualHeight : topLeft.Y;
        var centerY = after ? boundaryY + visualGapHalf : boundaryY - visualGapHalf;

        InsertionPreviewMarker.Width = markerWidth;
        InsertionPreviewPopup.HorizontalOffset = topLeft.X + ((container.ActualWidth - markerWidth) / 2.0);
        InsertionPreviewPopup.VerticalOffset = centerY - markerHalfHeight;
        InsertionPreviewPopup.IsOpen = true;
    }

    private void ClearInsertionFeedback()
    {
        InsertionPreviewPopup.IsOpen = false;
        _lastInsertionTargetContainer = null;
        _lastGroupInsertionTargetContainer = null;
    }

    private bool TryGetLastInsertionContainer(ListBox listBox, out ListBoxItem? container)
    {
        container = null;

        for (var i = listBox.Items.Count - 1; i >= 0; i--)
        {
            if (listBox.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem itemContainer &&
                itemContainer.DataContext is TodoItem todo &&
                _draggedTodoItem is not null &&
                todo.GroupId == _draggedTodoItem.GroupId &&
                todo.IsDone == _draggedTodoItem.IsDone)
            {
                container = itemContainer;
                return true;
            }
        }

        return false;
    }


    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint lpPoint);

    private static bool TryGetCursorPosition(out NativePoint point)
    {
        return GetCursorPos(out point);
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
            ClearGroupInsertionFeedback();
        }
    }

    private void GroupListBox_DragOver(object sender, WpfDragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(TaskGroup)) || sender is not ListBox listBox)
        {
            e.Effects = DragDropEffects.None;
            ClearGroupInsertionFeedback();
            e.Handled = true;
            return;
        }

        var source = e.Data.GetData(typeof(TaskGroup)) as TaskGroup;
        if (source is null || !Groups.Contains(source))
        {
            e.Effects = DragDropEffects.None;
            ClearGroupInsertionFeedback();
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Move;
        UpdateGroupDropInsertionFeedback(listBox, e.GetPosition(listBox), e.OriginalSource as DependencyObject);
        e.Handled = true;
    }

    private void GroupListBox_DragLeave(object sender, WpfDragEventArgs e)
    {
        if (!IsMouseActuallyOver(GroupListBox))
        {
            ClearGroupInsertionFeedback();
        }
    }

    private void GroupListBox_Drop(object sender, WpfDragEventArgs e)
    {
        try
        {
            if (!e.Data.GetDataPresent(typeof(TaskGroup)) || sender is not ListBox listBox)
            {
                return;
            }

            var source = e.Data.GetData(typeof(TaskGroup)) as TaskGroup;
            if (source is null || !Groups.Contains(source))
            {
                return;
            }

            var point = e.GetPosition(listBox);
            var ordered = Groups.OrderBy(g => g.SortOrder).ToList();
            var sourceIndex = ordered.IndexOf(source);
            if (sourceIndex < 0)
            {
                return;
            }

            var rawInsertIndex = GetGroupRawInsertIndex(listBox, point, e.OriginalSource as DependencyObject, ordered);
            ordered.RemoveAt(sourceIndex);

            if (rawInsertIndex > sourceIndex)
            {
                rawInsertIndex--;
            }

            var insertIndex = Math.Clamp(rawInsertIndex, 0, ordered.Count);
            ordered.Insert(insertIndex, source);

            ApplyGroupOrder(ordered);
            SelectedGroup = source;
            SaveState();
            e.Handled = true;
        }
        finally
        {
            ClearGroupInsertionFeedback();
            _draggedGroup = null;
        }
    }

    private int GetGroupRawInsertIndex(ListBox listBox, WpfPoint point, DependencyObject? originalSource, IList<TaskGroup> ordered)
    {
        var container = GetAncestor<ListBoxItem>(originalSource);
        var target = container?.DataContext as TaskGroup ?? GetItemFromPoint<TaskGroup>(point, listBox);

        if (target is null)
        {
            if (TryGetLastGroupInsertionContainer(listBox, out var lastContainer) && lastContainer is not null)
            {
                var lastTop = lastContainer.TransformToAncestor(listBox).Transform(new WpfPoint(0, 0));
                if (point.Y >= lastTop.Y + (lastContainer.ActualHeight * 0.5))
                {
                    return ordered.Count;
                }
            }

            return ordered.Count;
        }

        var index = ordered.IndexOf(target);
        if (index < 0)
        {
            return ordered.Count;
        }

        container ??= listBox.ItemContainerGenerator.ContainerFromItem(target) as ListBoxItem;
        if (container is null)
        {
            return index;
        }

        var pos = point;
        try
        {
            pos = Mouse.GetPosition(container);
        }
        catch
        {
            // point 값으로 fallback합니다.
        }

        return pos.Y > container.ActualHeight / 2.0 ? index + 1 : index;
    }

    private void ApplyGroupOrder(IList<TaskGroup> ordered)
    {
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].SortOrder = i;

            var currentIndex = Groups.IndexOf(ordered[i]);
            if (currentIndex >= 0 && currentIndex != i)
            {
                Groups.Move(currentIndex, i);
            }
        }
    }

    private void UpdateGroupDropInsertionFeedback(ListBox listBox, WpfPoint point, DependencyObject? originalSource)
    {
        if (_draggedGroup is null)
        {
            ClearGroupInsertionFeedback();
            return;
        }

        var container = GetAncestor<ListBoxItem>(originalSource);
        var targetGroup = container?.DataContext as TaskGroup ?? GetItemFromPoint<TaskGroup>(point, listBox);

        if (targetGroup is null)
        {
            if (TryGetLastGroupInsertionContainer(listBox, out var lastContainer) && lastContainer is not null)
            {
                var lastTop = lastContainer.TransformToAncestor(listBox).Transform(new WpfPoint(0, 0));
                if (point.Y >= lastTop.Y + (lastContainer.ActualHeight * 0.5))
                {
                    _lastGroupInsertionTargetContainer = lastContainer;
                    _lastGroupInsertionAfter = true;
                    UpdateGroupInsertionPreviewPosition(lastContainer, true);
                    return;
                }
            }

            ClearGroupInsertionFeedback();
            return;
        }

        container ??= listBox.ItemContainerGenerator.ContainerFromItem(targetGroup) as ListBoxItem;
        if (container is null)
        {
            ClearGroupInsertionFeedback();
            return;
        }

        var itemPos = Mouse.GetPosition(container);
        var after = itemPos.Y > container.ActualHeight / 2.0;

        if (ReferenceEquals(_lastGroupInsertionTargetContainer, container) && _lastGroupInsertionAfter == after)
        {
            UpdateGroupInsertionPreviewPosition(container, after);
            return;
        }

        _lastGroupInsertionTargetContainer = container;
        _lastGroupInsertionAfter = after;
        UpdateGroupInsertionPreviewPosition(container, after);
    }

    private void UpdateGroupInsertionPreviewPosition(ListBoxItem container, bool after)
    {
        if (RootChrome is null)
        {
            return;
        }

        const double visualGapHalf = 4.0; // 그룹 ListBoxItem bottom margin(8)의 중앙
        var topLeft = container.TransformToAncestor(RootChrome).Transform(new WpfPoint(0, 0));
        var markerWidth = Math.Max(110, container.ActualWidth - 24);
        var markerHalfHeight = InsertionPreviewMarker.Height / 2.0;
        var boundaryY = after ? topLeft.Y + container.ActualHeight : topLeft.Y;
        var centerY = after ? boundaryY + visualGapHalf : boundaryY - visualGapHalf;

        InsertionPreviewMarker.Width = markerWidth;
        InsertionPreviewPopup.HorizontalOffset = topLeft.X + ((container.ActualWidth - markerWidth) / 2.0);
        InsertionPreviewPopup.VerticalOffset = centerY - markerHalfHeight;
        InsertionPreviewPopup.IsOpen = true;
    }

    private void ClearGroupInsertionFeedback()
    {
        InsertionPreviewPopup.IsOpen = false;
        _lastGroupInsertionTargetContainer = null;
    }

    private bool TryGetLastGroupInsertionContainer(ListBox listBox, out ListBoxItem? container)
    {
        container = null;

        for (var i = listBox.Items.Count - 1; i >= 0; i--)
        {
            if (listBox.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem itemContainer &&
                itemContainer.DataContext is TaskGroup)
            {
                container = itemContainer;
                return true;
            }
        }

        return false;
    }

    private static bool IsMouseActuallyOver(FrameworkElement? element)
    {
        if (element is null || !element.IsVisible)
        {
            return false;
        }

        var point = Mouse.GetPosition(element);
        return point.X >= 0 && point.Y >= 0 && point.X <= element.ActualWidth && point.Y <= element.ActualHeight;
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
            if (ActiveTodoListBox.SelectedItems.Count > 0 || CompletedTodoListBox.SelectedItems.Count > 0)
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
            else if (CompletedTodoListBox.SelectedItem is TodoItem completedTodo)
            {
                StartInlineEdit(completedTodo);
                e.Handled = true;
            }
        }
    }

    private void CopySelectedTodos(bool cutAfterCopy)
    {
        var selected = ActiveTodoListBox.SelectedItems.Cast<TodoItem>()
            .Concat(CompletedTodoListBox.SelectedItems.Cast<TodoItem>())
            .Distinct()
            .ToList();
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


    private void HideToTray()
    {
        SaveState();
        _settingsWindow?.Hide();
        _archiveWindow?.Hide();
        Hide();
    }

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
        Dispatcher.Invoke(() =>
        {
            _allowRealClose = true;
            Close();
        });
    }

    private void TogglePinnedFromTray()
    {
        Dispatcher.Invoke(() => IsPinnedToDesktop = !IsPinnedToDesktop);
    }

    private void NotifySettingsPropertiesChanged()
    {
        OnPropertyChanged(nameof(AutoArchiveSettingText));
        OnPropertyChanged(nameof(CloseBehaviorSettingText));
        OnPropertyChanged(nameof(StartMinimizedSettingText));
        OnPropertyChanged(nameof(DefaultPrioritySettingText));
        OnPropertyChanged(nameof(DefaultRepeatSettingText));
        OnPropertyChanged(nameof(DefaultDueDateSettingText));
        OnPropertyChanged(nameof(CompletedStrikeThroughSettingText));
        OnPropertyChanged(nameof(ShowCompletedStrikethroughSetting));
        OnPropertyChanged(nameof(HasArchivedTodos));
        OnPropertyChanged(nameof(ArchivedCountText));
        OnPropertyChanged(nameof(ArchivedSummaryText));
        OnPropertyChanged(nameof(ArchiveLauncherButtonText));
    }

    public void ToggleCloseBehaviorSetting()
    {
        _state.Settings.HideToTrayOnClose = !_state.Settings.HideToTrayOnClose;
        NotifySettingsPropertiesChanged();
        SaveState();
    }

    public void ToggleStartMinimizedSetting()
    {
        _state.Settings.StartMinimizedToTray = !_state.Settings.StartMinimizedToTray;
        NotifySettingsPropertiesChanged();
        SaveState();
    }

    public void CycleAutoArchiveSetting()
    {
        _state.Settings.AutoArchiveDays = _state.Settings.AutoArchiveDays switch
        {
            0 => 7,
            7 => 30,
            _ => 0
        };

        NotifySettingsPropertiesChanged();
        RefreshVisibleTodos();
        SaveState();
    }

    public void CycleDefaultPrioritySetting()
    {
        _state.Settings.DefaultPriority = _state.Settings.DefaultPriority switch
        {
            TodoPriority.Low => TodoPriority.Normal,
            TodoPriority.Normal => TodoPriority.High,
            _ => TodoPriority.Low
        };
        NotifySettingsPropertiesChanged();
        SaveState();
    }

    public void CycleDefaultRepeatSetting()
    {
        _state.Settings.DefaultRepeat = _state.Settings.DefaultRepeat switch
        {
            TodoRepeat.None => TodoRepeat.Daily,
            TodoRepeat.Daily => TodoRepeat.Weekly,
            TodoRepeat.Weekly => TodoRepeat.Monthly,
            _ => TodoRepeat.None
        };
        NotifySettingsPropertiesChanged();
        SaveState();
    }

    public void ToggleDefaultDueDateSetting()
    {
        _state.Settings.DefaultUseDueDate = !_state.Settings.DefaultUseDueDate;
        NotifySettingsPropertiesChanged();
        SaveState();
    }

    public void ToggleCompletedStrikeThroughSetting()
    {
        ShowCompletedStrikethroughSetting = !ShowCompletedStrikethroughSetting;
    }

    private void ToggleCloseBehaviorSettingButton_Click(object sender, RoutedEventArgs e) => ToggleCloseBehaviorSetting();

    private void ToggleStartMinimizedSettingButton_Click(object sender, RoutedEventArgs e) => ToggleStartMinimizedSetting();

    private void CycleAutoArchiveSettingButton_Click(object sender, RoutedEventArgs e) => CycleAutoArchiveSetting();

    private void CycleDefaultPrioritySettingButton_Click(object sender, RoutedEventArgs e) => CycleDefaultPrioritySetting();

    private void CycleDefaultRepeatSettingButton_Click(object sender, RoutedEventArgs e) => CycleDefaultRepeatSetting();

    private void ToggleDefaultDueDateSettingButton_Click(object sender, RoutedEventArgs e) => ToggleDefaultDueDateSetting();

    private void ToggleCompletedStrikeThroughSettingButton_Click(object sender, RoutedEventArgs e) => ToggleCompletedStrikeThroughSetting();

    private void RestoreAllCompletedButton_Click(object sender, RoutedEventArgs e)
    {
        RestoreCompletedTodos(CompletedVisibleTodos.ToList());
    }

    private void ArchiveAllCompletedButton_Click(object sender, RoutedEventArgs e)
    {
        ArchiveTodos(CompletedVisibleTodos.ToList());
    }

    public void RestoreSelectedArchivedTodos(IEnumerable<TodoItem> todos)
    {
        RestoreArchivedTodos(todos);
    }

    public void DeleteSelectedArchivedTodos(IEnumerable<TodoItem> todos)
    {
        DeleteArchivedTodos(todos);
    }

    public void RestoreArchivedTodos(IEnumerable<TodoItem> todos)
    {
        var archived = todos.Where(t => t.IsArchived).Distinct().ToList();
        if (archived.Count == 0)
        {
            return;
        }

        var fallbackGroup = SelectedGroup ?? Groups.OrderBy(g => g.SortOrder).FirstOrDefault();
        var affectedGroups = new HashSet<string>();

        foreach (var todo in archived)
        {
            affectedGroups.Add(todo.GroupId);
            if (!Groups.Any(g => g.Id == todo.GroupId) && fallbackGroup is not null)
            {
                todo.GroupId = fallbackGroup.Id;
                affectedGroups.Add(fallbackGroup.Id);
            }

            todo.IsArchived = false;
            if (!todo.IsDone)
            {
                todo.CompletedAt = null;
            }
        }

        foreach (var groupId in affectedGroups.Where(id => Groups.Any(g => g.Id == id)))
        {
            ApplyGroupPartitionOrder(groupId);
        }

        NotifySettingsPropertiesChanged();
        RefreshVisibleTodos();
        SaveState();
    }

    public void DeleteArchivedTodos(IEnumerable<TodoItem> todos)
    {
        var archived = todos.Where(t => t.IsArchived).Distinct().ToList();
        if (archived.Count == 0)
        {
            return;
        }

        RememberDeletedTodos(archived);
        foreach (var todo in archived)
        {
            AllTodos.Remove(todo);
        }

        NotifySettingsPropertiesChanged();
        RefreshVisibleTodos();
        SaveState();
    }

    public void RestoreAllArchivedTodos() => RestoreArchivedTodos(AllTodos.Where(t => t.IsArchived).ToList());

    public void DeleteAllArchivedTodos() => DeleteArchivedTodos(AllTodos.Where(t => t.IsArchived).ToList());

    private void RestoreArchivedTodosButton_Click(object sender, RoutedEventArgs e) => RestoreAllArchivedTodos();

    private void DeleteArchivedTodosButton_Click(object sender, RoutedEventArgs e) => DeleteAllArchivedTodos();

    public void ExportBackup()
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

    public void ImportBackup()
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

        _state.Settings = imported.Settings ?? new AppSettings();
        Width = Math.Max(SafeFinite(_state.Settings.Width, 900), MinWidth);
        Height = Math.Max(SafeFinite(_state.Settings.Height, 760), MinHeight);
        Left = SafeFinite(_state.Settings.Left, 70);
        Top = SafeFinite(_state.Settings.Top, 50);
        EnsureWindowVisibleOnScreen();
        IsSidebarVisible = _state.Settings.IsSidebarVisible;
        IsPinnedToDesktop = _state.Settings.IsPinnedToDesktop;
        SelectedGroup = Groups.FirstOrDefault();
        UpdateSidebarLayout();
        UpdateMinimumWindowWidth();
        NotifySettingsPropertiesChanged();
        RefreshVisibleTodos();
        SaveState();
    }

    private void ExportBackupButton_Click(object sender, RoutedEventArgs e) => ExportBackup();

    private void ImportBackupButton_Click(object sender, RoutedEventArgs e) => ImportBackup();

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_allowRealClose && _state.Settings.HideToTrayOnClose)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        SaveState();
    }

    private void RefreshVisibleTodos()
    {
        var autoArchived = ApplyAutoArchivePolicy();
        VisibleTodos.Clear();
        CompletedVisibleTodos.Clear();
        ArchivedVisibleTodos.Clear();

        foreach (var archived in AllTodos.Where(t => t.IsArchived)
                     .OrderByDescending(t => t.CompletedAt ?? t.CreatedAt)
                     .ThenBy(t => t.Text))
        {
            ArchivedVisibleTodos.Add(archived);
        }

        if (!IsAllGroupsView && SelectedGroup is null)
        {
            SummaryText = "전체 0 · 진행 0 · 완료 0";
            IsEmptyVisible = true;
            OnPropertyChanged(nameof(HasCompletedTodos));
            OnPropertyChanged(nameof(CompletedCountText));
            OnPropertyChanged(nameof(HasArchivedTodos));
            OnPropertyChanged(nameof(ArchivedCountText));
            OnPropertyChanged(nameof(ArchivedSummaryText));
            return;
        }

        var groupOrder = Groups.ToDictionary(g => g.Id, g => g.SortOrder);
        var groupTodos = IsAllGroupsView
            ? AllTodos.Where(t => !t.IsArchived).ToList()
            : AllTodos.Where(t => t.GroupId == SelectedGroup!.Id && !t.IsArchived).ToList();

        IEnumerable<TodoItem> ordered = _currentSortMode switch
        {
            SortMode.Created => groupTodos.OrderBy(t => t.IsDone).ThenBy(t => t.CreatedAt).ThenBy(t => t.SortOrder),
            SortMode.Name => groupTodos.OrderBy(t => t.IsDone).ThenBy(t => t.Text, StringComparer.CurrentCultureIgnoreCase).ThenBy(t => t.CreatedAt),
            SortMode.Priority => groupTodos.OrderBy(t => t.IsDone).ThenBy(t => PriorityRank(t.Priority)).ThenBy(t => t.DueDate ?? DateTime.MaxValue).ThenBy(t => t.CreatedAt),
            SortMode.Manual when !IsAllGroupsView => groupTodos.OrderBy(t => t.IsDone).ThenBy(t => t.SortOrder),
            SortMode.Manual => groupTodos.OrderBy(t => t.IsDone).ThenBy(t => groupOrder.TryGetValue(t.GroupId, out var order) ? order : int.MaxValue).ThenBy(t => t.SortOrder),
            _ => groupTodos.OrderBy(t => t.IsDone).ThenBy(t => t.DueDate ?? DateTime.MaxValue).ThenBy(t => t.CreatedAt)
        };

        foreach (var todo in ordered.Where(MatchesCurrentFilter))
        {
            if (todo.IsDone)
            {
                CompletedVisibleTodos.Add(todo);
            }
            else
            {
                VisibleTodos.Add(todo);
            }
        }

        var total = groupTodos.Count;
        var completed = groupTodos.Count(t => t.IsDone);
        var remaining = total - completed;
        SummaryText = IsAllGroupsView
            ? $"모든 그룹 · 전체 {total} · 진행 {remaining} · 완료 {completed}"
            : $"전체 {total} · 진행 {remaining} · 완료 {completed}";
        IsEmptyVisible = VisibleTodos.Count == 0;
        OnPropertyChanged(nameof(HasCompletedTodos));
        OnPropertyChanged(nameof(CompletedCountText));
        OnPropertyChanged(nameof(HasArchivedTodos));
        OnPropertyChanged(nameof(ArchivedCountText));
        OnPropertyChanged(nameof(ArchivedSummaryText));
        OnPropertyChanged(nameof(ArchiveLauncherButtonText));
        VisibleTodosView.Refresh();

        if (autoArchived && _isInitialized)
        {
            SaveState();
        }
    }

    private List<TodoItem> GetGroupTodosInCurrentOrder(string groupId)
    {
        var groupTodos = AllTodos.Where(t => t.GroupId == groupId && !t.IsArchived).ToList();
        IEnumerable<TodoItem> ordered = _currentSortMode switch
        {
            SortMode.Created => groupTodos.OrderBy(t => t.IsDone).ThenBy(t => t.CreatedAt).ThenBy(t => t.SortOrder),
            SortMode.Name => groupTodos.OrderBy(t => t.IsDone).ThenBy(t => t.Text, StringComparer.CurrentCultureIgnoreCase).ThenBy(t => t.CreatedAt),
            SortMode.Priority => groupTodos.OrderBy(t => t.IsDone).ThenBy(t => PriorityRank(t.Priority)).ThenBy(t => t.DueDate ?? DateTime.MaxValue).ThenBy(t => t.CreatedAt),
            SortMode.Manual => groupTodos.OrderBy(t => t.IsDone).ThenBy(t => t.SortOrder),
            _ => groupTodos.OrderBy(t => t.IsDone).ThenBy(t => t.DueDate ?? DateTime.MaxValue).ThenBy(t => t.CreatedAt)
        };

        return ordered.ToList();
    }

    private void SnapCurrentOrderToManual(string groupId)
    {
        var ordered = GetGroupTodosInCurrentOrder(groupId);
        var active = ordered.Where(t => !t.IsDone).ToList();
        var completed = ordered.Where(t => t.IsDone).ToList();
        ApplyGroupPartitionOrder(groupId, active, completed);
    }

    private void EnsureManualSortForGroup(string groupId)
    {
        if (_currentSortMode == SortMode.Manual)
        {
            return;
        }

        if (SelectedGroup?.Id != groupId)
        {
            return;
        }

        SnapCurrentOrderToManual(groupId);
        _currentSortMode = SortMode.Manual;
        PersistGroupUiState(SelectedGroup);
        UpdateActionButtonLabels();
    }

    private List<TodoItem> GetOrderedGroupPartition(string groupId, bool completed)
        => AllTodos.Where(t => t.GroupId == groupId && !t.IsArchived && t.IsDone == completed)
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.CreatedAt)
            .ToList();

    private void ApplyGroupPartitionOrder(string groupId, IReadOnlyList<TodoItem>? activeOrder = null, IReadOnlyList<TodoItem>? completedOrder = null)
    {
        var active = (activeOrder ?? GetOrderedGroupPartition(groupId, false)).ToList();
        var completed = (completedOrder ?? GetOrderedGroupPartition(groupId, true)).ToList();
        var archived = AllTodos.Where(t => t.GroupId == groupId && t.IsArchived)
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.CreatedAt)
            .ToList();

        var index = 0;
        foreach (var todo in active.Concat(completed).Concat(archived))
        {
            todo.SortOrder = index++;
        }
    }

    private void ArchiveTodos(IEnumerable<TodoItem> todos)
    {
        var affectedGroups = new HashSet<string>();
        foreach (var todo in todos.Where(t => t.IsDone && !t.IsArchived).Distinct())
        {
            todo.ArchivedGroupName = Groups.FirstOrDefault(g => g.Id == todo.GroupId)?.Name ?? todo.ArchivedGroupName;
            todo.IsArchived = true;
            todo.CompletedAt ??= DateTime.Now;
            affectedGroups.Add(todo.GroupId);
        }

        foreach (var groupId in affectedGroups)
        {
            ApplyGroupPartitionOrder(groupId);
        }

        if (affectedGroups.Count > 0)
        {
            RefreshVisibleTodos();
            SaveState();
        }
    }

    private void RestoreCompletedTodos(IEnumerable<TodoItem> todos)
    {
        var affectedGroups = new HashSet<string>();
        foreach (var todo in todos.Distinct())
        {
            SetTodoCompletionState(todo, false, createRepeatClone: false);
            affectedGroups.Add(todo.GroupId);
        }

        if (affectedGroups.Count > 0)
        {
            RefreshVisibleTodos();
            SaveState();
        }
    }

    private bool ApplyAutoArchivePolicy()
    {
        if (_state.Settings.AutoArchiveDays <= 0)
        {
            return false;
        }

        var threshold = DateTime.Now.AddDays(-_state.Settings.AutoArchiveDays);
        var changed = false;
        var affectedGroups = new HashSet<string>();
        foreach (var todo in AllTodos.Where(t => t.IsDone && !t.IsArchived && t.CompletedAt.HasValue && t.CompletedAt.Value <= threshold).ToList())
        {
            todo.ArchivedGroupName = Groups.FirstOrDefault(g => g.Id == todo.GroupId)?.Name ?? todo.ArchivedGroupName;
            todo.IsArchived = true;
            changed = true;
            affectedGroups.Add(todo.GroupId);
        }

        foreach (var groupId in affectedGroups)
        {
            ApplyGroupPartitionOrder(groupId);
        }

        return changed;
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

    private static string AutoArchiveDaysToText(int days)
        => days switch
        {
            7 => "7일 후",
            30 => "30일 후",
            _ => "사용 안 함"
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

    private TodoItem? GetTodoFromMenuSender(object sender)
    {
        if (sender is not MenuItem item)
        {
            return null;
        }

        if (item.CommandParameter is string idFromParameter)
        {
            return AllTodos.FirstOrDefault(t => t.Id == idFromParameter);
        }

        var menu = GetOwningContextMenu(item);
        return menu?.PlacementTarget is FrameworkElement element ? element.DataContext as TodoItem : null;
    }

    private static ContextMenu? GetOwningContextMenu(MenuItem item)
    {
        ItemsControl? parent = ItemsControl.ItemsControlFromItemContainer(item);
        while (parent is not null)
        {
            if (parent is ContextMenu contextMenu)
            {
                return contextMenu;
            }

            parent = parent is MenuItem parentMenuItem
                ? ItemsControl.ItemsControlFromItemContainer(parentMenuItem)
                : null;
        }

        return null;
    }

    public string GetGroupName(string groupId)
        => Groups.FirstOrDefault(g => g.Id == groupId)?.Name ?? "알 수 없는 그룹";

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