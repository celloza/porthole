using CommunityToolkit.Mvvm.ComponentModel;

namespace Porthole.Core.ViewModels;

public partial class ShellViewModel : ObservableObject
{
    [ObservableProperty]
    private string windowTitle = "Porthole";

    [ObservableProperty]
    private string currentSectionTitle = "System Dashboard";

    public void SetSection(string sectionTitle)
    {
        CurrentSectionTitle = sectionTitle;
    }
}