using Plugin.Maui.Audio;

public class MusicService
{
    IAudioManager audioManager;
    IAudioPlayer player;
    CancellationTokenSource fadeCts;

    public MusicService(IAudioManager manager)
    {
        audioManager = manager;
    }

    public bool ShouldPlayMenuMusic = false;

    public async Task Play(string fileName)
    {
        fadeCts?.Cancel();

        if (player != null)
        {
            player.Stop();
            player.Dispose();
            player = null;
        }

        var stream = await FileSystem.OpenAppPackageFileAsync(fileName);

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

    public async Task FadeOut(int durationMs = 500)
    {
        if (player == null)
            return;

        fadeCts?.Cancel();
        fadeCts = new CancellationTokenSource();
        var token = fadeCts.Token;

        double startVolume = player.Volume;
        int steps = 50;
        int delay = durationMs / steps;

        try
        {
            for (int i = steps; i >= 0; i--)
            {
                if (token.IsCancellationRequested)
                    return;

                player.Volume = startVolume * i / steps;
                await Task.Delay(delay);
            }

            player.Stop();
            player.Dispose();
            player = null;
        }
        catch (TaskCanceledException) { }
    }

    public void Stop()
    {
        fadeCts?.Cancel();

        if (player != null)
        {
            player.Stop();
            player.Dispose();
            player = null;
        }
    }
}