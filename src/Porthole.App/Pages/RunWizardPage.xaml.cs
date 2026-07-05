using System.Text.Json;
using System.Globalization;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Porthole.Core.Models;
using Porthole.Core.ViewModels;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Porthole_App.Pages;

public sealed partial class RunWizardPage : Page
{
    private const int CurrentTemplateVersion = 2;

    private static readonly JsonSerializerOptions TemplateJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

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
            if (!TryParseTemplate(json, out ContainerConfig? template, out int? templateVersion, out string parseError)
                || template is null)
            {
                ViewModel.StatusMessage = parseError;
                return;
            }

            ViewModel.ApplyTemplate(template);
            ViewModel.StatusMessage = templateVersion.HasValue
                ? $"Loaded template v{templateVersion.Value} from '{file.Name}'."
                : $"Loaded legacy template from '{file.Name}'.";
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
            picker.SuggestedFileName = BuildTemplateFileName(config.ImageReference);

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

            var templateEnvelope = new TemplateEnvelopeV2(
                CurrentTemplateVersion,
                config,
                DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));

            string json = JsonSerializer.Serialize(templateEnvelope, TemplateJsonOptions);
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

    private static string BuildTemplateFileName(string imageReference)
    {
        string imageName = string.IsNullOrWhiteSpace(imageReference)
            ? "image"
            : imageReference.Trim();

        int digestSeparator = imageName.IndexOf('@');
        if (digestSeparator > 0)
        {
            imageName = imageName[..digestSeparator];
        }

        int tagSeparator = imageName.LastIndexOf(':');
        if (tagSeparator > 0 && tagSeparator > imageName.LastIndexOf('/'))
        {
            imageName = imageName[..tagSeparator];
        }

        int repositorySeparator = imageName.LastIndexOf('/');
        if (repositorySeparator >= 0 && repositorySeparator < imageName.Length - 1)
        {
            imageName = imageName[(repositorySeparator + 1)..];
        }

        string safeImageName = SanitizeFileNamePart(imageName);
        string timestamp = DateTimeOffset.UtcNow.ToString("ddMMyyHHmmss", CultureInfo.InvariantCulture);
        return $"porthole-{safeImageName}-{timestamp}.json";
    }

    private static string SanitizeFileNamePart(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return "image";
        }

        var sb = new StringBuilder(input.Length);
        bool previousWasDash = false;

        foreach (char ch in input.ToLowerInvariant())
        {
            bool isValid = char.IsLetterOrDigit(ch) || ch == '-';
            if (isValid)
            {
                sb.Append(ch);
                previousWasDash = false;
            }
            else if (!previousWasDash)
            {
                sb.Append('-');
                previousWasDash = true;
            }
        }

        string result = sb.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(result) ? "image" : result;
    }

    private static bool TryParseTemplate(string json, out ContainerConfig? config, out int? version, out string error)
    {
        config = null;
        version = null;
        error = string.Empty;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "Template file is invalid. Root JSON value must be an object.";
                return false;
            }

            if (!TryGetTemplateVersion(root, out int parsedVersion))
            {
                // Legacy unversioned template format: raw ContainerConfig fields at root.
                config = JsonSerializer.Deserialize<ContainerConfig>(root.GetRawText(), TemplateJsonOptions);
                if (!IsValidTemplateConfig(config))
                {
                    error = "Legacy template is invalid. It must include at least 'name' and 'imageReference'.";
                    return false;
                }

                return true;
            }

            version = parsedVersion;

            // Versioned envelopes can store the config under container or config.
            JsonElement configElement;
            if (root.TryGetProperty("container", out JsonElement container))
            {
                configElement = container;
            }
            else if (root.TryGetProperty("config", out JsonElement configNode))
            {
                configElement = configNode;
            }
            else
            {
                // Compatibility fallback for early versioned documents that inlined fields.
                configElement = root;
            }

            switch (parsedVersion)
            {
                case 1:
                case 2:
                    config = JsonSerializer.Deserialize<ContainerConfig>(configElement.GetRawText(), TemplateJsonOptions);
                    if (!IsValidTemplateConfig(config))
                    {
                        error = $"Template v{parsedVersion} is invalid. It must include at least 'name' and 'imageReference'.";
                        return false;
                    }

                    return true;
                default:
                    error = $"Template version {parsedVersion} is not supported by this build.";
                    return false;
            }
        }
        catch (JsonException ex)
        {
            error = $"Template JSON is invalid: {ex.Message}";
            return false;
        }
    }

    private static bool TryGetTemplateVersion(JsonElement root, out int version)
    {
        version = 0;
        if (!root.TryGetProperty("version", out JsonElement versionNode))
        {
            return false;
        }

        if (versionNode.ValueKind == JsonValueKind.Number)
        {
            return versionNode.TryGetInt32(out version);
        }

        if (versionNode.ValueKind == JsonValueKind.String
            && int.TryParse(versionNode.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            version = parsed;
            return true;
        }

        return false;
    }

    private static bool IsValidTemplateConfig(ContainerConfig? config) =>
        config is not null
        && !string.IsNullOrWhiteSpace(config.Name)
        && !string.IsNullOrWhiteSpace(config.ImageReference);

    private sealed record TemplateEnvelopeV2(int Version, ContainerConfig Container, string SavedAtUtc);
}
