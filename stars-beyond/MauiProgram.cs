using Microsoft.Extensions.Logging;
using CommunityToolkit.Maui;
using Plugin.Maui.Audio;



namespace stars_beyond
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseMauiCommunityToolkit()
                .UseMauiCommunityToolkitMediaElement()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                    fonts.AddFont("determination.ttf", "Determination");
                });
            builder.Services.AddSingleton(AudioManager.Current);
            builder.Services.AddSingleton<MusicService>();
            builder.Services.AddSingleton<SfxService>();

#if DEBUG
            builder.Logging.AddDebug();
            
#endif

            // Fullscreen is handled in the Windows platform App class.

            return builder.Build();
        }
    }
}
