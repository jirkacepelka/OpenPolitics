using Godot;
using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Politics;
using OpenPolitics.Core.Presentation;

namespace OpenPolitics.Client;

/// <summary>A washed-out field of node discs behind the start screens, echoing the map.</summary>
public partial class StartBackdrop : Control
{
    public StartBackdrop() { MouseFilter = MouseFilterEnum.Ignore; TextureRepeat = TextureRepeatEnum.Enabled; }
    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), AppTheme.MapBg);
        DrawTextureRect(AppTheme.GridDot, new Rect2(Vector2.Zero, Size), true);
        var rng = new RandomNumberGenerator { Seed = 11 };
        float cell = 96;
        for (float y = cell / 2; y < Size.Y + cell; y += cell)
            for (float x = cell / 2; x < Size.X + cell; x += cell)
            {
                var p = new Vector2(x + rng.RandfRange(-26, 26), y + rng.RandfRange(-26, 26));
                float r = rng.RandfRange(12, 34);
                bool policy = rng.Randf() < 0.4f;
                DrawCircle(p, r, policy ? new Color(AppTheme.ChromeTint, 0.8f) : new Color(1, 1, 1, 0.7f));
                DrawArc(p, r - 2, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau * rng.RandfRange(0.3f, 1f), 40, policy ? new Color(AppTheme.Chrome, 0.25f) : new Color("dcd7ce"), 2, true);
            }
        var c = Size / 2;
        for (int i = 0; i < 3; i++)
        {
            float a = -Mathf.Pi / 2 + Mathf.Tau * (i + 0.5f) / 3;
            var u = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            for (float d = 80; d < Size.Length(); d += 12) DrawLine(c + u * d, c + u * (d + 5), new Color("d6d0c6"), 1.2f, true);
        }
    }
    public override void _Notification(int what) { if (what == NotificationResized) QueueRedraw(); }
}

/// <summary>A small compass square with one party's dot (party list and party card on the new-game screen).</summary>
public partial class MiniCompass : Control
{
    private readonly Vector2 _pos; private readonly Color _color;
    public MiniCompass(double x, double y, Color color, float zoom, float size = 40)
    {
        _pos = new Vector2((float)Math.Clamp(x * zoom, -1, 1), (float)Math.Clamp(y * zoom, -1, 1)); _color = color;
        CustomMinimumSize = new Vector2(size, size); MouseFilter = MouseFilterEnum.Ignore; SizeFlagsVertical = SizeFlags.ShrinkCenter;
    }
    public override void _Draw()
    {
        var r = new Rect2(Vector2.Zero, Size);
        DrawStyleBox(AppTheme.Box(AppTheme.Panel, 6), r);
        DrawLine(new Vector2(r.Size.X / 2, 3), new Vector2(r.Size.X / 2, r.Size.Y - 3), new Color("c9c4bb"), 1);
        DrawLine(new Vector2(3, r.Size.Y / 2), new Vector2(r.Size.X - 3, r.Size.Y / 2), new Color("c9c4bb"), 1);
        var p = new Vector2(r.Size.X * (_pos.X + 1) / 2, r.Size.Y * (1 - (_pos.Y + 1) / 2));
        float dot = Math.Max(4.5f, r.Size.X / 8);
        DrawCircle(p, dot + 1, new Color("ffffff"));
        DrawCircle(p, dot, _color);
    }
}

/// <summary>
/// The two screens before a game: the main menu (continue, new game, load, settings, quit) and the new-game screen (scenario, party,
/// seed, start) laid out like a strategy game's setup: choices on the left and in the middle, what you are about to play on the right.
/// </summary>
public static class StartScreen
{
    private static void Backdrop(Main main)
    {
        var backdrop = new StartBackdrop();
        backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        main.AddChild(backdrop);
        var veil = new ColorRect { Color = new Color(0.93f, 0.92f, 0.89f, 0.45f), MouseFilter = Control.MouseFilterEnum.Ignore };
        veil.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        main.AddChild(veil);
    }

    // ---- main menu ------------------------------------------------------------------------------------------

    public static void BuildMenu(Main main, PackSet packs, string language)
    {
        var t = new Localizer(packs, language);
        Backdrop(main);
        bool compact = Layout.Compact;

        var panel = new PanelContainer { ThemeTypeVariation = "Sheet" };
        panel.SetAnchorsPreset(Control.LayoutPreset.LeftWide);
        panel.AnchorRight = compact ? 0.55f : 0.36f; panel.OffsetRight = 0;
        panel.CustomMinimumSize = new Vector2(compact ? 0 : 420, 0);
        var column = Ui.VBox(0);
        panel.AddChild(Ui.Margin(column, compact ? 28 : 48, compact ? 24 : 36));

        column.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1f, MouseFilter = Control.MouseFilterEnum.Ignore });
        var title = Ui.L("OpenPolitics", Tx.Display);
        title.AddThemeFontSizeOverride("font_size", compact ? 34 : 46);
        column.AddChild(title);
        column.AddChild(Ui.Gap(6));
        column.AddChild(Ui.L(t.T("ui.client.tagline"), Tx.Body, AppTheme.Ink2, true));
        column.AddChild(Ui.Gap(compact ? 18 : 30));

        var list = Ui.VBox(6);
        var saves = Session.SavesNewestFirst();
        bool primaryUsed = false;
        void Item(string text, IconKind icon, Action act, bool disabled = false, string? tip = null, bool primary = false)
        {
            var b = Ui.Btn(text, act, primary ? "ButtonPlay" : "ButtonMenu", icon, tip, disabled, compact ? 46 : 52);
            b.Alignment = HorizontalAlignment.Left;
            b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            if (primary) { b.AddThemeFontSizeOverride("font_size", 18); b.AddThemeConstantOverride("h_separation", 14); }
            list.AddChild(b);
        }
        if (saves.Count > 0)
        {
            string name = System.IO.Path.GetFileName(saves[0]).Replace(".save.yaml", "");
            Item(t.T("ui.menu.continue"), IconKind.Play, () => main.LoadGame(saves[0]), tip: name, primary: true);
            primaryUsed = true;
        }
        Item(t.T("ui.client.new_game"), IconKind.Plus, main.ShowNewGame, primary: !primaryUsed);
        Item(t.T("ui.start.load_game"), IconKind.Save, () => main.ShowLoad(), disabled: saves.Count == 0, tip: saves.Count == 0 ? t.T("ui.start.no_saves") : null);
        Item(t.T("ui.menu.settings"), IconKind.Gear, main.ShowSettings);
        Item(t.T("ui.menu.quit_game"), IconKind.Close, main.QuitGame);
        column.AddChild(list);

        column.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.4f, MouseFilter = Control.MouseFilterEnum.Ignore });
        column.AddChild(Ui.L(t.T("ui.client.illustrative"), Tx.Small, AppTheme.Ink3, true));
        main.AddChild(panel);
    }

    // ---- new game -------------------------------------------------------------------------------------------

    public static void BuildNewGame(Main main, PackSet packs, string language)
    {
        var t = new Localizer(packs, language);
        Backdrop(main);
        bool compact = Layout.Compact || Layout.Short;

        var scenarios = packs.Scenarios.OrderByDescending(s => s.Politics != null).ThenBy(s => s.Id, StringComparer.Ordinal).ToList();
        if (scenarios.Count == 0) { main.AddChild(Ui.Margin(Ui.L("No scenario", Tx.Body), 24)); return; }
        int chosen = 0;
        string? party = scenarios[0].Politics?.PlayerParty;

        var partyBox = Ui.VBox(compact ? 6 : 4);
        var detailBox = Ui.VBox(12);
        var scenarioBox = Ui.VBox(8);

        // The seed and the start button live outside the boxes that are rebuilt on every choice.
        var seed = new LineEdit { Text = ((Time.GetTicksMsec() % 99999) + 1).ToString(), CustomMinimumSize = new Vector2(104, 34), MaxLength = 9, TooltipText = t.T("ui.start.seed_hint") };
        var seedRow = Ui.HBox(8);
        seedRow.AddChild(Ui.L(t.T("ui.client.seed"), Tx.Small, AppTheme.Ink3));
        seedRow.AddChild(Ui.Spacer());
        seedRow.AddChild(seed);
        seedRow.AddChild(Ui.Btn("", () => { seed.Text = Random.Shared.Next(1, 999999).ToString(); NewStart(); }, "ButtonSecondary", IconKind.Arrows, t.T("ui.start.reroll"), false, 34));
        var start = Ui.Btn(t.T("ui.client.start"), () =>
        {
            ulong value = SeedValue();
            main.StartGame(scenarios[chosen].Id, value, language, scenarios[chosen].Politics != null ? party : null, randomStart: true);
        }, "ButtonPlay", IconKind.Play, null, false, 50);
        start.AddThemeFontOverride("font", AppTheme.Bold); start.AddThemeFontSizeOverride("font_size", 16);
        start.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        void RefreshScenarios()
        {
            Ui.Clear(scenarioBox);
            if (compact)
            {
                // a small window: a drop-down instead of a list of cards
                var pick = new OptionButton { FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 40), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
                foreach (var sc0 in scenarios) pick.AddItem(t.T("name." + sc0.Id));
                pick.Selected = chosen;
                pick.ItemSelected += i =>
                {
                    int k = (int)i;
                    if (chosen == k) return;
                    chosen = k; party = scenarios[k].Politics?.PlayerParty; RefreshScenarios(); RefreshParties(); RefreshDetail();
                };
                scenarioBox.AddChild(pick);
            }
            var group = new ButtonGroup();
            for (int i = 0; i < scenarios.Count && !compact; i++)
            {
                int k = i;
                var b = new Button { ToggleMode = true, ButtonGroup = group, ButtonPressed = i == chosen, ThemeTypeVariation = "ButtonCard", FocusMode = Control.FocusModeEnum.None,
                    SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseDefaultCursorShape = Control.CursorShape.PointingHand, CustomMinimumSize = new Vector2(0, 58) };
                var name = Ui.L(t.T("name." + scenarios[i].Id), Tx.BodyStrong, null, true);
                name.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
                var m = Ui.Margin(name, 12, 6);
                m.SetAnchorsPreset(Control.LayoutPreset.FullRect);
                Detail.SetIgnore(m);
                b.AddChild(m);
                b.Pressed += () => { if (chosen == k) return; chosen = k; party = scenarios[k].Politics?.PlayerParty; RefreshScenarios(); RefreshParties(); RefreshDetail(); };
                scenarioBox.AddChild(b);
            }
            var sc = scenarios[chosen];
            scenarioBox.AddChild(Ui.Panel(Ui.L(t.Description(sc.Id) ?? t.T(sc.Politics != null ? "ui.start.politics_desc" : "ui.start.sandbox_desc"), Tx.Small, AppTheme.Ink2, true), "Inset"));
        }

        // the seed decides the starting poll and government (decision 0020), so the party table follows it
        ulong SeedValue() => ulong.TryParse(seed.Text, out var v) && v > 0 ? v : 1;
        IReadOnlyList<PartyOption> Options() => PartyChoice.Options(packs, scenarios[chosen].Id, SeedValue(), true);
        void NewStart() { RefreshParties(); RefreshDetail(); }
        seed.TextChanged += _ => NewStart();
        float ZoomOf(IReadOnlyList<PartyOption> options)
        {
            float far = options.Count == 0 ? 0 : (float)options.Max(o => Math.Max(Math.Abs(o.X), Math.Abs(o.Y)));
            return far > 0.02f ? Math.Min(4f, 0.85f / far) : 1f;
        }

        // One row per party, largest first: colour, name, starting role, poll, seats, place on the compass.
        void RefreshParties()
        {
            Ui.Clear(partyBox);
            var options = Options();
            if (options.Count == 0)
            {
                partyBox.AddChild(Ui.Panel(Ui.L(t.T("ui.start.sandbox_desc"), Tx.Body, AppTheme.Ink2, true), "Inset"));
                return;
            }
            float zoom = ZoomOf(options);
            if (!compact)
            {
                var head = Ui.HBox(12);
                var first = Ui.HBox(0);
                first.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                first.AddChild(Ui.Gap(23)); // the party's colour dot and its gap
                first.AddChild(Ui.L(t.T("ui.start.col.party"), Tx.Overline, AppTheme.Ink3));
                head.AddChild(first);
                head.AddChild(Cell(Ui.L(t.T("ui.start.col.role"), Tx.Overline, AppTheme.Ink3), 112));
                head.AddChild(Cell(Ui.Right(Ui.L(t.T("ui.start.col.poll"), Tx.Overline, AppTheme.Ink3)), 64));
                head.AddChild(Cell(Ui.Right(Ui.L(t.T("ui.start.col.seats"), Tx.Overline, AppTheme.Ink3)), 56));
                head.AddChild(Cell(Ui.Right(Ui.L(t.T("ui.start.col.position"), Tx.Overline, AppTheme.Ink3)), 34));
                partyBox.AddChild(Ui.Margin(head, 14, 0));
            }
            var group = new ButtonGroup();
            foreach (var o in options)
            {
                string id = o.PartyId;
                var b = new Button { ToggleMode = true, ButtonGroup = group, ButtonPressed = id == party, ThemeTypeVariation = "ButtonRow", FocusMode = Control.FocusModeEnum.None,
                    SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseDefaultCursorShape = Control.CursorShape.PointingHand, CustomMinimumSize = new Vector2(0, compact ? 46 : 52),
                    TooltipText = t.Name(id) + " · " + t.T("ui.start.role." + o.Role.ToString().ToLowerInvariant()) };
                var row = Ui.HBox(12);
                var name = Ui.HBox(10);
                name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                name.AddChild(new ColorDot(Ui.PartyColor(packs, id), 6.5f));
                var nl = Ui.L(t.Name(id), Tx.BodyStrong);
                nl.ClipText = true; nl.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; nl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; nl.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
                name.AddChild(nl);
                row.AddChild(name);
                var pill = RolePill(t, o.Role, shortText: true);
                row.AddChild(Cell(pill, compact ? 96 : 112));
                var poll = Ui.Right(Ui.L(Ui.Num(o.Support * 100) + " %", Tx.Data));
                poll.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
                row.AddChild(Cell(poll, 64));
                if (!compact)
                {
                    var seats = Ui.Right(Ui.L(o.Seats.ToString(), Tx.Data));
                    seats.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
                    row.AddChild(Cell(seats, 56));
                    row.AddChild(new MiniCompass(o.X, o.Y, Ui.PartyColor(packs, id), zoom, 34));
                }
                var m = Ui.Margin(row, 12, 5);
                m.SetAnchorsPreset(Control.LayoutPreset.FullRect);
                Detail.SetIgnore(m);
                b.AddChild(m);
                b.Pressed += () => { party = id; RefreshParties(); RefreshDetail(); };
                partyBox.AddChild(b);
            }
        }

        // What you are about to play: the party (or the scenario) in full, and what its role means.
        void RefreshDetail()
        {
            Ui.Clear(detailBox);
            var options = Options();
            if (options.Count == 0)
            {
                var sc = scenarios[chosen];
                detailBox.AddChild(Ui.L(t.T("name." + sc.Id), Tx.Title, null, true));
                detailBox.AddChild(Ui.L(t.Description(sc.Id) ?? t.T("ui.start.sandbox_desc"), Tx.Body, AppTheme.Ink2, true));
                return;
            }
            var o = options.FirstOrDefault(x => x.PartyId == party) ?? options[0];
            var color = Ui.PartyColor(packs, o.PartyId);
            var head = Ui.HBox(10);
            head.AddChild(new ColorDot(color, 8f));
            var nm = Ui.L(t.Name(o.PartyId), Tx.Title, null, true);
            head.AddChild(nm);
            detailBox.AddChild(head);
            detailBox.AddChild(RolePill(t, o.Role, shortText: false));

            var figures = Ui.HBox(22);
            figures.AddChild(Figure(Ui.Num(o.Support * 100) + " %", t.T("ui.start.col.poll")));
            figures.AddChild(Figure(o.Seats.ToString(), t.T("ui.start.col.seats")));
            detailBox.AddChild(figures);

            var pos = Ui.HBox(12);
            pos.AddChild(new MiniCompass(o.X, o.Y, color, ZoomOf(options), compact ? 56 : 72));
            var words = Ui.VBox(2);
            words.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; words.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            words.AddChild(Ui.L(t.T("ui.start.col.position"), Tx.Small, AppTheme.Ink3));
            words.AddChild(Ui.L(CompassWords(t, o), Tx.Body, AppTheme.Ink, true));
            pos.AddChild(words);
            detailBox.AddChild(pos);

            var hint = Ui.HBox(10);
            hint.AddChild(new IconView(o.Role == Role.Opposition ? IconKind.Alert : IconKind.Ballot, 18, AppTheme.Chrome));
            hint.AddChild(Ui.L(t.T("ui.start.role_hint." + o.Role.ToString().ToLowerInvariant(), t.Name(o.PartyId)), Tx.Small, AppTheme.Ink2, true));
            detailBox.AddChild(Ui.Panel(hint, "Inset"));
        }

        RefreshScenarios(); RefreshParties(); RefreshDetail();

        // ---- frame
        var root = Ui.VBox(0);
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var bar = Ui.HBox(16);
        bar.AddChild(Ui.Btn(t.T("ui.menu.back"), main.ShowStart, "ButtonSecondary", IconKind.Back, null, false, 38));
        bar.AddChild(Ui.L(t.T("ui.client.new_game"), Tx.Title));
        root.AddChild(Ui.Panel(Ui.Margin(bar, compact ? 16 : 28, 12), "Sheet"));
        root.AddChild(new ColorRect { Color = AppTheme.Border, CustomMinimumSize = new Vector2(0, 1), MouseFilter = Control.MouseFilterEnum.Ignore });

        if (compact)
        {
            var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
            var body = Ui.VBox(14);
            body.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            if (scenarios.Count > 1) { body.AddChild(Ui.Section(t.T("ui.client.scenario"))); body.AddChild(scenarioBox); }
            body.AddChild(Ui.Section(t.T("ui.start.your_party")));
            body.AddChild(partyBox);
            body.AddChild(detailBox);
            var pad = Ui.Margin(body, 16, 14);
            pad.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            scroll.AddChild(pad);
            root.AddChild(scroll);
            root.AddChild(new ColorRect { Color = AppTheme.Border, CustomMinimumSize = new Vector2(0, 1), MouseFilter = Control.MouseFilterEnum.Ignore });
            var foot = Ui.VBox(8);
            foot.AddChild(seedRow);
            foot.AddChild(start);
            root.AddChild(Ui.Panel(Ui.Margin(foot, 16, 10), "Sheet"));
        }
        else
        {
            var cols = Ui.HBox(16);
            cols.SizeFlagsVertical = Control.SizeFlags.ExpandFill;

            var left = Ui.VBox(12);
            left.AddChild(Ui.Section(t.T("ui.client.scenario")));
            left.AddChild(scenarioBox);
            left.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
            left.AddChild(Ui.L(t.T("ui.client.illustrative"), Tx.Small, AppTheme.Ink3, true));
            var leftCard = Ui.Panel(left, "Card", false);
            leftCard.CustomMinimumSize = new Vector2(Layout.Wide ? 340 : 290, 0);
            leftCard.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            cols.AddChild(leftCard);

            var mid = Ui.VBox(10);
            mid.AddChild(Ui.Section(t.T("ui.start.your_party")));
            var partyScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            partyBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            partyScroll.AddChild(partyBox);
            mid.AddChild(partyScroll);
            var midCard = Ui.Panel(mid, "Card");
            midCard.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            cols.AddChild(midCard);

            var right = Ui.VBox(12);
            var detailScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            detailBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            detailScroll.AddChild(detailBox);
            right.AddChild(detailScroll);
            right.AddChild(Ui.Rule());
            right.AddChild(seedRow);
            right.AddChild(start);
            var rightCard = Ui.Panel(right, "Card", false);
            rightCard.CustomMinimumSize = new Vector2(Layout.Wide ? 420 : 360, 0);
            rightCard.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            cols.AddChild(rightCard);

            var pad = Ui.Margin(cols, 28, 20);
            pad.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            root.AddChild(pad);
        }
        main.AddChild(root);
    }

    private static Control Cell(Control c, float width)
    {
        var box = Ui.HBox(0);
        box.CustomMinimumSize = new Vector2(width, 0);
        c.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        box.AddChild(c);
        return box;
    }

    private static Control RolePill(Localizer t, Role role, bool shortText)
    {
        string key = role.ToString().ToLowerInvariant();
        var color = role == Role.Leader ? AppTheme.Chrome : role == Role.Partner ? AppTheme.Good : AppTheme.Ink3;
        return Ui.Pill(t.T(shortText ? "ui.start.role_short." + key : "ui.start.role." + key), color);
    }

    private static Control Figure(string value, string caption)
    {
        var col = Ui.VBox(0);
        col.AddChild(Ui.L(value, Tx.FigureXl));
        col.AddChild(Ui.L(caption, Tx.Small, AppTheme.Ink3));
        return col;
    }

    private static string CompassWords(Localizer t, PartyOption o) =>
        t.T(o.X < -0.15 ? "ui.start.econ.left" : o.X > 0.15 ? "ui.start.econ.right" : "ui.start.econ.centre") + ", " +
        t.T(o.Y < -0.15 ? "ui.start.social.liberal" : o.Y > 0.15 ? "ui.start.social.authoritarian" : "ui.start.social.centre");
}
