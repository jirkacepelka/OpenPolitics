using Godot;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Presentation;

namespace OpenPolitics.Client;

/// <summary>
/// The map, in the structure of Democracy 4: a full-window field of round nodes, sized by importance, packed into three sectors
/// (taxes, spending, regulation) that are separated by dashed radial lines from a central hub showing political capital. Policies are
/// chrome discs with a white pictogram and a ring for the slider position; indicators are grey discs with a dark pictogram and a
/// traffic-light ring for how they compare with the start of the term. No labels at rest: hover or select a node to see its name and
/// its influences (thick green/red arrows with moving dashes). The layout is recomputed for every window shape so the field is always full.
/// </summary>
public partial class WorldMap : Control
{
    private sealed class Node
    {
        public string Id = "";
        public bool Policy;
        public int Group;
        public float Weight = 1;
        public float R;
        public Vector2 P;
        public IconKind Icon;
    }

    private Session? _s;
    private readonly List<Node> _nodes = new();
    private readonly Dictionary<string, Node> _byId = new(StringComparer.Ordinal);
    private readonly float[] _bounds = new float[4]; // sector boundary angles: b0 < b1 < b2 < b0 + tau
    private Vector2 _laidOut = new(-1, -1);
    private string _packKey = "";
    private string? _hover;
    private float _flow;
    private Vector2 _hub;
    private float _hubW = 132, _hubH = 60;

    public string? SelectedId { get; private set; }
    /// <summary>A voter group whose concerns the map highlights (null = off).</summary>
    public string? LensGroup { get; private set; }
    public event Action<string>? NodeClicked;
    public event Action? HubClicked;
    /// <summary>Space kept free on the right (an overlay drawer in compact).</summary>
    public float RightInset { get; set; }

    public static int GroupOf(PolicyDef p) => p.RevenueBaseValueId != null ? 0 : p.CostPerUnit != 0 ? 1 : 2;
    public static IconKind GroupIcon(int g) => g == 0 ? IconKind.Percent : g == 1 ? IconKind.CoinOut : IconKind.Scale;

    public WorldMap()
    {
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true;
        TextureRepeat = TextureRepeatEnum.Enabled;
    }

    public void Setup(Session s)
    {
        _s = s;
        string key = $"{s.Packs.Values.Count}/{s.Packs.Policies.Count}/{s.Packs.Edges.Count}/{s.ScenarioId}";
        if (key != _packKey) { _packKey = key; BuildNodes(); _laidOut = new Vector2(-1, -1); }
        QueueRedraw();
    }

    public void Select(string? id) { SelectedId = id; QueueRedraw(); }
    public void SetLens(string? groupId) { LensGroup = groupId; QueueRedraw(); }

    // ---- nodes and layout ------------------------------------------------------------------------------

    private void BuildNodes()
    {
        _nodes.Clear(); _byId.Clear();
        var s = _s!;
        var degree = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var e in s.Packs.Edges) { degree[e.From] = degree.GetValueOrDefault(e.From) + 1; degree[e.To] = degree.GetValueOrDefault(e.To) + 1; }
        int maxDeg = Math.Max(1, degree.Values.DefaultIfEmpty(1).Max());
        var groupOfPolicy = s.Packs.Policies.ToDictionary(p => p.Id, GroupOf, StringComparer.Ordinal);
        foreach (var p in s.Packs.Policies.OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            float imp = (float)Math.Sqrt(degree.GetValueOrDefault(p.Id) / (double)maxDeg);
            Add(new Node { Id = p.Id, Policy = true, Group = GroupOf(p), Weight = 0.78f + 0.5f * imp, Icon = Icons.For(p.Id) });
        }
        foreach (var v in s.Packs.Values.OrderBy(v => v.Id, StringComparer.Ordinal))
        {
            // An indicator sits in the sector whose policies act on it most.
            var counts = new int[3];
            foreach (var e in s.Packs.Edges.Where(e => e.To == v.Id && groupOfPolicy.ContainsKey(e.From))) counts[groupOfPolicy[e.From]]++;
            int g = counts.Max() == 0 ? (Math.Abs(v.Id.GetHashCode() % 3)) : Array.IndexOf(counts, counts.Max());
            float imp = (float)Math.Sqrt(degree.GetValueOrDefault(v.Id) / (double)maxDeg);
            bool headline = v.Id is "base:gdp" or "base:unemployment" or "base:inflation" or "base:debt" or "base:deficit_pct";
            Add(new Node { Id = v.Id, Policy = false, Group = g, Weight = 0.9f + 0.55f * imp + (headline ? 0.35f : 0), Icon = Icons.For(v.Id) });
        }
        void Add(Node n) { _nodes.Add(n); _byId[n.Id] = n; }
    }

    private Rect2 Field => new(0, 0, Math.Max(Size.X - RightInset, 200), Size.Y);

    private static float Hash(string s, int salt)
    {
        unchecked
        {
            uint h = 2166136261u ^ (uint)salt;
            foreach (char c in s) h = (h ^ c) * 16777619u;
            return (h % 10000) / 10000f;
        }
    }

    private void EnsureLayout()
    {
        var field = Field;
        if (_nodes.Count == 0 || (Math.Abs(field.Size.X - _laidOut.X) < 1 && Math.Abs(field.Size.Y - _laidOut.Y) < 1)) return;
        _laidOut = field.Size;
        float W = field.Size.X, H = field.Size.Y;
        _hub = new Vector2(W / 2, H / 2);
        _hubH = Math.Clamp(W * 0.04f, 44, 64); _hubW = _hubH * 2.3f;

        // Radii: the nodes cover about 30 % of the field.
        float area = W * H;
        float sumW2 = _nodes.Sum(n => n.Weight * n.Weight);
        float k = Mathf.Clamp(Mathf.Sqrt(0.23f * area / (Mathf.Pi * sumW2)), 13f, 40f);
        foreach (var n in _nodes) n.R = k * n.Weight;

        // Sector boundaries: wedge areas proportional to the node area in each sector; taxes centred at the top.
        var need = new float[3];
        foreach (var n in _nodes) need[n.Group] += n.R * n.R;
        float total = need.Sum();
        const int samples = 1440;
        var cum = new float[samples + 1];
        float start = -Mathf.Pi / 2 - Mathf.Pi; // integrate from the bottom so the top is in the middle
        for (int i = 0; i < samples; i++)
        {
            float a = start + Mathf.Tau * (i + 0.5f) / samples;
            float d = EdgeDistance(a, W, H);
            cum[i + 1] = cum[i] + 0.5f * d * d * Mathf.Tau / samples;
        }
        float AngleAt(float areaFrom) // angle reached after areaFrom of the integral
        {
            int lo = 0;
            while (lo < samples && cum[lo + 1] < areaFrom) lo++;
            return start + Mathf.Tau * lo / samples;
        }
        float full = cum[samples];
        float half = full / 2; // the top (-pi/2) sits at half of the integral
        float tax = need[0] / total * full, spend = need[1] / total * full;
        _bounds[0] = AngleAt(half - tax / 2);
        _bounds[1] = AngleAt(half + tax / 2);
        _bounds[2] = AngleAt(Math.Min(half + tax / 2 + spend, full - 1));
        _bounds[3] = _bounds[0] + Mathf.Tau;

        // Initial positions: a jittered spread over each wedge, larger nodes nearer the hub.
        for (int g = 0; g < 3; g++)
        {
            var list = _nodes.Where(n => n.Group == g).OrderByDescending(n => n.R).ThenBy(n => n.Id, StringComparer.Ordinal).ToList();
            float a0 = _bounds[g], a1 = _bounds[g + 1];
            for (int i = 0; i < list.Count; i++)
            {
                float fa = (i * 0.618034f + Hash(list[i].Id, 1) * 0.2f) % 1f;
                float a = a0 + (a1 - a0) * (0.12f + 0.76f * fa);
                float d = EdgeDistance(a, W, H);
                float fr = 0.3f + 0.6f * Mathf.Sqrt((i + 0.5f) / list.Count);
                list[i].P = _hub + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d * fr;
            }
        }

        // Relaxation: soft repulsion that spreads the nodes over the field, kept in their wedge, off the hub and inside the field.
        const float gap = 8;
        float hubR = _hubW / 2 + 6;
        for (int it = 0; it < 260; it++)
        {
            float reach = it < 200 ? 1.9f : 1.25f;
            var move = new Vector2[_nodes.Count];
            for (int i = 0; i < _nodes.Count; i++)
                for (int j = i + 1; j < _nodes.Count; j++)
                {
                    var a = _nodes[i]; var b = _nodes[j];
                    var d = b.P - a.P;
                    float len = d.Length();
                    float want = (a.R + b.R + gap) * (a.Group == b.Group ? reach : 1.05f);
                    if (len >= want) continue;
                    var dir = len < 0.01f ? new Vector2(Hash(a.Id, j) - 0.5f, Hash(b.Id, i) - 0.5f).Normalized() : d / len;
                    float push = (want - len) * 0.5f * (len < a.R + b.R + gap ? 1f : 0.25f);
                    float wa = b.R / (a.R + b.R), wb = a.R / (a.R + b.R);
                    move[i] -= dir * push * wa; move[j] += dir * push * wb;
                }
            for (int i = 0; i < _nodes.Count; i++)
            {
                var n = _nodes[i];
                var p = n.P + move[i].LimitLength(24);
                p = KeepInWedge(p, n.R + 6, n.Group);
                var fromHub = p - _hub;
                float minHub = Math.Max(hubR, _hubH / 2 + 6) + n.R;
                // the hub is a pill: keep an ellipse around it clear
                var e = new Vector2(fromHub.X / (_hubW / 2 + 8 + n.R), fromHub.Y / (_hubH / 2 + 8 + n.R));
                if (e.Length() < 1) p = _hub + (e.Length() < 0.01f ? new Vector2(0, -1) : e.Normalized()) * new Vector2(_hubW / 2 + 8 + n.R, _hubH / 2 + 8 + n.R);
                _ = minHub;
                p.X = Math.Clamp(p.X, n.R + 6, W - n.R - 6);
                p.Y = Math.Clamp(p.Y, n.R + 6, H - n.R - 6);
                n.P = p;
            }
        }
    }

    private Vector2 KeepInWedge(Vector2 p, float margin, int g)
    {
        float a0 = _bounds[g], a1 = _bounds[g + 1];
        var rel = p - _hub;
        float ang = Mathf.Atan2(rel.Y, rel.X);
        while (ang < a0 - Mathf.Pi * 0.5f) ang += Mathf.Tau;
        while (ang > a0 + Mathf.Tau - Mathf.Pi * 0.5f) ang -= Mathf.Tau;
        float mid = (a0 + a1) / 2;
        // outside the wedge entirely: rotate it in
        float span = a1 - a0;
        float off = Mathf.Wrap(ang - mid, -Mathf.Pi, Mathf.Pi);
        if (Math.Abs(off) > span / 2)
        {
            float target = mid + Math.Sign(off) * span / 2 * 0.9f;
            float len = Math.Max(rel.Length(), margin * 2);
            return _hub + new Vector2(Mathf.Cos(target), Mathf.Sin(target)) * len;
        }
        // inside: keep a perpendicular distance from both boundary lines
        foreach (var (b, sign) in new[] { (a0, 1f), (a1, -1f) })
        {
            var u = new Vector2(Mathf.Cos(b), Mathf.Sin(b));
            if (u.Dot(rel) <= 0) continue;
            float cross = u.X * rel.Y - u.Y * rel.X; // positive = clockwise of the line
            float wantMin = margin;
            if (sign * cross < wantMin)
            {
                var normal = new Vector2(-u.Y, u.X) * sign;
                rel += normal * (wantMin - sign * cross);
            }
        }
        return _hub + rel;
    }

    /// <summary>Distance from the hub to the edge of the field in direction <paramref name="a"/>.</summary>
    private static float EdgeDistance(float a, float W, float H)
    {
        float cx = Mathf.Cos(a), cy = Mathf.Sin(a);
        float tx = Math.Abs(cx) < 1e-6f ? float.MaxValue : (W / 2) / Math.Abs(cx);
        float ty = Math.Abs(cy) < 1e-6f ? float.MaxValue : (H / 2) / Math.Abs(cy);
        return Math.Min(tx, ty);
    }

    // ---- drawing -----------------------------------------------------------------------------------------

    private bool Focused(out string? focus)
    {
        focus = _hover ?? SelectedId;
        return focus != null && _byId.ContainsKey(focus);
    }

    private string? _shownFocus; // the focus being faded in or out
    private float _focusT;       // 0..1 eased strength of the focus fade

    public override void _Process(double delta)
    {
        bool motion = !DisplaySettings.ReduceMotion && Main.Animations;
        bool focused = Focused(out var focus);
        if (focused) _shownFocus = focus;
        float target = focused ? 1 : 0;
        bool redraw = false;
        if (Math.Abs(_focusT - target) > 0.001f) { _focusT = motion ? Mathf.MoveToward(_focusT, target, (float)delta / 0.15f) : target; redraw = true; }
        else if (!focused) _shownFocus = null;
        if (focused && motion) { _flow = (_flow + (float)delta * 34f) % 1000f; redraw = true; }
        if (redraw) QueueRedraw();
    }

    private static readonly Color Separator = new("cfc9bf");
    private static readonly Color Watermark = new("e2ded6");

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), AppTheme.MapBg);
        DrawTextureRect(AppTheme.GridDot, new Rect2(Vector2.Zero, Size), true);
        if (_s == null || _nodes.Count == 0) return;
        EnsureLayout();
        var s = _s; var t = s.T;
        var field = Field;
        if (!Main.Animations || DisplaySettings.ReduceMotion) { bool f0 = Focused(out var fz); _focusT = f0 ? 1 : 0; _shownFocus = f0 ? fz : null; }

        // Sector names as quiet watermarks, then thin dashed separators.
        var wm = AppTheme.Face(600, false, 3);
        for (int g = 0; g < 3; g++)
        {
            float mid = (_bounds[g] + _bounds[g + 1]) / 2;
            float d = EdgeDistance(mid, field.Size.X, field.Size.Y);
            var at = _hub + new Vector2(Mathf.Cos(mid), Mathf.Sin(mid)) * d * 0.64f;
            string name = t.T("ui.client.group." + g).ToUpperInvariant();
            int fs = (int)Math.Clamp(field.Size.X / 48, 16, 30);
            float w = wm.GetStringSize(name, HorizontalAlignment.Left, -1, fs).X;
            DrawString(wm, at - new Vector2(w / 2, -fs / 3f), name, HorizontalAlignment.Left, -1, fs, Watermark);
        }
        for (int b = 0; b < 3; b++)
        {
            var u = new Vector2(Mathf.Cos(_bounds[b]), Mathf.Sin(_bounds[b]));
            float d = EdgeDistance(_bounds[b], field.Size.X, field.Size.Y);
            float from = _hubW / 2 + 14;
            for (float x = from; x < d; x += 12) DrawLine(_hub + u * x, _hub + u * Math.Min(x + 5, d), Separator, 1.2f, true);
        }

        string? focus = _shownFocus;
        bool focused = focus != null && _byId.ContainsKey(focus) && _focusT > 0.001f;
        var neighbours = new HashSet<string>(StringComparer.Ordinal);
        if (focused)
            foreach (var e in s.Packs.Edges)
                if ((e.From == focus || e.To == focus) && _byId.ContainsKey(e.From) && _byId.ContainsKey(e.To)) { neighbours.Add(e.From); neighbours.Add(e.To); }

        if (focused)
            foreach (var e in s.Packs.Edges)
            {
                if (e.From == e.To || (e.From != focus && e.To != focus)) continue;
                if (!_byId.TryGetValue(e.From, out var a) || !_byId.TryGetValue(e.To, out var b)) continue;
                DrawEdge(a, b, new Color(e.Strength >= 0 ? AppTheme.Good : AppTheme.Bad, _focusT), Math.Abs(e.Strength));
            }

        var group = LensGroup == null ? null : s.Packs.VoterGroups.FirstOrDefault(g => g.Id == LensGroup);
        var pending = s.Game.PendingPolicies.GroupBy(p => p.PolicyId).ToDictionary(g => g.Key, g => g.Last().Value, StringComparer.Ordinal);
        foreach (var n in _nodes.OrderBy(n => n.Id == focus ? 2 : neighbours.Contains(n.Id) ? 1 : 0))
        {
            float alpha = 1f;
            if (focused && n.Id != focus && !neighbours.Contains(n.Id)) alpha = 1 - 0.72f * _focusT;
            if (group != null)
            {
                bool relevant = n.Policy ? group.PolicyWeights.ContainsKey(n.Id) : group.ValueWeights.ContainsKey(n.Id) || (group.LevelWeights?.ContainsKey(n.Id) ?? false);
                if (!relevant) alpha = Math.Min(alpha, 0.25f);
            }
            DrawNode(n, alpha, group, pending, n.Id == focus ? _focusT : 0);
        }
        DrawHub();
        if (focused && _byId.TryGetValue(focus!, out var fn)) DrawTag(fn, _focusT);
    }

    private void DrawEdge(Node a, Node b, Color color, double strength)
    {
        var p0 = a.P; var p2 = b.P;
        var d = p2 - p0; float len = d.Length();
        if (len < 1) return;
        var normal = new Vector2(-d.Y, d.X) / len;
        var ctrl = (p0 + p2) / 2 + normal * len * 0.12f;
        const int n = 32;
        var pts = new List<Vector2>(n + 1);
        for (int i = 0; i <= n; i++)
        {
            float tt = i / (float)n;
            var p = (1 - tt) * (1 - tt) * p0 + 2 * (1 - tt) * tt * ctrl + tt * tt * p2;
            if (p.DistanceTo(p0) < a.R + 3) continue;
            if (p.DistanceTo(p2) < b.R + 5) break;
            pts.Add(p);
        }
        if (pts.Count < 2) return;
        float w = strength >= 0.5 ? 3f : strength >= 0.2 ? 2.3f : 1.7f;
        var faint = new Color(color, color.A * 0.22f);
        DrawPolyline(pts.ToArray(), faint, w, true);
        var col = new Color(color, color.A * 0.95f);
        if (DisplaySettings.ReduceMotion || !Main.Animations) DrawPolyline(pts.ToArray(), col, w, true);
        else
        {
            float period = 18, on = 9, phase = _flow * (0.6f + (float)Math.Min(strength, 1.0)) % period, acc = -phase;
            for (int i = 1; i < pts.Count; i++)
            {
                var s0 = pts[i - 1]; var s1 = pts[i]; float seg = s0.DistanceTo(s1);
                float pos = 0;
                while (pos < seg)
                {
                    float m = ((acc % period) + period) % period;
                    bool isOn = m < on;
                    float step = Math.Min(seg - pos, isOn ? on - m : period - m);
                    if (isOn) DrawLine(s0.Lerp(s1, pos / seg), s0.Lerp(s1, (pos + step) / seg), col, w, true);
                    pos += step; acc += step;
                    if (step <= 0.001f) break;
                }
            }
        }
        var tip = pts[^1]; var dir = (pts[^1] - pts[^2]).Normalized(); var side = new Vector2(-dir.Y, dir.X) * (w + 2.5f);
        DrawColoredPolygon(new[] { tip + dir * 3, tip - dir * (w * 3f + 4) + side, tip - dir * (w * 3f + 4) - side }, col);
    }

    private void DrawNode(Node n, float alpha, VoterGroupDef? group, Dictionary<string, double> pending, float focus)
    {
        var s = _s!;
        float r = n.R * (1 + 0.06f * focus);
        float ring = Mathf.Clamp(r * 0.075f, 2f, 3.5f);
        var p = n.P;
        // soft contact shadow (fine depth), stronger when focused
        DrawCircle(p + new Vector2(0, 1.5f + 2 * focus), r + 0.5f, new Color(AppTheme.Ink, (0.06f + 0.08f * focus) * alpha));
        if (n.Id == SelectedId) DrawArc(p, r + 5, 0, Mathf.Tau, 72, new Color(AppTheme.Chrome, alpha), 2f, true);
        float ringR = r - ring / 2 - 1;
        if (n.Policy)
        {
            var def = s.Packs.Policies.First(x => x.Id == n.Id);
            double v = s.Country.Policies.GetValueOrDefault(n.Id, def.Default);
            double frac = def.Max > def.Min ? (v - def.Min) / (def.Max - def.Min) : 0;
            var arcColor = AppTheme.Chrome;
            if (group != null && group.PolicyWeights.ContainsKey(n.Id)) { arcColor = AppTheme.Traffic(0.5 + 0.5 * Ui.Opinion(s, group, def)); frac = 1; }
            DrawCircle(p, r, new Color(AppTheme.ChromeTint.Lerp(new Color("ffffff"), 0.15f), alpha));
            DrawArc(p, ringR, 0, Mathf.Tau, 72, new Color(AppTheme.Chrome, 0.16f * alpha), ring, true);
            if (frac > 0.004) DrawArc(p, ringR, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau * (float)frac, 72, new Color(arcColor, alpha), ring, true);
            Icons.Draw(this, n.Icon, p, r * 0.92f, new Color(AppTheme.Chrome.Darkened(0.1f), alpha));
            if (pending.TryGetValue(n.Id, out var target) && Math.Abs(target - v) > 1e-9)
            {
                double tf = def.Max > def.Min ? (target - def.Min) / (def.Max - def.Min) : 0;
                float a = -Mathf.Pi / 2 + Mathf.Tau * (float)tf;
                var m = p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * ringR;
                DrawCircle(m, ring + 2.5f, new Color(AppTheme.Surface, alpha));
                DrawCircle(m, ring + 0.8f, new Color(AppTheme.Chrome, alpha));
            }
        }
        else
        {
            double good = Ui.Goodness(s, n.Id);
            bool cares = Math.Abs(Ui.VoterLike(s, n.Id)) > 1e-12;
            if (group != null && (group.ValueWeights.ContainsKey(n.Id) || (group.LevelWeights?.ContainsKey(n.Id) ?? false)))
            {
                double like = group.ValueWeights.GetValueOrDefault(n.Id) + (group.LevelWeights?.GetValueOrDefault(n.Id) ?? 0);
                double g0 = Ui.Goodness(s, n.Id);
                good = Math.Sign(like) == Math.Sign(Ui.VoterLike(s, n.Id)) ? g0 : 1 - g0;
                cares = true;
            }
            DrawCircle(p, r, new Color(new Color("ffffff"), alpha));
            DrawArc(p, ringR, 0, Mathf.Tau, 72, new Color(AppTheme.TrackLight, alpha), ring, true);
            if (cares)
            {
                float frac = (float)Math.Clamp(0.12 + 0.88 * good, 0.12, 1);
                DrawArc(p, ringR, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau * frac, 72, new Color(AppTheme.Traffic(good), alpha), ring, true);
            }
            Icons.Draw(this, n.Icon, p, r * 0.92f, new Color(AppTheme.Ink2, alpha));
        }
    }

    private Rect2 HubRect => new(_hub - new Vector2(_hubW, _hubH) / 2, new Vector2(_hubW, _hubH));

    private void DrawHub()
    {
        var s = _s!;
        var rect = HubRect;
        var shadow = AppTheme.Box(AppTheme.Graphite, (int)(_hubH / 2));
        shadow.ShadowColor = new Color(AppTheme.Ink, 0.18f); shadow.ShadowSize = 10; shadow.ShadowOffset = new Vector2(0, 3);
        DrawStyleBox(shadow, rect);
        float icon = _hubH * 0.62f;
        var ic = new Vector2(rect.Position.X + _hubH / 2, rect.GetCenter().Y);
        DrawCircle(ic, icon / 2, AppTheme.Chrome);
        bool politics = s.Politics?.Party is not null;
        Icons.Draw(this, politics ? IconKind.Coin : IconKind.Calendar, ic, icon * 0.6f, Colors.White);
        string text = politics ? Ui.Num(s.Politics!.Party!.PoliticalCapital, "0") : $"{s.Turn}/{Session.HorizonTurns}";
        int fs = (int)(_hubH * 0.38f);
        DrawString(AppTheme.FigSemi, new Vector2(ic.X + icon / 2 + 8, rect.GetCenter().Y + fs * 0.36f), text, HorizontalAlignment.Left, rect.End.X - ic.X - icon / 2 - 12, fs, Colors.White);
    }

    private void DrawTag(Node n, float t01)
    {
        var s = _s!; var t = s.T;
        string name = t.Name(n.Id);
        string sub = "";
        if (n.Policy && s.Packs.Policies.FirstOrDefault(x => x.Id == n.Id) is { } pd) sub = Ui.Fix(Fmt.Policy(pd, s.Country.Policies.GetValueOrDefault(n.Id, pd.Default), t));
        else if (s.Packs.Values.FirstOrDefault(x => x.Id == n.Id) is { } vd) sub = Ui.Fix(Fmt.Value(vd, s.Country.Values.GetValueOrDefault(n.Id)));
        var f1 = AppTheme.Semi; var f2 = AppTheme.FigSemi;
        float w = Math.Max(f1.GetStringSize(name, HorizontalAlignment.Left, -1, 13).X, f2.GetStringSize(sub, HorizontalAlignment.Left, -1, 12).X) + 24;
        float h = sub.Length > 0 ? 44 : 30;
        var pos = new Vector2(n.P.X - w / 2, n.P.Y - n.R - 12 - h);
        if (pos.Y < 4 || new Rect2(pos, new Vector2(w, h)).Intersects(HubRect.Grow(4))) pos.Y = n.P.Y + n.R + 12;
        if (pos.Y + h > Size.Y - 4) pos.Y = n.P.Y - n.R - 12 - h;
        pos.X = Math.Clamp(pos.X, 4, Size.X - w - 4);
        pos.Y += (1 - t01) * 4;
        var box = AppTheme.Box(new Color(AppTheme.Graphite, t01), 8);
        box.ShadowColor = new Color(AppTheme.Ink, 0.18f * t01); box.ShadowSize = 8; box.ShadowOffset = new Vector2(0, 3);
        DrawStyleBox(box, new Rect2(pos, new Vector2(w, h)));
        DrawString(f1, pos + new Vector2(12, 19), name, HorizontalAlignment.Left, -1, 13, new Color(1, 1, 1, t01));
        if (sub.Length > 0) DrawString(f2, pos + new Vector2(12, 36), sub, HorizontalAlignment.Left, -1, 12, new Color(1, 1, 1, 0.65f * t01));
    }

    // ---- interaction -------------------------------------------------------------------------------------

    private string? Hit(Vector2 mouse)
    {
        string? best = null; float bestD = float.MaxValue;
        foreach (var n in _nodes)
        {
            float d = n.P.DistanceTo(mouse);
            if (d <= n.R + 4 && d < bestD) { bestD = d; best = n.Id; }
        }
        return best;
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion mm)
        {
            var h = Hit(mm.Position);
            bool hub = HubRect.HasPoint(mm.Position);
            if (h != _hover) { _hover = h; SetProcess(true); QueueRedraw(); }
            MouseDefaultCursorShape = h != null || hub ? CursorShape.PointingHand : CursorShape.Arrow;
            TooltipText = hub && _s != null ? (_s.Politics?.Party != null ? _s.T.T("ui.party.capital") : _s.DateText()) : "";
        }
        else if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
        {
            if (HubRect.HasPoint(mb.Position)) { HubClicked?.Invoke(); return; }
            var h = Hit(mb.Position);
            if (h != null) { Select(h); NodeClicked?.Invoke(h); }
            else Select(null);
        }
    }

    /// <summary>Screen position of a node (for scripted runs and tests).</summary>
    public Vector2? PositionOf(string id) { EnsureLayout(); return _byId.TryGetValue(id, out var n) ? n.P : null; }

    /// <summary>Hovers a node as the mouse would (scripted screenshots).</summary>
    public void HoverForTest(string? id) { _hover = id; _shownFocus = id; _focusT = id == null ? 0 : 1; QueueRedraw(); }

    public override void _Notification(int what)
    {
        if (what == NotificationResized) QueueRedraw();
        else if (what == NotificationMouseExit && _hover != null) { _hover = null; SetProcess(true); QueueRedraw(); }
    }
}
