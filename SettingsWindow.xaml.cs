using System.Windows;
using System.Windows.Input;

namespace ToDoDo;

public partial class SettingsWindow : Window
{
    private MainWindow OwnerWindow => (MainWindow)Owner;

    public SettingsWindow(MainWindow owner)
    {
        InitializeComponent();
        Owner = owner;
        DataContext = owner;
        Icon = owner.Icon;
        FontFamily = owner.FontFamily;
    }

    private void HeaderDragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void PinButton_Click(object sender, RoutedEventArgs e) => OwnerWindow.TogglePinState();
    private void ToggleCloseBehaviorButton_Click(object sender, RoutedEventArgs e) => OwnerWindow.ToggleCloseBehaviorSetting();
    private void ToggleStartMinimizedButton_Click(object sender, RoutedEventArgs e) => OwnerWindow.ToggleStartMinimizedSetting();
    private void CycleDefaultPriorityButton_Click(object sender, RoutedEventArgs e) => OwnerWindow.CycleDefaultPrioritySetting();
    private void CycleDefaultRepeatButton_Click(object sender, RoutedEventArgs e) => OwnerWindow.CycleDefaultRepeatSetting();
    private void ToggleDefaultDueDateButton_Click(object sender, RoutedEventArgs e) => OwnerWindow.ToggleDefaultDueDateSetting();
    private void CycleAutoArchiveButton_Click(object sender, RoutedEventArgs e) => OwnerWindow.CycleAutoArchiveSetting();
    private void ToggleCompletedStrikeThroughButton_Click(object sender, RoutedEventArgs e) => OwnerWindow.ToggleCompletedStrikeThroughSetting();
    private void OpenArchiveButton_Click(object sender, RoutedEventArgs e) => OwnerWindow.OpenArchiveWindow();
    private void RestoreArchivedButton_Click(object sender, RoutedEventArgs e) => OwnerWindow.RestoreAllArchivedTodos();
    private void DeleteArchivedButton_Click(object sender, RoutedEventArgs e) => OwnerWindow.DeleteAllArchivedTodos();
    private void ExportBackupButton_Click(object sender, RoutedEventArgs e) => OwnerWindow.ExportBackup();
    private void ImportBackupButton_Click(object sender, RoutedEventArgs e) => OwnerWindow.ImportBackup();
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
