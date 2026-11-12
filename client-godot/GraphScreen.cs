using Godot;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Presentation;

namespace OpenPolitics.Client;

/// <summary>
/// The influence graph: every value and policy as a node, every edge of the packs as an arrow. Click a node to see
/// what it influences and, for values, what moved it last quarter ("why did this change?").
/// </summary>
public partial class GraphScreen : Screen
{
    public override string TitleKey => "ui.client.tab.graph";
    private readonly GraphView _view = new();
    private readonly VBoxContainer _side;
    private Session? _s;

    public GraphScreen()
    {
        var split = new HSplitContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        split.SetAnchorsPreset(LayoutPreset.FullRect);
        _view.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _view.SizeFlagsVertical = SizeFlags.ExpandFill;
        split.AddChild(_view);
        var (scroll, body) = Ui.Page();
        scroll.CustomMinimumSize = new Vector2(380, 0);
        _side = body;
        split.AddChild(scroll);
        AddChild(split);
        _view.Selected += _ => { if (_s != null) Fill(_s); };
    }

    public string? SelectedId { get => _view.SelectedId; set { _view.SelectedId = value; if (_s != null) Fill(_s); } }

    public override void Refresh(Session s)
    {
        _s = s;
        _view.Setup(s);
        Fill(s);
    }

    private void Fill(Session s)
    {
        Ui.Clear(_side);
        var t = s.T;
        _side.AddChild(Ui.Heading(t.T("ui.client.graph.title")));
        _side.AddChild(Ui.Text(t.T("ui.client.graph.hint"), Ui.Muted, 0, true));
        string? id = _view.SelectedId;
        if (id == null) return;
        _side.AddChild(Ui.Rule());
        _side.AddChild(Ui.Text(t.Name(id), Ui.Accent, 18));
        if (t.Description(id) is { } desc) _side.AddChild(Ui.Text(desc, Ui.Muted, 0, true));

        var value = s.Packs.Values.FirstOrDefault(v => v.Id == id);
        if (value != null)
        {
            _side.AddChild(Ui.Text(Fmt.Value(value, s.Country.Values.GetValueOrDefault(id)), null, 20));
            _side.AddChild(Ui.Text(t.T("ui.heading.why", t.Name(id)), Ui.Accent, 16));
            var change = s.Last?.Changes.FirstOrDefault(c => c.ValueId == id);
            if (change == null) _side.AddChild(Ui.Text(t.T("ui.why.none"), Ui.Muted, 0, true));
            else
            {
                _side.AddChild(Ui.Text(t.T("ui.info.change", Fmt.Value(value, change.Before), Fmt.Value(value, change.After))));
                var lines = change.Contributions.Where(c => Math.Abs(c.Amount) > 1e-9 * Math.Max(1.0, Math.Abs(change.Before)))
                    .OrderByDescending(c => Math.Abs(c.Amount)).ThenBy(c => c.EdgeId, StringComparer.Ordinal).ToList();
                foreach (var c in lines)
                    _side.AddChild(Ui.Text("• " + t.T("ui.why.line", t.Name(c.FromId), t.T(c.Amount > 0 ? "ui.why.up" : "ui.why.down")), c.Amount > 0 ? Ui.Good : Ui.Bad, 0, true));
                if (lines.Count == 0 && value.Owner == null) _side.AddChild(Ui.Text(t.T("ui.why.none"), Ui.Muted, 0, true));
                if (value.Owner != null) _side.AddChild(Ui.Text(t.T("ui.why.model"), Ui.Muted, 0, true));
            }
        }
        else if (s.Packs.Policies.FirstOrDefault(p => p.Id == id) is { } policy)
            _side.AddChild(Ui.Text(Fmt.Policy(policy, s.Country.Policies.GetValueOrDefault(id), t), null, 20));

        var outgoing = s.Packs.Edges.Where(e => e.From == id).OrderBy(e => e.Id, StringComparer.Ordinal).ToList();
        if (outgoing.Count > 0)
        {
            _side.AddChild(Ui.Text(t.T("ui.client.graph.influences"), Ui.Accent, 16));
            foreach (var e in outgoing)
                _side.AddChild(Ui.Text("→ " + t.Name(e.To) + "  " + (e.Strength >= 0 ? "▲" : "▼"), e.Strength >= 0 ? Ui.Good : Ui.Bad));
        }
        var incoming = s.Packs.Edges.Where(e => e.To == id).OrderBy(e => e.Id, StringComparer.Ordinal).ToList();
        if (incoming.Count > 0)
        {
            _side.AddChild(Ui.Text(t.T("ui.client.graph.influenced_by"), Ui.Accent, 16));
            foreach (var e in incoming)
                _side.AddChild(Ui.Text("← " + t.Name(e.From) + "  " + (e.Strength >= 0 ? "▲" : "▼"), e.Strength >= 0 ? Ui.Good : Ui.Bad));
        }
    }
}

/// <summary>Draws the nodes (values inside, policies on the outer ring) and arrows; click selects a node.</summary>
public partial class GraphView : Control
{
    private Session? _s;
    private readonly Dictionary<string, Vector2> _pos = new(StringComparer.Ordinal);
    public string? SelectedId { get; set; }
    public event Action<string?>? Selected;

    public GraphView() { CustomMinimumSize = new Vector2(560, 480); ClipContents = true; }

    public void Setup(Session s) { _s = s; QueueRedraw(); }

    private void Layout()
    {
        _pos.Clear();
        if (_s == null) return;
        var c = Size / 2;
        float r = Math.Min(Size.X, Size.Y) / 2 - 60;
        var values = _s.Packs.Values.OrderBy(v => v.Id, StringComparer.Ordinal).Select(v => v.Id).ToList();
        var policies = _s.Packs.Policies.OrderBy(p => p.Id, StringComparer.Ordinal).Select(p => p.Id).ToList();
        for (int i = 0; i < values.Count; i++)
        {
            double a = 2 * Math.PI * i / values.Count - Math.PI / 2;
            _pos[values[i]] = c + new Vector2((float)Math.Cos(a), (float)Math.Sin(a)) * r * 0.58f;
        }
        for (int i = 0; i < policies.Count; i++)
        {
            double a = 2 * Math.PI * (i + 0.5) / policies.Count - Math.PI / 2;
            _pos[policies[i]] = c + new Vector2((float)Math.Cos(a), (float)Math.Sin(a)) * r;
        }
    }

    public override void _Draw()
    {
        if (_s == null) return;
        Layout();
        var font = ThemeDB.FallbackFont;
        var neighbours = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in _s.Packs.Edges)
        {
            if (!_pos.TryGetValue(e.From, out var a) || !_pos.TryGetValue(e.To, out var b)) continue;
            bool hot = SelectedId != null && (e.From == SelectedId || e.To == SelectedId);
            if (hot) { neighbours.Add(e.From); neighbours.Add(e.To); }
            var color = e.Strength >= 0 ? Ui.Good : Ui.Bad;
            color.A = hot ? 0.95f : SelectedId == null ? 0.35f : 0.08f;
            float width = hot ? 2.5f : 1.2f;
            DrawLine(a, b, color, width, true);
            var dir = (b - a).Normalized();
            var tip = b - dir * 9;
            var side = new Vector2(-dir.Y, dir.X) * 4;
            DrawColoredPolygon(new[] { tip + dir * 7, tip + side, tip - side }, color);
        }
        foreach (var (id, p) in _pos)
        {
            bool isPolicy = _s.Packs.Policies.Any(x => x.Id == id);
            bool sel = id == SelectedId;
            var color = sel ? Colors.White : isPolicy ? Ui.Warn : Ui.Accent;
            if (SelectedId != null && !sel && !neighbours.Contains(id)) color.A = 0.35f;
            if (isPolicy) DrawRect(new Rect2(p - new Vector2(6, 6), new Vector2(12, 12)), color);
            else DrawCircle(p, sel ? 9 : 7, color);
            bool label = SelectedId == null || sel || neighbours.Contains(id);
            if (label)
                DrawString(font, p + new Vector2(10, 4), _s.T.Name(id), HorizontalAlignment.Left, 190, 12, sel ? Colors.White : new Color(0.85f, 0.87f, 0.9f, color.A));
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
        {
            string? best = null; float bestD = 18;
            foreach (var (id, p) in _pos) { float d = p.DistanceTo(mb.Position); if (d < bestD) { bestD = d; best = id; } }
            SelectedId = best == SelectedId ? null : best;
            QueueRedraw();
            Selected?.Invoke(SelectedId);
        }
    }
}
