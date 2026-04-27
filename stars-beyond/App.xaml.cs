namespace stars_beyond
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            var window = new Window(new AppShell());

            // On Windows, wait for the handler to be attached and then set the
            // native AppWindow presenter to FullScreen. Using HandlerChanged
            // avoids timing issues where the native window isn't yet available.
#if WINDOWS
            window.HandlerChanged += (s, e) =>
            {
                try
                {
                    var nativeWindow = window.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
                    if (nativeWindow != null)
                    {
                        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
                        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                        var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
                        appWindow?.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to set fullscreen in CreateWindow: {ex}");
                }
            };
#endif

            return window;
        }
    }
}