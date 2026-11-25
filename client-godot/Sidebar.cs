using Godot;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Presentation;

namespace OpenPolitics.Client;

/// <summary>One voter group in the Popularity panel: white name over a thin bar filled by happiness, a lighter band for its share of voters.</summary>
public partial class GroupRow : Control
{
    private readonly string _name;
    private readonly IconKind _icon;
    private double _sat, _share, _support, _delta, _scale = 1;
    private bool _supportMode;
    private Color _partyColor = AppTheme.Chrome;
    private bool _hover;
    public string? GroupId { get; }
    public bool Selected { get; set; }
    public bool Narrow { get; set; }
    public event Action? Pressed;

    public GroupRow(string? groupId, string name, IconKind icon)
    {
        GroupId = groupId; _name = name; _icon = icon;
        MouseFilter = MouseFilterEnum.Stop; MouseDefaultCursorShape = CursorShape.PointingHand;
        SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill;
        CustomMinimumSize = new Vector2(0, 36);
    }

    public void Set(double satisfaction, double share, string tooltip) { _sat = satisfaction; _share = share; _supportMode = false; TooltipText = tooltip; QueueRedraw(); }

    /// <summary>Decision 0015: the row shows the group's support for the player's party (share 0..1, change since last quarter) instead of its mood.</summary>
    public void SetSupport(double support, double delta, double scale, Color partyColor, double share, string tooltip)
    {
        _support = support; _delta = delta; _scale = Math.Max(scale, 1e-6); _partyColor = partyColor; _share = share; _supportMode = true; TooltipText = tooltip; QueueRedraw();
    }

    public override void _Draw()
    {
        var bg = Selected ? AppTheme.Graphite2 : _hover ? new Color(1, 1, 1, 0.045f) : new Color(0, 0, 0, 0);
        var rect = new Rect2(6, 2, Size.X - 12, Size.Y - 4);
        DrawStyleBox(AppTheme.Box(bg, 10), rect);
        double good = _supportMode ? Math.Clamp(_support / _scale, 0, 1) : (_sat + 1) / 2;
        var fill = _supportMode ? _partyColor.Lightened(0.15f) : AppTheme.Traffic(good);
        var track = new Color(1, 1, 1, 0.10f);
        if (Narrow)
        {
            Icons.Draw(this, _icon, new Vector2(Size.X / 2, Size.Y / 2 - 5), 16, Selected ? Colors.White : new Color("d4d3d8"));
            float bw = Size.X - 22, by = Size.Y / 2 + 8;
            DrawStyleBox(AppTheme.Box(track, 2), new Rect2(11, by, bw, 3));
            DrawStyleBox(AppTheme.Box(fill, 2), new Rect2(11, by, Math.Max(3, bw * (float)Math.Clamp(good, 0, 1)), 3));
            if (Selected) DrawStyleBox(AppTheme.Box(AppTheme.ChromeLight, 2), new Rect2(2, Size.Y / 2 - 9, 3, 18));
            return;
        }
        float pad = 18, cy = Size.Y / 2;
        float nameW = Size.X - pad * 2 - (_supportMode ? 110 : 46);
        DrawString(AppTheme.Semi, new Vector2(pad, cy - 1), Fit(_name, nameW), HorizontalAlignment.Left, -1, 13, new Color(1, 1, 1, Selected || _hover ? 1f : 0.86f), TextServer.JustificationFlag.None);
        if (_supportMode)
        {
            string pct = Ui.Num(_support * 100, "0.0") + " %";
            string arrow = Math.Abs(_delta) < 0.0005 ? "" : (_delta > 0 ? "▲" : "▼") + Ui.Num(Math.Abs(_delta) * 100, "0.0");
            var arrowColor = _delta > 0 ? new Color(0.55f, 0.9f, 0.6f) : new Color(1f, 0.6f, 0.55f);
            DrawString(AppTheme.FigSemi, new Vector2(Size.X - pad - 60, cy - 1), pct, HorizontalAlignment.Right, 60, 12, Colors.White);
            if (arrow.Length > 0) DrawString(AppTheme.FigSemi, new Vector2(Size.X - pad - 112, cy - 1), arrow, HorizontalAlignment.Right, 48, 10, arrowColor);
        }
        else
        {
            string v = Ui.Signed(_sat, "0.00");
            DrawString(AppTheme.FigSemi, new Vector2(Size.X - pad - 60, cy - 1), v, HorizontalAlignment.Right, 60, 12, AppTheme.Traffic(good).Lightened(0.25f));
        }
        float y = cy + 7, w = Size.X - pad * 2;
        DrawStyleBox(AppTheme.Box(track, 2), new Rect2(pad, y, w, 4));
        DrawStyleBox(AppTheme.Box(fill, 2), new Rect2(pad, y, Math.Max(4, w * (float)Math.Clamp(good, 0, 1)), 4));
        if (_share > 0) DrawRect(new Rect2(pad, y + 7, w * (float)Math.Clamp(_share, 0.02, 1), 1.5f), new Color(1, 1, 1, 0.22f));
        if (Selected) DrawStyleBox(AppTheme.Box(AppTheme.ChromeLight, 2), new Rect2(6, cy - 10, 3, 20));
    }

    private static string Fit(string text, float width)
    {
        var f = AppTheme.Semi;
        if (f.GetStringSize(text, HorizontalAlignment.Left, -1, 13).X <= width) return text;
        while (text.Length > 3 && f.GetStringSize(text + "\u2026", HorizontalAlignment.Left, -1, 13).X > width) text = text[..^1];
        return text.TrimEnd() + "\u2026";
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) { Pressed?.Invoke(); AcceptEvent(); }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationMouseEnter) { _hover = true; QueueRedraw(); }
        else if (what == NotificationMouseExit) { _hover = false; QueueRedraw(); }
    }
}

/// <summary>
/// The Popularity panel on the left (Democracy 4): overall approval, a sort strip, every voter group with a happiness bar; clicking a
/// group turns the map into that group's lens. Government seats at the bottom. Narrow mode (compact) shows icons and mini bars only.
/// </summary>
public partial class Sidebar : PanelContainer
{
    public static int SortMode { get; set; } = 1; // 0 name, 1 size, 2 happiness (or support)
    /// <summary>Decision 0015: show each group's support for the player's party (true) or its mood (false); null = by role (support outside the lead).</summary>
    public static bool? SupportView { get; set; }

    /// <summary>Whether the panel shows support for the player's party now: the player's choice, else support unless the player leads.</summary>
    public static bool ShowsSupport(Session s) => s.Politics?.Party?.GroupSupport is { Count: > 0 }
        && (SupportView ?? s.Politics.PlayerRole != OpenPolitics.Core.Politics.Role.Leader);
    private readonly Main _main;
    private readonly bool _narrow;
    private readonly VBoxContainer _rows = Ui.VBox(0);
    private readonly Control _overall;
    private double _overallMood;
    private readonly VBoxContainer _gov = Ui.VBox(4);
    private Session? _s;

    public Sidebar(Main main, bool narrow)
    {
        _main = main; _narrow = narrow;
        ThemeTypeVariation = "Sidebar";
        var column = Ui.VBox(0);
        var header = Ui.VBox(6);
        if (narrow)
        {
            var open = new SquareTab(IconKind.Smile, "") { CustomMinimumSize = new Vector2(0, 52), Style = TabStyle.Dark };
            open.Pressed += () => _main.ToggleSidebarDrawer();
            header.AddChild(open);
            _overall = new Control();
        }
        else
        {
            var title = Ui.L("", Tx.Overline, new Color(1, 1, 1, 0.6f));
            title.Name = "Title";
            header.AddChild(title);
            _overall = new OverallBar { CustomMinimumSize = new Vector2(0, 40) };
            header.AddChild(_overall);
        }
        column.AddChild(Ui.Margin(header, narrow ? 2 : 18, narrow ? 0 : 10, narrow ? 4 : 18, narrow ? 4 : 14));
        if (!narrow)
        {
            var sort = Ui.HBox(1);
            int i = 0;
            foreach (var icon in new[] { IconKind.SortAz, IconKind.SortSize, IconKind.Smile })
            {
                int k = i++;
                var b = new SquareTab(icon, "") { Style = TabStyle.Dark, Square = false, Inset = 2, IconScale = 0.5f, CustomMinimumSize = new Vector2(0, 30), SizeFlagsHorizontal = SizeFlags.ExpandFill, Active = SortMode == k };
                b.Name = "Sort" + k;
                b.Pressed += () => { SortMode = k; _main.RefreshSidebars(); };
                sort.AddChild(b);
            }
            var sortBox = new PanelContainer();
            sortBox.AddThemeStyleboxOverride("panel", AppTheme.Box(new Color(1, 1, 1, 0.05f), 10, null, 1, 2, 2));
            sortBox.AddChild(sort);
            column.AddChild(Ui.Margin(sortBox, 14, 0, 0, 8));
            // Mood / Support for <party> (decision 0015)
            var view = Ui.HBox(1);
            view.Name = "ViewToggle";
            column.AddChild(Ui.Margin(view, 14, 0, 0, 8));
        }
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _rows.SizeFlagsHorizontal = SizeFlags.ExpandFill; _rows.SizeFlagsVertical = SizeFlags.ExpandFill;
        scroll.AddChild(_rows);
        column.AddChild(scroll);
        column.AddChild(Ui.Margin(_gov, narrow ? 4 : 18, 14));
        AddChild(column);
    }

    private sealed partial class OverallBar : Control
    {
        public double Mood;
        public bool Percent;
        public override void _Draw()
        {
            double good = Percent ? Math.Clamp(Mood / 0.4, 0, 1) : (Mood + 1) / 2;
            DrawString(AppTheme.FigBold, new Vector2(0, 26), Percent ? Ui.Num(Mood * 100, "0.0") + " %" : Ui.Signed(Mood, "0.00"), HorizontalAlignment.Left, -1, 24, Colors.White);
            float y = Size.Y - 6;
            DrawStyleBox(AppTheme.Box(new Color(1, 1, 1, 0.10f), 3), new Rect2(0, y, Size.X, 5));
            DrawStyleBox(AppTheme.Box(AppTheme.Traffic(good), 3), new Rect2(0, y, Math.Max(5, Size.X * (float)good), 5));
        }
    }

    public void Update(Session s)
    {
        _s = s;
        var t = s.T;
        if (FindChild("Title", true, false) is Label title) title.Text = t.T("ui.rail.popularity").ToUpperInvariant();
        for (int k = 0; k < 3; k++) if (FindChild("Sort" + k, true, false) is SquareTab st) { st.Active = SortMode == k; st.TooltipText = t.T("ui.rail.sort." + k); st.QueueRedraw(); }
        double mood = Moods.Overall(s.Packs.VoterGroups, s.Country.Satisfaction);
        bool support = ShowsSupport(s);
        var ps = s.Politics?.Party;
        string party = s.Politics != null ? t.Name(s.Politics.PlayerParty) : "";
        if (FindChild("ViewToggle", true, false) is HBoxContainer toggle)
        {
            Ui.Clear(toggle);
            toggle.Visible = ps?.GroupSupport is { Count: > 0 };
            foreach (var (mode, key) in new[] { (false, "ui.rail.view_mood"), (true, "ui.rail.view_support_short") })
            {
                var b = new Button { Text = t.T(key, party), ToggleMode = true, ButtonPressed = support == mode, FocusMode = FocusModeEnum.None, ClipText = true,
                    TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 28),
                    TooltipText = t.T(mode ? "ui.rail.view_support_hint" : "ui.rail.view_mood_hint", party), MouseDefaultCursorShape = CursorShape.PointingHand };
                b.Name = mode ? "ViewSupport" : "ViewMood";
                b.AddThemeFontSizeOverride("font_size", 12);
                b.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.7f));
                b.AddThemeColorOverride("font_pressed_color", Colors.White);
                b.AddThemeColorOverride("font_hover_color", Colors.White);
                b.AddThemeStyleboxOverride("normal", AppTheme.Box(new Color(1, 1, 1, 0.05f), 8));
                b.AddThemeStyleboxOverride("hover", AppTheme.Box(new Color(1, 1, 1, 0.10f), 8));
                b.AddThemeStyleboxOverride("pressed", AppTheme.Box(AppTheme.Graphite2, 8));
                b.AddThemeStyleboxOverride("hover_pressed", AppTheme.Box(AppTheme.Graphite2, 8));
                bool m = mode;
                b.Pressed += () => { SupportView = m; _main.RefreshSidebars(); };
                toggle.AddChild(b);
            }
        }
        if (FindChild("Title", true, false) is Label title2 && support) title2.Text = t.T("ui.rail.support_title", party).ToUpperInvariant();
        if (_overall is OverallBar ob)
        {
            if (support && s.Politics is { } p0)
            {
                double share = p0.Support.GetValueOrDefault(p0.PlayerParty);
                ob.Mood = share; ob.Percent = true;
                ob.TooltipText = t.T("ui.rail.support_overall", party, Ui.Num(share * 100, "0.0"));
            }
            else { ob.Mood = mood; ob.Percent = false; ob.TooltipText = $"{t.T("ui.line.mood")}: {Ui.Num(mood, "0.00")} ({t.T(Moods.Key(mood))})"; }
            ob.QueueRedraw();
        }

        Ui.Clear(_rows);
        var groups = s.Packs.VoterGroups.AsEnumerable();
        groups = SortMode switch
        {
            0 => groups.OrderBy(g => Localizer.Fold(t.Name(g.Id)), StringComparer.Ordinal),
            2 when support => groups.OrderByDescending(g => ps!.GroupSupport!.GetValueOrDefault(g.Id)).ThenBy(g => g.Id, StringComparer.Ordinal),
            2 => groups.OrderByDescending(g => s.Country.Satisfaction.GetValueOrDefault(g.Id)).ThenBy(g => g.Id, StringComparer.Ordinal),
            _ => groups.OrderByDescending(g => g.Size).ThenBy(g => g.Id, StringComparer.Ordinal),
        };
        var everyone = new GroupRow(null, t.T("ui.rail.everyone"), IconKind.Users) { Narrow = _narrow, Selected = false };
        everyone.Set(mood, 0, $"{t.T("ui.rail.everyone")}: {Ui.Num(mood, "0.00")} ({t.T(Moods.Key(mood))})");
        double scale = support ? Math.Max(0.3, ps!.GroupSupport!.Values.DefaultIfEmpty(0).Max()) : 1;
        var partyColor = s.Politics != null ? Ui.PartyColor(s, s.Politics.PlayerParty) : AppTheme.Chrome;
        if (support && s.Politics is { } pe)
        {
            double share = pe.Support.GetValueOrDefault(pe.PlayerParty);
            everyone.SetSupport(share, 0, scale, partyColor, 0, t.T("ui.rail.support_overall", party, Ui.Num(share * 100, "0.0")));
        }
        everyone.Pressed += () => _main.SetLens(null);
        _rows.AddChild(everyone);
        double maxShare = s.Packs.VoterGroups.Select(g => g.Size).DefaultIfEmpty(1).Max();
        foreach (var g in groups)
        {
            double sat = s.Country.Satisfaction.GetValueOrDefault(g.Id);
            var row = new GroupRow(g.Id, t.Name(g.Id), Icons.For(g.Id)) { Narrow = _narrow, Selected = _main.Map?.LensGroup == g.Id };
            if (support)
            {
                double now = ps!.GroupSupport!.GetValueOrDefault(g.Id), prev = ps.GroupSupportPrev?.GetValueOrDefault(g.Id, now) ?? now;
                row.SetSupport(now, now - prev, scale, partyColor, g.Size / Math.Max(maxShare, 1e-9) * 0.9,
                    $"{t.Name(g.Id)}\n{t.T("ui.rail.support_row", party, Ui.Num(now * 100, "0.0"), Texts.Points(100 * (now - prev)))}\n{t.T("ui.client.voter_share", Ui.Num(g.Size * 100, "0"))} · {t.T("ui.line.mood")} {Ui.Num(sat, "0.00")} ({t.T(Moods.Key(sat))})\n{t.T("ui.rail.lens_hint")}");
            }
            else
                row.Set(sat, g.Size / Math.Max(maxShare, 1e-9) * 0.9,
                    $"{t.Name(g.Id)}\n{t.T("ui.client.voter_share", Ui.Num(g.Size * 100, "0"))} · {Ui.Num(sat, "0.00")} ({t.T(Moods.Key(sat))})\n{t.T("ui.rail.lens_hint")}");
            var id = g.Id;
            row.Pressed += () => _main.SetLens(_main.Map?.LensGroup == id ? null : id);
            _rows.AddChild(row);
        }

        Ui.Clear(_gov);
        if (s.Politics is { } pol)
        {
            int total = pol.Seats.Values.Sum(), held = pol.GovernmentParties.Sum(p => pol.Seats.GetValueOrDefault(p));
            var rules = s.Packs.Countries.First(c => c.Politics != null).Politics!;
            bool majority = held > total * rules.MajorityFraction;
            if (_narrow)
            {
                var l = Ui.L(held.ToString(), Tx.Data, Colors.White);
                l.HorizontalAlignment = HorizontalAlignment.Center;
                l.TooltipText = $"{t.T("ui.politics.government")}: {held} / {total}";
                l.MouseFilter = MouseFilterEnum.Stop;
                _gov.AddChild(l);
                return;
            }
            var head = Ui.HBox(6);
            head.AddChild(Ui.L(t.T("ui.rail.government"), Tx.Overline, new Color(1, 1, 1, 0.6f)));
            head.AddChild(Ui.Spacer());
            head.AddChild(Ui.L($"{held} / {total}", Tx.Data, Colors.White));
            _gov.AddChild(head);
            var bar = new Meter(held, 0, Math.Max(1, total), majority ? AppTheme.Good : AppTheme.Bad, 100, false, 5) { TrackColor = new Color(1, 1, 1, 0.12f), Mark = (float)rules.MajorityFraction, MarkColor = Colors.White, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            _gov.AddChild(bar);
            var note = pol.PlayerRole == OpenPolitics.Core.Politics.Role.Leader && pol.Ultimatums is { Count: > 0 } u ? t.T("ui.rail.ultimatums", u.Count)
                : t.T(majority ? "ui.rail.majority" : "ui.rail.no_majority") + " · " + (pol.TurnsToElection <= 1 ? t.T("ui.politics.election_next") : t.T("ui.rail.election_in", pol.TurnsToElection));
            var nl = Ui.L(note, Tx.Small, new Color(1, 1, 1, 0.6f));
            nl.ClipText = true; nl.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            _gov.AddChild(nl);
            _gov.TooltipText = t.T("ui.client.tab.politics");
        }
    }
}
