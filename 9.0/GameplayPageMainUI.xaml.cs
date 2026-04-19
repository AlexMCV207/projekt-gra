namespace stars_beyond;

public partial class GameplayPageMainUI : ContentPage
{
	public GameplayPageMainUI()
	{
        InitializeComponent();
	}
    enum TurnState
    {
        PlayerTurn,
        AttackMinigame,
        EnemyTurn
    }

    bool isInputLocked = false;
    bool damageBoostActive = false;
    double originalDialogWidth;

    Dictionary<int, int> itemCount = new()
{
    { 0, 3 }, // small heal
    { 1, 2 }, // medium heal
    { 2, 1 }, // full heal
    { 3, 2 }  // dmg boost
};

    string[] itemNames =
    {
    "Small heal (+5)",
    "Medium heal (+13)",
    "Full heal",
    "Damage boost (150%)"
};

    TurnState currentState = TurnState.PlayerTurn;

    int enemyMaxHp = 30;
    int enemyHp = 30;

    void FightClicked(object sender, EventArgs e)
    {
        if (isInputLocked || currentState != TurnState.PlayerTurn)
            return;

        isInputLocked = true;
        ActMenuGrid.IsVisible = false;
        MercyMenuGrid.IsVisible = false;
        ItemMenuGrid.IsVisible = false;

        StartAttackMinigame();
    }
    bool isSliderMoving;
    double sliderX;
    double sliderSpeed = 10;

    void StartAttackMinigame()
    {
        currentState = TurnState.AttackMinigame;

        DialogLabel.IsVisible = false;
        AttackMinigame.IsVisible = true;

        sliderX = 0;
        AttackSlider.TranslationX = 0;

        isSliderMoving = true;

        _ = AnimateSlider();
    }
    async Task AnimateSlider()
    {
        await Task.Delay(500); // slider delay

        double maxX = AttackMinigame.Width - AttackSlider.Width;

        while (isSliderMoving)
        {
            sliderX += sliderSpeed;

            if (sliderX >= maxX)
            {
                isSliderMoving = false;

                Console.WriteLine("MISS - 0 damage");

                DealDamage(0);
                EndAttackMinigame();
                return;
            }
            AttackSlider.TranslationX = sliderX;
            await Task.Delay(16);
        }
    }
    void OnAttackTap(object sender, EventArgs e)
    {
        if (currentState != TurnState.AttackMinigame)
            return;

        isSliderMoving = false;

        double center = AttackBar.Width / 2;
        double hit = AttackSlider.TranslationX + (AttackSlider.Width / 2);

        double distance = Math.Abs(hit - center);
        double accuracy = 1 - (distance / center);
        accuracy = Math.Clamp(accuracy, 0, 1);

        int damage = (int)(accuracy * 70);
        if (damageBoostActive)
        {
            damage = (int)(damage * 1.5);
            damageBoostActive = false;
        }

        Console.WriteLine($"HIT accuracy: {accuracy:0.00} damage: {damage}");

        DealDamage(damage);
        EndAttackMinigame();
    }
    void EndAttackMinigame()
    {
        AttackMinigame.IsVisible = false;

        currentState = TurnState.EnemyTurn;

        StartEnemyTurn();
    }
    void ActClicked(object sender, EventArgs e)
    {
        if (isInputLocked || currentState != TurnState.PlayerTurn)
            return;

        ActMenuGrid.IsVisible = true;
        MercyMenuGrid.IsVisible = false;
        ItemMenuGrid.IsVisible = false;

        DialogLabel.Text = "";
    }
    async void ActOptionClicked(object sender, EventArgs e)
    {
        if (isInputLocked)
            return;

        isInputLocked = true;

        if (sender is Button btn && btn.CommandParameter != null)
        {
            int index = int.Parse(btn.CommandParameter.ToString());

            ActMenuGrid.IsVisible = false;
            ActionButtons.IsVisible = true;

            RunAct(index);

            await Task.Delay(1200);

            StartEnemyTurn();

            isInputLocked = false;
        }
    }
    void RunAct(int index)
    {
        switch (index)
        {
            case 0:
                DialogLabel.Text = GetCheckText();
                break;

            case 1:
                DialogLabel.Text = GetOption1Text();
                break;

            case 2:
                DialogLabel.Text = GetOption2Text();
                break;

            case 3:
                DialogLabel.Text = GetOption3Text();
                break;
        }
    }
    string GetCheckText()
    {
        return "* Random guy ??HP \n * Info";
    }

    string GetOption1Text()
    {
        return "* Wow! Could this be option 1?";
    }

    string GetOption2Text()
    {
        return "* No way! Option 2?";
    }

    string GetOption3Text()
    {
        return "* This isn't actually option 3";
    }
    void MercyClicked(object sender, EventArgs e)
    {
        if (isInputLocked || currentState != TurnState.PlayerTurn)
            return;

        MercyMenuGrid.IsVisible = true;
        ActMenuGrid.IsVisible = false;
        ItemMenuGrid.IsVisible = false;

        DialogLabel.Text = "";
    }
    async void MercyOptionClicked(object sender, EventArgs e)
    {
        if (isInputLocked)
            return;

        isInputLocked = true;

        if (sender is Button btn && btn.CommandParameter != null)
        {
            int index = int.Parse(btn.CommandParameter.ToString());

            MercyMenuGrid.IsVisible = false;
            ActionButtons.IsVisible = true;

            await RunMercy(index);
        }
    }
    async Task RunMercy(int index)
    {
        switch (index)
        {
            case 0: // SPARE
                DialogLabel.Text = GetSpareText();

                await Task.Delay(3000);

                StartEnemyTurn();
                break;

            case 1: // FLEE
                DialogLabel.Text = "* You fled";

                await Task.Delay(1500);

                await FadeOutAndGoToMenu();
                break;
        }
    }
    async Task FadeOutAndGoToMenu()
    {
        await this.FadeTo(0, 400);

        await Shell.Current.GoToAsync("//MainMenu");

        this.Opacity = 1; // reset po powrocie
    }
    string GetSpareText()
    {
        if (enemyHp <= 0)
            return "* Spared";

        return "* No spare :<";
    }
    void ItemClicked(object sender, EventArgs e)
    {
        if (isInputLocked || currentState != TurnState.PlayerTurn)
            return;

        ItemMenuGrid.IsVisible = true;
        ActMenuGrid.IsVisible = false;
        MercyMenuGrid.IsVisible = false;

        RefreshItemButtons();

        DialogLabel.Text = " ";
    }
    void RefreshItemButtons()
    {
        Button[] buttons = { ItemBtn0, ItemBtn1, ItemBtn2, ItemBtn3 };

        for (int i = 0; i < buttons.Length; i++)
        {
            if (itemCount[i] > 0)
            {
                buttons[i].IsVisible = true;
                buttons[i].Text = $"* {itemNames[i]} x{itemCount[i]}";
            }
            else
            {
                buttons[i].IsVisible = false;
            }
        }
    }
    async void ItemOptionClicked(object sender, EventArgs e)
    {
        if (isInputLocked)
            return;

        isInputLocked = true;

        if (sender is Button btn && btn.CommandParameter != null)
        {
            int index = int.Parse(btn.CommandParameter.ToString());

            ItemMenuGrid.IsVisible = false;
            ActionButtons.IsVisible = true;

            await UseItem(index);
        }
    }
    async Task UseItem(int index)
    {
        if (itemCount[index] <= 0)
            return;

        itemCount[index]--;

        switch (index)
        {
            case 0: // +5
                HealPlayer(5);
                DialogLabel.Text = "* Used small heal (+5 HP)";
                break;

            case 1: // +13
                HealPlayer(13);
                DialogLabel.Text = "* Used medium heal (+13 HP)";
                break;

            case 2: // full
                HealPlayer(playerMaxHp);
                DialogLabel.Text = "* Used full heal";
                break;

            case 3: // boost
                damageBoostActive = true;
                DialogLabel.Text = "* Used damage boost (150%)";
                break;
        }

        await Task.Delay(3000);

        StartEnemyTurn();
    }
    void HealPlayer(int amount)
    {
        playerHp = Math.Min(playerHp + amount, playerMaxHp);
        UpdatePlayerHp();
    }
    async Task AnimateBattleTransition()
    {
        if (originalDialogWidth == 0)
            originalDialogWidth = DialogBox.Width;

        double targetWidth = 300;

        // animacja zwê¿ania
        await DialogBox.AnimateAsync("shrink",
            v => DialogBox.WidthRequest = v,
            DialogBox.Width,
            targetWidth,
            length: 500,
            easing: Easing.CubicIn);

        // poka¿ pole bitwy
        BattleFrame.Opacity = 0;
        BattleFrame.IsVisible = true;

        await BattleFrame.FadeTo(1, 150);

        DialogLabel.IsVisible = false;
        ActMenuGrid.IsVisible = false;
        MercyMenuGrid.IsVisible = false;
        ItemMenuGrid.IsVisible = false;
    }
    async Task AnimateBackToDialog()
    {
        BattleFrame.IsVisible = false;

        DialogLabel.IsVisible = true;

        await DialogBox.AnimateAsync("expand",
            v => DialogBox.WidthRequest = v,
            DialogBox.Width,
            originalDialogWidth,
            length: 500,
            easing: Easing.CubicOut);

        DialogBox.WidthRequest = -1;
    }
    async void StartEnemyTurn()
    {
        currentState = TurnState.EnemyTurn;

        DialogLabel.IsVisible = true;
        DialogLabel.Text = "* Random #!%@>* go!";

        await Task.Delay(800);

        await AnimateBattleTransition();

        MovementControls.IsVisible = true;
        ActionButtons.IsVisible = false;

        await RunEnemyAttack();


        EndEnemyTurn();
    }

    Random random = new();
    async Task RunEnemyAttack()
    {
        int attackId = random.Next(1);

        switch (attackId)
        {
            case 0:
                StartBulletHell();
                await Task.Delay(8000); // attack duration
                StopBulletHell();
                break;
        }
    }
    async Task EndEnemyTurn()
{
        currentState = TurnState.PlayerTurn;

        await AnimateBackToDialog();

        ActionButtons.IsVisible = true;
        DialogLabel.IsVisible = true;
        MovementControls.IsVisible = false;
        DialogLabel.Text = "* Must have been the wind";
        isInputLocked = false;
    }
    void DealDamage(int damage)
    {
        enemyHp -= damage;
        enemyHp = Math.Max(0, enemyHp);
        DialogLabel.Text = $"* Zada³eœ {damage} obra¿eñ!";
        _ = ShowDamageText(damage);

        if (enemyHp <= 0)
        {
            
        }
    }
    const int PlayerSize = 16;
    const int BulletSize = 10;

    const double PlayerSpeed = 6.0;
    const double BulletSpeed = 4.0;

    double moveX = 0;
    double moveY = 0;

    int playerHp = 20;
    int playerMaxHp = 20;

    bool gameRunning = false;

    IDispatcherTimer gameLoop;
    IDispatcherTimer spawnTimer;

    void LeftPressed(object s, EventArgs e) { moveX = -PlayerSpeed; moveY = 0; }
    void RightPressed(object s, EventArgs e) { moveX = PlayerSpeed; moveY = 0; }
    void UpPressed(object s, EventArgs e) { moveY = -PlayerSpeed; moveX = 0; }
    void DownPressed(object s, EventArgs e) { moveY = PlayerSpeed; moveX = 0; }
    void StopMove(object s, EventArgs e) { moveX = moveY = 0; }

    void StartBulletHell()
    {
        gameRunning = true;

        Player.TranslationX = 140; // œrodek (300 / 2 - 8)
        Player.TranslationY = 100;

        AbsoluteLayout.SetLayoutBounds(Player,
            new Rect(0, 0, PlayerSize, PlayerSize));

        gameLoop = Dispatcher.CreateTimer();
        gameLoop.Interval = TimeSpan.FromMilliseconds(16);
        gameLoop.Tick += GameTick;
        gameLoop.Start();

        spawnTimer = Dispatcher.CreateTimer();
        spawnTimer.Interval = TimeSpan.FromMilliseconds(180);
        spawnTimer.Tick += SpawnBullet;
        spawnTimer.Start();
    }

    void StopBulletHell()
    {
        gameRunning = false;

        spawnTimer?.Stop();
        gameLoop?.Stop();

        BattleField.Children.Clear();
        BattleField.Children.Add(Player);

        moveX = moveY = 0;
    }
    void GameTick(object sender, EventArgs e)
    {
        if (!gameRunning) return;
        if (BattleField.Width <= 0 || BattleField.Height <= 0) return;

        MovePlayer();
        CheckCollisions();
    }
    void MovePlayer()
    {
        double newX = Player.TranslationX + moveX;
        double newY = Player.TranslationY + moveY;

        double maxX = BattleField.Width - PlayerSize;
        double maxY = BattleField.Height - PlayerSize;

        Player.TranslationX = Math.Clamp(newX, 0, maxX);
        Player.TranslationY = Math.Clamp(newY, 0, maxY);
    }
    void SpawnBullet(object sender, EventArgs e)
    {
        var bullet = new BoxView
        {
            WidthRequest = BulletSize,
            HeightRequest = BulletSize,
            Color = Colors.White
        };

        double x = random.NextDouble() * (BattleField.Width - BulletSize);
        double y = -BulletSize;

        AbsoluteLayout.SetLayoutBounds(bullet, new Rect(x, y, BulletSize, BulletSize));
        BattleField.Children.Add(bullet);

        _ = MoveBullet(bullet);
    }
    async Task MoveBullet(BoxView bullet)
    {
        while (gameRunning && BattleField.Children.Contains(bullet))
        {
            bullet.TranslationY += BulletSpeed;

            if (bullet.TranslationY > BattleField.Height + 50)
            {
                BattleField.Children.Remove(bullet);
                break;
            }

            await Task.Delay(16);
        }
    }
    void CheckCollisions()
    {
        foreach (var b in BattleField.Children.OfType<BoxView>()
            .Where(x => x != Player).ToList())
        {
            if (IsColliding(Player, b))
            {
                BattleField.Children.Remove(b);

                playerHp--;
                UpdatePlayerHp();
            }
        }
    }
    void UpdatePlayerHp()
    {
        HpLabel.Text = $"{playerHp}/{playerMaxHp}";

        double percent = (double)playerHp / playerMaxHp;
        HpBar.WidthRequest = 45 * percent;

        if (playerHp <= 0)
        {
            StopBulletHell();
            DialogLabel.Text = "* Death";
        }
    }
    bool IsColliding(VisualElement a, VisualElement b)
    {
        var r1 = new Rect(a.TranslationX, a.TranslationY, a.Width, a.Height);
        var r2 = new Rect(b.X + b.TranslationX, b.Y + b.TranslationY, b.Width, b.Height);

        return r1.IntersectsWith(r2);
    }
    async Task ShowDamageText(int damage)
    {
        DamageLabel.Text = damage.ToString();
        DamageLabel.Opacity = 0;
        DamageLabel.TranslationX = 0;
        DamageLabel.TranslationY = 0;
        DamageLabel.IsVisible = true;

        Random rand = new();

        double side = rand.Next(2) == 0 ? -1 : 1;
        double angle = rand.NextDouble() * Math.PI * 2;
        double distance = rand.Next(40, 80);

        double startX = side * rand.Next(80, 140);
        double startY = rand.Next(-100, -60);
        double moveX = startX + Math.Cos(angle) * distance;
        double moveY = startY + -Math.Abs(Math.Sin(angle) * distance);

        var fadeIn = DamageLabel.FadeTo(1, 150);
        var move = DamageLabel.TranslateTo(moveX, moveY, 700, Easing.SinOut);

        await Task.WhenAll(fadeIn, move);

        await DamageLabel.FadeTo(0, 400);


        DamageLabel.IsVisible = false;
    }
    
}
public static class AnimationExtensions
{
    public static Task AnimateAsync(this VisualElement view,
        string name,
        Action<double> callback,
        double start,
        double end,
        uint length = 250,
        Easing easing = null)
    {
        var tcs = new TaskCompletionSource<bool>();

        var animation = new Animation(callback, start, end, easing);

        animation.Commit(view, name, 16, length, finished: (v, c) =>
        {
            tcs.SetResult(true);
        });

        return tcs.Task;
    }
}
