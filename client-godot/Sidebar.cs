using Godot;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Presentation;

namespace OpenPolitics.Client;

/// <summary>One voter group in the Popularity panel: white name over a thin bar filled by happiness, a lighter band for its share of voters.</summary>
public partial class GroupRow : Control
{
    private readonly string _name;
    private readonly IconKind _icon;
    private double _sat, _share;
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

    public void Set(double satisfaction, double share, string tooltip) { _sat = satisfaction; _share = share; TooltipText = tooltip; QueueRedraw(); }

    public override void _Draw()
    {
        var bg = Selected ? AppTheme.ChromeDark : _hover ? new Color(AppTheme.ChromeDark, 0.55f) : new Color(0, 0, 0, 0);
        DrawRect(new Rect2(Vector2.Zero, Size), bg);
        double good = (_sat + 1) / 2;
        var fill = AppTheme.Traffic(good);
        if (Narrow)
        {
            Icons.Draw(this, _icon, new Vector2(Size.X / 2, Size.Y / 2 - 6), 16, Colors.White);
            float bw = Size.X - 16, by = Size.Y / 2 + 8;
            DrawStyleBox(AppTheme.Box(Colors.White, 3), new Rect2(8, by, bw, 6));
            DrawStyleBox(AppTheme.Box(fill, 3), new Rect2(8, by, Math.Max(6, bw * (float)Math.Clamp(good, 0, 1)), 6));
            if (Selected) DrawRect(new Rect2(0, 0, 3, Size.Y), Colors.White);
            return;
        }
        float pad = 10, cy = Size.Y / 2;
        if (_share > 0) DrawRect(new Rect2(0, 2, (Size.X - 4) * (float)Math.Clamp(_share, 0.04, 1), Size.Y / 2 + 2), new Color(AppTheme.ChromeLight, 0.55f));
        DrawString(AppTheme.Bold, new Vector2(pad, cy + 1), _name, HorizontalAlignment.Left, Size.X - pad * 2, 13, Colors.White, TextServer.JustificationFlag.None);
        float y = cy + 7, w = Size.X - pad * 2;
        DrawStyleBox(AppTheme.Box(Colors.White, 4), new Rect2(pad, y, w, 7));
        DrawStyleBox(AppTheme.Box(fill, 4), new Rect2(pad, y, Math.Max(7, w * (float)Math.Clamp(good, 0, 1)), 7));
        if (Selected) DrawRect(new Rect2(0, 0, 4, Size.Y), Colors.White);
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
    public static int SortMode { get; set; } = 1; // 0 name, 1 size, 2 happiness
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
            var open = new SquareTab(IconKind.Smile, "") { CustomMinimumSize = new Vector2(0, 52), HoverFill = AppTheme.ChromeDark };
            open.Pressed += () => _main.ToggleSidebarDrawer();
            header.AddChild(open);
            _overall = new Control();
        }
        else
        {
            var title = Ui.L("", Tx.Heading, Colors.White);
            title.Name = "Title";
            title.HorizontalAlignment = HorizontalAlignment.Center;
            title.AddThemeFontSizeOverride("font_size", 16);
            header.AddChild(title);
            _overall = new OverallBar { CustomMinimumSize = new Vector2(0, 16) };
            header.AddChild(_overall);
        }
        column.AddChild(Ui.Margin(header, narrow ? 0 : 14, narrow ? 0 : 10, narrow ? 0 : 12, narrow ? 4 : 12));
        if (!narrow)
        {
            var sort = Ui.HBox(1);
            int i = 0;
            foreach (var icon in new[] { IconKind.SortAz, IconKind.SortSize, IconKind.Smile })
            {
                int k = i++;
                var b = new SquareTab(icon, "") { Fill = AppTheme.ChromeDark, HoverFill = AppTheme.ChromeDark.Darkened(0.15f), CustomMinimumSize = new Vector2(0, 30), SizeFlagsHorizontal = SizeFlags.ExpandFill, Active = SortMode == k };
                b.Name = "Sort" + k;
                b.Pressed += () => { SortMode = k; _main.RefreshSidebars(); };
                sort.AddChild(b);
            }
            column.AddChild(sort);
        }
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _rows.SizeFlagsHorizontal = SizeFlags.ExpandFill; _rows.SizeFlagsVertical = SizeFlags.ExpandFill;
        scroll.AddChild(_rows);
        column.AddChild(scroll);
        column.AddChild(Ui.Margin(_gov, narrow ? 4 : 12, 10));
        AddChild(column);
    }

    private sealed partial class OverallBar : Control
    {
        public double Mood;
        public override void _Draw()
        {
            DrawStyleBox(AppTheme.Box(Colors.White, 8), new Rect2(Vector2.Zero, Size));
            double good = (Mood + 1) / 2;
            DrawStyleBox(AppTheme.Box(AppTheme.Traffic(good), 8), new Rect2(0, 0, Math.Max(Size.Y, Size.X * (float)good), Size.Y));
        }
    }

    public void Update(Session s)
    {
        _s = s;
        var t = s.T;
        if (FindChild("Title", true, false) is Label title) title.Text = t.T("ui.rail.popularity").ToUpperInvariant();
        for (int k = 0; k < 3; k++) if (FindChild("Sort" + k, true, false) is SquareTab st) { st.Active = SortMode == k; st.TooltipText = t.T("ui.rail.sort." + k); st.QueueRedraw(); }
        double mood = Moods.Overall(s.Packs.VoterGroups, s.Country.Satisfaction);
        if (_overall is OverallBar ob) { ob.Mood = mood; ob.TooltipText = $"{t.T("ui.line.mood")}: {Ui.Num(mood, "0.00")} ({t.T(Moods.Key(mood))})"; ob.QueueRedraw(); }

        Ui.Clear(_rows);
        var groups = s.Packs.VoterGroups.AsEnumerable();
        groups = SortMode switch
        {
            0 => groups.OrderBy(g => Localizer.Fold(t.Name(g.Id)), StringComparer.Ordinal),
            2 => groups.OrderByDescending(g => s.Country.Satisfaction.GetValueOrDefault(g.Id)).ThenBy(g => g.Id, StringComparer.Ordinal),
            _ => groups.OrderByDescending(g => g.Size).ThenBy(g => g.Id, StringComparer.Ordinal),
        };
        var everyone = new GroupRow(null, t.T("ui.rail.everyone"), IconKind.Users) { Narrow = _narrow, Selected = false };
        everyone.Set(mood, 0, $"{t.T("ui.rail.everyone")}: {Ui.Num(mood, "0.00")} ({t.T(Moods.Key(mood))})");
        everyone.Pressed += () => _main.SetLens(null);
        _rows.AddChild(everyone);
        double maxShare = s.Packs.VoterGroups.Select(g => g.Size).DefaultIfEmpty(1).Max();
        foreach (var g in groups)
        {
            double sat = s.Country.Satisfaction.GetValueOrDefault(g.Id);
            var row = new GroupRow(g.Id, t.Name(g.Id), Icons.For(g.Id)) { Narrow = _narrow, Selected = _main.Map?.LensGroup == g.Id };
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
            head.AddChild(Ui.L(t.T("ui.rail.government"), Tx.Overline, Colors.White));
            head.AddChild(Ui.Spacer());
            head.AddChild(Ui.L($"{held} / {total}", Tx.Data, Colors.White));
            _gov.AddChild(head);
            var bar = new Meter(held, 0, Math.Max(1, total), majority ? AppTheme.Good : AppTheme.Bad, 100, false, 7) { TrackColor = Colors.White, Mark = (float)rules.MajorityFraction, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            _gov.AddChild(bar);
            var note = pol.Ultimatums is { Count: > 0 } u ? t.T("ui.rail.ultimatums", u.Count)
                : t.T(majority ? "ui.rail.majority" : "ui.rail.no_majority") + " · " + (pol.TurnsToElection <= 1 ? t.T("ui.politics.election_next") : t.T("ui.rail.election_in", pol.TurnsToElection));
            var nl = Ui.L(note, Tx.Small, Colors.White);
            nl.ClipText = true; nl.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            _gov.AddChild(nl);
            _gov.TooltipText = t.T("ui.client.tab.politics");
        }
    }
}
