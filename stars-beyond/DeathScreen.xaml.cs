using Microsoft.Maui.Layouts;

namespace stars_beyond;

public partial class DeathScreen : ContentPage
{
    public DeathScreen()
    {
        InitializeComponent();
        StartDeathAnimation();
    }

    async void StartDeathAnimation()
    {
        AbsoluteLayout.SetLayoutBounds(Heart, new Rect(0.5, 0.5, 60, 60));
        AbsoluteLayout.SetLayoutFlags(Heart, AbsoluteLayoutFlags.PositionProportional);

        await Task.Delay(1000); // static
        await HeartShake(); // shaking
        Heart.Source = "player_broken.png"; // switch to broken heart
        await Task.Delay(1000); // broken static
        var fadeTask = Heart.FadeTo(0, 300); // fade
        var fragmentsTask = SpawnFragments(); // fragments
        await Task.WhenAll(fadeTask, fragmentsTask);
        await Task.Delay(1500);
        await GameOverText.FadeTo(1, 1200);
        await Task.Delay(1500);
        RestartButtonFadeIn();
    }
    async Task HeartShake()
    {
        int shakes = 8;

        for (int i = 0; i < shakes; i++)
        {
            await Heart.TranslateTo(3, 0, 40);
            await Heart.TranslateTo(-3, 0, 40);
        }

        await Heart.TranslateTo(0, 0, 30);
    }
    async Task SpawnFragments()
    {
        var layout = (AbsoluteLayout)Heart.Parent;

        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(9);
            var piece = new BoxView
            {
                Color = Colors.Red,
                WidthRequest = 12,
                HeightRequest = 12
            };
            AbsoluteLayout.SetLayoutBounds(piece,AbsoluteLayout.GetLayoutBounds(Heart));
            layout.Children.Add(piece);

            var rand = new Random();
            double baseAngle = Math.PI / 2;
            double spread = Math.PI * (70.0 / 180.0);
            double angle = baseAngle + (rand.NextDouble() * 2 - 1) * spread;
            double speed = rand.Next(10, 20);
            double dx = Math.Cos(angle) * speed;
            double dy = Math.Sin(angle) * speed;

            _ = AnimateFragment(piece, dx, dy, layout);
        }
    }

    async Task AnimateFragment(BoxView piece, double dx, double dy, AbsoluteLayout layout)
    {
        while (layout.Children.Contains(piece))
        {
            piece.TranslationX += dx;
            piece.TranslationY += dy;

            // pobierz ekran
            double x = piece.TranslationX;
            double y = piece.TranslationY;

            // usuń dopiero gdy poza ekranem (z marginesem)
            if (x < -700 || x > layout.Width + 700 ||
                y < -100 || y > layout.Height + 1200)
            {
                layout.Children.Remove(piece);
                break;
            }

            await Task.Delay(16);
        }
    }

    async void OnRestartClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//GameplayPageMainUI");

        await Task.Delay(100);

        var page = Shell.Current.CurrentPage as GameplayPageMainUI;
        if (page != null)
            await page.ResetBattle();
    }

    async void OnMenuClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//MainMenu");
    }
    void RestartButtonFadeIn()
    {
        _ = RestartButton.FadeTo(1, 500);
        _ = MenuButton.FadeTo(1, 500);
    }
}