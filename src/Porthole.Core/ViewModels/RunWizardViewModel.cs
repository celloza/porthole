using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Porthole.Core.Models;
using Porthole.Core.Services;

namespace Porthole.Core.ViewModels;

public partial class RunWizardViewModel : ObservableObject
{
    private static readonly Regex ContainerNameRegex = new(@"^[a-zA-Z0-9][a-zA-Z0-9_-]*$", RegexOptions.Compiled);
    private static readonly Regex PortMappingRegex = new(@"^\d+:\d+(/(tcp|udp))?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex EnvVarRegex = new(@"^[^=]+=.*$", RegexOptions.Compiled);
    // Allow either a regular source path/name or a Windows drive source like C:\data.
    private static readonly Regex VolumeMountRegex = new(@"^((?:[a-zA-Z]:[\\/][^:]+)|(?:[^:]+)):[^:]+(:[^:]+)?$", RegexOptions.Compiled);

    private readonly IImageCatalogService _imageCatalogService;
    private readonly IContainerCatalogService _containerCatalogService;

    // Step tracking
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStepOneActive))]
    [NotifyPropertyChangedFor(nameof(IsStepTwoActive))]
    [NotifyPropertyChangedFor(nameof(IsStepThreeActive))]
    [NotifyPropertyChangedFor(nameof(IsTemplateChoiceStep))]
    [NotifyPropertyChangedFor(nameof(StepTitle))]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyPropertyChangedFor(nameof(CanGoPrevious))]
    [NotifyPropertyChangedFor(nameof(IsCreateStep))]
    private int currentStep;

    // Step 1: Basic Settings
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyPropertyChangedFor(nameof(ContainerNameValidation))]
    private string containerName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyPropertyChangedFor(nameof(SelectedImageDisplayName))]
    private ImageSummary? selectedImage;

    [ObservableProperty]
    private string startupCommand = string.Empty;

    // Step 2: Advanced Settings
    [ObservableProperty]
    private string newPortMapping = string.Empty;

    [ObservableProperty]
    private string newEnvironmentVariable = string.Empty;

    [ObservableProperty]
    private string newVolumeMount = string.Empty;

    [ObservableProperty]
    private string portMappingValidation = string.Empty;

    [ObservableProperty]
    private string envVarValidation = string.Empty;

    [ObservableProperty]
    private string volumeMountValidation = string.Empty;

    // Status
    [ObservableProperty]
    private string statusMessage = "Choose to start from a template file or create a new configuration.";

    [ObservableProperty]
    private bool isCreating;

    [ObservableProperty]
    private bool isLoadingImages;

    [ObservableProperty]
    private bool createSucceeded;

    [ObservableProperty]
    private string createdContainerId = string.Empty;

    public ObservableCollection<ImageSummary> AvailableImages { get; } = [];
    public ObservableCollection<string> PortMappings { get; } = [];
    public ObservableCollection<string> EnvironmentVariables { get; } = [];
    public ObservableCollection<string> VolumeMounts { get; } = [];

    public RunWizardViewModel(IImageCatalogService imageCatalogService, IContainerCatalogService containerCatalogService)
    {
        _imageCatalogService = imageCatalogService;
        _containerCatalogService = containerCatalogService;
    }

    public bool IsStepOneActive => CurrentStep == 1;
    public bool IsStepTwoActive => CurrentStep == 2;
    public bool IsStepThreeActive => CurrentStep == 3;
    public bool IsTemplateChoiceStep => CurrentStep == 0;
    public bool IsCreateStep => CurrentStep == 3;

    public string StepTitle => CurrentStep switch
    {
        0 => "Start — Choose Template",
        1 => "Step 1 of 3 — Basic Settings",
        2 => "Step 2 of 3 — Advanced Settings",
        3 => "Step 3 of 3 — Review & Create",
        _ => string.Empty,
    };

    public bool CanGoPrevious => CurrentStep > 0 && !IsCreating;

    public bool CanGoNext => CurrentStep < 3 && !IsCreating && IsCurrentStepValid();

    public string ContainerNameValidation
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ContainerName))
            {
                return string.Empty;
            }

            return ContainerNameRegex.IsMatch(ContainerName.Trim())
                ? string.Empty
                : "Name must start with a letter or digit and contain only letters, digits, hyphens, or underscores.";
        }
    }

    public string SelectedImageDisplayName => SelectedImage?.DisplayName ?? "(none selected)";

    public string ReviewSummary
    {
        get
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Container name: {ContainerName.Trim()}");
            sb.AppendLine($"Image: {SelectedImage?.Reference ?? "(none)"}");

            if (!string.IsNullOrWhiteSpace(StartupCommand))
            {
                sb.AppendLine($"Command: {StartupCommand.Trim()}");
            }

            if (PortMappings.Count > 0)
            {
                sb.AppendLine($"Ports: {string.Join(", ", PortMappings)}");
            }

            if (EnvironmentVariables.Count > 0)
            {
                sb.AppendLine($"Environment: {string.Join(", ", EnvironmentVariables)}");
            }

            if (VolumeMounts.Count > 0)
            {
                sb.AppendLine($"Volumes: {string.Join(", ", VolumeMounts)}");
            }

            return sb.ToString().TrimEnd();
        }
    }

    public async Task LoadImagesAsync(CancellationToken cancellationToken = default)
    {
        IsLoadingImages = true;
        try
        {
            var images = await _imageCatalogService.ListImagesAsync(cancellationToken);
            AvailableImages.Clear();
            foreach (var image in images)
            {
                AvailableImages.Add(image);
            }

            if (SelectedImage is not null)
            {
                EnsureImageOptionExists(SelectedImage.Reference);
                SelectedImage = AvailableImages.FirstOrDefault(i => string.Equals(i.Reference, SelectedImage.Reference, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                SelectedImage = AvailableImages.FirstOrDefault();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load images: {ex.Message}";
        }
        finally
        {
            IsLoadingImages = false;
        }
    }

    public void Reset()
    {
        CurrentStep = 0;
        ContainerName = string.Empty;
        SelectedImage = AvailableImages.FirstOrDefault();
        StartupCommand = string.Empty;
        NewPortMapping = string.Empty;
        NewEnvironmentVariable = string.Empty;
        NewVolumeMount = string.Empty;
        PortMappingValidation = string.Empty;
        EnvVarValidation = string.Empty;
        VolumeMountValidation = string.Empty;
        PortMappings.Clear();
        EnvironmentVariables.Clear();
        VolumeMounts.Clear();
        StatusMessage = "Choose to start from a template file or create a new configuration.";
        IsCreating = false;
        CreateSucceeded = false;
        CreatedContainerId = string.Empty;
    }

    public void StartNewConfiguration()
    {
        CurrentStep = 1;
        StatusMessage = "Configure a new container using the steps below.";
    }

    public void ApplyTemplate(ContainerConfig config)
    {
        ContainerName = config.Name;
        StartupCommand = config.StartupCommand ?? string.Empty;

        PortMappings.Clear();
        if (config.PortMappings is not null)
        {
            foreach (string mapping in config.PortMappings.Where(m => !string.IsNullOrWhiteSpace(m)))
            {
                PortMappings.Add(mapping.Trim());
            }
        }

        EnvironmentVariables.Clear();
        if (config.EnvironmentVariables is not null)
        {
            foreach (string envVar in config.EnvironmentVariables.Where(v => !string.IsNullOrWhiteSpace(v)))
            {
                EnvironmentVariables.Add(envVar.Trim());
            }
        }

        VolumeMounts.Clear();
        if (config.VolumeMounts is not null)
        {
            foreach (string volume in config.VolumeMounts.Where(v => !string.IsNullOrWhiteSpace(v)))
            {
                VolumeMounts.Add(volume.Trim());
            }
        }

        PortMappingValidation = string.Empty;
        EnvVarValidation = string.Empty;
        VolumeMountValidation = string.Empty;
        CreateSucceeded = false;
        CreatedContainerId = string.Empty;

        EnsureImageOptionExists(config.ImageReference);
        SelectedImage = AvailableImages.FirstOrDefault(i => string.Equals(i.Reference, config.ImageReference, StringComparison.OrdinalIgnoreCase));

        CurrentStep = 1;
        StatusMessage = $"Template loaded from file. Ready to review and run '{ContainerName}'.";
        OnPropertyChanged(nameof(ReviewSummary));
    }

    public bool TryBuildContainerConfig(out ContainerConfig? config, out string validationMessage)
    {
        string name = ContainerName.Trim();
        string imageRef = SelectedImage?.Reference ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name) || !ContainerNameRegex.IsMatch(name))
        {
            validationMessage = "Container name is invalid. Use letters, digits, hyphens, or underscores.";
            config = null;
            return false;
        }

        if (string.IsNullOrWhiteSpace(imageRef))
        {
            validationMessage = "No image selected.";
            config = null;
            return false;
        }

        config = new ContainerConfig(
            name,
            imageRef,
            string.IsNullOrWhiteSpace(StartupCommand) ? null : StartupCommand.Trim(),
            PortMappings.Count > 0 ? [.. PortMappings] : null,
            EnvironmentVariables.Count > 0 ? [.. EnvironmentVariables] : null,
            VolumeMounts.Count > 0 ? [.. VolumeMounts] : null);

        validationMessage = string.Empty;
        return true;
    }

    [RelayCommand]
    private void GoNext()
    {
        if (CanGoNext)
        {
            CurrentStep++;
            OnPropertyChanged(nameof(ReviewSummary));
        }
    }

    [RelayCommand]
    private void GoPrevious()
    {
        if (CanGoPrevious)
        {
            CurrentStep--;
        }
    }

    [RelayCommand]
    private void AddPortMapping()
    {
        string mapping = NewPortMapping.Trim();
        if (string.IsNullOrWhiteSpace(mapping))
        {
            PortMappingValidation = "Enter a port mapping before adding it.";
            return;
        }

        if (!PortMappingRegex.IsMatch(mapping))
        {
            PortMappingValidation = "Invalid format. Use host:container or host:container/tcp (e.g., 8080:80 or 8080:80/tcp).";
            return;
        }

        if (PortMappings.Contains(mapping))
        {
            PortMappingValidation = "This port mapping is already in the list.";
            return;
        }

        PortMappings.Add(mapping);
        NewPortMapping = string.Empty;
        PortMappingValidation = string.Empty;
    }

    [RelayCommand]
    private void RemovePortMapping(string mapping)
    {
        PortMappings.Remove(mapping);
    }

    [RelayCommand]
    private void AddEnvironmentVariable()
    {
        string envVar = NewEnvironmentVariable.Trim();
        if (string.IsNullOrWhiteSpace(envVar))
        {
            EnvVarValidation = "Enter an environment variable before adding it.";
            return;
        }

        if (!EnvVarRegex.IsMatch(envVar))
        {
            EnvVarValidation = "Invalid format. Use KEY=value (e.g., MY_VAR=hello).";
            return;
        }

        if (EnvironmentVariables.Contains(envVar))
        {
            EnvVarValidation = "This environment variable is already in the list.";
            return;
        }

        EnvironmentVariables.Add(envVar);
        NewEnvironmentVariable = string.Empty;
        EnvVarValidation = string.Empty;
    }

    [RelayCommand]
    private void RemoveEnvironmentVariable(string envVar)
    {
        EnvironmentVariables.Remove(envVar);
    }

    [RelayCommand]
    private void AddVolumeMount()
    {
        string volume = NewVolumeMount.Trim();
        if (string.IsNullOrWhiteSpace(volume))
        {
            VolumeMountValidation = "Enter a volume mount before adding it.";
            return;
        }

        if (!VolumeMountRegex.IsMatch(volume))
        {
            VolumeMountValidation = "Invalid format. Use source:target or source:target:options (e.g., myvolume:/app/data or /host/path:/container/path:ro).";
            return;
        }

        if (VolumeMounts.Contains(volume))
        {
            VolumeMountValidation = "This volume mount is already in the list.";
            return;
        }

        VolumeMounts.Add(volume);
        NewVolumeMount = string.Empty;
        VolumeMountValidation = string.Empty;
    }

    [RelayCommand]
    private void RemoveVolumeMount(string volume)
    {
        VolumeMounts.Remove(volume);
    }

    [RelayCommand]
    private async Task CreateContainerAsync(CancellationToken cancellationToken = default)
    {
        if (!IsStepThreeActive || IsCreating)
        {
            return;
        }

        if (!TryBuildContainerConfig(out ContainerConfig? config, out string validationMessage) || config is null)
        {
            StatusMessage = $"Cannot run container: {validationMessage}";
            return;
        }

        string name = config.Name;

        IsCreating = true;
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(CanGoPrevious));
        StatusMessage = $"Creating container '{name}'...";

        try
        {
            string containerId = await _containerCatalogService.CreateContainerAsync(config, cancellationToken);

            CreatedContainerId = containerId;
            CreateSucceeded = true;
            StatusMessage = $"Container '{name}' created successfully.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Container creation was canceled.";
        }
        catch (Exception ex)
        {
            CreateSucceeded = false;
            StatusMessage = $"Failed to create container: {ex.Message}";
        }
        finally
        {
            IsCreating = false;
            OnPropertyChanged(nameof(CanGoNext));
            OnPropertyChanged(nameof(CanGoPrevious));
        }
    }

    private bool IsCurrentStepValid()
    {
        return CurrentStep switch
        {
            1 => !string.IsNullOrWhiteSpace(ContainerName)
                && ContainerNameRegex.IsMatch(ContainerName.Trim())
                && SelectedImage is not null,
            2 => true,
            _ => false,
        };
    }

    private void EnsureImageOptionExists(string imageReference)
    {
        if (string.IsNullOrWhiteSpace(imageReference)
            || AvailableImages.Any(i => string.Equals(i.Reference, imageReference, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        string repository = imageReference;
        string tag = "latest";
        int tagSeparator = imageReference.LastIndexOf(':');
        if (tagSeparator > 0 && tagSeparator > imageReference.LastIndexOf('/'))
        {
            repository = imageReference[..tagSeparator];
            tag = imageReference[(tagSeparator + 1)..];
        }

        AvailableImages.Insert(0, new ImageSummary(
            $"template:{imageReference}",
            repository,
            tag,
            "from template",
            "n/a",
            imageReference));
    }

    partial void OnContainerNameChanged(string value)
    {
        OnPropertyChanged(nameof(ContainerNameValidation));
    }
}
