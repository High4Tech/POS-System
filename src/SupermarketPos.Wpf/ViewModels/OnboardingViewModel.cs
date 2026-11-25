using CommunityToolkit.Mvvm.ComponentModel;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Domain.Business;

namespace SupermarketPos.Wpf.ViewModels;
public sealed partial class OnboardingViewModel(IBusinessSetup setup) : ObservableObject
{
    [ObservableProperty] private BusinessProfile _profile = new();
    [ObservableProperty] private int _step;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private bool _busy;
    public bool HasUsers { get; private set; }
    public bool PreviouslyCompleted { get; private set; }
    public int ResumeStep { get; private set; }
    public string Username { get; set; } = "";
    public string LicenseKey { get; set; } = "";
    public string DeviceLabel { get; set; } = Environment.MachineName;
    public string DataFolder { get; set; } = "";
    public string? RecoveryCode { get; set; }
    public async Task LoadAsync()
    {
        var state = await setup.LoadAsync();
        // An old store's saved logo is not sample artwork for a new onboarding session.
        Profile = !state.Completed && !state.Profile.SetupLogoChosen ? state.Profile with { Logo = null } : state.Profile;
        HasUsers = state.HasUsers;
        PreviouslyCompleted = state.Completed;
        ResumeStep = state.Step;
        Step = 0; // Language is always the first interactive setup screen, including resumption.
    }
    public Task SaveAsync(int step) => setup.SaveDraftAsync(Profile, step);
}
