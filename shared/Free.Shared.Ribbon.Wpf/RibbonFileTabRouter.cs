using System.Windows.Controls;
using Free.Shared.Ribbon;

namespace Free.Shared.Ribbon.Wpf;

/// <summary>
/// Routes a Word-style File tab to a Backstage surface while keeping the last real ribbon tab selected.
/// </summary>
public sealed class RibbonFileTabRouter : IDisposable
{
    private const string FileTabCommandId = "FileTab";

    private readonly TabControl _tabs;
    private readonly TabItem _fileTab;
    private readonly Action _showBackstage;
    private int _lastContentTabIndex;
    private bool _suppressSelectionReentry;
    private bool _isDisposed;

    private RibbonFileTabRouter(
        TabControl tabs,
        TabItem fileTab,
        Action showBackstage,
        int initialContentTabIndex)
    {
        _tabs = tabs;
        _fileTab = fileTab;
        _showBackstage = showBackstage;
        _lastContentTabIndex = CoerceContentTabIndex(initialContentTabIndex);
        _tabs.SelectionChanged += OnSelectionChanged;
    }

    public int LastContentTabIndex => _lastContentTabIndex;

    public static RibbonFileTabRouter Attach(
        TabControl tabs,
        TabItem fileTab,
        Action showBackstage,
        int initialContentTabIndex = 1)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        ArgumentNullException.ThrowIfNull(fileTab);
        ArgumentNullException.ThrowIfNull(showBackstage);

        return new RibbonFileTabRouter(tabs, fileTab, showBackstage, initialContentTabIndex);
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _tabs.SelectionChanged -= OnSelectionChanged;
        _isDisposed = true;
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, _tabs) || _suppressSelectionReentry)
            return;

        if (ReferenceEquals(_tabs.SelectedItem, _fileTab))
        {
            _suppressSelectionReentry = true;
            try
            {
                _tabs.SelectedIndex = CoerceContentTabIndex(_lastContentTabIndex);
            }
            finally
            {
                _suppressSelectionReentry = false;
            }

            try
            {
                _showBackstage();
            }
            catch (Exception ex)
            {
                // This callback runs inside TabControl.SelectionChanged. The WPF application
                // boundary records dispatcher faults but does not mark them handled, so an
                // exception here would otherwise terminate the process.
                RibbonCommandFaultReporter.Report(ex, FileTabCommandId);
            }
            return;
        }

        if (_tabs.SelectedIndex > 0)
            _lastContentTabIndex = _tabs.SelectedIndex;
    }

    private int CoerceContentTabIndex(int requestedIndex)
    {
        if (_tabs.Items.Count <= 1)
            return 0;

        return Math.Clamp(requestedIndex, 1, _tabs.Items.Count - 1);
    }
}
