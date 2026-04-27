using stars_beyond;
using static stars_beyond.GameplayPageMainUI;
namespace stars_beyond;

[QueryProperty(nameof(WinType), "WinType")]
public partial class WinPage : ContentPage
{
    public WinType WinType
    {
        set
        {
            ApplyResult(value);
        }
    }

    void ApplyResult(WinType type)
    {
        switch (type)
        {
            case WinType.Pacifist:
                ResultLabel.Text = "Completed ending - pacifist";
                break;

            case WinType.Kill:
                ResultLabel.Text = "Completed ending - violent";
                break;
        }
    }
}