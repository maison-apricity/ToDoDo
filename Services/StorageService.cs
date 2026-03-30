using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ToDoDo.Models;

namespace ToDoDo.Services;

public static class StorageService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    public static string BaseFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ToDoDo");

    public static string StatePath => Path.Combine(BaseFolder, "state.json");

    public static AppState Load()
    {
        try
        {
            Directory.CreateDirectory(BaseFolder);

            if (!File.Exists(StatePath))
            {
                return CreateDefaultState();
            }

            var json = File.ReadAllText(StatePath);
            var state = JsonSerializer.Deserialize<AppState>(json, JsonOptions) ?? CreateDefaultState();
            SanitizeState(state);

            if (state.Groups.Count == 0)
            {
                state.Groups.Add(new TaskGroup { Name = "기본", SortOrder = 0 });
            }

            return state;
        }
        catch
        {
            return CreateDefaultState();
        }
    }

    public static void Save(AppState state)
    {
        Directory.CreateDirectory(BaseFolder);
        SanitizeState(state);
        var json = JsonSerializer.Serialize(state, JsonOptions);
        File.WriteAllText(StatePath, json);
    }

    private static AppState CreateDefaultState()
    {
        var group = new TaskGroup { Name = "기본", SortOrder = 0 };
        return new AppState
        {
            Groups = new List<TaskGroup> { group },
            Todos = new List<TodoItem>(),
            Settings = new AppSettings()
        };
    }

    private static void SanitizeState(AppState state)
    {
        if (state.Settings is null)
        {
            state.Settings = new AppSettings();
            return;
        }

        state.Settings.Width = SafeFinite(state.Settings.Width, 900);
        state.Settings.Height = SafeFinite(state.Settings.Height, 760);
        state.Settings.Left = SafeFinite(state.Settings.Left, 70);
        state.Settings.Top = SafeFinite(state.Settings.Top, 50);

        if (state.Settings.Width < 640)
        {
            state.Settings.Width = 640;
        }

        if (state.Settings.Height < 520)
        {
            state.Settings.Height = 520;
        }

        state.Settings.SidebarWidth = SafeFinite(state.Settings.SidebarWidth, 220);
        if (state.Settings.SidebarWidth < 180)
        {
            state.Settings.SidebarWidth = 180;
        }
        if (state.Settings.SidebarWidth > 360)
        {
            state.Settings.SidebarWidth = 360;
        }

        if (state.Settings.AutoArchiveDays is not (0 or 7 or 30))
        {
            state.Settings.AutoArchiveDays = 0;
        }

        foreach (var todo in state.Todos)
        {
            if (string.IsNullOrWhiteSpace(todo.Id))
            {
                todo.Id = Guid.NewGuid().ToString("N");
            }

            if (todo.CreatedAt == default)
            {
                todo.CreatedAt = DateTime.Now;
            }

            if (todo.IsDone && todo.CompletedAt is null)
            {
                todo.CompletedAt = DateTime.Now;
            }

            if (!todo.IsDone)
            {
                todo.CompletedAt = null;
            }
        }
    }

    private static double SafeFinite(double value, double fallback)
        => double.IsFinite(value) ? value : fallback;
}
