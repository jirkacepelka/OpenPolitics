using Godot;

namespace OpenPolitics.Client;

public enum IconKind
{
    Overview, Policies, Voters, Politics, Party, News, Coin, Menu, Save, Play, FastForward,
    Percent, Tag, Factory, House, Leaf, Arrows, Pill, Ballot, Target, Star, Bus, Family, Briefcase, Wheat, Wifi, Key, Globe, Clock,
    Bolt, Eye, Hand, Health, Education, Shield, Flask, Road, Columns, Alert, Scale, ChartUp, Gear, Users, Bank, CoinIn, CoinOut, Gauge,
    Chart, Close, Map, Grid, Check, Plus, Minus, Fit, Undo, SortAz, SortSize, Smile, Back, Search, Calendar,
    Cloud, Sun, Tree, Chimney, Cart, Gavel, Bricks, Contract, Pie, HousePercent, Diamond, Banknote, Bottle, Threshold, Elder, Pram,
    Inheritance, Sugar, BankLevy, Windfall, Vehicle, ElderCare, Disability, Vocational, SchoolMeals, Scholarship, Rail, Flood, Nuclear, Cyber, Justice, Prison, Border, Startup, Pin, Heart, Gun, Lock, Chat, Venus, Rings, Candle, Cigarette, Wine, Syringe, Pencil, Cross, Umbrella, Fist, Stop, Podium, Clipboard, NoCoin, Quill, Passport, Door, Crane, Dna, Block, Scroll, Ladder, Monitor, Tractor, Piggy, Safe,
}

/// <summary>
/// One family of outline pictograms drawn with primitives on a 24-unit grid, so the client needs no image assets and stays sharp at any
/// size. <see cref="For"/> picks a pictogram for a policy or indicator id.
/// </summary>
public static class Icons
{
    /// <summary>The pictogram for a policy, indicator or voter group id (by keyword, so new content gets a sensible icon).</summary>
    private static readonly Dictionary<string, IconKind> Exact = new()
    {
        ["inheritance_tax"] = IconKind.Inheritance,
        ["sugar_tax"] = IconKind.Sugar,
        ["bank_levy"] = IconKind.BankLevy,
        ["windfall_tax"] = IconKind.Windfall,
        ["vehicle_tax"] = IconKind.Vehicle,
        ["digital_services_tax"] = IconKind.Monitor,
        ["elderly_care"] = IconKind.ElderCare,
        ["disability_support"] = IconKind.Disability,
        ["mental_health"] = IconKind.Smile,
        ["vocational_training"] = IconKind.Vocational,
        ["school_meals"] = IconKind.SchoolMeals,
        ["scholarships"] = IconKind.Scholarship,
        ["university_fees"] = IconKind.Scroll,
        ["religious_education"] = IconKind.Cross,
        ["rail_network"] = IconKind.Rail,
        ["flood_protection"] = IconKind.Flood,
        ["nuclear_programme"] = IconKind.Nuclear,
        ["cybersecurity"] = IconKind.Cyber,
        ["justice_system"] = IconKind.Justice,
        ["prisons_and_rehabilitation"] = IconKind.Prison,
        ["border_guard"] = IconKind.Border,
        ["startup_support"] = IconKind.Startup,
        ["agriculture_subsidies"] = IconKind.Tractor,
        ["electoral_system"] = IconKind.Grid,
        ["pension_subsidy"] = IconKind.CoinIn, ["pension_tax_relief"] = IconKind.Piggy, ["mandatory_pension_pillar"] = IconKind.Safe,
        ["regional_funds"] = IconKind.Pin,
        ["foreign_aid"] = IconKind.Heart,
        ["public_housing"] = IconKind.Block,
        ["active_labour_policy"] = IconKind.Ladder,
        ["gun_laws"] = IconKind.Gun,
        ["data_protection"] = IconKind.Lock,
        ["press_freedom"] = IconKind.Chat,
        ["abortion_access"] = IconKind.Venus,
        ["same_sex_unions"] = IconKind.Rings,
        ["euthanasia"] = IconKind.Candle,
        ["smoking_ban"] = IconKind.Cigarette,
        ["alcohol_policy"] = IconKind.Wine,
        ["vaccination_policy"] = IconKind.Syringe,
        ["curriculum_reform"] = IconKind.Pencil,
        ["working_week_hours"] = IconKind.Calendar,
        ["paid_leave"] = IconKind.Umbrella,
        ["union_rights"] = IconKind.Fist,
        ["strike_rules"] = IconKind.Stop,
        ["foreign_investment_screening"] = IconKind.Search,
        ["competition_policy"] = IconKind.Podium,
        ["procurement_transparency"] = IconKind.Clipboard,
        ["anti_corruption"] = IconKind.NoCoin,
        ["judicial_independence"] = IconKind.Quill,
        ["citizenship_rules"] = IconKind.Passport,
        ["asylum_policy"] = IconKind.Door,
        ["zoning_planning"] = IconKind.Crane,
        ["gmo_policy"] = IconKind.Dna,
        ["deficit_pct"] = IconKind.Gauge,
        ["interest_rate"] = IconKind.Threshold,
        ["culture_sport"] = IconKind.Star,
    };

    public static IconKind For(string id)
    {
        string k = id.Contains(':') ? id[(id.IndexOf(':') + 1)..] : id;
        if (Exact.TryGetValue(k, out var one)) return one;
        (string Key, IconKind Icon)[] map =
        {
            ("carbon", IconKind.Cloud), ("green_subsidies", IconKind.Sun), ("environment_regulation", IconKind.Chimney), ("environment", IconKind.Tree),
            ("price_level", IconKind.Cart), ("business_regulation", IconKind.Gavel), ("capital", IconKind.Bricks), ("labour_flexibility", IconKind.Contract),
            ("gdp_nominal", IconKind.Overview), ("real_growth", IconKind.ChartUp), ("gdp", IconKind.Pie), ("property_tax", IconKind.HousePercent),
            ("wealth", IconKind.Diamond), ("minimum_wage", IconKind.Banknote), ("excise", IconKind.Bottle), ("electoral_threshold", IconKind.Threshold),
            ("pensions", IconKind.Elder), ("childcare", IconKind.Pram),
            ("pensioners", IconKind.Clock), ("rural_conservative", IconKind.Wheat), ("young_urban", IconKind.Users), ("middle_class", IconKind.House),
            ("low_income", IconKind.Hand), ("high_income", IconKind.Briefcase),
            ("income_tax", IconKind.Percent), ("vat", IconKind.Tag), ("corporate", IconKind.Factory), ("property", IconKind.House),
            ("carbon", IconKind.Leaf), ("wealth", IconKind.Coin), ("transaction", IconKind.Arrows), ("excise", IconKind.Pill),
            ("electoral", IconKind.Ballot), ("defence", IconKind.Target), ("culture", IconKind.Star), ("transport", IconKind.Bus),
            ("housing", IconKind.House), ("family", IconKind.Family), ("childcare", IconKind.Family), ("labour_policy", IconKind.Briefcase),
            ("rural", IconKind.Wheat), ("green", IconKind.Leaf), ("digital", IconKind.Wifi), ("environment", IconKind.Leaf),
            ("migration", IconKind.Globe), ("minimum_wage", IconKind.Coin), ("flexibility", IconKind.Briefcase), ("rent", IconKind.Key),
            ("drug", IconKind.Pill), ("university", IconKind.Education), ("retirement", IconKind.Clock), ("energy", IconKind.Bolt),
            ("business", IconKind.Factory), ("surveillance", IconKind.Eye), ("pension", IconKind.Hand), ("health", IconKind.Health),
            ("education", IconKind.Education), ("police", IconKind.Shield), ("research", IconKind.Flask), ("benefit", IconKind.Hand),
            ("infrastructure", IconKind.Road), ("administration", IconKind.Columns), ("crime", IconKind.Alert), ("inequality", IconKind.Scale),
            ("real_growth", IconKind.ChartUp), ("gdp", IconKind.ChartUp), ("capital", IconKind.Factory), ("productivity", IconKind.Gear),
            ("labour_force", IconKind.Users), ("unemployment", IconKind.Briefcase), ("inflation", IconKind.Tag), ("price", IconKind.Tag),
            ("debt", IconKind.Bank), ("interest", IconKind.Percent), ("revenue", IconKind.CoinIn), ("spending", IconKind.CoinOut),
            ("deficit", IconKind.Gauge),
        };
        foreach (var (key, icon) in map) if (k.Contains(key, StringComparison.Ordinal)) return icon;
        return IconKind.Gauge;
    }

    public static void Draw(CanvasItem c, IconKind kind, Vector2 center, float size, Color color)
    {
        float u = size / 24f;
        Vector2 P(float x, float y) => center + new Vector2(x, y) * u;
        float w = Math.Max(1.35f, 1.8f * u);
        void L(float x1, float y1, float x2, float y2) => c.DrawLine(P(x1, y1), P(x2, y2), color, w, true);
        void Poly(params float[] xy)
        {
            var pts = new Vector2[xy.Length / 2];
            for (int i = 0; i < pts.Length; i++) pts[i] = P(xy[2 * i], xy[2 * i + 1]);
            c.DrawPolyline(pts, color, w, true);
        }
        void Ring(float x, float y, float r) => c.DrawArc(P(x, y), r * u, 0, Mathf.Tau, 32, color, w, true);
        void Dot(float x, float y, float r) => c.DrawCircle(P(x, y), r * u, color);
        void Arc(float x, float y, float r, float a0, float a1) => c.DrawArc(P(x, y), r * u, a0, a1, 24, color, w, true);
        void Box(float x, float y, float bw, float bh) => c.DrawRect(new Rect2(P(x, y), new Vector2(bw, bh) * u), color, false, w);
        void Fill(float x, float y, float bw, float bh) => c.DrawRect(new Rect2(P(x, y), new Vector2(bw, bh) * u), color);
        void Tri(params float[] xy) => c.DrawColoredPolygon(new[] { P(xy[0], xy[1]), P(xy[2], xy[3]), P(xy[4], xy[5]) }, color);

        switch (kind)
        {
            case IconKind.Overview: Fill(-9, 2, 4, 7); Fill(-2, -3, 4, 12); Fill(5, -8, 4, 17); break;
            case IconKind.Chart: Poly(-10, 6, -4, 0, 1, 3, 9, -6); L(-10, 9, 10, 9); break;
            case IconKind.Policies:
                foreach (var (y, k) in new[] { (-7f, -3f), (0f, 4f), (7f, -1f) }) { L(-10, y, 10, y); Dot(k, y, 3.2f); }
                break;
            case IconKind.Voters:
                Ring(-4, -4, 3.6f); Arc(-4, 9, 7, Mathf.Pi * 1.08f, Mathf.Pi * 1.92f);
                Ring(6, -2, 3f); Arc(6, 9, 6, Mathf.Pi * 1.15f, Mathf.Pi * 1.9f);
                break;
            case IconKind.Politics:
                foreach (var (r, n) in new[] { (5f, 3), (8.5f, 5), (12f, 7) })
                    for (int i = 0; i < n; i++) { float a = Mathf.Pi + Mathf.Pi * i / (n - 1); Dot(Mathf.Cos(a) * r, 7 + Mathf.Sin(a) * r, 1.7f); }
                L(-12, 9.5f, 12, 9.5f);
                break;
            case IconKind.Party: L(-6, 11, -6, -10); c.DrawColoredPolygon(new[] { P(-6, -10), P(9, -5), P(-6, 0) }, color); break;
            case IconKind.News:
                Box(-10, -8, 20, 16); Fill(-7, -5, 6, 5);
                foreach (float y in new[] { -4f, -0.5f }) L(2, y, 7, y);
                L(-7, 3.5f, 7, 3.5f);
                break;
            case IconKind.Coin:
            {
                void Ell(float y, float from, float to) { var pts = new Vector2[25]; for (int i = 0; i <= 24; i++) { float a = from + (to - from) * i / 24; pts[i] = P(Mathf.Cos(a) * 9, y + Mathf.Sin(a) * 3.5f); } c.DrawPolyline(pts, color, w, true); }
                Ell(-6, 0, Mathf.Tau); L(-9, -6, -9, 6); L(9, -6, 9, 6);
                Ell(-1, 0, Mathf.Pi); Ell(3, 0, Mathf.Pi); Ell(6, 0, Mathf.Pi);
                break;
            }
            case IconKind.Menu: foreach (float y in new[] { -6f, 0f, 6f }) L(-8, y, 8, y); break;
            case IconKind.Save: Poly(-9, -9, 5, -9, 9, -5, 9, 9, -9, 9, -9, -9); Box(-5, -9, 8, 5); Box(-5, 2, 10, 7); break;
            case IconKind.Play: c.DrawColoredPolygon(new[] { P(-6, -9), P(9, 0), P(-6, 9) }, color); break;
            case IconKind.FastForward: Tri(-10, -8, 0, 0, -10, 8); Tri(0, -8, 10, 0, 0, 8); break;
            case IconKind.Close: L(-6, -6, 6, 6); L(-6, 6, 6, -6); break;
            case IconKind.Percent: L(7, -8, -7, 8); Ring(-5.5f, -5.5f, 3); Ring(5.5f, 5.5f, 3); break;
            case IconKind.Tag: Poly(-10, -2, -2, -10, 9, -10, 9, 1, 1, 9, -10, -2); Dot(4.5f, -5.5f, 1.8f); break;
            case IconKind.Factory: Poly(-10, 9, -10, -2, -4, 2, -4, -2, 2, 2, 2, -9, 7, -9, 7, 9, -10, 9); Fill(-7, 4, 3, 2.5f); Fill(-1, 4, 3, 2.5f); break;
            case IconKind.House: Poly(-10, 0, 0, -9, 10, 0); Poly(-7, -2, -7, 9, 7, 9, 7, -2); Box(-2, 3, 4, 6); break;
            case IconKind.Leaf:
            {
                var pts = new Vector2[25];
                for (int i = 0; i <= 12; i++)
                {
                    float tt = i / 12f, x = -10 + 20 * tt, y = -Mathf.Sin(Mathf.Pi * tt) * 6.5f;
                    pts[i] = center + new Vector2(x, y).Rotated(-Mathf.Pi / 4) * u;
                    pts[24 - i] = center + new Vector2(x, -y).Rotated(-Mathf.Pi / 4) * u;
                }
                c.DrawPolyline(pts, color, w, true);
                c.DrawLine(center + new Vector2(-12, 0).Rotated(-Mathf.Pi / 4) * u, center + new Vector2(5, 0).Rotated(-Mathf.Pi / 4) * u, color, w, true);
                break;
            }
            case IconKind.Arrows: L(-9, -4, 9, -4); Poly(5, -8, 9, -4, 5, 0); L(9, 4, -9, 4); Poly(-5, 0, -9, 4, -5, 8); break;
            case IconKind.Pill:
                c.DrawSetTransform(center, -Mathf.Pi / 4, Vector2.One);
                c.DrawStyleBox(AppTheme.Box(new Color(0, 0, 0, 0), (int)(5 * u), color, (int)Math.Round(w)), new Rect2(new Vector2(-11, -5) * u, new Vector2(22, 10) * u));
                c.DrawLine(new Vector2(0, -5) * u, new Vector2(0, 5) * u, color, w, true);
                c.DrawRect(new Rect2(new Vector2(-9, -3) * u, new Vector2(9, 6) * u), color);
                c.DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                break;
            case IconKind.Ballot: Poly(-10, -1, -10, 9, 10, 9, 10, -1, -10, -1); L(-4, 4, 4, 4); Poly(-5, -1, -5, -10, 5, -10, 5, -1); L(-2, -6, 0, -4); L(0, -4, 3, -8); break;
            case IconKind.Target: Ring(0, 0, 9); Ring(0, 0, 4.5f); Dot(0, 0, 1.6f); L(0, -12, 0, -7); L(0, 7, 0, 12); L(-12, 0, -7, 0); L(7, 0, 12, 0); break;
            case IconKind.Star:
            {
                var pts = new Vector2[11];
                for (int i = 0; i < 10; i++) { float a = -Mathf.Pi / 2 + i * Mathf.Pi / 5; float r = i % 2 == 0 ? 10.5f : 4.5f; pts[i] = P(Mathf.Cos(a) * r, Mathf.Sin(a) * r + 1); }
                pts[10] = pts[0];
                c.DrawPolyline(pts, color, w, true);
                break;
            }
            case IconKind.Bus: Poly(-8, 6, -8, -8, 8, -8, 8, 6, -8, 6); L(-8, -1, 8, -1); Dot(-4.5f, 3, 1.5f); Dot(4.5f, 3, 1.5f); L(-6, 6, -6, 9); L(6, 6, 6, 9); break;
            case IconKind.Family: Ring(-5, -6, 3); Arc(-5, 6, 6, Mathf.Pi * 1.1f, Mathf.Pi * 1.9f); Ring(5, -6, 3); Arc(5, 6, 6, Mathf.Pi * 1.1f, Mathf.Pi * 1.9f); Ring(0, 3, 2.2f); Arc(0, 11, 4, Mathf.Pi * 1.15f, Mathf.Pi * 1.85f); break;
            case IconKind.Briefcase: Poly(-10, -4, 10, -4, 10, 8, -10, 8, -10, -4); Poly(-4, -4, -4, -8, 4, -8, 4, -4); L(-10, 1, 10, 1); break;
            case IconKind.Wheat:
                L(0, 10, 0, -10);
                foreach (float y in new[] { -6f, -1f, 4f }) { Poly(0, y + 3, -5, y - 1, -5, y - 3); Poly(0, y + 3, 5, y - 1, 5, y - 3); }
                break;
            case IconKind.Wifi:
                Dot(0, 7, 1.9f);
                foreach (float r in new[] { 5f, 10f, 15f }) Arc(0, 9, r, Mathf.Pi * 1.25f, Mathf.Pi * 1.75f);
                break;
            case IconKind.Key: Ring(-5, 0, 4.5f); L(-0.5f, 0, 10, 0); L(7, 0, 7, 4); L(4, 0, 4, 3); break;
            case IconKind.Globe:
            {
                Ring(0, 0, 9.5f); L(-9.5f, 0, 9.5f, 0);
                var pts = new Vector2[33];
                for (int i = 0; i <= 32; i++) { float a = i * Mathf.Tau / 32; pts[i] = P(Mathf.Cos(a) * 4.2f, Mathf.Sin(a) * 9.5f); }
                c.DrawPolyline(pts, color, w, true);
                break;
            }
            case IconKind.Clock: Ring(0, 0, 9.5f); L(0, 0, 0, -6); L(0, 0, 4.5f, 2.5f); break;
            case IconKind.Bolt: c.DrawColoredPolygon(new[] { P(2, -11), P(-7, 2), P(-1, 2), P(-3, 11), P(7, -2), P(1, -2) }, color); break;
            case IconKind.Eye: Arc(0, 9, 13, Mathf.Pi * 1.22f, Mathf.Pi * 1.78f); Arc(0, -9, 13, Mathf.Pi * 0.22f, Mathf.Pi * 0.78f); Ring(0, 0, 3.6f); Dot(0, 0, 1.4f); break;
            case IconKind.Hand: Poly(-11, 3, -6, 3, -1, 5, 4, 5); Poly(-11, 9, -5, 9, 3, 10, 11, 4, 9, 2, 3, 5); Ring(3, -5, 4.5f); break;
            case IconKind.Health: c.DrawStyleBox(AppTheme.Box(color, (int)(2 * u)), new Rect2(P(-3, -10), new Vector2(6, 20) * u)); c.DrawStyleBox(AppTheme.Box(color, (int)(2 * u)), new Rect2(P(-10, -3), new Vector2(20, 6) * u)); break;
            case IconKind.Education: c.DrawColoredPolygon(new[] { P(-12, -3), P(0, -9), P(12, -3), P(0, 3) }, color); Poly(-7, 0, -7, 6, 0, 9, 7, 6, 7, 0); L(10, -2, 10, 6); break;
            case IconKind.Shield: Poly(0, -10, 9, -7, 9, 0, 7, 5, 0, 10, -7, 5, -9, 0, -9, -7, 0, -10); L(-4, 0, -1, 3); L(-1, 3, 5, -4); break;
            case IconKind.Flask: Poly(-3, -10, -3, -3, -9, 8, 9, 8, 3, -3, 3, -10); L(-5, -10, 5, -10); L(-6, 3, 6, 3); break;
            case IconKind.Road: L(-4, -10, -9, 10); L(4, -10, 9, 10); L(0, -9, 0, -6); L(0, -2, 0, 2); L(0, 6, 0, 10); break;
            case IconKind.Columns: Poly(-11, -4, 0, -10, 11, -4, -11, -4); L(-11, 9, 11, 9); foreach (float x in new[] { -7f, -2.3f, 2.3f, 7f }) L(x, -2, x, 7); break;
            case IconKind.Alert: Poly(0, -10, 10, 8, -10, 8, 0, -10); L(0, -3, 0, 2); Dot(0, 5, 1.4f); break;
            case IconKind.Scale: L(0, -10, 0, 9); L(-6, 9, 6, 9); L(-9, -6, 9, -6); Poly(-9, -6, -12, 2, -6, 2, -9, -6); Poly(9, -6, 6, 2, 12, 2, 9, -6); break;
            case IconKind.ChartUp: Poly(-10, 7, -4, 0, 0, 3, 9, -6); Poly(4, -6, 9, -6, 9, -1); L(-10, 10, 10, 10); break;
            case IconKind.Gear:
                Ring(0, 0, 6.5f); Ring(0, 0, 2.5f);
                for (int i = 0; i < 8; i++) { float a = i * Mathf.Tau / 8; L(Mathf.Cos(a) * 6.5f, Mathf.Sin(a) * 6.5f, Mathf.Cos(a) * 10, Mathf.Sin(a) * 10); }
                break;
            case IconKind.Users: Ring(0, -5, 4); Arc(0, 10, 9, Mathf.Pi * 1.12f, Mathf.Pi * 1.88f); Ring(-8, -2, 2.6f); Ring(8, -2, 2.6f); break;
            case IconKind.Bank: Poly(-11, -4, 0, -10, 11, -4, -11, -4); L(-11, 9, 11, 9); L(-11, 6, 11, 6); foreach (float x in new[] { -7f, 0f, 7f }) L(x, -1, x, 4); break;
            case IconKind.CoinIn: Box(-11, -1, 22, 12); Ring(0, 5, 2.8f); L(0, -11, 0, -4); Poly(-3.5f, -7, 0, -3.5f, 3.5f, -7); break;
            case IconKind.CoinOut: Box(-11, -1, 22, 12); Ring(0, 5, 2.8f); L(0, -4, 0, -11); Poly(-3.5f, -8, 0, -11.5f, 3.5f, -8); break;
            case IconKind.Gauge: Arc(0, 4, 10, Mathf.Pi, Mathf.Tau); L(-10, 4, 10, 4); L(0, 4, 5, -3); Dot(0, 4, 1.8f); break;
            case IconKind.Map:
                L(-7, -6, 6, -8); L(-7, -6, -2, 6); L(6, -8, -2, 6); L(6, -8, 8, 5); L(-2, 6, 8, 5);
                Dot(-7, -6, 3.2f); Dot(6, -8, 2.6f); Dot(-2, 6, 3.6f); Dot(8, 5, 2.4f);
                break;
            case IconKind.Grid: Box(-9, -9, 7.5f, 7.5f); Box(1.5f, -9, 7.5f, 7.5f); Box(-9, 1.5f, 7.5f, 7.5f); Box(1.5f, 1.5f, 7.5f, 7.5f); break;
            case IconKind.Check: Poly(-8, 0, -3, 6, 9, -7); break;
            case IconKind.Plus: L(-8, 0, 8, 0); L(0, -8, 0, 8); break;
            case IconKind.Minus: L(-8, 0, 8, 0); break;
            case IconKind.Fit: Poly(-9, -4, -9, -9, -4, -9); Poly(4, -9, 9, -9, 9, -4); Poly(9, 4, 9, 9, 4, 9); Poly(-4, 9, -9, 9, -9, 4); break;
            case IconKind.Undo: Arc(1, 2, 8, -Mathf.Pi * 0.95f, Mathf.Pi * 0.75f); Poly(-11, -6, -7, -1, -2, -5); break;
            case IconKind.SortAz: L(-8, -8, -8, 8); Poly(-11, 5, -8, 8, -5, 5); L(0, -7, 9, -7); L(0, -1, 7, -1); L(0, 5, 5, 5); break;
            case IconKind.SortSize: Fill(-9, 2, 4, 7); Fill(-2, -2, 4, 11); Fill(5, -7, 4, 16); break;
            case IconKind.Smile: Ring(0, 0, 9.5f); Dot(-3.5f, -3, 1.6f); Dot(3.5f, -3, 1.6f); Arc(0, 1, 5, Mathf.Pi * 0.15f, Mathf.Pi * 0.85f); break;
            case IconKind.Back: L(-8, 0, 9, 0); Poly(-2, -6, -8, 0, -2, 6); break;
            case IconKind.Search: Ring(-2, -2, 6.5f); L(3, 3, 9, 9); break;
            case IconKind.Cloud: Arc(-4, 2, 5, Mathf.Pi * 0.5f, Mathf.Pi * 1.55f); Arc(1, -2, 6, Mathf.Pi * 1.1f, Mathf.Pi * 1.95f); Arc(6, 2, 5, -Mathf.Pi * 0.5f, Mathf.Pi * 0.5f); L(-4, 7, 6, 7); break;
            case IconKind.Sun: Ring(0, 0, 4.5f); for (int i = 0; i < 8; i++) { float a = i * Mathf.Tau / 8; L(Mathf.Cos(a) * 7, Mathf.Sin(a) * 7, Mathf.Cos(a) * 10.5f, Mathf.Sin(a) * 10.5f); } break;
            case IconKind.Tree: Ring(0, -3, 7); L(0, 4, 0, 11); L(0, 1, -3, -2); L(0, 3, 3, 0); break;
            case IconKind.Chimney: Poly(-10, 10, -10, 0, -3, -4, -3, 0, 4, -4, 4, 10, -10, 10); Poly(6, 10, 6, -6, 10, -6, 10, 10); Arc(9, -10, 2.5f, Mathf.Pi, Mathf.Tau); break;
            case IconKind.Cart: Poly(-11, -8, -7, -8, -4, 5, 8, 5, 10, -4, -6, -4); Dot(-3, 9, 1.8f); Dot(7, 9, 1.8f); break;
            case IconKind.Gavel:
                c.DrawSetTransform(center, -Mathf.Pi / 4, Vector2.One);
                c.DrawRect(new Rect2(new Vector2(-7, -9) * u, new Vector2(14, 7) * u), color, false, w);
                c.DrawLine(new Vector2(0, -2) * u, new Vector2(0, 11) * u, color, w, true);
                c.DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                L(-10, 10, 2, 10);
                break;
            case IconKind.Bricks: Box(-10, 2, 9, 7); Box(1, 2, 9, 7); Box(-5, -6, 10, 7); break;
            case IconKind.Contract: Poly(-7, -10, 4, -10, 8, -6, 8, 10, -7, 10, -7, -10); L(-4, -4, 4, -4); L(-4, 0, 4, 0); L(-4, 5, 0, 5); break;
            case IconKind.Pie:
            {
                Ring(0, 0, 9.5f);
                var wedge = new List<Vector2> { P(1.5f, -1.5f) };
                for (int i = 0; i <= 10; i++) { float a = -Mathf.Pi / 2 + Mathf.Pi * 0.6f * i / 10; wedge.Add(P(1.5f + Mathf.Cos(a) * 10.5f, -1.5f + Mathf.Sin(a) * 10.5f)); }
                c.DrawColoredPolygon(wedge.ToArray(), color);
                break;
            }
            case IconKind.HousePercent: Poly(-10, 0, 0, -9, 10, 0); Poly(-7, -2, -7, 9, 7, 9, 7, -2); L(3, 0, -3, 7); Dot(-2.5f, 1, 1.2f); Dot(2.5f, 6, 1.2f); break;
            case IconKind.Diamond: Poly(-10, -3, -5, -9, 5, -9, 10, -3, 0, 10, -10, -3); L(-10, -3, 10, -3); L(-3, -9, 0, 10); L(3, -9, 0, 10); break;
            case IconKind.Banknote: Box(-11, -6, 22, 12); Ring(0, 0, 3.2f); Dot(-7, 0, 1.2f); Dot(7, 0, 1.2f); break;
            case IconKind.Bottle: Poly(-2, -10, 2, -10, 2, -5, 5, -1, 5, 10, -5, 10, -5, -1, -2, -5, -2, -10); L(-5, 3, 5, 3); break;
            case IconKind.Threshold: Fill(-9, 2, 4, 7); Fill(-2, -4, 4, 13); Fill(5, 5, 4, 4); L(-11, 0, 11, 0); break;
            case IconKind.Elder: Ring(-1, -8, 2.8f); Poly(-1, -4, 0, 3, -3, 10); L(0, 3, 3, 10); L(-1, -2, 5, 1); L(5, 1, 6, 10); break;
            case IconKind.Pram: Arc(0, 0, 8, Mathf.Pi, Mathf.Pi * 1.5f); L(-8, 0, 8, 0); L(0, -8, 0, 0); Poly(-8, 0, -5, 5, 5, 5, 8, 0); Dot(-4, 8, 1.8f); Dot(4, 8, 1.8f); L(8, 0, 11, -6); break;
            case IconKind.Calendar: Box(-9, -7, 18, 16); L(-9, -2, 9, -2); L(-4, -10, -4, -5); L(4, -10, 4, -5); break;
            case IconKind.Inheritance: Poly(-5, -10, -5, -5, 0, 0, -5, 5, -5, 10); Poly(5, -10, 5, -5, 0, 0, 5, 5, 5, 10); L(-8, -10, 8, -10); L(-8, 10, 8, 10); break;
            case IconKind.Sugar: Poly(-9, -3, 0, -8, 9, -3, 0, 2, -9, -3); Poly(-9, -3, -9, 6, 0, 11, 9, 6, 9, -3); L(0, 2, 0, 11); break;
            case IconKind.BankLevy: Poly(-11, -3, -3, -8, 5, -3, -11, -3); L(-11, 8, 5, 8); L(-8, 0, -8, 5); L(-3, 0, -3, 5); L(2, 0, 2, 5); L(9, -8, 9, 4); Poly(6, 1, 9, 5, 12, 1); break;
            case IconKind.Windfall: Ring(-3, 4, 6.5f); L(-3, 1, -3, 7); L(-6, 4, 0, 4); c.DrawColoredPolygon(new[] { P(8, -11), P(3, -4), P(7, -4), P(5, 2), P(11, -6), P(7, -6) }, color); break;
            case IconKind.Vehicle: Poly(-11, 3, -11, -2, -7, -2, -5, -7, 5, -7, 7, -2, 11, -2, 11, 3, -11, 3); Dot(-6, 6, 2.2f); Dot(6, 6, 2.2f); L(-3, -2, 3, -2); break;
            case IconKind.ElderCare: Arc(0, -5, 5, Mathf.Pi, Mathf.Tau); L(5, -5, 5, 10); L(-5, -5, -5, -2); Dot(-5, 3, 1.4f); break;
            case IconKind.Disability: Dot(-1, -8, 2); Poly(-1, -5, -1, 2, 6, 2, 8, 9); L(-1, -1, 5, -1); Ring(-2, 5, 5.5f); break;
            case IconKind.Vocational: L(-9, 9, 3, -3); Arc(6, -6, 4.5f, Mathf.Pi * 0.6f, Mathf.Pi * 2.4f); break;
            case IconKind.SchoolMeals: L(-7, -10, -7, -3); L(-4, -10, -4, -3); L(-1, -10, -1, -3); Poly(-7, -3, -7, 0, -1, 0, -1, -3); L(-4, 0, -4, 10); Ring(6, -6, 3.5f); L(6, -2.5f, 6, 10); break;
            case IconKind.Scholarship: Ring(0, -2, 6); Poly(-4, 3, -6, 10, 0, 7, 6, 10, 4, 3); Dot(0, -2, 1.5f); break;
            case IconKind.Rail: Poly(-6, -9, 6, -9, 8, 5, -8, 5, -6, -9); L(-6, -2, 6, -2); Dot(-4, 1.5f, 1.2f); Dot(4, 1.5f, 1.2f); L(-8, 10, -4, 5); L(8, 10, 4, 5); L(-5, 8, 5, 8); break;
            case IconKind.Flood: Poly(-11, 2, -7, -1, -3, 2, 1, -1, 5, 2, 9, -1, 11, 1); Poly(-11, 8, -7, 5, -3, 8, 1, 5, 5, 8, 9, 5, 11, 7); Poly(0, -11, -3.5f, -6, -3.5f, -4.5f, 0, -2, 3.5f, -4.5f, 3.5f, -6, 0, -11); break;
            case IconKind.Nuclear:
                Dot(0, 0, 1.8f);
                for (int k = 0; k < 3; k++)
                {
                    var pts = new Vector2[33];
                    for (int i = 0; i <= 32; i++) { float a = i * Mathf.Tau / 32; pts[i] = center + new Vector2(Mathf.Cos(a) * 10, Mathf.Sin(a) * 4).Rotated(k * Mathf.Pi / 3) * u; }
                    c.DrawPolyline(pts, color, w, true);
                }
                break;
            case IconKind.Cyber: Box(-6, -6, 12, 12); Box(-2.5f, -2.5f, 5, 5); foreach (float x in new[] { -3f, 0f, 3f }) { L(x, -6, x, -10); L(x, 6, x, 10); L(-6, x, -10, x); L(6, x, 10, x); } break;
            case IconKind.Justice: Poly(-10, -6, -10, 8, 0, 10, 10, 8, 10, -6, 0, -4, -10, -6); L(0, -4, 0, 10); L(-7, -2, -3, -1.5f); L(-7, 2, -3, 2.5f); L(3, -1.5f, 7, -2); L(3, 2.5f, 7, 2); break;
            case IconKind.Prison: Box(-9, -9, 18, 18); foreach (float x in new[] { -4.5f, 0f, 4.5f }) L(x, -9, x, 9); break;
            case IconKind.Border: Poly(-11, -5, 11, -5, 11, 0, -11, 0, -11, -5); L(-6, -5, -8, 0); L(-1, -5, -3, 0); L(4, -5, 2, 0); L(9, -5, 7, 0); L(-9, 0, -9, 10); L(9, 0, 9, 10); break;
            case IconKind.Startup: Poly(0, -11, 4, -4, 4, 4, -4, 4, -4, -4, 0, -11); Ring(0, -2, 1.8f); Poly(-4, 0, -8, 6, -4, 4); Poly(4, 0, 8, 6, 4, 4); L(-1.5f, 7, 0, 11); L(1.5f, 7, 0, 11); break;
            case IconKind.Pin: Ring(0, -4, 6); L(-5, 0, 0, 10); L(5, 0, 0, 10); Dot(0, -4, 2); break;
            case IconKind.Heart: Arc(-4.5f, -3, 5, Mathf.Pi * 0.8f, Mathf.Tau); Arc(4.5f, -3, 5, Mathf.Pi, Mathf.Pi * 2.2f); L(-9.2f, 0.5f, 0, 10); L(9.2f, 0.5f, 0, 10); break;
            case IconKind.Gun: Poly(-11, -6, 7, -6, 7, -1, 0, -1, -2, 2, -4, 9, -9, 9, -7, -1, -11, -1, -11, -6); break;
            case IconKind.Lock: Box(-7, -1, 14, 11); Arc(0, -3, 5, Mathf.Pi, Mathf.Tau); L(-5, -3, -5, -1); L(5, -3, 5, -1); Dot(0, 4, 1.5f); break;
            case IconKind.Chat: Poly(-10, -8, 10, -8, 10, 4, 2, 4, -4, 9, -4, 4, -10, 4, -10, -8); L(-6, -3, 6, -3); L(-6, 0, 2, 0); break;
            case IconKind.Venus: Ring(0, -4, 5.5f); L(0, 1.5f, 0, 10); L(-3.5f, 6, 3.5f, 6); break;
            case IconKind.Rings: Ring(-3.5f, 0, 6); Ring(3.5f, 0, 6); break;
            case IconKind.Candle: Box(-3.5f, -2, 7, 12); Poly(0, -11, -2.5f, -6, 0, -3.5f, 2.5f, -6, 0, -11); break;
            case IconKind.Cigarette: Poly(-11, 2, 5, 2, 5, 7, -11, 7, -11, 2); L(-5, 2, -5, 7); Poly(8, 2, 11, 2, 11, 7, 8, 7, 8, 2); Poly(7, -1, 9, -4, 7, -7, 9, -10); break;
            case IconKind.Wine: Poly(-5, -10, 5, -10, 4, -3, 0, 1, -4, -3, -5, -10); L(0, 1, 0, 9); L(-4, 10, 4, 10); L(-4.5f, -6, 4.5f, -6); break;
            case IconKind.Syringe:
                c.DrawSetTransform(center, -Mathf.Pi / 4, Vector2.One);
                c.DrawRect(new Rect2(new Vector2(-6, -3) * u, new Vector2(11, 6) * u), color, false, w);
                c.DrawLine(new Vector2(-11, 0) * u, new Vector2(-6, 0) * u, color, w, true);
                c.DrawLine(new Vector2(5, 0) * u, new Vector2(11, 0) * u, color, w, true);
                c.DrawLine(new Vector2(-6, -5) * u, new Vector2(-6, 5) * u, color, w, true);
                c.DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                break;
            case IconKind.Pencil:
                c.DrawSetTransform(center, -Mathf.Pi / 4, Vector2.One);
                c.DrawRect(new Rect2(new Vector2(-10, -2.5f) * u, new Vector2(14, 5) * u), color, false, w);
                c.DrawPolyline(new[] { new Vector2(4, -2.5f) * u, new Vector2(10, 0) * u, new Vector2(4, 2.5f) * u }, color, w, true);
                c.DrawLine(new Vector2(-6, -2.5f) * u, new Vector2(-6, 2.5f) * u, color, w, true);
                c.DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                break;
            case IconKind.Cross: L(0, -10, 0, 10); L(-6, -4, 6, -4); break;
            case IconKind.Umbrella: Arc(0, -1, 10, Mathf.Pi, Mathf.Tau); L(0, -1, 0, 8); Arc(2.5f, 8, 2.5f, 0, Mathf.Pi); break;
            case IconKind.Fist: Poly(-6, -3, -6, -6, 6, -6, 6, -3); Box(-6, -3, 12, 10); L(-2, -3, -2, 2); L(2, -3, 2, 2); Poly(-6, 1, -9, 1, -9, 5, -6, 5); break;
            case IconKind.Stop:
            {
                var pts = new Vector2[9];
                for (int i = 0; i < 9; i++) { float a = Mathf.Pi / 8 + i * Mathf.Tau / 8; pts[i] = P(Mathf.Cos(a) * 10.5f, Mathf.Sin(a) * 10.5f); }
                c.DrawPolyline(pts, color, w, true);
                L(-5, 0, 5, 0);
                break;
            }
            case IconKind.Podium: Box(-11, 1, 7, 9); Box(-4, -4, 8, 14); Box(4, 3, 7, 7); Dot(0, -8, 1.6f); break;
            case IconKind.Clipboard: Box(-7, -8, 14, 18); Box(-3, -10, 6, 4); Poly(-4, 0, -2, 2, 3, -3); L(-4, 5, 4, 5); break;
            case IconKind.NoCoin: Ring(0, 0, 9.5f); Ring(0, 0, 4); L(-6.7f, -6.7f, 6.7f, 6.7f); break;
            case IconKind.Quill: Poly(10, -10, 3, -9, -4, -2, -6, 4, -1, 2, 5, -3, 10, -10); L(-6, 4, -10, 10); break;
            case IconKind.Passport: Box(-7, -10, 14, 20); Ring(0, -2, 3.5f); L(-4, 4, 4, 4); L(-4, 7, 4, 7); break;
            case IconKind.Door: Poly(-6, 10, -6, -10, 6, -10, 6, 10); L(-9, 10, 9, 10); Dot(3, 1, 1.3f); break;
            case IconKind.Crane: L(-6, 10, -6, -9); L(-6, -9, 10, -9); L(8, -9, 8, -3); Box(6, -3, 4, 3); L(-9, 10, -3, 10); L(-6, -5, 2, -9); break;
            case IconKind.Dna:
            {
                var a1 = new Vector2[25]; var a2 = new Vector2[25];
                for (int i = 0; i <= 24; i++) { float y = -10 + 20f * i / 24, x = Mathf.Sin(i * Mathf.Tau / 24) * 5; a1[i] = P(x, y); a2[i] = P(-x, y); }
                c.DrawPolyline(a1, color, w, true); c.DrawPolyline(a2, color, w, true);
                foreach (int i in new[] { 3, 9, 15, 21 }) c.DrawLine(a1[i], a2[i], color, w * 0.7f, true);
                break;
            }
            case IconKind.Block: Box(-6, -10, 12, 20); foreach (float y in new[] { -6f, -1f, 4f }) { Fill(-3.5f, y, 2.5f, 2.5f); Fill(1, y, 2.5f, 2.5f); } L(-8, 10, 8, 10); break;
            case IconKind.Scroll: Box(-7, -8, 14, 16); L(-4, -4, 4, -4); L(-4, 0, 4, 0); Dot(3, 5, 2); break;
            case IconKind.Ladder: L(-4, -10, -4, 10); L(4, -10, 4, 10); foreach (float y in new[] { -6f, -1f, 4f, 9f }) L(-4, y, 4, y); break;
            case IconKind.Tractor: Poly(-9, 0, -3, 0, -3, -8, 2, -8, 2, -2, 10, -2, 10, 3); Ring(-5, 5, 4); Ring(7, 6, 2.6f); L(-9, 0, -9, 5); break;
            case IconKind.Piggy: Ring(-1, 1, 7); Dot(3.5f, -1, 1.1f); L(-3, -6, 1, -6); Poly(-5, 7, -5, 10); Poly(3, 7, 3, 10); Poly(7, 1, 11, 0); break;
            case IconKind.Safe: Box(-9, -8, 18, 16); Box(-6, -5, 12, 10); Ring(0, 0, 2.5f); L(-5, 8, -5, 10); L(5, 8, 5, 10); break;
            case IconKind.Monitor: Box(-10, -8, 20, 13); L(-4, 10, 4, 10); L(0, 5, 0, 10); L(-6, -3, 6, -3); break;
        }
    }
}

