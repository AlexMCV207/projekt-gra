using Plugin.Maui.Audio;

public class SfxService
{
    IAudioManager audioManager;

    IAudioPlayer menuClickPlayer;
    IAudioPlayer menuStartPlayer;

    public SfxService(IAudioManager manager)
    {
        audioManager = manager;
    }

    public async Task Initialize()
    {
        if (menuClickPlayer != null)
            return;

        var clickStream = await FileSystem.OpenAppPackageFileAsync("sb_menu_click.mp3");
        menuClickPlayer = audioManager.CreatePlayer(clickStream);

        var startStream = await FileSystem.OpenAppPackageFileAsync("sb_menu_start.mp3");
        menuStartPlayer = audioManager.CreatePlayer(startStream);
    }

    public void PlayMenuClick()
    {
        if (menuClickPlayer == null)
            return;

        menuClickPlayer.Stop();
        menuClickPlayer.Volume = Preferences.Get("sfxVolume", 0.5);
        menuClickPlayer.Play();
    }

    public void PlayStart()
    {
        if (menuStartPlayer == null)
            return;

        menuStartPlayer.Stop();
        menuStartPlayer.Volume = Preferences.Get("sfxVolume", 0.5);
        menuStartPlayer.Play();
    }

    public void SetVolume(double volume)
    {
        Preferences.Set("sfxVolume", volume);

        if (menuClickPlayer != null)
            menuClickPlayer.Volume = volume;

        if (menuStartPlayer != null)
            menuStartPlayer.Volume = volume;
    }
}