using Godot;
using OpenPolitics.Core.Packs;

namespace OpenPolitics.Client;

public partial class Main : Control
{
    public override void _Ready()
    {
        GD.Print("core reference ok: " + typeof(PackLoader).Name);
        GetTree().Quit();
    }
}
