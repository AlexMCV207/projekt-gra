namespace stars_beyond;

public partial class GameplayPageMainUI : ContentPage
{
    // Windows-only frame animation helpers (use PNG sequence included in Resources)
#if WINDOWS
    static System.Collections.Concurrent.ConcurrentDictionary<string, List<ImageSource>> gifFrameCache = new System.Collections.Concurrent.ConcurrentDictionary<string, List<ImageSource>>();
    static System.Collections.Concurrent.ConcurrentDictionary<Image, IDispatcherTimer> gifTimers = new System.Collections.Concurrent.ConcurrentDictionary<Image, IDispatcherTimer>();

    // Load frames from packaged PNGs named like "grapple_up_0.png", "grapple_up_1.png", ...
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
        // On-screen D-Pad removed; keyboard (WASD) only
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
    // reduced slider speed for more manageable attack minigame; adjust for Windows if needed
    double sliderSpeed = 30;

    void StartAttackMinigame()
    {
        currentState = TurnState.AttackMinigame;

        DialogLabel.IsVisible = false;
        AttackMinigame.IsVisible = true;

        // Ensure action buttons remain visible during the attack minigame
        try { ActionButtons.IsVisible = true; } catch { }

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
        System.Diagnostics.Debug.WriteLine("FadeOutAndGoToMenu: starting fade");
        await this.FadeTo(0, 400);

        System.Diagnostics.Debug.WriteLine("FadeOutAndGoToMenu: navigating to MainMenu");
        await Shell.Current.GoToAsync("//MainMenu");

        System.Diagnostics.Debug.WriteLine("FadeOutAndGoToMenu: navigation complete, restoring opacity");
        this.Opacity = 1; // reset po powrocie
    }

    // Reset the battle state so a fresh encounter starts.
    public void ResetBattle()
    {
        try
        {
            // stop any running loops/timers
            try { gameLoop?.Stop(); } catch { }
            try { spawnTimer?.Stop(); } catch { }

            gameRunning = false;
            attackRunning = false;
            isInvincible = false;
            isInputLocked = false;
            damageBoostActive = false;

            // clear battlefield and chains
            try
            {
                ClearChains();
                BattleField.Children.Clear();
                BattleField.Children.Add(Player);
            }
            catch { }

            // reset HP and phase
            enemyHp = enemyMaxHp;
            playerHp = playerMaxHp;
            currentPhase = BattlePhase.Phase1;
            currentState = TurnState.PlayerTurn;

            // reset UI
            try
            {
                DialogLabel.IsVisible = true;
                DialogLabel.Text = "* Ready";

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
        DialogLabel.Text = "* Random #!%@>* go!";

        await Task.Delay(800);

        await AnimateBattleTransition();

        // keep action buttons visible during enemy attacks per design

        await RunEnemyAttack();

        await EndEnemyTurn();
    }
    bool attackRunning = false;
    bool isInvincible = false;
    void StartGameLoop()
    {
        gameRunning = true;

        Player.TranslationX = 140;
        Player.TranslationY = 100;

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
            Attack_BasicRain,
            Attack_FastRain,
            Attack_Chaos
        },

            BattlePhase.Phase2 => new List<Func<Task>>
        {
            Attack_Chains,
            Attack_MiddleFlashEdgeChains,
            Attack_BasicRain,
            Attack_FastRain,
            Attack_Chaos
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
            foreach (var b in BattleField.Children.OfType<BoxView>().Where(x => x != Player && !(x is Frame)).ToList())
                BattleField.Children.Remove(b);
        }
        catch { }


        // first attack should allow player to be damaged by chains

        // brief delay before the first chain appears to give player a moment
        await Task.Delay(400);

        var directions = new[] { "up", "right", "down", "left" };
        int dirIndex = 0;

        for (int i = 0; i < 12; i++)
        {
            await SpawnChain(directions[dirIndex], BaseChainSpeed, null, null, stopAfterInitialMove: false);
            dirIndex = (dirIndex + 1) % 4;

            await Task.Delay(1200);
        }

        await Task.Delay(4000);

        ClearChains();
        StopGameLoop();
    }

    // Second phase-1 attack: flash middle area then spawn four edge chains in sequence
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

        // clear any lingering bullets (BoxView) so they don't hit during this scripted attack
        try
        {
            foreach (var b in BattleField.Children.OfType<BoxView>().Where(x => x != Player).ToList())
            {
                BattleField.Children.Remove(b);
            }
        }
        catch { }

        // configuration for the central flash (easy to tweak later)
        double flashWidth = Math.Max(80, BattleField.Width * 0.7); // half width, min size
        double flashHeight = Math.Max(80, BattleField.Height * 0.7); // half height, min size
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

        double doubleSpeed = BaseChainSpeed * 2.0;

        // bottom, near left edge: direction = "up", fixed X near left edge
        await SpawnChain("up", doubleSpeed, fixedX: 20, fixedY: null, stopAfterInitialMove: true);
        await Task.Delay(300);

        // left, near top edge: direction = "right", fixed Y near top edge
        await SpawnChain("right", doubleSpeed, fixedX: null, fixedY: 20, stopAfterInitialMove: true);
        await Task.Delay(300);

        // top, near right edge: direction = "down", fixed X near right edge
        double rightX = Math.Max(0, BattleField.Width - 40);
        await SpawnChain("down", doubleSpeed, fixedX: rightX, fixedY: null, stopAfterInitialMove: true);
        await Task.Delay(300);

        // right, near bottom edge: direction = "left", fixed Y near bottom edge
        double bottomY = Math.Max(0, BattleField.Height - 40);
        await SpawnChain("left", doubleSpeed, fixedX: null, fixedY: bottomY, stopAfterInitialMove: true);

        // allow chains to run briefly
        await Task.Delay(3500);

        ClearChains();
        StopGameLoop();

        // restore vulnerability
        isInvincible = false;
    }

    List<ChainAttack> activeChains = new();
    // remember last two spawn positions for vertical (x) and horizontal (y) chains
    List<double> lastVerticalPositions = new();
    List<double> lastHorizontalPositions = new();
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

        void StartFrameAnimation(Image image, string baseName, int frames, int ms)
        {
            int idx = 0;
            var t = Dispatcher.CreateTimer();
            t.Interval = TimeSpan.FromMilliseconds(ms);
            t.Tick += (s,e) =>
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
                CheckChainCollision(img);

                await Task.Delay(16);
                movedMs += 16;
            }

            // stop movement by zeroing direction; sprite remains on battlefield until cleared
            chain.DirX = 0;
            chain.DirY = 0;
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
    const double ChainScale = 1.2;

    // base chain movement speed (used by SpawnChain; can be multiplied for faster variants)
    const double BaseChainSpeed = 10.0;

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
        // HP bar full width (tuned for Windows UI)
        const double HpBarFullWidth = 120;
        HpBar.WidthRequest = HpBarFullWidth * percent;

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
    public bool StopAfterInitialMove;
}
