using Godot;
using OpenPolitics.Core.Gameplay;
using OpenPolitics.Core.Packs;
using OpenPolitics.Core.Presentation;

namespace OpenPolitics.Client;

/// <summary>The saved games, newest first: name, scenario, party and the date in the game, when it was saved. Choosing one loads it.</summary>
public static class LoadDialog
{
    public static Dialog Open(Main main, PackSet packs, Localizer t, IReadOnlyList<string> files)
    {
        var d = new Dialog(t.T("ui.start.load_game"), IconKind.Save, t.T("ui.client.close")) { PreferredWidth = 640, Key = "load" };
        if (files.Count == 0) d.Body.AddChild(Ui.L(t.T("ui.start.no_saves"), Tx.Body, AppTheme.Ink2, true));
        foreach (var file in files)
        {
            string name = System.IO.Path.GetFileName(file).Replace(".save.yaml", "");
            string? error = null; string what = "", when = "";
            try { (what, when) = Describe(packs, t, SaveData.FromYaml(System.IO.File.ReadAllText(file))); }
            catch (Exception e) { error = e.Message.Split('\n')[0]; }
            var b = new Button { ThemeTypeVariation = "ButtonCard", FocusMode = Control.FocusModeEnum.None, Disabled = error != null, TooltipText = file,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseDefaultCursorShape = error != null ? Control.CursorShape.Forbidden : Control.CursorShape.PointingHand,
                CustomMinimumSize = new Vector2(0, 60) };
            var row = Ui.HBox(12);
            var col = Ui.VBox(2);
            col.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; col.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            col.AddChild(Ui.L(name, Tx.BodyStrong));
            var sub = Ui.L(error != null ? t.T("ui.load.broken", error) : what, Tx.Small, error != null ? AppTheme.BadText : AppTheme.Ink2);
            sub.ClipText = true; sub.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            col.AddChild(sub);
            row.AddChild(col);
            var right = Ui.VBox(2);
            right.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            right.AddChild(Ui.Right(Ui.L(when, Tx.Data)));
            right.AddChild(Ui.Right(Ui.L(System.IO.File.GetLastWriteTime(file).ToString("d. M. yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture), Tx.Small, AppTheme.Ink3)));
            row.AddChild(right);
            var m = Ui.Margin(row, 14, 8);
            m.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            Detail.SetIgnore(m);
            b.AddChild(m);
            string path = file;
            b.Pressed += () => main.LoadGame(path);
            d.Body.AddChild(b);
        }
        d.Refit();
        return d;
    }

    /// <summary>("Czechia 2015 · Citizens' Movement", "2016 Q2"): what was being played and how far it had come.</summary>
    private static (string What, string When) Describe(PackSet packs, Localizer t, SaveData save)
    {
        var scenario = packs.GetScenario(save.ScenarioId);
        string party = save.PlayerParty ?? scenario.Politics?.PlayerParty ?? "";
        int tpy = scenario.TurnsPerYear;
        string what = t.T("name." + scenario.Id) + (party.Length > 0 ? " · " + t.Name(party) : "");
        return (what, $"{scenario.StartYear + save.Turn / tpy} Q{save.Turn % tpy + 1}");
    }
}
