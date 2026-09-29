namespace HsmAdapter;

// Scope lifetime is independent of the renderer's row hints or native canvas objects.
internal interface ITextFrame { }
internal interface ITextHandle
{
    void Update(ITextFrame frame);
    void Remove(bool refresh);
    void Forget();
}
internal interface ITextBackend
{
    ScreenTextFeatures Features { get; }
    ScreenTextResult Prepare(ScreenTextLayout layout, out ITextFrame? frame);
    ITextHandle CreateHandle(ReferenceHub hub, string group, string key);
}
internal interface IRichNoticeBackend
{
    ITextFrame PrepareRichNotice(string richText, int fontSize, float lineHeight, ScreenRect rect);
}
internal static class TextBackends
{
    internal static ITextBackend? Screen => HsmBackend.Ready();
}
