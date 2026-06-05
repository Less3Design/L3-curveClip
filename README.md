# L3 Curve Clip

One-shot transform animation clips driven by editable `AnimationCurve` channels.

`CurveClip` is designed for simple authored animations where an Animator Controller would be too much ceremony. Create a clip asset, edit the position, rotation, scale, or custom curves in the inspector, then play it on a transform.

```csharp
using Less3.CurveClips;
using UnityEngine;

public class CurveClipExample : MonoBehaviour
{
    public CurveClip clip;

    public void Play()
    {
        clip.Play(transform);
    }
}
```

## Runtime

- Applies position, rotation, and scale curves to a target `Transform`.
- Supports local or world transform space.
- Supports absolute or relative values.
- Supports delta, unscaled, or fixed update playback.
- Exposes custom curve samples through callbacks/events for small one-shot authored values.

## Editor

The custom inspector is built with UI Toolkit. It includes compact settings, curve visibility controls, and separate overlay graph panels for position, rotation, scale, and custom curves.

Graph controls:

- Double click to add a key.
- Drag keys to edit time/value.
- Shift or action-select for multi key selection.
- Drag marquee to select multiple keys.
- Drag the selection box corners to scale selected keys.
- Drag tangent handles on selected keys.
- Right click for key, framing, and tangent actions.
- Middle mouse or Alt-drag to pan.
- Mouse wheel to zoom.

