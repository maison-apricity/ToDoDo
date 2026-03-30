using System.Linq;
using System.Windows;
using System.Windows.Input;
using ToDoDo.Models;

namespace ToDoDo;

public partial class ArchiveWindow : Window
{
    private MainWindow OwnerWindow => (MainWindow)Owner;

    public ArchiveWindow(MainWindow owner)
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

    private TodoItem[] SelectedArchivedTodos()
        => ArchivedListBox.SelectedItems.Cast<TodoItem>().ToArray();

    private void RestoreSelectedButton_Click(object sender, RoutedEventArgs e)
        => OwnerWindow.RestoreSelectedArchivedTodos(SelectedArchivedTodos());

    private void DeleteSelectedButton_Click(object sender, RoutedEventArgs e)
        => OwnerWindow.DeleteSelectedArchivedTodos(SelectedArchivedTodos());

    private void RestoreAllButton_Click(object sender, RoutedEventArgs e)
        => OwnerWindow.RestoreAllArchivedTodos();

    private void DeleteAllButton_Click(object sender, RoutedEventArgs e)
        => OwnerWindow.DeleteAllArchivedTodos();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
