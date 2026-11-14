using Godot;

namespace OpenPolitics.Client;

/// <summary>One tab of the main window. Rebuilt from the session whenever the game changes.</summary>
public abstract partial class Screen : Control
{
    public abstract string TitleKey { get; }

    /// <summary>How much of the window width the floating panel takes for this screen (0..1).</summary>
    public virtual float PreferredWidth => 0.62f;
    public abstract void Refresh(Session s);

    protected Screen()
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
    }
}
