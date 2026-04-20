namespace stars_beyond;

public partial class GameplayPageMainUI : ContentPage
{
	public GameplayPageMainUI()
	{
        InitializeComponent();
        OnPhase1Start();
        HookPlatformKeyboardHandlers();
        BindingContext = this; // for KeyboardAccelerator command bindings in XAML
        // ensure on-screen D-Pad buttons are wired to movement handlers (covers any XAML disconnect)
        try
        {
            if (UpButton != null)
            {
                UpButton.InputTransparent = false;
                UpButton.IsEnabled = true;
                UpButton.Clicked += async (s, e) =>
                {
                    if (touchRepeatTimer?.IsRunning == true) return;
                    StartTouchMove(0, -PlayerSpeed);
                    await Task.Delay(300);
                    StopMove(s, e);
                };
            }
            if (DownButton != null)
            {
                DownButton.InputTransparent = false;
                DownButton.IsEnabled = true;
                DownButton.Clicked += async (s, e) =>
                {
                    if (touchRepeatTimer?.IsRunning == true) return;
                    StartTouchMove(0, PlayerSpeed);
                    await Task.Delay(300);
                    StopMove(s, e);
                };
            }

            if (LeftButton != null)
            {
                LeftButton.InputTransparent = false;
                LeftButton.IsEnabled = true;
                LeftButton.Clicked += async (s, e) =>
                {
                    if (touchRepeatTimer?.IsRunning == true) return;
                    StartTouchMove(-PlayerSpeed, 0);
                    await Task.Delay(300);
                    StopMove(s, e);
                };
            }

            if (RightButton != null)
            {
                RightButton.InputTransparent = false;
                RightButton.IsEnabled = true;
                RightButton.Clicked += async (s, e) =>
                {
                    if (touchRepeatTimer?.IsRunning == true) return;
                    StartTouchMove(PlayerSpeed, 0);
                    await Task.Delay(300);
                    StopMove(s, e);
                };
            }
        }
        catch { }

        // debug info
        try
        {
            System.Diagnostics.Debug.WriteLine($"DPad buttons: Up={UpButton!=null}, Down={DownButton!=null}, Left={LeftButton!=null}, Right={RightButton!=null}");
            System.Diagnostics.Debug.WriteLine($"MovementControls visible: {MovementControls?.IsVisible}");
        }
        catch { }
    }

    // platform-specific keyboard hook (partial implemented per-platform)
    partial void HookPlatformKeyboardHandlers();
    void SetupKeyboardHandlers()
    {
#if WINDOWS
        try
        {
            // Windows-specific handlers are optional; keyboard accelerators in XAML will handle most cases.
        }
        catch
        {
            // ignore if platform APIs unavailable
        }
#endif
    }

#if WINDOWS
    // track pressed keys so hold/release behavior matches touch buttons
    System.Collections.Generic.HashSet<Windows.System.VirtualKey> keysPressed = new System.Collections.Generic.HashSet<Windows.System.VirtualKey>();

    partial void HookPlatformKeyboardHandlers()
    {
        try
        {
            var core = Windows.UI.Core.CoreWindow.GetForCurrentThread();
            if (core != null)
            {
                core.KeyDown += Core_KeyDown;
                core.KeyUp += Core_KeyUp;
                System.Diagnostics.Debug.WriteLine("CoreWindow handlers attached");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("CoreWindow not available for this thread");
            }
        }
        catch { }
    }

    private void Core_KeyDown(Windows.UI.Core.CoreWindow sender, Windows.UI.Core.KeyEventArgs args)
    {
        var key = args.VirtualKey;
        if (!keysPressed.Contains(key))
        {
            keysPressed.Add(key);

            System.Diagnostics.Debug.WriteLine($"Core_KeyDown: {key}");

            switch (key)
            {
                case Windows.System.VirtualKey.W:
                    // ignore keyboard if recent touch input occurred
                    lastKeyboardInput = DateTime.UtcNow;
                    if ((DateTime.UtcNow - lastTouchInput).TotalMilliseconds > 100)
                        UpPressed(this, EventArgs.Empty);
                    break;
                case Windows.System.VirtualKey.A:
                    lastKeyboardInput = DateTime.UtcNow;
                    if ((DateTime.UtcNow - lastTouchInput).TotalMilliseconds > 100)
                        LeftPressed(this, EventArgs.Empty);
                    break;
                case Windows.System.VirtualKey.S:
                    lastKeyboardInput = DateTime.UtcNow;
                    if ((DateTime.UtcNow - lastTouchInput).TotalMilliseconds > 100)
                        DownPressed(this, EventArgs.Empty);
                    break;
                case Windows.System.VirtualKey.D:
                    lastKeyboardInput = DateTime.UtcNow;
                    if ((DateTime.UtcNow - lastTouchInput).TotalMilliseconds > 100)
                        RightPressed(this, EventArgs.Empty);
                    break;
            }
        }
    }

    private void Core_KeyUp(Windows.UI.Core.CoreWindow sender, Windows.UI.Core.KeyEventArgs args)
    {
        var key = args.VirtualKey;
        if (keysPressed.Contains(key))
            keysPressed.Remove(key);

        System.Diagnostics.Debug.WriteLine($"Core_KeyUp: {key}");

        // choose remaining key to continue movement, or stop if none
        if (keysPressed.Contains(Windows.System.VirtualKey.W))
            UpPressed(this, EventArgs.Empty);
        else if (keysPressed.Contains(Windows.System.VirtualKey.S))
            DownPressed(this, EventArgs.Empty);
        else if (keysPressed.Contains(Windows.System.VirtualKey.A))
            LeftPressed(this, EventArgs.Empty);
        else if (keysPressed.Contains(Windows.System.VirtualKey.D))
            RightPressed(this, EventArgs.Empty);
        else
            StopMove(this, EventArgs.Empty);
    }
#endif
    enum BattlePhase
    {
        Phase1,
        Phase2,
        Phase3
    }
    BattlePhase currentPhase = BattlePhase.Phase1;
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

    int enemyMaxHp = 1200;
    int enemyHp = 1200;

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

        int damage = (int)(accuracy * 50);
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
    bool attackRunning = false;
    bool isInvincible = false;
    void StartGameLoop()
    {
        gameRunning = true;

        // ensure on-screen movement controls are visible for testing
        try { MovementControls.IsVisible = true; MovementControls.IsEnabled = true; } catch { }

        Player.TranslationX = 140;
        Player.TranslationY = 100;

        AbsoluteLayout.SetLayoutBounds(Player,
            new Rect(0, 0, PlayerSize, PlayerSize));

        gameLoop = Dispatcher.CreateTimer();
        gameLoop.Interval = TimeSpan.FromMilliseconds(16);
        gameLoop.Tick += GameTick;
        gameLoop.Start();
    }
    void StopGameLoop()
    {
        gameRunning = false;

        gameLoop?.Stop();

        BattleField.Children.Clear();
        BattleField.Children.Add(Player);

        moveX = moveY = 0;
    }
    Random random = new();
    async Task RunEnemyAttack()
    {
        var attacks = GetAttacksForCurrentPhase();

        if (attacks.Count == 0)
            return;

        var attack = attacks[random.Next(attacks.Count)];


        if (attackRunning) return;
        attackRunning = true;

        await attack();

        attackRunning = false;
    }
    List<Func<Task>> GetAttacksForCurrentPhase()
    {
        return currentPhase switch
        {
            BattlePhase.Phase1 => new List<Func<Task>>
        {
            Attack_Chains
        },

            BattlePhase.Phase2 => new List<Func<Task>>
        {
            Attack_BasicRain,
            Attack_FastRain
        },

            BattlePhase.Phase3 => new List<Func<Task>>
        {
            Attack_BasicRain,
            Attack_FastRain,
            Attack_Chaos
        },

            _ => new List<Func<Task>>()
        };
    }
    async Task Attack_Chains()
    {
        StartGameLoop();

        var directions = new[] { "up", "right", "down", "left" };
        int dirIndex = 0;

        for (int i = 0; i < 12; i++)
        {
            SpawnChain(directions[dirIndex]);
            dirIndex = (dirIndex + 1) % 4;

            await Task.Delay(1000);
        }

        await Task.Delay(4000);

        ClearChains();
        StopGameLoop();
    }
    List<ChainAttack> activeChains = new();
    void SpawnChain(string direction)
    {
        var img = new Image
        {
            Source = $"grapple_{direction}.gif",
            Aspect = Aspect.Fill
        };

        // decide orientation and size so the chain spans (or exceeds) the battlefield
        bool vertical = direction == "up" || direction == "down";
        double baseWidth = vertical ? 40 : Math.Max(40, BattleField.Width + 200);
        double baseHeight = vertical ? Math.Max(40, BattleField.Height + 200) : 40;

        double width = baseWidth * ChainScale;
        double height = baseHeight * ChainScale;

        img.WidthRequest = width;
        img.HeightRequest = height;

        // use direction-specific sprite files (no programmatic rotation)

        double x = 0;
        double y = 0;

        // constant speed for chains; travel time will depend on chain length and this speed
        const double chainSpeed = 6.0;
        double dx = 0;
        double dy = 0;

        switch (direction)
        {
            case "up":
                // spawn above, random x so chains are not always centered
                x = random.NextDouble() * Math.Max(0, BattleField.Width - width);
                y = -height - 50;
                dx = 0;
                dy = chainSpeed;
                break;

            case "down":
                // spawn below, random x
                x = random.NextDouble() * Math.Max(0, BattleField.Width - width);
                y = BattleField.Height + 50;
                dx = 0;
                dy = -chainSpeed;
                break;

            case "left":
                // spawn left, random y
                x = -width - 50;
                y = random.NextDouble() * Math.Max(0, BattleField.Height - height);
                dx = chainSpeed;
                dy = 0;
                break;

            case "right":
                // spawn right, random y
                x = BattleField.Width + 50;
                y = random.NextDouble() * Math.Max(0, BattleField.Height - height);
                dx = -chainSpeed;
                dy = 0;
                break;
        }

        // Set layout bounds using the computed size and position
        AbsoluteLayout.SetLayoutBounds(img, new Rect(x, y, width, height));
        BattleField.Children.Add(img);

        var chain = new ChainAttack
        {
            Sprite = img,
            DirX = dx,
            DirY = dy,
            Width = width,
            Height = height
        };

        activeChains.Add(chain);

        _ = MoveChain(chain);

        // schedule removal after configured lifetime unless configured to persist until end
        if (!ChainsRemovedOnlyOnEnd)
            _ = RemoveChainAfterDelay(img, ChainLifetimeMs);
    }

    async Task RemoveChainAfterDelay(Image img, int ms)
    {
        await Task.Delay(ms);

        // ensure removal on UI thread
        if (BattleField.Children.Contains(img))
        {
            Dispatcher.Dispatch(() =>
            {
                if (BattleField.Children.Contains(img))
                    BattleField.Children.Remove(img);
            });
        }
    }
    async Task MoveChain(ChainAttack chain)
    {
        var img = chain.Sprite;

        while (gameRunning && BattleField.Children.Contains(img))
        {
            img.TranslationX += chain.DirX;
            img.TranslationY += chain.DirY;

            CheckChainCollision(img);

            // if configured to keep chains until end, don't remove them when off-screen
            if (!ChainsRemovedOnlyOnEnd && IsOutside(chain))
            {
                // remove sprite but also remove chain from active list
                BattleField.Children.Remove(img);
                activeChains.Remove(chain);
                break;
            }

            await Task.Delay(16);
        }
    }
    void RemoveChain(ChainAttack chain)
    {
        if (BattleField.Children.Contains(chain.Sprite))
            BattleField.Children.Remove(chain.Sprite);
    }

    void ClearChains()
    {
        foreach (var c in activeChains)
        {
            if (BattleField.Children.Contains(c.Sprite))
                BattleField.Children.Remove(c.Sprite);
        }

        activeChains.Clear();
    }
    void CheckChainCollision(VisualElement chain)
    {
        if (IsColliding(Player, chain))
        {
            TakeDamage(2);
            UpdatePlayerHp();
        }
    }
    bool IsOutside(ChainAttack chain)
    {
        var v = chain.Sprite;

        // use the AbsoluteLayout bounds (these are the values set with SetLayoutBounds)
        var bounds = AbsoluteLayout.GetLayoutBounds(v);

        double left = bounds.X + v.TranslationX;
        double top = bounds.Y + v.TranslationY;
        double right = left + chain.Width;
        double bottom = top + chain.Height;

        return right < -300 ||
               left > BattleField.Width + 300 ||
               bottom < -300 ||
               top > BattleField.Height + 300;
    }
    async Task Attack_BasicRain()
    {
        StartGameLoop();

        spawnTimer = Dispatcher.CreateTimer();
        spawnTimer.Interval = TimeSpan.FromMilliseconds(180);
        spawnTimer.Tick += SpawnBullet;
        spawnTimer.Start();

        await Task.Delay(8000);

        spawnTimer.Stop();
        StopGameLoop();
    }
    async Task Attack_FastRain()
    {
        StartGameLoop();

        var fastTimer = Dispatcher.CreateTimer();
        fastTimer.Interval = TimeSpan.FromMilliseconds(80); // szybciej ni¿ normalnie

        fastTimer.Tick += (s, e) =>
        {
            SpawnFastBullet();
        };

        fastTimer.Start();

        await Task.Delay(6000);

        fastTimer.Stop();
        StopGameLoop();
    }
    void SpawnFastBullet()
    {
        var bullet = new BoxView
        {
            WidthRequest = BulletSize,
            HeightRequest = BulletSize,
            Color = Colors.Red
        };

        double x = random.NextDouble() * (BattleField.Width - BulletSize);
        double y = -BulletSize;

        AbsoluteLayout.SetLayoutBounds(bullet, new Rect(x, y, BulletSize, BulletSize));
        BattleField.Children.Add(bullet);

        _ = MoveFastBullet(bullet);
    }
    async Task MoveFastBullet(BoxView bullet)
    {
        double speed = BulletSpeed * 1.8;

        while (gameRunning && BattleField.Children.Contains(bullet))
        {
            bullet.TranslationY += speed;

            if (bullet.TranslationY > BattleField.Height + 50)
            {
                BattleField.Children.Remove(bullet);
                break;
            }

            await Task.Delay(16);
        }
    }
    async Task Attack_Chaos()
    {
        StartGameLoop();

        var chaosTimer = Dispatcher.CreateTimer();
        chaosTimer.Interval = TimeSpan.FromMilliseconds(120);

        chaosTimer.Tick += (s, e) =>
        {
            SpawnChaosBullet();
        };

        chaosTimer.Start();

        await Task.Delay(7000);

        chaosTimer.Stop();
        StopGameLoop();
    }
    void SpawnChaosBullet()
    {
        var bullet = new BoxView
        {
            WidthRequest = BulletSize,
            HeightRequest = BulletSize,
            Color = Colors.White
        };

        int side = random.Next(4);

        double x = 0, y = 0;
        double dx = 0, dy = 0;

        switch (side)
        {
            case 0: // góra
                x = random.NextDouble() * BattleField.Width;
                y = -BulletSize;
                dy = BulletSpeed;
                break;

            case 1: // dó³
                x = random.NextDouble() * BattleField.Width;
                y = BattleField.Height + BulletSize;
                dy = -BulletSpeed;
                break;

            case 2: // lewo
                x = -BulletSize;
                y = random.NextDouble() * BattleField.Height;
                dx = BulletSpeed;
                break;

            case 3: // prawo
                x = BattleField.Width + BulletSize;
                y = random.NextDouble() * BattleField.Height;
                dx = -BulletSpeed;
                break;
        }

        AbsoluteLayout.SetLayoutBounds(bullet, new Rect(x, y, BulletSize, BulletSize));
        BattleField.Children.Add(bullet);

        _ = MoveChaosBullet(bullet, dx, dy);
    }
    async Task MoveChaosBullet(BoxView bullet, double dx, double dy)
    {
        while (gameRunning && BattleField.Children.Contains(bullet))
        {
            bullet.TranslationX += dx;
            bullet.TranslationY += dy;

            if (bullet.TranslationY > BattleField.Height + 50 ||
                bullet.TranslationY < -50 ||
                bullet.TranslationX > BattleField.Width + 50 ||
                bullet.TranslationX < -50)
            {
                BattleField.Children.Remove(bullet);
                break;
            }

            await Task.Delay(16);
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
        CheckPhaseTransition();
        DialogLabel.Text = $"* Zada³eœ {damage} obra¿eñ!";
        _ = ShowDamageText(damage);

        if (enemyHp <= 0)
        {
            
        }
    }
    void CheckPhaseTransition()
    {
        if (enemyHp <= 400 && currentPhase != BattlePhase.Phase3)
        {
            currentPhase = BattlePhase.Phase3;
            OnPhase3Start();
        }
        else if (enemyHp <= 800 && currentPhase != BattlePhase.Phase2)
        {
            currentPhase = BattlePhase.Phase2;
            OnPhase2Start();
        }
    }
    void OnPhase1Start()
    {
        // 1st phase
    }

    void OnPhase2Start()
    {
        // 2nd phase
    }

    void OnPhase3Start()
    {
        // 3rd phase
    }

    const int PlayerSize = 16;
    const int BulletSize = 10;

    // chain spawn / lifetime configuration (ms)
    const int ChainLifetimeMs = 6000; // how long a chain stays on the field
    // scale applied to chain sprite sizes (0.9 = 10% smaller)
    const double ChainScale = 0.90;

    // When true, chains are NOT removed after a lifetime but only when the minigame ends
    // (ClearChains is called at the end of the attack). Set to `true` to enable the
    // "persist-until-end" variant requested.
    const bool ChainsRemovedOnlyOnEnd = true;

    const double PlayerSpeed = 5.0;
    const double BulletSpeed = 4.0;

    double moveX = 0;
    double moveY = 0;

    int playerHp = 20;
    int playerMaxHp = 20;

    bool gameRunning = false;

    IDispatcherTimer gameLoop;
    IDispatcherTimer spawnTimer;

    // timer to enforce continuous movement while D-Pad is held (touch)
    IDispatcherTimer touchRepeatTimer;

    // current target movement values used by the repeat timer (avoid closure capture)
    double touchTargetX = 0;
    double touchTargetY = 0;

    // small diagnostic cache to avoid flooding the console every tick
    double lastLoggedMoveX = 0;
    double lastLoggedMoveY = 0;

    // track last input times to avoid input sources fighting (touch vs keyboard)
    DateTime lastTouchInput = DateTime.MinValue;
    DateTime lastKeyboardInput = DateTime.MinValue;

#if WINDOWS
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vKey);

    const int VK_W = 0x57;
    const int VK_A = 0x41;
    const int VK_S = 0x53;
    const int VK_D = 0x44;
#endif

    void LeftPressed(object s, EventArgs e)
    {
        // touch input
        lastTouchInput = DateTime.UtcNow;
        StartTouchMove(-PlayerSpeed, 0);
        System.Diagnostics.Debug.WriteLine("LeftPressed invoked");
    }
    void RightPressed(object s, EventArgs e)
    {
        lastTouchInput = DateTime.UtcNow;
        StartTouchMove(PlayerSpeed, 0);
        System.Diagnostics.Debug.WriteLine("RightPressed invoked");
    }
    void UpPressed(object s, EventArgs e)
    {
        lastTouchInput = DateTime.UtcNow;
        StartTouchMove(0, -PlayerSpeed);
        System.Diagnostics.Debug.WriteLine("UpPressed invoked");
    }
    void DownPressed(object s, EventArgs e)
    {
        lastTouchInput = DateTime.UtcNow;
        StartTouchMove(0, PlayerSpeed);
        System.Diagnostics.Debug.WriteLine("DownPressed invoked");
    }
    void StopMove(object s, EventArgs e)
    {
        // avoid spamming logs when already stopped
        if (moveX == 0 && moveY == 0)
            return;

        StopTouchMove();

        moveX = moveY = 0;
        System.Diagnostics.Debug.WriteLine("StopMove invoked");
    }

    void StartTouchMove(double x, double y)
    {
        // set immediately
        touchTargetX = x;
        touchTargetY = y;

        moveX = touchTargetX;
        moveY = touchTargetY;

        // create timer if missing
        if (touchRepeatTimer == null)
        {
            touchRepeatTimer = Dispatcher.CreateTimer();
            touchRepeatTimer.Interval = TimeSpan.FromMilliseconds(50);
            touchRepeatTimer.Tick += (s, e) =>
            {
                // refresh move values to ensure continuous movement
                moveX = touchTargetX;
                moveY = touchTargetY;
            };
        }

        if (!touchRepeatTimer.IsRunning)
            touchRepeatTimer.Start();
    }

    void StopTouchMove()
    {
        if (touchRepeatTimer != null && touchRepeatTimer.IsRunning)
            touchRepeatTimer.Stop();

        // don't zero moveX/moveY here; StopMove will handle it
    }


    void GameTick(object sender, EventArgs e)
    {
        if (!gameRunning) return;
        if (BattleField.Width <= 0 || BattleField.Height <= 0) return;
        // poll keyboard on Windows to support hold/release behavior
#if WINDOWS
        try
        {
            bool w = (GetAsyncKeyState(VK_W) & 0x8000) != 0;
            bool a = (GetAsyncKeyState(VK_A) & 0x8000) != 0;
            bool s = (GetAsyncKeyState(VK_S) & 0x8000) != 0;
            bool d = (GetAsyncKeyState(VK_D) & 0x8000) != 0;
            // don't let keyboard polling override very recent touch input
            if ((DateTime.UtcNow - lastTouchInput).TotalMilliseconds > 100)
            {
                lastKeyboardInput = DateTime.UtcNow;

                if (w)
                {
                    UpPressed(this, EventArgs.Empty);
                }
                else if (s)
                {
                    DownPressed(this, EventArgs.Empty);
                }
                else if (a)
                {
                    LeftPressed(this, EventArgs.Empty);
                }
                else if (d)
                {
                    RightPressed(this, EventArgs.Empty);
                }
                else
                {
                    // no movement keys pressed
                    StopMove(this, EventArgs.Empty);
                }
            }
        }
        catch { }
#endif

        // log only when movement state changes to avoid flooding
        if (moveX != lastLoggedMoveX || moveY != lastLoggedMoveY)
        {
            System.Diagnostics.Debug.WriteLine($"GameTick: moveX={moveX} moveY={moveY} MovementControlsVisible={MovementControls?.IsVisible} gameRunning={gameRunning}");
            lastLoggedMoveX = moveX;
            lastLoggedMoveY = moveY;
        }

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

    async Task MoveBulletSide(BoxView bullet)
    {
        while (gameRunning && BattleField.Children.Contains(bullet))
        {
            bullet.TranslationX += BulletSpeed;

            if (bullet.TranslationX > BattleField.Width + 50)
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

                TakeDamage(1);
                UpdatePlayerHp();
            }
        }
    }
    async void TakeDamage(int dmg)
    {
        if (isInvincible)
            return;

        playerHp -= dmg;
        UpdatePlayerHp();

        isInvincible = true;

        _ = FlashPlayer(); // efekt wizualny

        await Task.Delay(800);

        isInvincible = false;
    }
    async Task FlashPlayer()
    {
        for (int i = 0; i < 4; i++)
        {
            Player.Opacity = 0.3;
            await Task.Delay(100);

            Player.Opacity = 1;
            await Task.Delay(100);
        }
    }
    void UpdatePlayerHp()
    {
        HpLabel.Text = $"{playerHp}/{playerMaxHp}";

        double percent = (double)playerHp / playerMaxHp;
        HpBar.WidthRequest = 45 * percent;

        if (playerHp <= 0)
        {
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
class ChainAttack
{
    public Image Sprite;
    public double DirX;
    public double DirY;
    public double Width;
    public double Height;
}
