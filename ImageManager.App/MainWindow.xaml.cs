using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ImageManager.App.ViewModels;
using ImageManager.App.Services;
using ImageManager.Persistence;
using ImageManager.Services;

namespace ImageManager.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private bool _compressionLogAutoScroll = true;

    public MainWindow()
    {
        InitializeComponent();
        var configStore = new ConfigStore();
        var trackingStore = new TrackingStore();
        var folderDialogs = new FolderDialogService();
        var conflictPrompt = new ConflictPromptService();
        _viewModel = new MainWindowViewModel(
            configStore,
            trackingStore,
            new FileIndexService(trackingStore),
            new BackupService(trackingStore, conflictPrompt),
            new StageService(trackingStore, conflictPrompt),
            new ArchiveService(trackingStore, conflictPrompt),
            new CompressionService(),
            new AuditService(),
            new CountsService(),
            new PurgeService(),
            folderDialogs);
        DataContext = _viewModel;
        Loaded += async (_, _) => await _viewModel.InitializeAsync();

        CompressionLogBox.TextChanged += (_, _) =>
        {
            if (_compressionLogAutoScroll)
            {
                CompressionLogBox.ScrollToEnd();
            }
        };

        CompressionLogBox.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, e) =>
        {
            // Only toggle user intent when this is a user scroll (extent unchanged).
            if (e.ExtentHeightChange != 0)
            {
                return;
            }

            var sv = e.OriginalSource as ScrollViewer;
            if (sv is null)
            {
                return;
            }

            // If the user is at the bottom, keep following; otherwise stop auto-follow.
            _compressionLogAutoScroll = sv.VerticalOffset >= sv.ScrollableHeight - 1.0;
        }));
    }

    /// <summary>
    /// Handles label clicks: toggles folder expansion via the view-model (bound to TreeViewItem.IsExpanded)
    /// and marks the event handled so the row is not selected (repeat clicks on the name keep working).
    /// </summary>
    private void FileTreeNodeName_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not FileTreeNodeViewModel vm)
        {
            return;
        }

        if (vm.IsFolder)
        {
            vm.IsExpanded = !vm.IsExpanded;
        }

        e.Handled = true;
    }
}