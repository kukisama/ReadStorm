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
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();

            _serviceProvider = ConfigureServices();

            desktop.MainWindow = new MainWindow
            {
                DataContext = _serviceProvider.GetRequiredService<MainWindowViewModel>(),
            };

            desktop.Exit += (_, _) => _serviceProvider?.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<ISearchBooksUseCase, HybridSearchBooksUseCase>();
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