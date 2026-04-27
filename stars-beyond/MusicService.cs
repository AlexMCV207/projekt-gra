using Plugin.Maui.Audio;

public class MusicService
{
    IAudioManager audioManager;
    IAudioPlayer player;

    public MusicService(IAudioManager manager)
    {
        audioManager = manager;
    }

    public async Task PlayMusic()
    {
        if (player != null)
            return;

        var stream = await FileSystem.OpenAppPackageFileAsync("menu_early.mp3");

        player = audioManager.CreatePlayer(stream);
        player.Loop = true;
        player.Volume = Preferences.Get("musicVolume", 0.5);
        player.Play();
    }

    public void SetVolume(double volume)
    {
        if (player != null)
            player.Volume = volume;
    }
    public async Task FadeOut(int durationMs = 50)
    {
        if (player == null)
            return;

        double startVolume = player.Volume;
        int steps = 50;
        int delay = durationMs / steps;

        for (int i = steps; i >= 0; i--)
        {
            player.Volume = startVolume * i / steps;
            await Task.Delay(delay);
        }

        player.Stop();
    }
}
