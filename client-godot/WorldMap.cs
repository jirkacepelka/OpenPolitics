using Godot;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Presentation;

namespace OpenPolitics.Client;

/// <summary>
/// The living map behind everything, in the spirit of Democracy 4: the values of the country in the middle, the policies around them,
/// arrows for every effect. Click a node for what it does and why it moved. The map keeps clear of the open panel.
/// </summary>
public partial class WorldMap : Control
{
    private Session? _s;
    private readonly Dictionary<string, Vector2> _unit = new(StringComparer.Ordinal); // layout in the unit disc
    private readonly Dictionary<string, Color> _color = new(StringComparer.Ordinal);
    private string? _hover;
    private float _insetLeft;
    private readonly PanelContainer _card = new();
    private readonly VBoxContainer _cardBody = Ui.VBox(6);
    private ulong _layoutKey;

    public string? SelectedId { get; private set; }
    public event Action<string?>? Selected;

    public WorldMap()
    {
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true;
        _card.AddThemeStyleboxOverride("panel", AppTheme.Box(new Color(AppTheme.Header, 0.985f), 16, AppTheme.Border, 1, 16));
        _card.CustomMinimumSize = new Vector2(340, 0);
        _card.Visible = false;
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(330, 0), SizeFlagsVertical = SizeFlags.ExpandFill };
        scroll.AddChild(_cardBody);
        _cardBody.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _card.AddChild(scroll);
        AddChild(_card);
        _card.SetAnchorsPreset(LayoutPreset.RightWide);
        _card.OffsetLeft = -372; _card.OffsetRight = -16; _card.OffsetTop = 92; _card.OffsetBottom = -116;
    }

    public void Setup(Session s)
    {
        _s = s;
        EnsureLayout();
        if (SelectedId != null) FillCard();
        QueueRedraw();
    }

    /// <summary>Keeps the map out from under a panel docked on the left.</summary>
    public void SetInset(float left) { _insetLeft = left; QueueRedraw(); }

    public void Select(string? id)
    {
        SelectedId = id;
        _card.Visible = id != null;
        if (id != null) FillCard();
        QueueRedraw();
        Selected?.Invoke(id);
    }

    // ---- layout ----------------------------------------------------------------------------------------

    private void EnsureLayout()
    {
        if (_s == null) return;
        ulong key = (ulong)(_s.Packs.Values.Count * 1000 + _s.Packs.Policies.Count * 10 + _s.Packs.Edges.Count);
        if (key == _layoutKey && _unit.Count > 0) return;
        _layoutKey = key;
        _unit.Clear(); _color.Clear();
        var values = _s.Packs.Values.Select(v => v.Id).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var policies = _s.Packs.Policies.OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
        var pos = new Dictionary<string, Vector2>(StringComparer.Ordinal);
        for (int i = 0; i < values.Count; i++)
        {
            float a = Mathf.Tau * i / values.Count;
            pos[values[i]] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.5f;
        }
        var links = _s.Packs.Edges.Where(e => pos.ContainsKey(e.From) && pos.ContainsKey(e.To) && e.From != e.To).ToList();
        for (int it = 0; it < 260; it++)
        {
            var force = values.ToDictionary(v => v, _ => Vector2.Zero, StringComparer.Ordinal);
            for (int i = 0; i < values.Count; i++)
                for (int j = i + 1; j < values.Count; j++)
                {
                    var d = pos[values[i]] - pos[values[j]];
                    float len = Math.Max(d.Length(), 0.02f);
                    var f = d / len * (0.02f / (len * len));
                    force[values[i]] += f; force[values[j]] -= f;
                }
            foreach (var e in links)
            {
                var d = pos[e.To] - pos[e.From];
                float len = Math.Max(d.Length(), 0.02f);
                var f = d / len * (len - 0.3f) * 0.05f;
                force[e.From] += f; force[e.To] -= f;
            }
            foreach (var v in values) pos[v] += (force[v] - pos[v] * 0.08f).LimitLength(0.05f);
        }
        float far = Math.Max(values.Max(v => pos[v].Length()), 0.01f);
        foreach (var v in values) _unit[v] = pos[v] / far * 0.66f;

        // Policies sit on the outer ring, at the angle of the values they act on, spread so they never overlap.
        var wanted = new List<(string Id, float Angle)>();
        foreach (var p in policies)
        {
            var targets = _s.Packs.Edges.Where(e => e.From == p.Id && _unit.ContainsKey(e.To)).Select(e => _unit[e.To]).ToList();
            var mean = targets.Count > 0 ? targets.Aggregate(Vector2.Zero, (a, b) => a + b) / targets.Count : Vector2.Right;
            wanted.Add((p.Id, mean.Length() < 0.01f ? (float)(p.Id.GetHashCode() % 628) / 100f : mean.Angle()));
        }
        wanted = wanted.OrderBy(w => w.Angle).ThenBy(w => w.Id, StringComparer.Ordinal).ToList();
        int n = wanted.Count;
        var angles = wanted.Select(w => w.Angle).ToArray();
        float minGap = Mathf.Tau / n * 0.85f;
        for (int pass = 0; pass < 200; pass++)
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                float gap = angles[j] - angles[i] + (j == 0 ? Mathf.Tau : 0);
                if (gap < minGap) { float push = (minGap - gap) / 2 * 0.5f; angles[i] -= push; angles[j] += push; }
            }
        for (int i = 0; i < n; i++) _unit[wanted[i].Id] = new Vector2(Mathf.Cos(angles[i]), Mathf.Sin(angles[i])) * 1.0f;

        foreach (var v in values) _color[v] = AppTheme.ValueNode;
        foreach (var p in policies)
            _color[p.Id] = p.RevenueBaseValueId != null ? AppTheme.Tax : p.CostPerUnit != 0 ? AppTheme.Spend : AppTheme.Regulation;
    }

    private Vector2 Center => new((_insetLeft + Size.X) / 2f, Size.Y / 2f + 6);
    private float Radius => Math.Max(80f, Math.Min(Size.X - _insetLeft - 140, Size.Y - 230) / 2f);
    private Vector2 At(string id) => Center + _unit[id] * Radius;

    // ---- drawing ---------------------------------------------------------------------------------------

    private double VoterLike(string valueId) =>
        _s!.Packs.VoterGroups.Sum(g => g.Size * g.Weight * g.ValueWeights.GetValueOrDefault(valueId));

    public override void _Draw()
    {
        if (_s == null || _unit.Count == 0) return;
        var font = GetThemeDefaultFont();
        var c = Center; float r = Radius;
        for (int i = 6; i >= 1; i--) DrawCircle(c, r * (0.35f + 0.13f * i), new Color(AppTheme.Accent, 0.012f + 0.004f * (6 - i)));
        DrawArc(c, r * 1.0f, 0, Mathf.Tau, 96, new Color(AppTheme.Border, 0.35f), 1.2f, true);
        DrawArc(c, r * 0.7f, 0, Mathf.Tau, 96, new Color(AppTheme.Border, 0.18f), 1.0f, true);

        var neighbours = new HashSet<string>(StringComparer.Ordinal);
        string? focus = SelectedId ?? _hover;
        foreach (var e in _s.Packs.Edges)
        {
            if (!_unit.ContainsKey(e.From) || !_unit.ContainsKey(e.To) || e.From == e.To) continue;
            bool hot = focus != null && (e.From == focus || e.To == focus);
            if (hot) { neighbours.Add(e.From); neighbours.Add(e.To); }
            var col = e.Strength >= 0 ? AppTheme.Good : AppTheme.Bad;
            col.A = hot ? 0.95f : focus == null ? 0.2f : 0.04f;
            var a = At(e.From); var b = At(e.To);
            var mid = (a + b) / 2f; var ctrl = mid + (c - mid) * 0.45f;
            var pts = new Vector2[18];
            for (int k = 0; k < pts.Length; k++)
            {
                float t = k / (float)(pts.Length - 1);
                pts[k] = (1 - t) * (1 - t) * a + 2 * (1 - t) * t * ctrl + t * t * b;
            }
            DrawPolyline(pts, col, hot ? 2.6f : 1.1f, true);
            if (hot || focus == null)
            {
                var dir = (pts[^1] - pts[^3]).Normalized(); var tip = pts[^1] - dir * 16; var side = new Vector2(-dir.Y, dir.X) * (hot ? 5f : 3.5f);
                DrawColoredPolygon(new[] { tip + dir * 9, tip + side, tip - side }, col);
            }
        }

        var changes = _s.Last?.Changes.ToDictionary(x => x.ValueId, StringComparer.Ordinal);
        foreach (var (id, _) in _unit)
        {
            var p = At(id);
            bool isPolicy = _s.Packs.Policies.Any(x => x.Id == id);
            bool sel = id == SelectedId, dim = focus != null && id != focus && !neighbours.Contains(id);
            var baseColor = _color[id];
            var col = new Color(baseColor, dim ? 0.3f : 1f);
            if (isPolicy)
            {
                var def = _s.Packs.Policies.First(x => x.Id == id);
                double frac = def.Max > def.Min ? (_s.Country.Policies.GetValueOrDefault(id, def.Default) - def.Min) / (def.Max - def.Min) : 0;
                var rect = new Rect2(p - new Vector2(13, 13), new Vector2(26, 26));
                DrawStyleBox(AppTheme.Box(new Color(AppTheme.Surface, dim ? 0.5f : 1f), 7, col, sel ? 3 : 2), rect);
                DrawRect(new Rect2(rect.Position + new Vector2(4, 22 - 14 * (float)frac), new Vector2(18, 14 * (float)frac + 0.5f)), new Color(col, 0.8f));
            }
            else
            {
                var ring = AppTheme.Muted;
                if (changes != null && changes.TryGetValue(id, out var ch) && Math.Abs(ch.After - ch.Before) > 1e-9)
                    ring = Math.Sign(ch.After - ch.Before) * Math.Sign(VoterLike(id)) > 0 ? AppTheme.Good : Math.Sign(VoterLike(id)) == 0 ? AppTheme.Muted : AppTheme.Bad;
                DrawCircle(p, sel ? 19 : 16, new Color(AppTheme.Surface, dim ? 0.5f : 1f));
                DrawArc(p, sel ? 19 : 16, 0, Mathf.Tau, 36, new Color(ring, dim ? 0.3f : 1f), 3f, true);
                DrawCircle(p, 9, new Color(baseColor, dim ? 0.3f : 0.9f));
            }
            bool label = !isPolicy || sel || id == _hover || (focus != null && neighbours.Contains(id));
            if (label)
            {
                string name = _s.T.Name(id);
                var align = p.X < c.X - 20 ? HorizontalAlignment.Right : p.X > c.X + 20 ? HorizontalAlignment.Left : HorizontalAlignment.Center;
                float x = align == HorizontalAlignment.Right ? p.X - 22 - 170 : align == HorizontalAlignment.Left ? p.X + 22 : p.X - 85;
                float y = isPolicy && Math.Abs(p.X - c.X) <= 20 ? p.Y + (p.Y < c.Y ? -22 : 34) : p.Y + (align == HorizontalAlignment.Center ? 34 : 5);
                DrawString(font, new Vector2(x, y), name, align, 170, isPolicy ? 12 : 13, new Color(sel || id == _hover ? Colors.White : AppTheme.Text, dim ? 0.4f : 0.95f));
            }
        }
        DrawLegend(font);
    }

    private void DrawLegend(Font font)
    {
        if (_s == null) return;
        float x = _insetLeft + 24, y = Size.Y - 128;
        foreach (var (color, key) in new[] { (AppTheme.Tax, "ui.map.taxes"), (AppTheme.Spend, "ui.map.spending"), (AppTheme.Regulation, "ui.map.regulation"), (AppTheme.ValueNode, "ui.map.values") })
        {
            DrawCircle(new Vector2(x + 5, y - 4), 5, color);
            DrawString(font, new Vector2(x + 16, y), _s.T.T(key), HorizontalAlignment.Left, 120, 12, AppTheme.Muted);
            x += 22 + font.GetStringSize(_s.T.T(key), HorizontalAlignment.Left, -1, 12).X + 12;
        }
    }

    // ---- interaction -----------------------------------------------------------------------------------

    private string? Hit(Vector2 mouse)
    {
        string? best = null; float bestD = 22;
        foreach (var (id, _) in _unit) { float d = At(id).DistanceTo(mouse); if (d < bestD) { bestD = d; best = id; } }
        return best;
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion mm)
        {
            var h = Hit(mm.Position);
            if (h != _hover) { _hover = h; QueueRedraw(); }
        }
        else if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
        {
            var h = Hit(mb.Position);
            Select(h == SelectedId ? null : h);
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized) QueueRedraw();
        else if (what == NotificationMouseExit && _hover != null) { _hover = null; QueueRedraw(); }
    }

    // ---- the card --------------------------------------------------------------------------------------

    private void FillCard()
    {
        var s = _s!; var t = s.T;
        Ui.Clear(_cardBody);
        string id = SelectedId!;
        bool isPolicy = s.Packs.Policies.Any(p => p.Id == id);
        _cardBody.AddChild(Ui.Pill(t.T(isPolicy ? "ui.map.policy" : "ui.map.value"), _color.GetValueOrDefault(id, AppTheme.Accent)));
        _cardBody.AddChild(Ui.Big(t.Name(id), Colors.White, 22));
        if (t.Description(id) is { } desc) _cardBody.AddChild(Ui.Text(desc, Ui.Muted, 13, true));

        var value = s.Packs.Values.FirstOrDefault(v => v.Id == id);
        if (value != null)
        {
            _cardBody.AddChild(Ui.Big(Fmt.Value(value, s.Country.Values.GetValueOrDefault(id)), Ui.Accent, 26));
            var change = s.Last?.Changes.FirstOrDefault(c => c.ValueId == id);
            _cardBody.AddChild(Ui.Section(t.T("ui.heading.why", t.Name(id))));
            if (change == null) _cardBody.AddChild(Ui.Text(t.T("ui.why.none"), Ui.Muted, 13, true));
            else
            {
                _cardBody.AddChild(Ui.Text(t.T("ui.info.change", Fmt.Value(value, change.Before), Fmt.Value(value, change.After)), null, 13, true));
                var lines = change.Contributions.Where(c => Math.Abs(c.Amount) > 1e-9 * Math.Max(1.0, Math.Abs(change.Before)))
                    .OrderByDescending(c => Math.Abs(c.Amount)).ThenBy(c => c.EdgeId, StringComparer.Ordinal).ToList();
                foreach (var c in lines)
                    _cardBody.AddChild(Ui.Text("• " + t.T("ui.why.line", t.Name(c.FromId), t.T(c.Amount > 0 ? "ui.why.up" : "ui.why.down")), c.Amount > 0 ? Ui.Good : Ui.Bad, 13, true));
                if (lines.Count == 0 && value.Owner == null) _cardBody.AddChild(Ui.Text(t.T("ui.why.none"), Ui.Muted, 13, true));
                if (value.Owner != null) _cardBody.AddChild(Ui.Text(t.T("ui.why.model"), Ui.Muted, 12, true));
            }
            if (Charts.All.FirstOrDefault(c => c.Series(s).Any(x => x.Key == History.ValueKey(id))) is { } spec)
                _cardBody.AddChild(Ui.Btn(t.T("ui.map.open_chart"), () => ChartDialog.Open(this, s, spec)));
        }
        else if (s.Packs.Policies.FirstOrDefault(p => p.Id == id) is { } policy)
            _cardBody.AddChild(Ui.Big(Fmt.Policy(policy, s.Country.Policies.GetValueOrDefault(id), t), Ui.Accent, 26));

        void Links(string headKey, IEnumerable<EdgeDef> edges, Func<EdgeDef, string> other, string arrow)
        {
            var list = edges.OrderBy(e => e.Id, StringComparer.Ordinal).ToList();
            if (list.Count == 0) return;
            _cardBody.AddChild(Ui.Section(t.T(headKey)));
            foreach (var e in list)
                _cardBody.AddChild(Ui.Text($"{arrow} {t.Name(other(e))}  {(e.Strength >= 0 ? "▲" : "▼")}", e.Strength >= 0 ? Ui.Good : Ui.Bad, 13));
        }
        Links("ui.client.graph.influences", s.Packs.Edges.Where(e => e.From == id), e => e.To, "→");
        Links("ui.client.graph.influenced_by", s.Packs.Edges.Where(e => e.To == id), e => e.From, "←");
    }
}
