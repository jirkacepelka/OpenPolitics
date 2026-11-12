using Godot;

namespace OpenPolitics.Client;

/// <summary>One tab of the main window. Rebuilt from the session whenever the game changes.</summary>
public abstract partial class Screen : Control
{
    public abstract string TitleKey { get; }
    public abstract void Refresh(Session s);

    protected Screen()
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
    }
}
