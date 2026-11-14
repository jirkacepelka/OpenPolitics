using Godot;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Presentation;

namespace OpenPolitics.Client;

/// <summary>
/// The living map behind everything, in the spirit of Democracy 4: the indicators of the country in the middle, the policies on an
/// outer ring grouped by category, arrows for every effect. Each node carries a pictogram and a label; policies show how far their
/// slider is turned as an arc. Hover a node to see its links; click it for what it does and why it moved.
/// </summary>
public partial class WorldMap : Control
{
    private Session? _s;
    private readonly Dictionary<string, Vector2> _unit = new(StringComparer.Ordinal); // values: unit disc; policies: point on the unit ellipse
    private readonly Dictionary<string, Color> _color = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IconKind> _icon = new(StringComparer.Ordinal);
    private readonly HashSet<string> _policyIds = new(StringComparer.Ordinal);
    private readonly List<(int Group, float From, float To)> _sectors = new(); // perimeter fractions
    private readonly List<Vector2> _ellipse = new(); // unit ellipse samples by perimeter fraction
    private string? _hover;
    private float _insetLeft;
    private readonly PanelContainer _card = new();
    private readonly VBoxContainer _cardBody = Ui.VBox(10);
    private ulong _layoutKey;

    public string? SelectedId { get; private set; }
    public event Action<string?>? Selected;

    public static int GroupOf(PolicyDef p) => p.RevenueBaseValueId != null ? 0 : p.CostPerUnit != 0 ? 1 : 2;
    public static Color GroupColor(int g) => g == 0 ? AppTheme.Tax : g == 1 ? AppTheme.Spend : AppTheme.Regulation;
    public static IconKind GroupIcon(int g) => g == 0 ? IconKind.Percent : g == 1 ? IconKind.CoinOut : IconKind.Scale;

    public WorldMap()
    {
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true;
        _card.AddThemeStyleboxOverride("panel", AppTheme.Glass(0.985f, 16));
        _card.Visible = false;
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(Ui.Margin(_cardBody, 20));
        _cardBody.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        ((Control)_cardBody.GetParent()).SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _card.AddChild(scroll);
        AddChild(_card);
        _card.SetAnchorsPreset(LayoutPreset.RightWide);
        _card.OffsetLeft = -392; _card.OffsetRight = -16; _card.OffsetTop = 76; _card.OffsetBottom = -100;
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

    private const float Aspect = 1.7f; // the ring is an ellipse: the window is wide
    private const float ValueSpreadX = 0.76f, ValueSpreadY = 0.74f; // the indicators fill this much of the ring

    private void EnsureLayout()
    {
        if (_s == null) return;
        ulong key = (ulong)(_s.Packs.Values.Count * 1000 + _s.Packs.Policies.Count * 10 + _s.Packs.Edges.Count);
        if (key == _layoutKey && _unit.Count > 0) return;
        _layoutKey = key;
        _unit.Clear(); _color.Clear(); _icon.Clear(); _policyIds.Clear(); _sectors.Clear(); _ellipse.Clear();
        var t = _s.T;
        var values = _s.Packs.Values.Select(v => v.Id).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var policies = _s.Packs.Policies.OrderBy(GroupOf).ThenBy(p => Localizer.Fold(t.Name(p.Id)), StringComparer.Ordinal).ToList();

        // Indicators: a small force layout in a disc that is later stretched to the ellipse.
        var pos = new Dictionary<string, Vector2>(StringComparer.Ordinal);
        for (int i = 0; i < values.Count; i++)
        {
            float a = Mathf.Tau * i / values.Count;
            pos[values[i]] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.5f;
        }
        var links = _s.Packs.Edges.Where(e => pos.ContainsKey(e.From) && pos.ContainsKey(e.To) && e.From != e.To).ToList();
        for (int it = 0; it < 320; it++)
        {
            var force = values.ToDictionary(v => v, _ => Vector2.Zero, StringComparer.Ordinal);
            for (int i = 0; i < values.Count; i++)
                for (int j = i + 1; j < values.Count; j++)
                {
                    var d = pos[values[i]] - pos[values[j]];
                    d.Y *= 1.6f; // labels sit under the nodes: keep rows apart
                    float len = Math.Max(d.Length(), 0.02f);
                    var f = d / len * (0.03f / (len * len));
                    force[values[i]] += f; force[values[j]] -= f;
                }
            foreach (var e in links)
            {
                var d = pos[e.To] - pos[e.From];
                float len = Math.Max(d.Length(), 0.02f);
                var f = d / len * (len - 0.3f) * 0.04f;
                force[e.From] += f; force[e.To] -= f;
            }
            foreach (var v in values) pos[v] += (force[v] - pos[v] * 0.08f).LimitLength(0.05f);
        }
        float far = Math.Max(values.Max(v => pos[v].Length()), 0.01f);
        foreach (var v in values) pos[v] /= far;

        // Then make room for the labels: in screen-like units (a reference ring of 250 px), no two node+label boxes may overlap.
        float A = 250 * Aspect * ValueSpreadX, B = 250 * ValueSpreadY;
        var box = values.ToDictionary(v => v, v => new Vector2(Math.Max(56, Math.Min(130, AppTheme.Semi.GetStringSize(t.Name(v), HorizontalAlignment.Left, -1, AppTheme.Caption).X) + 16), 68), StringComparer.Ordinal);
        var sp = values.ToDictionary(v => v, v => new Vector2(pos[v].X * A, pos[v].Y * B), StringComparer.Ordinal);
        for (int it = 0; it < 400; it++)
        {
            bool moved = false;
            for (int i = 0; i < values.Count; i++)
                for (int j = i + 1; j < values.Count; j++)
                {
                    var a = values[i]; var b = values[j];
                    var d = sp[b] - sp[a];
                    float ox = (box[a].X + box[b].X) / 2 - Math.Abs(d.X), oy = (box[a].Y + box[b].Y) / 2 - Math.Abs(d.Y);
                    if (ox <= 0 || oy <= 0) continue;
                    moved = true;
                    if (ox / box[a].X < oy / box[a].Y) { float m = ox / 2 * Math.Sign(d.X == 0 ? i - j : d.X); sp[a] -= new Vector2(m, 0); sp[b] += new Vector2(m, 0); }
                    else { float m = oy / 2 * Math.Sign(d.Y == 0 ? i - j : d.Y); sp[a] -= new Vector2(0, m); sp[b] += new Vector2(0, m); }
                }
            foreach (var v in values)
            {
                float e = (sp[v].X / A) * (sp[v].X / A) + (sp[v].Y / B) * (sp[v].Y / B);
                if (e > 1) sp[v] /= Mathf.Sqrt(e);
            }
            if (!moved) break;
        }
        foreach (var v in values) _unit[v] = new Vector2(sp[v].X / A, sp[v].Y / B);

        // The unit ellipse, sampled evenly by arc length so the policies are evenly spaced on screen.
        const int samples = 720;
        var raw = new List<Vector2>(); var cum = new List<float> { 0 };
        for (int i = 0; i <= samples; i++) { float a = -Mathf.Pi * 0.75f + Mathf.Tau * i / samples; raw.Add(new Vector2(Mathf.Cos(a) * Aspect, Mathf.Sin(a))); }
        for (int i = 1; i < raw.Count; i++) cum.Add(cum[^1] + raw[i].DistanceTo(raw[i - 1]));
        float total = cum[^1];
        int j2 = 0;
        for (int k = 0; k < 400; k++)
        {
            float target = total * k / 400f;
            while (j2 < cum.Count - 2 && cum[j2 + 1] < target) j2++;
            _ellipse.Add(raw[j2]);
        }

        // Policies: in category order around the ring, with a gap between categories.
        const float gapSlots = 1.6f;
        int groups = policies.Select(GroupOf).Distinct().Count();
        float slots = policies.Count + gapSlots * groups;
        float cursor = gapSlots / 2;
        int lastGroup = -1; float sectorStart = 0;
        foreach (var p in policies)
        {
            int g = GroupOf(p);
            if (g != lastGroup)
            {
                if (lastGroup >= 0) { _sectors.Add((lastGroup, sectorStart / slots, (cursor - 1) / slots)); cursor += gapSlots; }
                sectorStart = cursor; lastGroup = g;
            }
            _unit[p.Id] = EllipseAt(cursor / slots);
            _policyIds.Add(p.Id);
            cursor += 1;
        }
        if (lastGroup >= 0) _sectors.Add((lastGroup, sectorStart / slots, (cursor - 1) / slots));

        foreach (var v in values) { _color[v] = AppTheme.ValueNode; _icon[v] = Icons.For(v); }
        foreach (var p in policies) { _color[p.Id] = GroupColor(GroupOf(p)); _icon[p.Id] = Icons.For(p.Id); }
    }

    private Vector2 EllipseAt(float fraction)
    {
        float f = ((fraction % 1f) + 1f) % 1f;
        return _ellipse[Math.Clamp((int)(f * _ellipse.Count), 0, _ellipse.Count - 1)];
    }

    private const float Top = 64, Bottom = 96;
    private Vector2 Center => new((_insetLeft + Size.X) / 2f, (Top + Size.Y - Bottom) / 2f);
    private float RadiusY => Math.Max(60f, Math.Min((Size.Y - Top - Bottom) / 2f - 70, ((Size.X - _insetLeft) / 2f - 170) / Aspect));
    private bool Compact => RadiusY < 200;

    private Vector2 At(string id)
    {
        float ry = RadiusY, rx = ry; // _unit of policies already carries the aspect
        if (_policyIds.Contains(id)) return Center + _unit[id] * new Vector2(rx, ry);
        return Center + _unit[id] * new Vector2(ry * Aspect * ValueSpreadX, ry * ValueSpreadY);
    }

    // ---- drawing ---------------------------------------------------------------------------------------

    private double VoterLike(string valueId) =>
        _s!.Packs.VoterGroups.Sum(g => g.Size * g.Weight * g.ValueWeights.GetValueOrDefault(valueId));

    private static string Fit(Font font, string text, int size, float width)
    {
        if (font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X <= width) return text;
        while (text.Length > 3 && font.GetStringSize(text + "…", HorizontalAlignment.Left, -1, size).X > width) text = text[..^1];
        return text.TrimEnd() + "…";
    }

    private readonly List<Rect2> _placed = new();

    private static Rect2 LabelRect(Font font, Vector2 pos, string text, HorizontalAlignment align)
    {
        float w = font.GetStringSize(text, HorizontalAlignment.Left, -1, AppTheme.Caption).X;
        float x = align == HorizontalAlignment.Right ? pos.X - w : align == HorizontalAlignment.Center ? pos.X - w / 2 : pos.X;
        return new Rect2(x - 3, pos.Y - 13, w + 6, 17);
    }

    private void Label(Font font, Vector2 pos, string text, HorizontalAlignment align, float width, int size, Color color)
    {
        float x = align == HorizontalAlignment.Right ? pos.X - width : align == HorizontalAlignment.Center ? pos.X - width / 2 : pos.X;
        DrawStringOutline(font, new Vector2(x, pos.Y), text, align, width, size, 5, new Color(AppTheme.Bg, 0.92f));
        DrawString(font, new Vector2(x, pos.Y), text, align, width, size, color);
    }

    /// <summary>True when an open panel leaves too little room for a readable map; the map then steps back entirely.</summary>
    private bool Hidden => Size.X - _insetLeft < 600;

    private bool _ghost;

    public override void _Draw()
    {
        if (_s == null || _unit.Count == 0) return;
        // Behind a wide panel the map is only a quiet backdrop: full width, dimmed, no labels, not clickable.
        float inset = _insetLeft;
        _ghost = Hidden;
        if (_ghost) _insetLeft = 0;
        try { DrawMap(); } finally { _insetLeft = inset; }
    }

    private void DrawMap()
    {
        var c = Center; float ry = RadiusY, rx = ry * Aspect;
        var t = _s.T;

        // Rings and category bands.
        DrawEllipse(c, rx * 0.84f, ry * 0.84f, new Color(AppTheme.ValueNode, 0.03f), true);
        DrawEllipse(c, rx, ry, new Color(AppTheme.BorderStrong, 0.35f), false, 1.2f);
        foreach (var (g, from, to) in _sectors)
        {
            var col = GroupColor(g);
            int n = Math.Max(2, (int)((to - from) * 200));
            var pts = new Vector2[n + 1];
            for (int i = 0; i <= n; i++) pts[i] = c + EllipseAt(from + (to - from) * i / n) * new Vector2(ry, ry);
            DrawPolyline(pts, new Color(col, 0.10f), 34f, true);
        }

        var neighbours = new HashSet<string>(StringComparer.Ordinal);
        string? focus = _ghost ? null : SelectedId ?? _hover;
        foreach (var e in _s.Packs.Edges)
        {
            if (!_unit.ContainsKey(e.From) || !_unit.ContainsKey(e.To) || e.From == e.To) continue;
            bool hot = focus != null && (e.From == focus || e.To == focus);
            if (hot) { neighbours.Add(e.From); neighbours.Add(e.To); }
        }
        foreach (var e in _s.Packs.Edges)
        {
            if (!_unit.ContainsKey(e.From) || !_unit.ContainsKey(e.To) || e.From == e.To) continue;
            bool hot = focus != null && (e.From == focus || e.To == focus);
            if (focus != null && !hot) continue;
            var col = hot ? (e.Strength >= 0 ? AppTheme.Good : AppTheme.Bad) : new Color(AppTheme.Text2, 0.10f);
            var a = At(e.From); var b = At(e.To);
            var mid = (a + b) / 2f; var ctrl = mid + (c - mid) * 0.35f;
            var pts = new Vector2[20];
            for (int k = 0; k < pts.Length; k++)
            {
                float tt = k / (float)(pts.Length - 1);
                pts[k] = (1 - tt) * (1 - tt) * a + 2 * (1 - tt) * tt * ctrl + tt * tt * b;
            }
            float endR = _policyIds.Contains(e.To) ? 17 : 23;
            int cut = pts.Length - 1;
            while (cut > 1 && pts[cut].DistanceTo(b) < endR + 2) cut--;
            var line = pts[..(cut + 1)];
            DrawPolyline(line, col, hot ? 2.4f : 1.0f, true);
            if (hot)
            {
                var dir = (line[^1] - line[^2]).Normalized(); var tip = line[^1]; var side = new Vector2(-dir.Y, dir.X) * 5.5f;
                DrawColoredPolygon(new[] { tip, tip - dir * 10 + side, tip - dir * 10 - side }, col);
            }
        }

        var changes = _s.Last?.Changes.ToDictionary(x => x.ValueId, StringComparer.Ordinal);
        // Obstacles for the policy labels: every node, and the indicator labels (they are fixed under their nodes).
        _placed.Clear();
        _placed.Add(new Rect2(-1000, -1000, Size.X + 2000, 1000 + Top + 6)); // the top bar
        _placed.Add(new Rect2(Size.X / 2 - 330, Size.Y - Bottom + 6, 660, 1000)); // the toolbar
        foreach (var (oid, _) in _unit)
        {
            var op = At(oid); float orad = _policyIds.Contains(oid) ? 17 : 23;
            _placed.Add(new Rect2(op - new Vector2(orad, orad), new Vector2(orad, orad) * 2));
            if (!_policyIds.Contains(oid) && !_ghost)
                _placed.Add(LabelRect(AppTheme.Semi, new Vector2(op.X, op.Y + 21 + 17), Fit(AppTheme.Semi, t.Name(oid), AppTheme.Caption, 130), HorizontalAlignment.Center));
        }
        var pending = _s.Game.PendingPolicies.Select(p => p.PolicyId).ToHashSet(StringComparer.Ordinal);
        var font = AppTheme.Semi;
        int index = 0;
        foreach (var (id, _) in _unit.OrderBy(kv => _policyIds.Contains(kv.Key) ? 0 : 1))
        {
            var p = At(id);
            bool isPolicy = _policyIds.Contains(id);
            bool sel = id == SelectedId, hov = id == _hover, dim = _ghost || (focus != null && id != focus && !neighbours.Contains(id));
            float alpha = dim ? 0.28f : 1f;
            var baseColor = _color[id];
            if (isPolicy)
            {
                var def = _s.Packs.Policies.First(x => x.Id == id);
                double frac = def.Max > def.Min ? (_s.Country.Policies.GetValueOrDefault(id, def.Default) - def.Min) / (def.Max - def.Min) : 0;
                float r = sel || hov ? 17 : 15;
                DrawCircle(p, r + 3, new Color(AppTheme.Bg, alpha * 0.9f));
                DrawCircle(p, r, new Color(AppTheme.Surface, alpha));
                DrawArc(p, r, 0, Mathf.Tau, 40, new Color(baseColor, 0.28f * alpha), 3f, true);
                if (frac > 0.005) DrawArc(p, r, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau * (float)frac, 40, new Color(baseColor, alpha), 3f, true);
                Icons.Draw(this, _icon[id], p, r * 1.05f, new Color(sel || hov ? Colors.White : baseColor, alpha));
                if (pending.Contains(id)) { DrawCircle(p + new Vector2(r * 0.75f, -r * 0.75f), 5, AppTheme.Bg); DrawCircle(p + new Vector2(r * 0.75f, -r * 0.75f), 3.5f, AppTheme.Warn); }
                if (!_ghost && (!Compact || sel || hov))
                {
                    var outward = new Vector2(_unit[id].X / Aspect, _unit[id].Y).Normalized();
                    string name = t.Name(id);
                    var col = new Color(sel || hov ? Colors.White : AppTheme.Text2, dim ? 0.35f : 1f);
                    bool side = Math.Abs(outward.X) > 0.5f;
                    float w = sel || hov ? 220 : side ? 150 : 100;
                    string text = sel || hov ? name : Fit(font, name, AppTheme.Caption, w);
                    var align = side ? (outward.X < 0 ? HorizontalAlignment.Right : HorizontalAlignment.Left) : HorizontalAlignment.Center;
                    var candidates = new List<Vector2>();
                    if (side)
                    {
                        var at = p + outward * (r + 9) + new Vector2(0, 5 + outward.Y * 4);
                        foreach (float dy in new[] { 0f, 9f, -9f, 17f, -17f }) candidates.Add(at + new Vector2(0, dy));
                    }
                    else
                    {
                        bool up = outward.Y < 0;
                        Vector2 Out(int k) => new(p.X, up ? p.Y - r - 8 - k * 16 : p.Y + r + 18 + k * 16);
                        Vector2 In(int k) => new(p.X, up ? p.Y + r + 18 + k * 16 : p.Y - r - 8 - k * 16);
                        candidates.AddRange(new[] { Out(0), Out(1), In(0), Out(2), In(1) });
                    }
                    Vector2 best = candidates[0]; float bestOverlap = float.MaxValue;
                    foreach (var cand in candidates)
                    {
                        var rect = LabelRect(font, cand, text, align);
                        float overlap = 0;
                        foreach (var o in _placed) if (o.Intersects(rect)) { var i2 = o.Intersection(rect); overlap += i2.Area; }
                        if (overlap < bestOverlap - 0.5f) { bestOverlap = overlap; best = cand; }
                        if (overlap <= 0) break;
                    }
                    _placed.Add(LabelRect(font, best, text, align));
                    Label(font, best, text, align, w, AppTheme.Caption, col);
                }
                index++;
            }
            else
            {
                var ring = AppTheme.BorderStrong;
                if (changes != null && changes.TryGetValue(id, out var ch) && Math.Abs(ch.After - ch.Before) > 1e-9)
                    ring = Math.Sign(VoterLike(id)) == 0 ? AppTheme.Muted : Math.Sign(ch.After - ch.Before) * Math.Sign(VoterLike(id)) > 0 ? AppTheme.Good : AppTheme.Bad;
                float r = sel || hov ? 24 : 21;
                var glow = new Color(ring == AppTheme.BorderStrong ? AppTheme.ValueNode : ring, 0.10f * alpha);
                DrawCircle(p, r + 8, glow);
                DrawCircle(p, r, new Color(AppTheme.Card, alpha));
                DrawArc(p, r, 0, Mathf.Tau, 48, new Color(ring, alpha), 3f, true);
                Icons.Draw(this, _icon[id], p, r * 0.95f, new Color(sel || hov ? Colors.White : baseColor, alpha));
                if (_ghost) continue;
                string name = t.Name(id);
                float w = sel || hov ? 220 : 130;
                Label(AppTheme.Semi, new Vector2(p.X, p.Y + r + 17), sel || hov ? name : Fit(AppTheme.Semi, name, AppTheme.Caption, w), HorizontalAlignment.Center, w, AppTheme.Caption,
                    new Color(sel || hov ? Colors.White : AppTheme.Text, dim ? 0.35f : 1f));
            }
        }
        if (!_ghost) DrawLegend();
    }

    private void DrawEllipse(Vector2 c, float rx, float ry, Color color, bool filled, float width = 1f)
    {
        var pts = new Vector2[97];
        for (int i = 0; i <= 96; i++) { float a = Mathf.Tau * i / 96; pts[i] = c + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry); }
        if (filled) DrawColoredPolygon(pts[..96], color);
        else DrawPolyline(pts, color, width, true);
    }

    private void DrawLegend()
    {
        if (_s == null) return;
        float x = 24, y = Size.Y - 108;
        foreach (var (color, icon, key) in new[] { (AppTheme.Tax, IconKind.Percent, "ui.map.taxes"), (AppTheme.Spend, IconKind.CoinOut, "ui.map.spending"),
                     (AppTheme.Regulation, IconKind.Scale, "ui.map.regulation"), (AppTheme.ValueNode, IconKind.Gauge, "ui.map.values") })
        {
            if (_insetLeft == 0)
            {
                DrawCircle(new Vector2(x + 8, y - 5), 9, AppTheme.Tint(color, 0.18f));
                Icons.Draw(this, icon, new Vector2(x + 8, y - 5), 12, color);
                Label(AppTheme.Medium, new Vector2(x + 24, y), _s.T.T(key), HorizontalAlignment.Left, 200, AppTheme.Caption, AppTheme.Text2);
            }
            y += 24;
        }
    }

    // ---- interaction -----------------------------------------------------------------------------------

    private string? Hit(Vector2 mouse)
    {
        if (Hidden) return null;
        string? best = null; float bestD = 26;
        foreach (var (id, _) in _unit) { float d = At(id).DistanceTo(mouse); if (d < bestD) { bestD = d; best = id; } }
        return best;
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion mm)
        {
            var h = Hit(mm.Position);
            if (h != _hover) { _hover = h; MouseDefaultCursorShape = h != null ? CursorShape.PointingHand : CursorShape.Arrow; QueueRedraw(); }
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
        bool isPolicy = _policyIds.Contains(id);
        var color = _color.GetValueOrDefault(id, AppTheme.Accent);

        var head = Ui.HBox(12);
        head.AddChild(new IconBadge(_icon.GetValueOrDefault(id, IconKind.Gauge), color, 44));
        var titles = Ui.VBox(2);
        titles.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        titles.AddChild(Ui.Caption(t.T(isPolicy ? "ui.map.policy" : "ui.map.value"), color));
        var name = Ui.Text(t.Name(id), Colors.White, AppTheme.SectionSize, true, AppTheme.Bold);
        titles.AddChild(name);
        head.AddChild(titles);
        var close = new IconButton(IconKind.Close, "", t.T("ui.client.close"), new Vector2(32, 32));
        close.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        close.Pressed += () => Select(null);
        head.AddChild(close);
        _cardBody.AddChild(head);

        var value = s.Packs.Values.FirstOrDefault(v => v.Id == id);
        if (value != null) _cardBody.AddChild(Ui.Big(Fmt.Value(value, s.Country.Values.GetValueOrDefault(id)), Colors.White, AppTheme.Display));
        else if (s.Packs.Policies.FirstOrDefault(p => p.Id == id) is { } policy)
        {
            _cardBody.AddChild(Ui.Big(Fmt.Policy(policy, s.Country.Policies.GetValueOrDefault(id), t), Colors.White, AppTheme.Display));
            double frac = policy.Max > policy.Min ? (s.Country.Policies.GetValueOrDefault(id) - policy.Min) / (policy.Max - policy.Min) : 0;
            var m = new Meter(frac, 0, 1, color, 300) { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            _cardBody.AddChild(m);
        }
        if (t.Description(id) is { } desc) _cardBody.AddChild(Ui.Text(desc, AppTheme.Text2, 14, true));

        if (value != null)
        {
            var change = s.Last?.Changes.FirstOrDefault(c => c.ValueId == id);
            _cardBody.AddChild(Ui.Section(t.T("ui.heading.why", t.Name(id))));
            if (change == null) _cardBody.AddChild(Ui.Text(t.T("ui.why.none"), Ui.Muted, 14, true));
            else
            {
                _cardBody.AddChild(Ui.Text(t.T("ui.info.change", Fmt.Value(value, change.Before), Fmt.Value(value, change.After)), AppTheme.Text2, 14, true));
                var lines = change.Contributions.Where(c => Math.Abs(c.Amount) > 1e-9 * Math.Max(1.0, Math.Abs(change.Before)))
                    .OrderByDescending(c => Math.Abs(c.Amount)).ThenBy(c => c.EdgeId, StringComparer.Ordinal).ToList();
                foreach (var c in lines)
                    _cardBody.AddChild(LinkRow(c.FromId, t.T("ui.why.line", t.Name(c.FromId), t.T(c.Amount > 0 ? "ui.why.up" : "ui.why.down")), c.Amount > 0));
                if (lines.Count == 0 && value.Owner == null) _cardBody.AddChild(Ui.Text(t.T("ui.why.none"), Ui.Muted, 14, true));
                if (value.Owner != null) _cardBody.AddChild(Ui.Text(t.T("ui.why.model"), Ui.Muted, AppTheme.Caption, true));
            }
            if (Charts.All.FirstOrDefault(c => c.Series(s).Any(x => x.Key == History.ValueKey(id))) is { } spec)
            {
                var b = Ui.Btn("  " + t.T("ui.map.open_chart"), () => ChartDialog.Open(this, s, spec));
                b.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
                _cardBody.AddChild(b);
            }
        }

        void Links(string headKey, IEnumerable<EdgeDef> edges, Func<EdgeDef, string> other)
        {
            var list = edges.OrderBy(e => e.Id, StringComparer.Ordinal).ToList();
            if (list.Count == 0) return;
            _cardBody.AddChild(Ui.Section(t.T(headKey)));
            foreach (var e in list) _cardBody.AddChild(LinkRow(other(e), t.Name(other(e)), e.Strength >= 0));
        }
        Links("ui.client.graph.influences", s.Packs.Edges.Where(e => e.From == id), e => e.To);
        Links("ui.client.graph.influenced_by", s.Packs.Edges.Where(e => e.To == id), e => e.From);
    }

    private Control LinkRow(string otherId, string text, bool up)
    {
        var row = Ui.HBox(10);
        row.AddChild(new IconView(_icon.GetValueOrDefault(otherId, IconKind.Gauge), 18, _color.GetValueOrDefault(otherId, AppTheme.Muted)));
        var l = Ui.Text(text, AppTheme.Text, 14, true);
        row.AddChild(l);
        row.AddChild(Ui.Text(up ? "▲" : "▼", up ? Ui.Good : Ui.Bad, 14));
        return row;
    }
}
