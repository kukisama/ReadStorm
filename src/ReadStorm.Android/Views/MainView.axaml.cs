using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ReadStorm.Android;
using ReadStorm.Desktop.ViewModels;
using ReadStorm.Domain.Models;

namespace ReadStorm.Desktop.Views;

public partial class MainView : UserControl
{
    private MainWindowViewModel? _vm;

    // ═══ 导航状态 ═══
    private int _activeBottomTab; // 0=搜索 1=任务 2=书架 3=更多
    private bool _isMorePanelActive; // 当前是否在"更多"跳板页（非子页面）
    private bool _isReaderToolbarVisible;
    private Point? _swipeStartPoint;
    private bool _startupSplashDismissed;
    private DateTimeOffset _startupSplashShownAt;
    private DispatcherTimer? _startupSplashTimer;
    private bool _pointerInteractionHandled;

    // Android 15 上系统启动页结束后，Avalonia 首帧到达前容易出现短暂白屏。
    // 这里通过“最短展示 + 就绪判定 + 超时兜底”三段策略平滑过渡。
    private static readonly TimeSpan StartupSplashMinDuration = TimeSpan.FromMilliseconds(550);
    private static readonly TimeSpan StartupSplashMaxDuration = TimeSpan.FromSeconds(3);

    // Tab 索引常量（与 MainWindowViewModel 保持一致）
    private const int TabSearch = 0;
    private const int TabTasks = 1;
    private const int TabDiagnostic = 2;
    private const int TabBookshelf = 3;
    private const int TabReader = 4;
    private const int TabRules = 5;
    private const int TabSettings = 6;
    private const int TabAbout = 7;
    private const int TabLog = 8;

    public MainView()
    {
        InitializeComponent();
        _startupSplashShownAt = DateTimeOffset.Now;
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        StartStartupSplashDismissLoop();
    }

    private void StartStartupSplashDismissLoop()
    {
        if (_startupSplashDismissed || _startupSplashTimer is not null)
            return;

        _startupSplashTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(80),
        };

        _startupSplashTimer.Tick += (_, _) =>
        {
            if (_startupSplashDismissed)
            {
                _startupSplashTimer?.Stop();
                _startupSplashTimer = null;
                return;
            }

            var elapsed = DateTimeOffset.Now - _startupSplashShownAt;
            var minElapsed = elapsed >= StartupSplashMinDuration;
            var timeoutReached = elapsed >= StartupSplashMaxDuration;
            var ready = IsMainContentReady();

#if DEBUG
            if (ready)
            {
                Trace.WriteLine($"[ReadStorm] Startup splash ready. elapsed={elapsed.TotalMilliseconds:F0}ms");
            }
#endif

            if ((minElapsed && ready) || timeoutReached)
            {
                DismissStartupSplash();
                _startupSplashTimer?.Stop();
                _startupSplashTimer = null;
            }
        };

        _startupSplashTimer.Start();
    }

    private bool IsMainContentReady()
    {
        if (DataContext is not MainWindowViewModel vm)
            return false;

        // 关键 UI 与核心 VM 就绪：避免系统 Splash 退场后马上露出尚未稳定的首帧。
        return MainTabControl.Bounds.Width > 0
            && MainTabControl.Bounds.Height > 0
            && !string.IsNullOrWhiteSpace(vm.StatusMessage);
    }

    private void DismissStartupSplash()
    {
        if (_startupSplashDismissed)
            return;

        _startupSplashDismissed = true;
        Dispatcher.UIThread.Post(() =>
        {
            StartupSplash.IsVisible = false;
        }, DispatcherPriority.Render);
    }

    // ═══════════════════════════════════════════════════════════════
    //  DataContext 变化 → 订阅 ViewModel 属性变更
    // ═══════════════════════════════════════════════════════════════

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnMainVmPropertyChanged;
            _vm.Reader.PropertyChanged -= OnReaderVmPropertyChanged;
        }

        _vm = DataContext as MainWindowViewModel;

        if (DataContext is MainWindowViewModel vm)
        {
            vm.PropertyChanged += OnMainVmPropertyChanged;

            // 订阅 Reader 子 VM 的属性变更（ScrollToTop、TOC 关闭等）
            if (vm.Reader is { } reader)
                reader.PropertyChanged += OnReaderVmPropertyChanged;

            UpdateNavigationUI();
        }
    }

    private void OnMainVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.SelectedTabIndex))
        {
            Dispatcher.UIThread.Post(UpdateNavigationUI);
        }
    }

    private void OnReaderVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            // 打开目录时隐藏工具栏
            case nameof(ReaderViewModel.IsTocOverlayVisible):
                if (sender is ReaderViewModel { IsTocOverlayVisible: true })
                    SetReaderToolbar(false);
                break;

            // 阅读扩展到刘海区域开关
            case nameof(ReaderViewModel.ReaderExtendIntoCutout):
                if (sender is ReaderViewModel reader
                    && (DataContext as MainWindowViewModel)?.SelectedTabIndex == TabReader)
                {
                    AndroidSystemUiBridge.ApplyReaderCutoutMode(reader.ReaderExtendIntoCutout);
                }
                break;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  底部导航栏点击
    // ═══════════════════════════════════════════════════════════════

    private void OnNavSearchClick(object? s, RoutedEventArgs e) => NavigateToBottomTab(0);
    private void OnNavTasksClick(object? s, RoutedEventArgs e) => NavigateToBottomTab(1);
    private void OnNavBookshelfClick(object? s, RoutedEventArgs e) => NavigateToBottomTab(2);
    private void OnNavMoreClick(object? s, RoutedEventArgs e) => NavigateToBottomTab(3);

    private void NavigateToBottomTab(int bottomTab)
    {
        _activeBottomTab = bottomTab;
        MorePanel.IsVisible = false;
        _isMorePanelActive = false;

        switch (bottomTab)
        {
            case 0: SetVmTabIndex(TabSearch); break;
            case 1: SetVmTabIndex(TabTasks); break;
            case 2: SetVmTabIndex(TabBookshelf); break;
            case 3: // 更多
                _isMorePanelActive = true;
                MorePanel.IsVisible = true;
                break;
        }
        UpdateNavigationUI();
    }

    // ═══════════════════════════════════════════════════════════════
    //  "更多"页 → 子页面导航
    // ═══════════════════════════════════════════════════════════════

    private void OnMoreDiagnosticClick(object? s, RoutedEventArgs e) => NavigateToSubPage(TabDiagnostic, "书源诊断");
    private void OnMoreRulesClick(object? s, RoutedEventArgs e) => NavigateToSubPage(TabRules, "规则管理");
    private void OnMoreSettingsClick(object? s, RoutedEventArgs e) => NavigateToSubPage(TabSettings, "设置");
    private void OnMoreAboutClick(object? s, RoutedEventArgs e) => NavigateToSubPage(TabAbout, "关于");
    private void OnMoreLogClick(object? s, RoutedEventArgs e) => NavigateToSubPage(TabLog, "诊断日志");

    private void NavigateToSubPage(int vmTabIndex, string title)
    {
        MorePanel.IsVisible = false;
        _isMorePanelActive = false;
        SetVmTabIndex(vmTabIndex);
        SubPageTitle.Text = title;
        UpdateNavigationUI();
    }

    // ═══════════════════════════════════════════════════════════════
    //  返回按钮
    // ═══════════════════════════════════════════════════════════════

    private void OnBackButtonClick(object? s, RoutedEventArgs e)
    {
        _ = HandleSystemBackNavigation();
    }

    /// <summary>
    /// 供 Android 系统返回手势/返回键调用的应用内回退逻辑。
    /// true=已消费返回；false=交给系统默认行为（如退出）。
    /// </summary>
    public bool HandleSystemBackNavigation()
    {
        if (DataContext is not MainWindowViewModel vm)
            return false;

        var currentTab = vm.SelectedTabIndex;
        var reader = vm.Reader;

        // 阅读页优先关闭临时层，再回到书架
        if (currentTab == TabReader)
        {
            if (ReaderSettingsPanel.IsVisible)
            {
                ReaderSettingsPanel.IsVisible = false;
                return true;
            }

            if (reader.IsTocOverlayVisible && reader.ToggleTocOverlayCommand.CanExecute(null))
            {
                reader.ToggleTocOverlayCommand.Execute(null);
                return true;
            }

            if (_isReaderToolbarVisible)
            {
                SetReaderToolbar(false);
                return true;
            }

            SetVmTabIndex(TabBookshelf);
            _activeBottomTab = 2;
            UpdateNavigationUI();
            return true;
        }

        // 子页面返回到“更多”跳板页
        if (currentTab is TabDiagnostic or TabRules or TabSettings or TabAbout or TabLog)
        {
            _activeBottomTab = 3;
            _isMorePanelActive = true;
            MorePanel.IsVisible = true;
            UpdateNavigationUI();
            return true;
        }

        // 从“更多”跳板页返回到搜索首页
        if (_isMorePanelActive || _activeBottomTab == 3)
        {
            NavigateToBottomTab(0);
            return true;
        }

        // 从任务/书架返回到搜索首页
        if (_activeBottomTab is 1 or 2)
        {
            NavigateToBottomTab(0);
            return true;
        }

        // 搜索首页不消费，交给系统（退出）
        return false;
    }

    // ═══════════════════════════════════════════════════════════════
    //  UI 状态刷新（顶部栏、底部栏、导航按钮高亮）
    // ═══════════════════════════════════════════════════════════════

    private void UpdateNavigationUI()
    {
        var tabIndex = (DataContext as MainWindowViewModel)?.SelectedTabIndex ?? 0;
        var isReader = tabIndex == TabReader;
        var isSubPage = !_isMorePanelActive && tabIndex is TabDiagnostic or TabRules or TabSettings or TabAbout or TabLog;

        // 顶部栏："更多"跳板页使用主 Header；阅读器完全全屏，不显示 SubPageHeader
        PrimaryHeader.IsVisible = !isReader && !isSubPage && (_isMorePanelActive || _activeBottomTab != 3);
        SubPageHeader.IsVisible = isSubPage;

        // "更多"跳板页也显示主 Header
        if (_isMorePanelActive)
        {
            PrimaryHeader.IsVisible = true;
            SubPageHeader.IsVisible = false;
        }

        // 底部导航栏
        BottomNavBar.IsVisible = !isReader;

        // 导航按钮高亮
        UpdateNavButtonActive(NavSearch, _activeBottomTab == 0);
        UpdateNavButtonActive(NavTasks, _activeBottomTab == 1);
        UpdateNavButtonActive(NavBookshelf, _activeBottomTab == 2);
        UpdateNavButtonActive(NavMore, _activeBottomTab == 3);

        // 阅读器模式下重置工具栏
        if (!isReader)
            SetReaderToolbar(false);

        if (DataContext is MainWindowViewModel vm)
        {
            AndroidSystemUiBridge.ApplyReaderCutoutMode(isReader && vm.Reader.ReaderExtendIntoCutout);
        }
    }

    private static void UpdateNavButtonActive(Button btn, bool isActive)
    {
        if (isActive)
        {
            if (!btn.Classes.Contains("active"))
                btn.Classes.Add("active");
        }
        else
        {
            btn.Classes.Remove("active");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  阅读器交互：中央点击切换工具栏
    // ═══════════════════════════════════════════════════════════════

    private void OnReaderContentTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Grid panel) return;

        // PointerReleased 已处理过同一交互（例如滑动/点击），避免重复触发。
        if (_pointerInteractionHandled)
        {
            _pointerInteractionHandled = false;
            return;
        }

        var pos = e.GetPosition(panel);
        HandleReaderTapAtPosition(panel, pos);
    }

    /// <summary>视口尺寸变化时通知 ViewModel 重新计算分页。</summary>
    private void OnReaderContentSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var reader = (DataContext as MainWindowViewModel)?.Reader;
        reader?.UpdateViewportSize(e.NewSize.Width, e.NewSize.Height);
    }

    private void SetReaderToolbar(bool visible)
    {
        _isReaderToolbarVisible = visible;
        ReaderTopToolbar.IsVisible = visible;
        ReaderBottomToolbar.IsVisible = visible;

        // 显示工具栏时关闭设置面板
        if (!visible)
            ReaderSettingsPanel.IsVisible = false;
    }

    // ═══════════════════════════════════════════════════════════════
    //  阅读器交互：左右滑动翻章
    // ═══════════════════════════════════════════════════════════════

    private void OnReaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _swipeStartPoint = e.GetPosition(sender as Visual);
    }

    private void OnReaderPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_swipeStartPoint is not { } start) return;

        _pointerInteractionHandled = false;
        var visual = sender as Visual;
        var end = e.GetPosition(visual);
        var deltaX = end.X - start.X;
        var deltaY = end.Y - start.Y;
        _swipeStartPoint = null;

        // 水平滑动阈值：>100px 且水平分量 > 垂直分量的 2 倍
        if (Math.Abs(deltaX) > 100 && Math.Abs(deltaX) > Math.Abs(deltaY) * 2)
        {
            var reader = (DataContext as MainWindowViewModel)?.Reader;
            if (reader is null) return;

            if (deltaX > 0) // 右滑 → 上一页
            {
                if (reader.PreviousPageCommand.CanExecute(null))
                    reader.PreviousPageCommand.Execute(null);
            }
            else // 左滑 → 下一页
            {
                if (reader.NextPageCommand.CanExecute(null))
                    reader.NextPageCommand.Execute(null);
            }

            _pointerInteractionHandled = true;

            return;
        }

        // 非滑动：按点击热区处理，提升真机触发一致性。
        if (Math.Abs(deltaX) <= 16 && Math.Abs(deltaY) <= 16 && sender is Grid panel)
        {
            HandleReaderTapAtPosition(panel, end);
            _pointerInteractionHandled = true;
        }
    }

    private void HandleReaderTapAtPosition(Grid panel, Point pos)
    {
        var width = panel.Bounds.Width;
        if (width <= 0) return;

        var ratio = pos.X / width;
        var reader = (DataContext as MainWindowViewModel)?.Reader;
        if (reader is null) return;

#if DEBUG
        Trace.WriteLine($"[ReadStorm] Reader tap x={pos.X:F1}/{width:F1}, ratio={ratio:F3}");
#endif

        // 以内容容器可见宽度划分三分区。
        if (ratio < 1.0 / 3.0)
        {
            if (reader.PreviousPageCommand.CanExecute(null))
                reader.PreviousPageCommand.Execute(null);
            return;
        }

        if (ratio > 2.0 / 3.0)
        {
            if (reader.NextPageCommand.CanExecute(null))
                reader.NextPageCommand.Execute(null);
            return;
        }

        _isReaderToolbarVisible = !_isReaderToolbarVisible;
        SetReaderToolbar(_isReaderToolbarVisible);
    }

    // ═══════════════════════════════════════════════════════════════
    //  阅读器：工具栏按钮事件
    // ═══════════════════════════════════════════════════════════════

    private void OnReaderBackClick(object? s, RoutedEventArgs e)
    {
        SetVmTabIndex(TabBookshelf);
        _activeBottomTab = 2;
        UpdateNavigationUI();
    }

    private void OnReaderSettingsClick(object? s, RoutedEventArgs e)
    {
        ReaderSettingsPanel.IsVisible = !ReaderSettingsPanel.IsVisible;
    }

    private void OnCloseReaderSettingsClick(object? s, RoutedEventArgs e)
    {
        ReaderSettingsPanel.IsVisible = false;
    }

    private void OnFontSizeDecClick(object? s, RoutedEventArgs e)
    {
        var reader = (DataContext as MainWindowViewModel)?.Reader;
        if (reader is not null && reader.ReaderFontSize > 14)
            reader.ReaderFontSize -= 2;
    }

    private void OnFontSizeIncClick(object? s, RoutedEventArgs e)
    {
        var reader = (DataContext as MainWindowViewModel)?.Reader;
        if (reader is not null && reader.ReaderFontSize < 32)
            reader.ReaderFontSize += 2;
    }

    private void OnLineHeightDecClick(object? s, RoutedEventArgs e)
    {
        var reader = (DataContext as MainWindowViewModel)?.Reader;
        if (reader is not null && reader.ReaderLineHeight > 20)
            reader.ReaderLineHeight -= 4;
    }

    private void OnLineHeightIncClick(object? s, RoutedEventArgs e)
    {
        var reader = (DataContext as MainWindowViewModel)?.Reader;
        if (reader is not null && reader.ReaderLineHeight < 56)
            reader.ReaderLineHeight += 4;
    }

    // ═══════════════════════════════════════════════════════════════
    //  目录面板：点击遮罩关闭
    // ═══════════════════════════════════════════════════════════════

    private void OnTocBackdropTapped(object? s, TappedEventArgs e)
    {
        var reader = (DataContext as MainWindowViewModel)?.Reader;
        if (reader is not null && reader.ToggleTocOverlayCommand.CanExecute(null))
            reader.ToggleTocOverlayCommand.Execute(null);
    }

    // ═══════════════════════════════════════════════════════════════
    //  书架：单击打开阅读
    // ═══════════════════════════════════════════════════════════════

    private void OnBookshelfItemTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: BookEntity book }
            && DataContext is MainWindowViewModel vm)
        {
            if (vm.Bookshelf.OpenDbBookCommand.CanExecute(book))
                vm.Bookshelf.OpenDbBookCommand.Execute(book);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  书架：长按上下文菜单操作
    // ═══════════════════════════════════════════════════════════════

    private void OnBookReadClick(object? s, RoutedEventArgs e)
        => ExecuteBookCommand(s, vm => vm.OpenDbBookCommand);
    private void OnBookResumeClick(object? s, RoutedEventArgs e)
        => ExecuteBookCommand(s, vm => vm.ResumeBookDownloadCommand);
    private void OnBookCheckUpdateClick(object? s, RoutedEventArgs e)
        => ExecuteBookCommand(s, vm => vm.CheckNewChaptersCommand);
    private void OnBookExportClick(object? s, RoutedEventArgs e)
        => ExecuteBookCommand(s, vm => vm.ExportDbBookCommand);
    private void OnBookRefreshCoverClick(object? s, RoutedEventArgs e)
        => ExecuteBookCommand(s, vm => vm.RefreshCoverCommand);
    private void OnBookDeleteClick(object? s, RoutedEventArgs e)
        => ExecuteBookCommand(s, vm => vm.RemoveDbBookCommand);

    private void ExecuteBookCommand(object? sender,
        Func<BookshelfViewModel, System.Windows.Input.ICommand> commandSelector)
    {
        if (sender is MenuItem { DataContext: BookEntity book }
            && DataContext is MainWindowViewModel vm)
        {
            var command = commandSelector(vm.Bookshelf);
            if (command.CanExecute(book))
                command.Execute(book);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  工具方法
    // ═══════════════════════════════════════════════════════════════

    private void SetVmTabIndex(int index)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.SelectedTabIndex = index;
    }
}
