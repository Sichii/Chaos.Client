namespace Chaos.Client.Rendering;

/// <summary>
///     A complete sprite animation (all frames for one animation file). Owns and disposes all contained SpriteFrames.
/// </summary>
public sealed class SpriteAnimation : IDisposable
{
    public SpriteFrame[] Frames { get; }
    public int FrameCount => Frames.Length;

    public SpriteAnimation(SpriteFrame[] frames) => Frames = frames;

    public void Dispose()
    {
        foreach (var frame in Frames)
            frame.Dispose();
    }

    public SpriteFrame? GetFrame(int index) => (index >= 0) && (index < Frames.Length) ? Frames[index] : null;
}