namespace ImageManager.App.ViewModels;

public sealed class FunctionPageViewModel(string title, string description) : ObservableObject
{
    public string Title { get; } = title;
    public string Description { get; } = description;
    public List<string> Actions { get; } = [];
}
