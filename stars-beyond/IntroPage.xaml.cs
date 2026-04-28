using Microsoft.Maui.Storage;

namespace stars_beyond;

public partial class IntroPage : ContentPage
{
    MusicService musicService;
    public IntroPage(MusicService service)
    {
        InitializeComponent();
        musicService = service;
    }
    CancellationTokenSource cts = new();
    private async void PageLoaded(object sender, EventArgs e)
    {
        try
        {
            await Task.Delay(1000, cts.Token);

            musicService.ShouldPlayMenuMusic = true;

            var file = await FileSystem.OpenAppPackageFileAsync("intro.mp4");
            var tempPath = Path.Combine(FileSystem.CacheDirectory, "intro.mp4");

            using (var stream = File.Create(tempPath))
            {
                await file.CopyToAsync(stream);
            }

            IntroVideo.Source = tempPath;
            IntroVideo.Play();

            await Task.Delay(5000, cts.Token);

            if (!cts.IsCancellationRequested)
                await Shell.Current.GoToAsync("//MainMenu");
        }
        catch (TaskCanceledException)
        {
        }
    }
    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        try { cts.Cancel(); } catch { }
    }
    private void VideoEnded(object sender, EventArgs e)
    {
        if (cts.IsCancellationRequested)
            return;

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (!cts.IsCancellationRequested)
                await Shell.Current.GoToAsync("//MainMenu");
        });
    }
}