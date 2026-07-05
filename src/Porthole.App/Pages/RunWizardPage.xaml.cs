using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Porthole.Core.ViewModels;

namespace Porthole_App.Pages;

public sealed partial class RunWizardPage : Page
{
    public RunWizardViewModel ViewModel { get; }

    public RunWizardPage()
    {
        ViewModel = (RunWizardViewModel)App.Services.GetService(typeof(RunWizardViewModel))!;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadImagesAsync();
    }

    private void RemovePortMappingButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el && el.Tag is string mapping)
        {
            ViewModel.RemovePortMappingCommand.Execute(mapping);
        }
    }

    private void RemoveEnvVarButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el && el.Tag is string envVar)
        {
            ViewModel.RemoveEnvironmentVariableCommand.Execute(envVar);
        }
    }

    private void RemoveVolumeMountButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el && el.Tag is string volume)
        {
            ViewModel.RemoveVolumeMountCommand.Execute(volume);
        }
    }

    private void ResetWizardButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.Reset();
    }

    private Visibility GetStepVisibility(bool isActive) =>
        isActive ? Visibility.Visible : Visibility.Collapsed;

    private Visibility GetVisibleWhenNonEmpty(string text) =>
        string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;

    private Visibility GetVisibleWhenLoading(bool isLoading) =>
        isLoading ? Visibility.Visible : Visibility.Collapsed;

    private Visibility GetVisibleWhenCreating(bool isCreating) =>
        isCreating ? Visibility.Visible : Visibility.Collapsed;

    private Visibility GetSuccessVisibility(bool succeeded) =>
        succeeded ? Visibility.Visible : Visibility.Collapsed;

    private Visibility GetHideOnCreateStep(bool isCreateStep) =>
        isCreateStep ? Visibility.Collapsed : Visibility.Visible;

    private bool GetCreateEnabled(bool isCreating, bool createSucceeded) =>
        !isCreating && !createSucceeded;
}
