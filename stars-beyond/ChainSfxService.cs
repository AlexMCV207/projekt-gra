using Plugin.Maui.Audio;

public class ChainSfxService
{
    IAudioManager audioManager;
    IAudioPlayer player;

    public ChainSfxService(IAudioManager manager)
    {
        audioManager = manager;
    }

    public async Task Start()
    {
        if (player != null)
            return; // już gra

        var stream = await FileSystem.OpenAppPackageFileAsync("chain_roll.mp3");

        player = audioManager.CreatePlayer(stream);
        player.Loop = true;
        player.Volume = Preferences.Get("sfxVolume", 0.5);
        player.Play();
    }

    public void SetVolume(double volume)
    {
        if (player != null)
            player.Volume = volume;
    }

    public void Stop()
    {
        if (player == null)
            return;

        player.Stop();
        player.Dispose();
        player = null;
    }

    public async Task FadeOut(int durationMs = 300)
    {
        if (player == null)
            return;

        double startVolume = player.Volume;
        int steps = 20;
        int delay = durationMs / steps;

        for (int i = steps; i >= 0; i--)
        {
            player.Volume = startVolume * i / steps;
            await Task.Delay(delay);
        }

        Stop();
    }
}