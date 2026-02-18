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
        DisableAvaloniaDataAnnotationValidation();

        // Android 特有：将打包在 APK Assets 中的规则 JSON 释放到文件系统，
        // 以便 RulePathResolver 通过 System.IO 正常加载。
        DeployBundledRules();

        _serviceProvider = ConfigureServices();

        var mainViewModel = _serviceProvider.GetRequiredService<MainWindowViewModel>();

        if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            singleView.MainView = new MainView { DataContext = mainViewModel };
        }

        base.OnFrameworkInitializationCompleted();
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
            var userRulesDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ReadStorm", "rules");
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

        services.AddSingleton<ISearchBooksUseCase, HybridSearchBooksUseCase>();
        services.AddSingleton<CoverService>();
        services.AddSingleton<ICoverUseCase>(sp => sp.GetRequiredService<CoverService>());
        services.AddSingleton<IDownloadBookUseCase, RuleBasedDownloadBookUseCase>();
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
