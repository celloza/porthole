using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Porthole.Core.Models;
using Porthole.Core.ViewModels;
using Windows.Storage;
using Windows.Storage.Pickers;

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
        ViewModel.Reset();
    }

    private void CreateNewButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.StartNewConfiguration();
    }

    private async void LoadTemplateButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".json");
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;

            if (!TryInitializePickerWithMainWindow(picker))
            {
                ViewModel.StatusMessage = "Unable to open file picker right now.";
                return;
            }

            StorageFile? file = await picker.PickSingleFileAsync();
            if (file is null)
            {
                ViewModel.StatusMessage = "Template load canceled.";
                return;
            }

            string json = await FileIO.ReadTextAsync(file);
            var template = JsonSerializer.Deserialize<ContainerConfig>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                PropertyNameCaseInsensitive = true,
            });

            if (template is null || string.IsNullOrWhiteSpace(template.Name) || string.IsNullOrWhiteSpace(template.ImageReference))
            {
                ViewModel.StatusMessage = "Template file is invalid. It must include at least 'name' and 'imageReference'.";
                return;
            }

            ViewModel.ApplyTemplate(template);
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"Failed to load template: {ex.Message}";
        }
    }

    private async void SaveAndRunSplitButton_Click(SplitButton sender, SplitButtonClickEventArgs args)
    {
        await SaveTemplateAndRunAsync();
    }

    private async void RunWithoutSavingMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.CreateContainerCommand.ExecuteAsync(null);
    }

    private async Task SaveTemplateAndRunAsync()
    {
        if (!ViewModel.TryBuildContainerConfig(out ContainerConfig? config, out string validationMessage) || config is null)
        {
            ViewModel.StatusMessage = $"Cannot save template: {validationMessage}";
            return;
        }

        try
        {
            var picker = new FileSavePicker();
            picker.FileTypeChoices.Add("Container Template", [".json"]);
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.SuggestedFileName = string.IsNullOrWhiteSpace(config.Name)
                ? "container-template"
                : config.Name;

            if (!TryInitializePickerWithMainWindow(picker))
            {
                ViewModel.StatusMessage = "Unable to open file save picker right now.";
                return;
            }

            StorageFile? file = await picker.PickSaveFileAsync();
            if (file is null)
            {
                ViewModel.StatusMessage = "Save canceled.";
                return;
            }

            string json = JsonSerializer.Serialize(config, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true,
            });
            await FileIO.WriteTextAsync(file, json);

            ViewModel.StatusMessage = $"Template saved to {file.Name}. Running container...";
            await ViewModel.CreateContainerCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"Failed to save template: {ex.Message}";
        }
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

    private Visibility GetNextButtonVisibility(bool isTemplateChoiceStep, bool isCreateStep) =>
        isTemplateChoiceStep || isCreateStep ? Visibility.Collapsed : Visibility.Visible;

    private bool GetRunActionsEnabled(bool isCreating, bool createSucceeded) =>
        !isCreating && !createSucceeded;

    private static bool TryInitializePickerWithMainWindow(object picker)
    {
        if (App.AppWindow is null)
        {
            return false;
        }

        nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.AppWindow);
        if (hwnd == nint.Zero)
        {
            return false;
        }

        if (picker is FileOpenPicker openPicker)
        {
            WinRT.Interop.InitializeWithWindow.Initialize(openPicker, hwnd);
            return true;
        }

        if (picker is FileSavePicker savePicker)
        {
            WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hwnd);
            return true;
        }

        return false;
    }
}
