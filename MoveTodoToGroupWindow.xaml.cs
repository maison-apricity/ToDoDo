using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using ToDoDo.Models;

namespace ToDoDo;

public partial class MoveTodoToGroupWindow : Window
{
    public ObservableCollection<TaskGroup> Groups { get; } = new();
    public TaskGroup? SelectedGroup { get; set; }
    public string? SelectedGroupId { get; private set; }

    public MoveTodoToGroupWindow(System.Collections.Generic.IEnumerable<TaskGroup> groups, string currentGroupId)
    {
        InitializeComponent();

        foreach (var group in groups.Where(g => g.Id != currentGroupId))
        {
            Groups.Add(group);
        }

        SelectedGroup = Groups.FirstOrDefault();
        DataContext = this;
    }

    private void HeaderDragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (GroupListBox.SelectedItem is TaskGroup group)
        {
            SelectedGroupId = group.Id;
            DialogResult = true;
            return;
        }

        if (SelectedGroup is not null)
        {
            SelectedGroupId = SelectedGroup.Id;
            DialogResult = true;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
