

<img width="4096" height="2495" alt="Frame 296" src="https://github.com/user-attachments/assets/02159345-5913-4a49-9b63-8b6ac787f5a8" />

> vibed with codex 5.5

# Usecase
Created this for situations where you want to give something an animation, but animation clips and animators felt too bulky. 
> Jiggle an object in a satisfying way when the player hovers it.

This is great for that.

https://github.com/user-attachments/assets/1f7dbad1-ec26-4687-805c-49ee8439a2a0

# Editor
The object is just a handful of animations curves, so the main lift was creating a nice editor to make authoring these curves not such a chore.

I tried to have roughly the same feature set as the multi-curve-editor featured in Unity's particle systems.

# Usage
You can play the curves targeting a transform.
```csharp
public CurveClip clip;
private CurveClipPlayback playback;

public void Play()
{
    playback = clip.Play(transform);
}

public void PlayBoundsRelative(Renderer renderer)
{
    playback = clip.Play(transform, renderer);
}

public void PlayBoundsRelative(GameObject root)
{
    playback = clip.Play(transform, root);
}

public void CancelCurrent()
{
    playback?.Cancel();
}

public void CancelAllOnTransform()
{
    CurveClip.Cancel(transform);
}
```
Position curves can also be scaled per playback:
```csharp
clip.Play(transform, 2f);
clip.Play(transform, new Vector3(1f, 2f, 1f));
clip.Play(transform, renderer);
clip.Play(transform, gameObject);
```
The renderer and GameObject overloads measure bounds in the same space used by the target's `localPosition`, so position values can be authored as object-relative movement. The GameObject overload recursively combines active enabled child renderers.

Multiple clips can play on the same transform at once. They are combined in local, relative space and the transform is restored when the last clip ends or is cancelled.

Or use the `CurveClipPlayer` component when you want a serialized list of clips and random selection.
```csharp
public CurveClipPlayer player
public void Play()
{
    player.Play();
}
```
# Performance
- Clips are played on a coroutine.
- It is not super optimized right now. Should eventually pool it nicely inline with how [`https://github.com/Less3Design/L3-tween`](https://github.com/Less3Design/L3-tween) works
