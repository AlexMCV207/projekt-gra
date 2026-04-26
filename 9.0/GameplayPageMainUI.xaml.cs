namespace stars_beyond;

public partial class GameplayPageMainUI : ContentPage
{
    // Windows-only frame animation helpers (use PNG sequence included in Resources)
#if WINDOWS
    static System.Collections.Concurrent.ConcurrentDictionary<string, List<ImageSource>> gifFrameCache = new System.Collections.Concurrent.ConcurrentDictionary<string, List<ImageSource>>();
    static System.Collections.Concurrent.ConcurrentDictionary<Image, IDispatcherTimer> gifTimers = new System.Collections.Concurrent.ConcurrentDictionary<Image, IDispatcherTimer>();

    async Task<List<ImageSource>> LoadPngFramesAsync(string prefix, int maxFrames = 16)
    {
        if (gifFrameCache.TryGetValue(prefix, out var cached))
            return cached;

        var frames = new List<ImageSource>();

        for (int i = 0; i < maxFrames; i++)
        {
            // try both variants: prefix + i and prefix + _ + i (handles both "grapple_up0.png" and "grapple_up_0.png")
            var variants = new List<string>();
            variants.Add($"{prefix}{i}.png");
            variants.Add($"{prefix}_{i}.png");

            bool loaded = false;

            foreach (var v in variants)
            {
                // try several likely logical names where the file may be packaged
                var candidates = new[]
                {
                    v,
                    // common MAUI image folder
                    $"Images/{v}",
                    // raw assets folder
                    $"Raw/{v}",
                    $"Resources/Raw/{v}",
                    $"Resources/Images/{v}"
                };

                foreach (var candidate in candidates)
                {
                    try
                    {
                        using var s = await FileSystem.OpenAppPackageFileAsync(candidate);
                        using var ms = new System.IO.MemoryStream();
                        await s.CopyToAsync(ms);
                        var data = ms.ToArray();

                        frames.Add(ImageSource.FromStream(() => new System.IO.MemoryStream(data)));
                        System.Diagnostics.Debug.WriteLine($"Loaded frame {candidate}");
                        loaded = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        // try next candidate
                        System.Diagnostics.Debug.WriteLine($"Frame not found at {candidate}: {ex.Message}");
                    }
                }

                if (loaded)
                    break;
            }

            if (!loaded)
            {
                // no more frames available
                break;
            }
        }

        if (frames.Count > 0)
            gifFrameCache[prefix] = frames;

        return frames;
    }

    async void StartGifAnimation(Image img, string assetName, int frameMs = 200)
    {
        try
        {
            // assetName may be "grapple_up.gif"; convert to PNG prefix "grapple_up_"
            string baseName = assetName.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)
                ? assetName.Substring(0, assetName.Length - 4)
                : assetName;

            string prefix = baseName.EndsWith("_") ? baseName : baseName + "_";

            var frames = await LoadPngFramesAsync(prefix);
            if (frames == null || frames.Count == 0)
                return;

            int idx = 0;
            img.Source = frames[0];

            var timer = Dispatcher.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(frameMs);
            timer.Tick += (s, e) =>
            {
                try
                {
                    idx = (idx + 1) % frames.Count;
                    img.Source = frames[idx];
                }
                catch { }
            };

            if (gifTimers.TryAdd(img, timer))
                timer.Start();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"StartGifAnimation error: {ex}");
        }
    }

    void StopGifAnimation(Image img)
    {
        if (gifTimers.TryRemove(img, out var timer))
        {
            try { timer.Stop(); } catch { }
        }
    }
#endif
	public GameplayPageMainUI()
	{
        InitializeComponent();
        OnPhase1Start();
        HookPlatformKeyboardHandlers();
        BindingContext = this; // for KeyboardAccelerator command bindings in XAML
    }
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
            lastKeyboardInput = DateTime.UtcNow;
            System.Diagnostics.Debug.WriteLine($"Core_KeyDown: {key}");
        }
    }

    private void Core_KeyUp(Windows.UI.Core.CoreWindow sender, Windows.UI.Core.KeyEventArgs args)
    {
        var key = args.VirtualKey;
        if (keysPressed.Contains(key))
            keysPressed.Remove(key);

        System.Diagnostics.Debug.WriteLine($"Core_KeyUp: {key}");
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
    bool isTyping;
    bool isInputLocked = false;
    bool damageBoostActive = false;
    double originalDialogWidth;

    Dictionary<int, int> itemCount = new()
{
    { 0, 5 }, // chocolate d12 dice
    { 1, 3 }, // kiwi
    { 2, 2 }, // hopiumite crystal
    { 3, 2 }  // dmg boost
};

    string[] itemNames =
    {
    "Chocolate D12 dice",
    "Kiwi",
    "Hopiumite crystal",
    "Damage boost (150%)"
};

    TurnState currentState = TurnState.PlayerTurn;

    int enemyMaxHp = 1800;
    int enemyHp = 1800;
    
    CancellationTokenSource typingCts;
    bool introPlayed = false;
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (introPlayed)
            return;

        introPlayed = true;

        await ResetBattle(); // tylko gameplay state

        await StartIntroDialog(); // tylko tekst
    }
    async Task StartIntroDialog()
    {
        isInputLocked = true;

        DialogLabel.IsVisible = true;
        ActionButtons.IsVisible = false;

        await TypeText("* The void feels eerily still");
    }
    async Task ResetBattleAfterDeath()
    {
        isDead = false;
        isInputLocked = false;
        gameRunning = false;

        await ResetBattle();
    }
    async Task TypeText(string text, int delay = 25)
    {
        typingCts?.Cancel();
        typingCts = new CancellationTokenSource();

        var token = typingCts.Token;

        isTyping = true;
        DialogLabel.Text = "";

        foreach (char c in text)
        {
            if (token.IsCancellationRequested)
            {
                DialogLabel.Text = text;
                isTyping = false;
                return;
            }

            DialogLabel.Text += c;
            await Task.Delay(delay);
        }

        isTyping = false;
    }
    void SkipTyping()
    {
        typingCts?.Cancel();
    }
    void StopTyping()
    {
        typingCts?.Cancel();
    }
    void OnDialogClicked(object sender, EventArgs e)
    {
        if (currentState == TurnState.AttackMinigame)
            return;
        SkipTyping();
    }
    async Task ShowEnemyDialog(string text, int duration = 1500)
    {
        var bounds = AbsoluteLayout.GetLayoutBounds(CharacterImage);

        double enemyX = bounds.X;
        double enemyY = bounds.Y;

        double offsetX = bounds.Width + 180;
        double offsetY = 0;

        AbsoluteLayout.SetLayoutBounds(EnemyDialogBox,
            new Rect(enemyX + offsetX, enemyY + offsetY, 450, 220));

        AbsoluteLayout.SetLayoutBounds(EnemyDialogLabel,
            new Rect(enemyX + offsetX + 20, enemyY + offsetY + 20, 410, 180));

        EnemyDialogBox.IsVisible = true;
        EnemyDialogLabel.IsVisible = true;

        await TypeTextEnemy(text);

        await Task.Delay(duration);

        EnemyDialogBox.IsVisible = false;
        EnemyDialogLabel.IsVisible = false;
    }
    CancellationTokenSource enemyTypingCts;

    async Task TypeTextEnemy(string text, int delay = 25)
    {
        enemyTypingCts?.Cancel();
        enemyTypingCts = new CancellationTokenSource();

        var token = enemyTypingCts.Token;

        EnemyDialogLabel.Text = "";

        foreach (char c in text)
        {
            if (token.IsCancellationRequested)
            {
                EnemyDialogLabel.Text = text;
                return;
            }

            EnemyDialogLabel.Text += c;
            await Task.Delay(delay);
        }
    }
    async void FightClicked(object sender, EventArgs e)
    {
        if (isInputLocked || currentState != TurnState.PlayerTurn)
            return;
        if (isTyping)
        {
            SkipTyping();
            return;
        }

        isInputLocked = true;
        ActMenuGrid.IsVisible = false;
        MercyMenuGrid.IsVisible = false;
        ItemMenuGrid.IsVisible = false;

        StartAttackMinigame();
    }
    bool isSliderMoving;
    double sliderX;
    double sliderSpeed = 30;
    bool isPerfectHit;
    bool attackResolved = false;

    void StartAttackMinigame()
    {
        currentState = TurnState.AttackMinigame;

        attackResolved = false;

        DialogLabel.IsVisible = false;
        AttackMinigame.IsVisible = true;

        try { ActionButtons.IsVisible = true; } catch { }

        sliderX = 0;
        AttackSliderBorder.TranslationX = 0;

        isSliderMoving = true;

        _ = AnimateSlider();
    }
    async Task AnimateSlider()
    {
        await Task.Delay(250); // slider delay

        double maxX = AttackMinigame.Width - AttackSlider.Width;

        while (isSliderMoving && currentState == TurnState.AttackMinigame)
        {
            sliderX += sliderSpeed;

            if (sliderX >= maxX)
            {
                isSliderMoving = false;

                if (!attackResolved)
                {
                    attackResolved = true;
                    DealDamage(0);
                    EndAttackMinigame();
                }
                return;
            }

            AttackSliderBorder.TranslationX = sliderX;
            await Task.Delay(16);
        }
    }
    async void OnAttackTap(object sender, EventArgs e)
    {
        if (currentState != TurnState.AttackMinigame || attackResolved)
            return;

        attackResolved = true;

        isSliderMoving = false;

        double center = AttackBar.Width / 2;
        double hit = AttackSliderBorder.TranslationX + (AttackSlider.Width / 2);

        double distance = Math.Abs(hit - center);
        double accuracy = 1 - (distance / center);
        accuracy = Math.Clamp(accuracy, 0, 1);

        int damage = (int)(accuracy * 50);

        isPerfectHit = accuracy > 0.975;

        if (damageBoostActive)
        {
            damage = (int)(damage * 1.5);
            damageBoostActive = false;
        }

        if (isPerfectHit)
            damage = 50;

        await AnimateSliderHit();

        DealDamage(damage);

        EndAttackMinigame();
    }
    async Task AnimateSliderHit()
    {
        int flashes = 5;

        for (int i = 0; i < flashes; i++)
        {
            if (isPerfectHit)
            {
                AttackSlider.Color = Color.FromRgb(0, 255, 255);
            }
            else
            {
                AttackSlider.Color = Colors.Black;
            }

            await Task.Delay(75);

            AttackSlider.Color = Colors.White;

            await Task.Delay(75);
        }
    }
    async Task EndAttackMinigame()
    {
        AttackMinigame.IsVisible = false;
        currentState = TurnState.EnemyTurn;
        await HandleEnemyDialogue();
        StartEnemyTurn();
    }
    async Task HandleEnemyDialogue()
{
    string text = "";

    if (enemyHp > 1200)
    {
        text = "* placeholder";
    }
    else if (enemyHp > 600)
    {
        text = "* ";
    }
    else
    {
        text = "* ";
    }

    await ShowEnemyDialog(text);
}
    async void ActClicked(object sender, EventArgs e)
    {
        if (isInputLocked || currentState != TurnState.PlayerTurn)
            return;
        if (isTyping)
        {
            SkipTyping();
            return;
        }

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

            await Task.Delay(1500);

            StartEnemyTurn();

            isInputLocked = false;
        }
    }
    async void RunAct(int index)
    {
        switch (index)
        {
            case 0:
                await TypeText(GetCheckText());
                break;

            case 1:
                await TypeText(GetOption1Text());
                break;

            case 2:
                await TypeText(GetOption2Text());
                break;

            case 3:
                await TypeText(GetOption3Text());
                break;
        }
    }
    string GetCheckText()
    {
        return "* ??? - ATK ?? DEF ??\n* An amalgamatic physical form of Ahryoul... and your friend";
    }

    string GetOption1Text()
    {
        if (true)
        {
            return "* You tried to reach out to Melch...\n* but you couldn't locate his faint presence";
        }
        else
        {
            // placeholder if it works
        }
    }

    string GetOption2Text()
    {
        return "* No way! Option 2?";
    }

    string GetOption3Text()
    {
        return "* This isn't actually option 3";
    }
    async void MercyClicked(object sender, EventArgs e)
    {
        if (isInputLocked || currentState != TurnState.PlayerTurn)
            return;
        if (isTyping)
        {
            SkipTyping();
            return;
        }

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
                await TypeText(GetSpareText());

                await Task.Delay(1500);

                StartEnemyTurn();
                break;

            case 1: // FLEE
                await TypeText("* You fled");

                await Task.Delay(1500);

                await FadeOutAndGoToMenu();
                break;
        }
    }
    async Task FadeOutAndGoToMenu()
    {
        await this.FadeTo(0, 400);

        await Shell.Current.GoToAsync("//MainMenu");

        this.Opacity = 1;
    }
    public async Task ResetBattle()
    {
        try
        {
            // stop any running loops/timers
            try { gameLoop?.Stop(); } catch { }
            try { spawnTimer?.Stop(); } catch { }

            gameRunning = false;
            attackRunning = false;
            isDead = false;
            isInvincible = false;
            isInputLocked = false;
            damageBoostActive = false;

            try
            {
                ClearChains();
                BattleField.Children.Clear();
                BattleField.Children.Add(Player);
            }
            catch { }

            enemyHp = enemyMaxHp;
            playerHp = playerBaseMaxHp;
            playerMaxHp = playerBaseMaxHp;
            currentPhase = BattlePhase.Phase1;
            currentState = TurnState.PlayerTurn;

            try
            {
                AttackMinigame.IsVisible = false;
                BattleFrame.IsVisible = false;
                ActMenuGrid.IsVisible = false;
                MercyMenuGrid.IsVisible = false;
                ItemMenuGrid.IsVisible = false;
                ActionButtons.IsVisible = true;

                this.Opacity = 1;
            }
            catch { }

            UpdatePlayerHp();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ResetBattle error: {ex}");
        }
    }
    string GetSpareText()
    {
        return "...";
    }
    void ItemClicked(object sender, EventArgs e)
    {
        if (isInputLocked || currentState != TurnState.PlayerTurn)
            return;
        if (isTyping)
        {
            SkipTyping();
            return;
        }

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
            case 0: // Chocolate d12 dice
                {
                    int roll = random.Next(1, 13); // 1–12

                    HealPlayer(roll);

                    string text;

                    if (roll <= 3)
                    {
                        text = "* Your teeth scratch againts plastic... \n* yet somehow, you still recovered +" + roll + " HP";
                    }
                    else if (roll <= 7)
                    {
                        text = "* A burning sensation of sweet chilli spills across your body \n* You recovered " + roll + " HP!";
                    }
                    else if (roll <= 11)
                    {
                        text = "* You feel a nudge of cinnamon and... butterscotch? \n* You recovered " + roll + " HP!";
                    }
                    else // 12
                    {
                        text = "* The dice bestowed you with a blessing and duplicated itself! \n* You recovered your item and " + roll + " HP!";
                        itemCount[index]++;
                    }

                    await TypeText(text);
                    break;
                }

            case 1: // kiwi
                HealPlayer(10);
                await TypeText("* You recovered 10 HP! \n* Do not question the power of a kiwi");
                break;

            case 2: // hopiumite crystal
                overhealActive = true;
                playerMaxHp = 25;
                HealPlayer(playerMaxHp);
                await TypeText("* You consumed the crystal... \n* It fills you with hope! \n* HP fully recovered, overhealed 5HP!");
                break;

            case 3: // boost
                damageBoostActive = true;
                await TypeText("* Used damage boost (+150%)");
                break;
        }

        await Task.Delay(1500);

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

        double targetWidth = 450;

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
        await TypeText("* Random #!%@>* go!");

        await Task.Delay(800);
        await AnimateBattleTransition();
        DialogLabel.Text = "";
        await RunEnemyAttack();
        await EndEnemyTurn();
    }
    bool attackRunning = false;
    bool isInvincible = false;
    void StartGameLoop()
    {
        gameRunning = true;
        double centerX = (BattleField.Width - PlayerSize) / 2;
        double centerY = (BattleField.Height - PlayerSize) / 2;

        Player.TranslationX = centerX;
        Player.TranslationY = centerY;

        AbsoluteLayout.SetLayoutBounds(Player,
            new Rect(0, 0, PlayerSize, PlayerSize));
        try { AbsoluteLayout.SetLayoutBounds(Player, new Rect(0,0,PlayerSize,PlayerSize)); } catch { }
        try { Player.ZIndex = 1001; } catch { }

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

    // choose a spawn coordinate that is not too close to previous same-orientation spawns
    double PickSafePosition(List<double> history, double minVal, double maxVal)
    {
        if (maxVal <= minVal)
            return minVal;

        for (int attempt = 0; attempt < MaxPositionAttempts; attempt++)
        {
            double pos = minVal + random.NextDouble() * (maxVal - minVal);
            bool ok = true;
            foreach (var h in history)
            {
                if (Math.Abs(h - pos) < MinChainSpacing)
                {
                    ok = false;
                    break;
                }
            }

            if (ok)
                return pos;
        }

        // fallback: pick the candidate with the maximum minimal distance to history
        double bestPos = minVal;
        double bestDist = -1;
        for (int s = 0; s < 24; s++)
        {
            double pos = minVal + random.NextDouble() * (maxVal - minVal);
            double dist = history.Count == 0 ? double.PositiveInfinity : history.Min(h => Math.Abs(h - pos));
            if (dist > bestDist)
            {
                bestDist = dist;
                bestPos = pos;
            }
        }

        return bestPos;
    }
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
            Attack_Chains,
            Attack_MiddleFlashEdgeChains,
            Attack_ChainsSmallSafezone,
        },

            BattlePhase.Phase2 => new List<Func<Task>>
        {
            Attack_Chains,
            Attack_MiddleFlashEdgeChains,
        },

            BattlePhase.Phase3 => new List<Func<Task>>
        {
            Attack_Chains,
            Attack_MiddleFlashEdgeChains,
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

        // ensure BattleFrame visible so overlay shows
        try { BattleFrame.IsVisible = true; } catch { }

        // stop any existing spawn timers to avoid stray bullets
        try { spawnTimer?.Stop(); } catch { }

        // clear existing chains and bullets so scripted sequence runs clean
        try { ClearChains(); } catch { }
        try
        {
            foreach (var b in BattleField.Children.OfType<BoxView>().Where(x => !(x is Frame)).ToList())
                BattleField.Children.Remove(b);
        }
        catch { }


        // first attack should allow player to be damaged by chains

        // brief delay before the first chain appears to give player a moment
        await Task.Delay(100);

        var directions = new[] { "up", "right", "down", "left" };
        int dirIndex = 0;

        for (int i = 0; i < 12; i++)
        {
            string dir = directions[dirIndex];
            var (fx, fy) = GetSafeSpawn(dir);

            await ShowChainWarning(dir, fx, fy);
            await Task.Delay(50); // delay between warning and chain spawn for better telegraphing
            await SpawnChain(dir, BaseChainSpeed, fx, fy, false);
            dirIndex = (dirIndex + 1) % 4;
            await Task.Delay(200); // delay between chains
        }

        await Task.Delay(3600);

        ClearChains();
        StopGameLoop();
    }
    async Task Attack_MiddleFlashEdgeChains()
    {
        StartGameLoop();

        // ensure battlefield measured so overlay positions are correct
        int waitMs = 0;
        while ((BattleField.Width <= 0 || BattleField.Height <= 0) && waitMs < 1000)
        {
            await Task.Delay(16);
            waitMs += 16;
        }
        try
        {
            foreach (var b in BattleField.Children.OfType<BoxView>().ToList())
            {
                BattleField.Children.Remove(b);
            }
        }
        catch { }

        // configuration for the central flash (easy to tweak later)
        double flashWidth = Math.Max(80, BattleField.Width * 0.40); // half width, min size
        double flashHeight = Math.Max(80, BattleField.Height * 0.55); // half height, min size
        Color flashColor = Colors.Green;
        int flashCycles = 4;
        int flashIntervalMs = 150;

        // create overlay rectangle centered in battlefield (use Frame so collision code ignores it)
        var overlay = new Frame
        {
            BackgroundColor = flashColor,
            Opacity = 0,
            WidthRequest = flashWidth,
            HeightRequest = flashHeight,
            IsVisible = true,
            HasShadow = false,
            CornerRadius = 0
        };

        double ox = (BattleField.Width - flashWidth) / 2;
        double oy = (BattleField.Height - flashHeight) / 2;

        overlay.InputTransparent = true;
        overlay.ZIndex = 1000;
        AbsoluteLayout.SetLayoutBounds(overlay, new Rect(ox, oy, flashWidth, flashHeight));
        BattleField.Children.Add(overlay);

        // flash overlay a few times
        for (int i = 0; i < flashCycles; i++)
        {
            await overlay.FadeTo(0.4, (uint)flashIntervalMs);
            await Task.Delay(flashIntervalMs);
            await overlay.FadeTo(0, (uint)flashIntervalMs);
            await Task.Delay(flashIntervalMs);
        }

        // remove overlay
        if (BattleField.Children.Contains(overlay))
            BattleField.Children.Remove(overlay);

        // After flash, spawn four chains near the corners/edges in order:
        // 1) from bottom (near left edge)
        // 2) from left (near top edge)
        // 3) from top (near right edge)
        // 4) from right (near bottom edge)
        isInvincible = false;
        // bottom, near left edge: direction = "up", fixed X near left edge
        await SpawnChain("up", BaseChainSpeed, fixedX: 90, fixedY: null, stopAfterInitialMove: true);
        await Task.Delay(300);

        // left, near top edge: direction = "right", fixed Y near top edge
        await SpawnChain("right", BaseChainSpeed, fixedX: null, fixedY: 20, stopAfterInitialMove: true);
        await Task.Delay(300);

        // top, near right edge: direction = "down", fixed X near right edge
        double rightX = Math.Max(0, BattleField.Width - 150);
        await SpawnChain("down", BaseChainSpeed, fixedX: rightX, fixedY: null, stopAfterInitialMove: true);
        await Task.Delay(300);

        // right, near bottom edge: direction = "left", fixed Y near bottom edge
        double bottomY = Math.Max(0, BattleField.Height - 80);
        await SpawnChain("left", BaseChainSpeed, fixedX: null, fixedY: bottomY, stopAfterInitialMove: true);

        await Task.Delay(1500);
        var directions = new[] { "up", "down", "left", "right" };
        string lastDir = null;

        var verticalDirs = new[] { "up", "down" };
        var horizontalDirs = new[] { "left", "right" };

        bool useVertical = random.Next(2) == 0;

        for (int i = 0; i < 8; i++)
        {
            string dir;

            if (useVertical)
            {
                do
                {
                    dir = verticalDirs[random.Next(2)];
                }
                while (dir == lastDir);
            }
            else
            {
                do
                {
                    dir = horizontalDirs[random.Next(2)];
                }
                while (dir == lastDir);
            }

            lastDir = dir;

            useVertical = !useVertical;

            double px = Player.TranslationX + Player.Width / 2;
            double py = Player.TranslationY + Player.Height / 2;

            double targetX = px - 20;
            double targetY = py - 20;

            if (dir == "up" || dir == "down")
                await ShowChainWarning(dir, targetX, null);
            else
                await ShowChainWarning(dir, null, targetY);

            await Task.Delay(100); // delay between warning and chain spawn

            await SpawnTargetedChain(dir, targetX, targetY, BaseChainSpeed * 3);

            await Task.Delay(150); // delay between chains
        }

        await Task.Delay(2000);
        ClearChains();
        StopGameLoop();

    }
    async Task Attack_ChainsSmallSafezone()
    {
        StartGameLoop();

        int waitMs = 0;
        while ((BattleField.Width <= 0 || BattleField.Height <= 0) && waitMs < 1000)
        {
            await Task.Delay(16);
            waitMs += 16;
        }

        try
        {
            foreach (var b in BattleField.Children.OfType<BoxView>().ToList())
            {
                BattleField.Children.Remove(b);
            }
        }
        catch { }

        double safeThickness = 60;

        var safezone = new Frame
        {
            BackgroundColor = Colors.Green,
            Opacity = 0,
            HasShadow = false,
            CornerRadius = 0,
            InputTransparent = true,
            ZIndex = 999
        };

        string[] variants = { "down", "up", "left", "right" }; // determines which edge the safezone will be at
        string variant = variants[random.Next(variants.Length)];
        string lastVariant = variant;
        double x = 0, y = 0, w = 0, h = 0;

        switch (variant)
        {
            case "down":
                x = 0;
                y = BattleField.Height - safeThickness;
                w = BattleField.Width;
                h = safeThickness;
                break;

            case "up":
                x = 0;
                y = 0;
                w = BattleField.Width;
                h = safeThickness;
                break;

            case "left":
                x = 0;
                y = 0;
                w = safeThickness;
                h = BattleField.Height;
                break;

            case "right":
                x = BattleField.Width - safeThickness;
                y = 0;
                w = safeThickness;
                h = BattleField.Height;
                break;
        }
        int flashCycles = 4;
        int flashIntervalMs = 150;
        safezone.WidthRequest = w;
        safezone.HeightRequest = h;
        AbsoluteLayout.SetLayoutBounds(safezone, new Rect(x, y, w, h));
        BattleField.Children.Add(safezone);

        for (int i = 0; i < flashCycles; i++)
        {
            await safezone.FadeTo(0.5, (uint)flashIntervalMs);
            await Task.Delay(flashIntervalMs);
            await safezone.FadeTo(0.1, (uint)flashIntervalMs);
            await Task.Delay(flashIntervalMs);
        }
        safezone.Opacity = 0;
        if (BattleField.Children.Contains(safezone))
        {
            BattleField.Children.Remove(safezone);
        }
        safezone.Opacity = 0.4;

        isInvincible = false;

        await Task.Delay(600);


        double step = 60; // distance between chains in the same wave; adjust as needed for balance and visual clarity

        if (variant == "down")
        {
            int count = 4;
            double startY = 0;
            double chainY = startY;

            for (int i = 0; i < count; i++)
            {
                string dir = (i % 2 == 0) ? "right" : "left";
                await SpawnChain(dir, BaseChainSpeed, fixedX: null, fixedY: chainY);
                await Task.Delay(120);
                chainY += step;
            }
        }
        else if (variant == "up")
        {
            int count = 4;
            double startY = BattleField.Height - 60;
            double chainY = startY;
            for (int i = 0; i < count; i++)
            {
                string dir = (i % 2 == 0) ? "right" : "left";
                await SpawnChain(dir, BaseChainSpeed, fixedX: null, fixedY: chainY);
                await Task.Delay(120);

                chainY -= step;
            }
        }
        else if (variant == "left")
        {
            int count = 6;
            double startX = BattleField.Width - 80;
            double chainX = startX;
            for (int i = 0; i < count; i++)
            {
                string dir = (i % 2 == 0) ? "down" : "up";
                await SpawnChain(dir, BaseChainSpeed, fixedX: chainX, fixedY: null);
                await Task.Delay(120);

                chainX -= step;
            }
        }
        else if (variant == "right")
        {
            int count = 6;
            double startX = 20;
            double chainX = startX;
            for (int i = 0; i < count; i++)
            {
                string dir = (i % 2 == 0) ? "down" : "up";
                await SpawnChain(dir, BaseChainSpeed, fixedX: chainX, fixedY: null);
                await Task.Delay(120);
                chainX += step;
            }
        }

        double targetX = x + w / 2;
        double targetY = (y + h / 2);

        if (lastVariant == "up")
        {
            await Task.Delay(2200);
            // chain z PRAWEJ
            await ShowChainWarning("left", targetX, targetY - 30);
            await ShowChainWarning("left", targetX, targetY + 10);
            await Task.Delay(80);

            double spawnX = BattleField.Width;
            
            await SpawnChain(
                "left",
                BaseChainSpeed * 3,
                fixedX: spawnX,
                fixedY: targetY - 30
            );
            await Task.Delay(50);
            await SpawnChain(
                "left",
                BaseChainSpeed * 3,
                fixedX: spawnX,
                fixedY: targetY + 10
            );
        }
        else if (lastVariant == "down")
        {
            await Task.Delay(2200);
            // chain z LEWEJ
            await ShowChainWarning("left", targetX, targetY);
            await ShowChainWarning("left", targetX, targetY - 50);
            await Task.Delay(80);
            double spawnX = 20;

            await SpawnChain(
                "right",
                BaseChainSpeed * 3,
                fixedX: spawnX,
                fixedY: targetY
            );
            await Task.Delay(50);
            await SpawnChain(
                "right",
                BaseChainSpeed * 3,
                fixedX: spawnX,
                fixedY: targetY - 70
            );           
        }
        else if (lastVariant == "left")
        {
            await Task.Delay(1800);
            // chain z DO£U
            await ShowChainWarning("up", targetX - 40, targetY);
            await ShowChainWarning("up", targetX + 10, targetY);
            await Task.Delay(80);

            double spawnY = BattleField.Height - 20;

            await SpawnChain(
                "up",
                BaseChainSpeed * 3,
                fixedX: targetX - 40,
                fixedY: spawnY
            );
            await Task.Delay(50);
            await SpawnChain(
                "up",
                BaseChainSpeed * 3,
                fixedX: targetX + 10,
                fixedY: spawnY
            );
        }
        else if (lastVariant == "right")
        {
            await Task.Delay(1800);
            // chain z GÓRY
            await ShowChainWarning("up", targetX + 20, targetY);
            await ShowChainWarning("up", targetX - 40, targetY);
            await Task.Delay(80);

            double spawnY = 10;

            await SpawnChain(
                "down",
                BaseChainSpeed * 3,
                fixedX: targetX + 20,
                fixedY: spawnY
            );
            await Task.Delay(50);
            await SpawnChain(
                "down",
                BaseChainSpeed * 3,
                fixedX: targetX - 60,
                fixedY: spawnY
            );
        }

        await Task.Delay(1000);
        // cleanup
        if (BattleField.Children.Contains(safezone))
            BattleField.Children.Remove(safezone);

        ClearChains();
        StopGameLoop();
    }
    async Task ShowChainWarning(string direction, double? fixedX, double? fixedY)
    {
        var warning = new Image
        {
            Aspect = Aspect.Fill
        };

        bool vertical = direction == "up" || direction == "down";

        double width = vertical ? 40 : Math.Max(40, BattleField.Width + 100);
        double height = vertical ? Math.Max(40, BattleField.Height + 100) : 40;

        warning.WidthRequest = width;
        warning.HeightRequest = height;

        // animacja 3-klatkowa
        StartFrameAnimation(
            warning,
            vertical ? "warning_vertical_" : "warning_horizontal_",
            3,
            80
        );

        double x = 0;
        double y = 0;

        switch (direction)
        {
            case "up":
            case "down":
                {
                    double minX = 0;
                    double maxX = Math.Max(0, BattleField.Width - width);

                    double finalX = fixedX.HasValue
                        ? Math.Clamp(fixedX.Value, minX, maxX)
                        : minX;

                    x = finalX;
                    y = 0;
                    break;
                }

            case "left":
            case "right":
                {
                    double minY = 0;
                    double maxY = Math.Max(0, BattleField.Height - height);

                    double finalY = fixedY.HasValue
                        ? Math.Clamp(fixedY.Value, minY, maxY)
                        : minY;

                    x = 0;
                    y = finalY;
                    break;
                }
        }

        AbsoluteLayout.SetLayoutBounds(warning, new Rect(x, y, width, height));
        BattleField.Children.Add(warning);

        await Task.Delay(400); // warning duration

        if (BattleField.Children.Contains(warning))
            BattleField.Children.Remove(warning);
    }
    void StartFrameAnimation(Image image, string baseName, int frames, int ms)
    {
        int idx = 0;
        var t = Dispatcher.CreateTimer();
        t.Interval = TimeSpan.FromMilliseconds(ms);
        t.Tick += (s, e) =>
        {
            try
            {
                image.Source = ImageSource.FromFile($"{baseName}{idx}.png");
                idx = (idx + 1) % frames;
            }
            catch { }
        };
        t.Start();
        // zapisz timer gdzieœ jeœli musisz go zatrzymaæ/usun¹æ póŸniej
    }
    // Second phase-1 attack: flash middle area then spawn four edge chains in sequence
    List<ChainAttack> activeChains = new();
    // remember last two spawn positions for vertical (x) and horizontal (y) chains
    List<double> lastVerticalPositions = new();
    List<double> lastHorizontalPositions = new();
    (double? fx, double? fy) GetSafeSpawn(string direction)
    {
        if (direction == "up" || direction == "down")
        {
            double min = 0;
            double max = BattleField.Width;

            double x = PickSafePosition(lastVerticalPositions, min, max);

            lastVerticalPositions.Add(x);
            if (lastVerticalPositions.Count > 2)
                lastVerticalPositions.RemoveAt(0);

            return (x, null);
        }
        else
        {
            double min = 0;
            double max = BattleField.Height;

            double y = PickSafePosition(lastHorizontalPositions, min, max);

            lastHorizontalPositions.Add(y);
            if (lastHorizontalPositions.Count > 2)
                lastHorizontalPositions.RemoveAt(0);

            return (null, y);
        }
    }
    const double MinChainSpacing = 50; // minimalna odleg³oœæ miêdzy ³añcuchami tej samej orientacji
    const int MaxPositionAttempts = 8;  // ile prób losowania pozycji zanim zaakceptujemy przeciwn¹
    async Task SpawnChain(string direction, double speed = BaseChainSpeed, double? fixedX = null, double? fixedY = null, bool stopAfterInitialMove = false)
    {
        Image img = new Image { Aspect = Aspect.Fill };

        // On Windows load frames first, set initial source, then add to visual tree to avoid native decoder errors
#if WINDOWS
        string gifName = $"grapple_{direction}.gif";
        var frames = await LoadPngFramesAsync(gifName.Substring(0, gifName.Length - 4));
        if (frames != null && frames.Count > 0)
        {
            img.Source = frames[0];
            // start animation timer
            StartGifAnimation(img, gifName, 200);
        }
        else
        {
            // fallback to frame sequence loader
            StartFrameAnimation(img, $"grapple_{direction}_", 2, 200);
        }
#else
        // start animacji 2 klatek, 200ms per frame
        StartFrameAnimation(img, $"grapple_{direction}_", 2, 200);
#endif

        // will add to visual tree after computing bounds (below)

        // decide orientation and size so the chain spans (or exceeds) the battlefield
        bool vertical = direction == "up" || direction == "down";
        double baseWidth = vertical ? 40 : Math.Max(40, BattleField.Width + 700);
        double baseHeight = vertical ? Math.Max(40, BattleField.Height + 700) : 40;

        double width = baseWidth * ChainScale;
        double height = baseHeight * ChainScale;

        img.WidthRequest = width;
        img.HeightRequest = height;

        // use direction-specific sprite files (no programmatic rotation)

        double x = 0;
        double y = 0;

        // movement speed for this chain (may be passed in)
        double chainSpeed = speed;
        double dx = 0;
        double dy = 0;

        switch (direction)
        {
            case "up":
                // spawn above, choose x not too close to recent vertical spawns
                double minX = 0;
                double maxX = Math.Max(0, BattleField.Width - width);
                x = fixedX.HasValue ? Math.Clamp(fixedX.Value, minX, maxX) : PickSafePosition(lastVerticalPositions, minX, maxX);
                // record last vertical spawn (x)
                lastVerticalPositions.Add(x);
                if (lastVerticalPositions.Count > 2) lastVerticalPositions.RemoveAt(0);
                y = -height - 50;
                dx = 0;
                dy = chainSpeed;
                break;

            case "down":
                // spawn below, choose x not too close to recent vertical spawns
                minX = 0;
                maxX = Math.Max(0, BattleField.Width - width);
                x = fixedX.HasValue ? Math.Clamp(fixedX.Value, minX, maxX) : PickSafePosition(lastVerticalPositions, minX, maxX);
                lastVerticalPositions.Add(x);
                if (lastVerticalPositions.Count > 2) lastVerticalPositions.RemoveAt(0);
                y = BattleField.Height + 50;
                dx = 0;
                dy = -chainSpeed;
                break;

            case "left":
                // spawn left, choose y not too close to recent horizontal spawns
                double minY = 0;
                double maxY = Math.Max(0, BattleField.Height - height);
                x = -width - 50;
                y = fixedY.HasValue ? Math.Clamp(fixedY.Value, minY, maxY) : PickSafePosition(lastHorizontalPositions, minY, maxY);
                lastHorizontalPositions.Add(y);
                if (lastHorizontalPositions.Count > 2) lastHorizontalPositions.RemoveAt(0);
                dx = chainSpeed;
                dy = 0;
                break;

            case "right":
                // spawn right, choose y not too close to recent horizontal spawns
                minY = 0;
                maxY = Math.Max(0, BattleField.Height - height);
                x = BattleField.Width + 50;
                y = fixedY.HasValue ? Math.Clamp(fixedY.Value, minY, maxY) : PickSafePosition(lastHorizontalPositions, minY, maxY);
                lastHorizontalPositions.Add(y);
                if (lastHorizontalPositions.Count > 2) lastHorizontalPositions.RemoveAt(0);
                dx = -chainSpeed;
                dy = 0;
                break;
        }

        // If no ImageSource was set (frames failed to load), try direct FromFile fallbacks for the first frame
        if (img.Source == null)
        {
            var tryNames = new[]
            {
                $"grapple_{direction}_0.png",
                $"grapple_{direction}0.png",
                $"Images/grapple_{direction}_0.png",
                $"Resources/Images/grapple_{direction}_0.png",
                $"Raw/grapple_{direction}_0.png",
                $"Resources/Raw/grapple_{direction}_0.png"
            };

            foreach (var n in tryNames)
            {
                try
                {
                    img.Source = ImageSource.FromFile(n);
                    System.Diagnostics.Debug.WriteLine($"SpawnChain: set ImageSource.FromFile({n})");
                    break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"SpawnChain: FromFile({n}) failed: {ex.Message}");
                }
            }
        }

        // ensure Image is visible and add to layout
        img.IsVisible = true;
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

        // set behavior flags
        chain.StopAfterInitialMove = stopAfterInitialMove;

        activeChains.Add(chain);

        _ = MoveChain(chain);

        // schedule removal after configured lifetime unless configured to persist until end
        if (!ChainsRemovedOnlyOnEnd)
            _ = RemoveChainAfterDelay(img, ChainLifetimeMs);
    }
    async Task SpawnTargetedChain(string direction, double targetX, double targetY, double speed = BaseChainSpeed)
    {
        Image img = new Image { Aspect = Aspect.Fill };

#if WINDOWS
    string gifName = $"grapple_{direction}.gif";
    var frames = await LoadPngFramesAsync(gifName.Substring(0, gifName.Length - 4));
    if (frames != null && frames.Count > 0)
    {
        img.Source = frames[0];
        StartGifAnimation(img, gifName, 200);
    }
    else
    {
        StartFrameAnimation(img, $"grapple_{direction}_", 2, 200);
    }
#else
        StartFrameAnimation(img, $"grapple_{direction}_", 2, 200);
#endif

        bool vertical = direction == "up" || direction == "down";

        double baseWidth = vertical ? 40 : Math.Max(40, BattleField.Width + 700);
        double baseHeight = vertical ? Math.Max(40, BattleField.Height + 700) : 40;

        double width = baseWidth * ChainScale;
        double height = baseHeight * ChainScale;

        img.WidthRequest = width;
        img.HeightRequest = height;

        double centerX = targetX;
        double centerY = targetY;

        double x = 0;
        double y = 0;

        double dx = 0;
        double dy = 0;

        const double verticalOffsetX = 20; // how much to offset vertical chains from target Y to ensure they hit the player
        const double horizontalOffsetY = 20; // how much to offset horizontal chains from target X

        double chainSpeed = speed;

        switch (direction)
        {
            case "up":
                {
                    double minX = 0;
                    double maxX = Math.Max(0, BattleField.Width - width);

                    x = Math.Clamp(centerX - width / 2, minX, maxX);
                    y = -height - 50;

                    dx = 0;
                    dy = chainSpeed;
                    break;
                }

            case "down":
                {
                    double minX = 0;
                    double maxX = Math.Max(0, BattleField.Width - width);

                    x = Math.Clamp(centerX - width / 2 + verticalOffsetX, minX, maxX);
                    y = BattleField.Height + 50;

                    dx = 0;
                    dy = -chainSpeed;
                    break;
                }

            case "left":
                {
                    double minY = 0;
                    double maxY = Math.Max(0, BattleField.Height - height);

                    x = -width - 50;
                    y = Math.Clamp(centerY - height / 2 + horizontalOffsetY, minY, maxY);

                    dx = chainSpeed;
                    dy = 0;
                    break;
                }

            case "right":
                {
                    double minY = 0;
                    double maxY = Math.Max(0, BattleField.Height - height);

                    x = BattleField.Width + 50;
                    y = Math.Clamp(centerY - height / 2, minY, maxY);

                    dx = -chainSpeed;
                    dy = 0;
                    break;
                }
        }

        img.IsVisible = true;
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
                {
#if WINDOWS
                    try { StopGifAnimation(img); } catch { }
#endif
                    BattleField.Children.Remove(img);
                }
            });
        }
    }
    async Task MoveChain(ChainAttack chain)
    {
        var img = chain.Sprite;

        if (chain.StopAfterInitialMove)
        {
            // move briefly so the visible front part passes, then stop movement and keep sprite until ClearChains
            int movedMs = 0;
            while (movedMs < ChainInitialMoveMs && gameRunning && BattleField.Children.Contains(img))
            {
                img.TranslationX += chain.DirX;
                img.TranslationY += chain.DirY;

                // only check collisions during initial motion

                await Task.Delay(16);
                movedMs += 16;
            }

            // stop movement by zeroing direction; sprite remains on battlefield until cleared
            chain.DirX = 0;
            chain.DirY = 0;
            while (gameRunning && BattleField.Children.Contains(img))
            {
                CheckChainCollision(img);
                await Task.Delay(16);
            }
        }
        else
        {
            // continue moving until game stops or chain goes off-screen
            while (gameRunning && BattleField.Children.Contains(img))
            {
                img.TranslationX += chain.DirX;
                img.TranslationY += chain.DirY;

                CheckChainCollision(img);

                if (!ChainsRemovedOnlyOnEnd && IsOutside(chain))
                {
#if WINDOWS
                    try { StopGifAnimation(img); } catch { }
#endif
                    BattleField.Children.Remove(img);
                    activeChains.Remove(chain);
                    break;
                }

                await Task.Delay(16);
            }
        }
    }
    void RemoveChain(ChainAttack chain)
    {
        if (BattleField.Children.Contains(chain.Sprite))
        {
#if WINDOWS
            try { StopGifAnimation(chain.Sprite); } catch { }
#endif
            BattleField.Children.Remove(chain.Sprite);
        }

        try { activeChains.Remove(chain); } catch { }
    }

    void ClearChains()
    {
        foreach (var c in activeChains)
        {
            if (BattleField.Children.Contains(c.Sprite))
            {
#if WINDOWS
                try { StopGifAnimation(c.Sprite); } catch { }
#endif
                BattleField.Children.Remove(c.Sprite);
            }
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
        if (true) // start player turn text
        {
            await TypeText("* Save him");
        } else
        {
            // start player turn text for subsequent turns
        }
        isInputLocked = false;
    }
    void DealDamage(int damage)
    {
        enemyHp -= damage;
        enemyHp = Math.Max(0, enemyHp);
        CheckPhaseTransition();
        _ = ShowDamageText(damage);

        if (enemyHp <= 0)
        {
            
        }
    }
    void CheckPhaseTransition()
    {
        if (enemyHp <= 600 && currentPhase != BattlePhase.Phase3)
        {
            currentPhase = BattlePhase.Phase3;
            OnPhase3Start();
        }
        else if (enemyHp <= 1200 && currentPhase != BattlePhase.Phase2)
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
    const double ChainScale = 1.4;

    // base chain movement speed (used by SpawnChain; can be multiplied for faster variants)
    const double BaseChainSpeed = 18.0;

    // how long a chain moves after spawn (ms) before stopping and remaining on screen
    // increased so the leading edge has time to pass and not remain visible
    const int ChainInitialMoveMs = 600;

    // When true, chains are NOT removed after a lifetime but only when the minigame ends
    // (ClearChains is called at the end of the attack). Set to `true` to enable the
    // "persist-until-end" variant requested.
    const bool ChainsRemovedOnlyOnEnd = true;

    const double PlayerSpeed = 6.0;
    const double BulletSpeed = 5.0;

    double moveX = 0;
    double moveY = 0;

    int playerHp = 20;
    int playerMaxHp = 20;
    int playerBaseMaxHp = 20;
    bool overhealActive = false;

    bool gameRunning = false;

    IDispatcherTimer gameLoop;
    IDispatcherTimer spawnTimer;

    // keyboard-only input: no on-screen D-Pad or touch repeat timer

    // small diagnostic cache to avoid flooding the console every tick
    double lastLoggedMoveX = 0;
    double lastLoggedMoveY = 0;

    // keep minimal keyboard tracking (no touch input)
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
        // removed: on-screen controls disabled
    }
    void RightPressed(object s, EventArgs e)
    {
        // removed: on-screen controls disabled
    }
    void UpPressed(object s, EventArgs e)
    {
        // removed: on-screen controls disabled
    }
    void DownPressed(object s, EventArgs e)
    {
        // removed: on-screen controls disabled
    }
    // removed touch-based handlers and timers


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

            lastKeyboardInput = DateTime.UtcNow;

            // set movement directly for continuous WASD behavior
            if (w && !s)
            {
                moveX = 0;
                moveY = -PlayerSpeed;
            }
            else if (s && !w)
            {
                moveX = 0;
                moveY = PlayerSpeed;
            }
            else if (a && !d)
            {
                moveX = -PlayerSpeed;
                moveY = 0;
            }
            else if (d && !a)
            {
                moveX = PlayerSpeed;
                moveY = 0;
            }
            else if (!w && !a && !s && !d)
            {
                moveX = 0;
                moveY = 0;
            }
        }
        catch { }
#endif

        // log only when movement state changes to avoid flooding
        if (moveX != lastLoggedMoveX || moveY != lastLoggedMoveY)
        {
            System.Diagnostics.Debug.WriteLine($"GameTick: moveX={moveX} moveY={moveY} gameRunning={gameRunning}");
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
            .Where(x => true))
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
        if(isDead) 
            return;
        if (isInvincible)
            return;

        playerHp -= dmg;

        if (overhealActive)
        {
            if (playerHp <= playerBaseMaxHp)
            {
                playerMaxHp = playerBaseMaxHp;
                overhealActive = false;
            } else
            {
                playerMaxHp = playerHp;
            }
        }

        UpdatePlayerHp();
        isInvincible = true;
        _ = FlashPlayer(); 

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
    bool isDead = false;
    async void UpdatePlayerHp()
    { 
        HpLabel.Text = $"{playerHp}/{playerMaxHp}";

        double percent = (double)playerHp / playerMaxHp;
        const double HpBarFullWidth = 120;
        HpBar.WidthRequest = HpBarFullWidth * percent;

        if (playerHp <= 0 && !isDead)
        {
            _ = HandlePlayerDeath();
        }
    }
    async Task HandlePlayerDeath()
    {
        if (isDead) return;
        isDead = true;
        isInvincible = true;
        gameRunning = false;
        isInputLocked = true;

        try
        {
            gameLoop?.Stop();
            spawnTimer?.Stop();
        }
        catch { }

        ClearChains();

        await Task.Delay(200);

        await Shell.Current.GoToAsync("//DeathScreen");
    }
    public async Task RestartFromDeath()
    {
        isDead = false;

        playerHp = playerMaxHp;
        enemyHp = enemyMaxHp;

        gameRunning = false;
        isInvincible = false;
        isInputLocked = false;

        try
        {
            gameLoop?.Stop();
            spawnTimer?.Stop();
        }
        catch { }

        ClearChains();

        BattleField.Children.Clear();
        BattleField.Children.Add(Player);

        UpdatePlayerHp();

        currentState = TurnState.PlayerTurn;

        DialogLabel.IsVisible = true;
        DialogLabel.Text = "* The void feels eerily still";

        ActionButtons.IsVisible = true;

        await Task.Delay(50);
    }
    bool IsColliding(VisualElement a, VisualElement b)
{
    Rect r1;
    Rect r2;
    const double PlayerHitboxScale = 0.25; // hitbox scaling (wackyasshell)
        if (a == Player)
    {
        double sizeW = a.Width * PlayerHitboxScale;
        double sizeH = a.Height * PlayerHitboxScale;

        r1 = new Rect(
            a.TranslationX + (a.Width - sizeW) / 2,
            a.TranslationY + (a.Height - sizeH) / 2,
            sizeW,
            sizeH);
    }
    else
    {
            r1 = new Rect(a.TranslationX, a.TranslationY, a.Width, a.Height);
        }

    if (b == Player)
    {
        double sizeW = b.Width * PlayerHitboxScale;
        double sizeH = b.Height * PlayerHitboxScale;

        r2 = new Rect(
            b.TranslationX + (b.Width - sizeW) / 2,
            b.TranslationY + (b.Height - sizeH) / 2,
            sizeW,
            sizeH);
    }
    else
    {
            r2 = new Rect(b.X + b.TranslationX, b.Y + b.TranslationY, b.Width, b.Height);
        }

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
    public bool StopAfterInitialMove;
}