using System.Collections.Generic;
using System.Linq;

namespace HsmAdapter;

internal sealed class HsmTextFrame : ITextFrame
{
    internal HsmTextFrame(List<RenderedRow> rows, bool fastUpdate = true, bool forceUpdate = true,
        bool forceMembershipUpdate = true, bool fastMembershipUpdate = true)
    { Rows = rows; FastUpdate = fastUpdate; ForceUpdate = forceUpdate;
      ForceMembershipUpdate = forceMembershipUpdate; FastMembershipUpdate = fastMembershipUpdate; }
    internal readonly List<RenderedRow> Rows;
    internal readonly bool FastUpdate;
    internal readonly bool ForceUpdate;
    internal readonly bool ForceMembershipUpdate;
    internal readonly bool FastMembershipUpdate;
}

internal sealed class HsmTextHandle : ITextHandle
{
    private readonly HsmBackend _backend;
    private readonly object _display;
    private readonly string _group, _key;
    private readonly List<object> _hints = new();
    private List<RenderedRow> _rows = new();
    private bool _fastMembershipUpdate = true;
    private bool _forceMembershipUpdate = true;
    internal HsmTextHandle(HsmBackend backend, ReferenceHub hub, string group, string key)
    {
        _backend = backend; _display = backend.Display(hub); _group = group; _key = key;
    }
    public void Update(ITextFrame frame)
    {
        var hsmFrame = (HsmTextFrame)frame;
        var rows = hsmFrame.Rows;
        bool inPlaceChanged = false;
        bool membershipChanged = false;
        for (int i = 0; i < rows.Count; i++)
        {
            if (i < _hints.Count)
            {
                if (rows[i].Dynamic != _rows[i].Dynamic)
                {
                    var replacement = _backend.Create(rows.Count == 1 ? _key : _key + "." + i, rows[i]);
                    _backend.Remove(_display, _hints[i], _group);
                    _hints[i] = replacement;
                    _backend.Add(_display, _hints[i], _group);
                    membershipChanged = true;
                }
                else if (!rows[i].SameAs(_rows[i])) { _backend.Set(_hints[i], rows[i]); inPlaceChanged = true; }
            }
            else
            {
                var hint = _backend.Create(rows.Count == 1 ? _key : _key + "." + i, rows[i]);
                _hints.Add(hint);
                _backend.Add(_display, hint, _group);
                membershipChanged = true;
            }
        }
        while (_hints.Count > rows.Count)
        {
            int i = _hints.Count - 1;
            _backend.Remove(_display, _hints[i], _group); _hints.RemoveAt(i); membershipChanged = true;
        }
        _rows = rows;
        _fastMembershipUpdate = hsmFrame.FastMembershipUpdate;
        _forceMembershipUpdate = hsmFrame.ForceMembershipUpdate;
        if (membershipChanged && _forceMembershipUpdate) _backend.Update(_display, _fastMembershipUpdate);
        else if (inPlaceChanged && hsmFrame.ForceUpdate) _backend.Update(_display, hsmFrame.FastUpdate);
    }
    public void Remove(bool refresh)
    {
        if (_backend.IsDestructed(_display)) { Forget(); return; }
        foreach (var hint in _hints.ToArray())
        {
            if (_backend.IsDestructed(_display)) { Forget(); return; }
            _backend.Remove(_display, hint, _group); _hints.Remove(hint);
        }
        if (refresh && _forceMembershipUpdate && !_backend.IsDestructed(_display))
            _backend.Update(_display, _fastMembershipUpdate);
    }
    public void Forget() { _hints.Clear(); _rows.Clear(); }
}
