using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml.Styling;
using System.Linq;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using ReadStorm.Application.Abstractions;
using ReadStorm.Infrastructure.Services;
using ReadStorm.Desktop.ViewModels;
using ReadStorm.Desktop.Views;
using ReadStorm.Android.ViewModels;

namespace ReadStorm.Desktop;

public partial class App : Avalonia.Application
{
    private ServiceProvider? _serviceProvider;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

#if DEBUG
        // Debug 环境加载保守字重样式，优先保证模拟器中文字稳定显示。
        var debugStyleUri = new Uri("avares://ReadStorm.Android/Styles/DebugTypography.axaml");
        Styles.Add(new StyleInclude(debugStyleUri) { Source = debugStyleUri });
#endif
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // 全局未观察异步异常捕获，避免 Android 闪退
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            System.Diagnostics.Trace.WriteLine($"[ReadStorm] UnobservedTaskException: {e.Exception}");
            e.SetObserved();
        };

        DisableAvaloniaDataAnnotationValidation();

        // 将日志目录指向外部存储 Documents/ReadStorm/logs，文件管理器可直接访问
        SetupExternalLogDirectory();

        // Android 特有：将打包在 APK Assets 中的规则 JSON 释放到文件系统，
        // 以便 RulePathResolver 通过 System.IO 正常加载。
        DeployBundledRules();

        try
        {
            _serviceProvider = ConfigureServices();

            var mainViewModel = _serviceProvider.GetRequiredService<MainWindowViewModel>();
            var logViewModel = _serviceProvider.GetRequiredService<LogViewModel>();

            // 启动时记录关键环境信息
            logViewModel.Append($"[{DateTimeOffset.Now:HH:mm:ss.fff}] === ReadStorm Android 启动 ===");
            logViewModel.Append($"[env] OS={System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
            logViewModel.Append($"[env] Runtime={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
            logViewModel.Append($"[env] AppData={Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}");
            logViewModel.Append($"[env] LocalAppData={Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}");
            logViewModel.Append($"[env] MyDocuments={Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}");
            logViewModel.Append($"[env] Personal={Environment.GetFolderPath(Environment.SpecialFolder.Personal)}");
            logViewModel.Append($"[env] BaseDirectory={AppContext.BaseDirectory}");
            logViewModel.Append($"[env] WorkDir={WorkDirectoryManager.GetDefaultWorkDirectory()}");
            logViewModel.Append($"[env] ExternalLogDir={WorkDirectoryManager.ExternalLogDirectoryOverride ?? "(未设置)"}");
            logViewModel.Append($"[env] SettingsFile={WorkDirectoryManager.GetSettingsFilePath()}");
            logViewModel.Append($"[env] DbPath={WorkDirectoryManager.GetDatabasePath(WorkDirectoryManager.GetDefaultWorkDirectory())}");
            logViewModel.Append($"[env] FileMgr可见日志={WorkDirectoryManager.GetLogsDirectory(WorkDirectoryManager.GetDefaultWorkDirectory())}");

            if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
            {
                var mainView = new MainView { DataContext = mainViewModel };
                // 将 LogViewModel 作为资源注入，供 AXAML 中的日志 Tab 绑定
                mainView.Resources["LogViewModel"] = logViewModel;
                global::ReadStorm.Android.AndroidSystemUiBridge.RegisterMainView(mainView);
                global::ReadStorm.Android.AndroidSystemUiBridge.ApplyReaderCutoutMode(false);
                singleView.MainView = mainView;
            }

            logViewModel.Append($"[{DateTimeOffset.Now:HH:mm:ss.fff}] 启动完成，UI 已加载");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[ReadStorm] Startup failed: {ex}");
            // 显示一个最小化的错误页面，而不是直接闪退
            if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
            {
                singleView.MainView = new Avalonia.Controls.TextBlock
                {
                    Text = $"启动失败：{ex.Message}\n\n{ex.StackTrace}",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Margin = new Avalonia.Thickness(16),
                    FontSize = 14,
                };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// 将日志目录指向外部存储，优先使用应用专属外部目录（无需权限，USB 可访问），
    /// API 28 及以下尝试使用公共 Documents 目录。
    /// </summary>
    internal static void SetupExternalLogDirectory()
    {
        try
        {
            var context = global::Android.App.Application.Context;

            // API 29+（Scoped Storage）：直接使用应用专属外部目录，无需任何权限
            // 路径形如 /storage/emulated/0/Android/data/com.readstorm.app/files/Documents/
            // 用户可通过 USB 连接电脑访问
            if (OperatingSystem.IsAndroidVersionAtLeast(29))
            {
                var externalFilesDir = context.GetExternalFilesDir(
                    global::Android.OS.Environment.DirectoryDocuments)?.AbsolutePath;

                if (!string.IsNullOrEmpty(externalFilesDir))
                {
                    var logDir = Path.Combine(externalFilesDir, "ReadStorm", "logs");
                    Directory.CreateDirectory(logDir);
                    WorkDirectoryManager.ExternalLogDirectoryOverride = logDir;
                    return;
                }
            }

            // API 28 及以下：尝试公共 Documents 目录（需 WRITE_EXTERNAL_STORAGE 权限）
            var documentsDir = global::Android.OS.Environment
                .GetExternalStoragePublicDirectory(global::Android.OS.Environment.DirectoryDocuments)?
                .AbsolutePath;

            if (!string.IsNullOrEmpty(documentsDir))
            {
                var logDir = Path.Combine(documentsDir, "ReadStorm", "logs");
                try
                {
                    Directory.CreateDirectory(logDir);
                    WorkDirectoryManager.ExternalLogDirectoryOverride = logDir;
                    return;
                }
                catch
                {
                    // 权限不足，继续回退
                }
            }

            // 最终回退：应用专属外部目录
            {
                var externalFilesDir = context.GetExternalFilesDir(
                    global::Android.OS.Environment.DirectoryDocuments)?.AbsolutePath;

                if (!string.IsNullOrEmpty(externalFilesDir))
                {
                    var logDir = Path.Combine(externalFilesDir, "ReadStorm", "logs");
                    Directory.CreateDirectory(logDir);
                    WorkDirectoryManager.ExternalLogDirectoryOverride = logDir;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[ReadStorm] SetupExternalLogDirectory failed: {ex.Message}");
            // 回退到默认内部日志目录，不阻塞启动
        }
    }

    /// <summary>
    /// 将嵌入在程序集中的规则 JSON（EmbeddedResource）释放到 %APPDATA%/ReadStorm/rules/。
    /// 仅当目标目录中不存在对应文件时释放（用户修改不被覆盖）。
    /// LogicalName 格式：BundledRules.rule-{N}.json
    /// </summary>
    private static void DeployBundledRules()
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrEmpty(appData))
                appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(appData))
                appData = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
            if (string.IsNullOrEmpty(appData))
                appData = AppContext.BaseDirectory;

            var userRulesDir = Path.Combine(appData, "ReadStorm", "rules");
            Directory.CreateDirectory(userRulesDir);

            var assembly = typeof(App).Assembly;
            const string prefix = "BundledRules.";

            foreach (var resName in assembly.GetManifestResourceNames())
            {
                if (!resName.StartsWith(prefix, StringComparison.Ordinal))
                    continue;
                if (!resName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    continue;

                // LogicalName = "BundledRules.rule-2.json" → fileName = "rule-2.json"
                var fileName = resName.Substring(prefix.Length);

                var destPath = Path.Combine(userRulesDir, fileName);
                if (File.Exists(destPath))
                    continue; // 不覆盖用户已编辑的规则

                using var input = assembly.GetManifestResourceStream(resName);
                if (input is null) continue;
                using var output = File.Create(destPath);
                input.CopyTo(output);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[ReadStorm] DeployBundledRules failed: {ex.Message}");
        }
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Android 专用实时日志
        services.AddSingleton<LogViewModel>();
        services.AddSingleton<ILiveDiagnosticSink>(sp => sp.GetRequiredService<LogViewModel>());

        services.AddSingleton<ISearchBooksUseCase, HybridSearchBooksUseCase>();
        services.AddSingleton<CoverService>();
        services.AddSingleton<ICoverUseCase>(sp => sp.GetRequiredService<CoverService>());
        services.AddSingleton<IDownloadBookUseCase>(sp =>
            new RuleBasedDownloadBookUseCase(
                sp.GetRequiredService<IAppSettingsUseCase>(),
                sp.GetRequiredService<IBookRepository>(),
                sp.GetRequiredService<CoverService>(),
                sp.GetService<ISearchBooksUseCase>(),
                liveSink: sp.GetService<ILiveDiagnosticSink>()));
        services.AddSingleton<IAppSettingsUseCase, JsonFileAppSettingsUseCase>();
        services.AddSingleton<IRuleCatalogUseCase, EmbeddedRuleCatalogUseCase>();
        services.AddSingleton<ISourceDiagnosticUseCase, RuleBasedSourceDiagnosticUseCase>();
        services.AddSingleton<ISourceHealthCheckUseCase, FastSourceHealthCheckUseCase>();
        services.AddSingleton<IBookshelfUseCase, JsonFileBookshelfUseCase>();
        services.AddSingleton<IBookRepository, SqliteBookRepository>();
        services.AddSingleton<IRuleEditorUseCase, FileBasedRuleEditorUseCase>();

        services.AddTransient<MainWindowViewModel>();

        return services.BuildServiceProvider();
    }
}
