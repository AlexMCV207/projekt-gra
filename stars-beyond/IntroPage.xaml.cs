using Microsoft.Maui.Storage;

namespace stars_beyond;

public partial class IntroPage : ContentPage
{
    public IntroPage()
    {
        InitializeComponent();
    }

    private async void PageLoaded(object sender, EventArgs e)
    {
        await Task.Delay(1000);

        var file = await FileSystem.OpenAppPackageFileAsync("intro.mp4");
        var tempPath = Path.Combine(FileSystem.CacheDirectory, "intro.mp4");

        using (var stream = File.Create(tempPath))
        {
            await file.CopyToAsync(stream);
        }

        IntroVideo.Source = tempPath;
        IntroVideo.Play();

        await Task.Delay(5000);

        System.Diagnostics.Debug.WriteLine("IntroPage: timed navigation to MainMenu");
        try { IntroVideo.Stop(); } catch { }
        await Shell.Current.GoToAsync("//MainMenu");
    }

    private void VideoEnded(object sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            System.Diagnostics.Debug.WriteLine("IntroPage: VideoEnded handler navigating to MainMenu");
            await Shell.Current.GoToAsync("//MainMenu");
        });
    }
}