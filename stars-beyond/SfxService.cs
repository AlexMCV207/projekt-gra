using Plugin.Maui.Audio;

public class SfxService
{
    IAudioManager audioManager;
    IAudioPlayer menuClickPlayer;
    IAudioPlayer menuStartPlayer;
    Dictionary<string, IAudioPlayer> players = new();

    public SfxService(IAudioManager manager)
    {
        audioManager = manager;
    }

    public async Task Play(string fileName)
    {
        if (!players.ContainsKey(fileName))
        {
            var stream = await FileSystem.OpenAppPackageFileAsync(fileName);
            var player = audioManager.CreatePlayer(stream);

            players[fileName] = player;
        }

        var p = players[fileName];

        p.Stop(); // restart dźwięku jeśli już grał
        p.Volume = Preferences.Get("sfxVolume", 0.5);
        p.Play();
    }

    public void SetVolume(double volume)
    {
        Preferences.Set("sfxVolume", volume);

        foreach (var p in players.Values)
        {
            p.Volume = volume;
        }
    }
}