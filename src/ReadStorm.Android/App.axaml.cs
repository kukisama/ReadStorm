using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
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
    }

    public override void OnFrameworkInitializationCompleted()
    {
        DisableAvaloniaDataAnnotationValidation();

        _serviceProvider = ConfigureServices();

        var mainViewModel = _serviceProvider.GetRequiredService<MainWindowViewModel>();

        if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            singleView.MainView = new MainView { DataContext = mainViewModel };
        }

        base.OnFrameworkInitializationCompleted();
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
