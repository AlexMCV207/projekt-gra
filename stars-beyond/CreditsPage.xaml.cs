namespace stars_beyond;

public partial class CreditsPage : ContentPage
{
    SfxService sfxService;
    public CreditsPage( SfxService sfx)
	{
        sfxService = sfx;
        InitializeComponent();
	}
    private async void GoToMain(object sender, EventArgs e)
    {
        sfxService.PlayMenuClick();
        await Shell.Current.GoToAsync("//MainMenu");
    }
}