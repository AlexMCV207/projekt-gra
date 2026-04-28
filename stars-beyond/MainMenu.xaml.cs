namespace stars_beyond;

public partial class MainMenu : ContentPage
{
    MusicService musicService;
    SfxService sfxService;
    // Parameterless ctor required for Shell DataTemplate activation.
    public MainMenu()
    {
        InitializeComponent();

        try
        {
            var services = Application.Current?.Handler?.MauiContext?.Services;
            if (services != null)
            {
                musicService = services.GetService(typeof(MusicService)) as MusicService;
                sfxService = services.GetService(typeof(SfxService)) as SfxService;
            }
        }
        catch { }
    }

    // Keep DI-friendly constructor for programmatic creation/tests
    public MainMenu(MusicService service, SfxService sfx)
    {
        InitializeComponent();
        musicService = service;
        sfxService = sfx;
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        FadeOverlay.Opacity = 0;

        if (musicService.ShouldPlayMenuMusic)
        {
            musicService.ShouldPlayMenuMusic = false;

            await musicService.Play("menu_early.mp3");
        }
    }
    private async void GoToGameplay(object sender, EventArgs e)
{
    _ = sfxService.Play("sb_menu_start.mp3");

    _ = musicService.FadeOut(2000);

    await FadeOverlay.FadeTo(1, 1000, Easing.CubicIn);
    await Shell.Current.GoToAsync("//GameplayPageMainUI");

    // After navigation, reset the battle to start fresh every time START is clicked
    try
    {
        // find the page instance and call ResetBattle
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (Shell.Current.CurrentPage is null)
            {
                System.Diagnostics.Debug.WriteLine("GoToGameplay: Shell.Current.CurrentPage is null");
                return;
            }

            if (Shell.Current.CurrentPage is GameplayPageMainUI gp)
            {
                gp.ResetBattle();
            }
            else
            {
                // try to locate by route
                foreach (var p in Shell.Current.Items)
                {
                    // no-op; typically Shell.Current.CurrentPage will be the correct instance
                }
            }
        });
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"GoToGameplay: failed to reset battle: {ex}");
    }
}

    private async void GoToOptions(object sender, EventArgs e)
    {
        _ = sfxService.Play("sb_menu_click.mp3");
        await Shell.Current.GoToAsync("//OptionsPage");
    }

    private async void GoToCredits(object sender, EventArgs e)
    {
        _ = sfxService.Play("sb_menu_click.mp3");
        await Shell.Current.GoToAsync("//CreditsPage");
    }

    private async void Exit(object sender, EventArgs e)
    {
        _ = sfxService.Play("sb_menu_click.mp3");
        Application.Current.Quit();
    }
}