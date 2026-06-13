

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
public CurveClipPlayback playback;

public void ThingHappened()
{
    playback = clip.Play(transform);
}

public void CancelTheThing()
{
    playback.Cancel()
}
```
You can play multiple clips on a single transform and they will additively mix.
```
public void CancelAllClips()
{
    CurveClip.Cancel(transform);
}
```
It's important to note that position can't be normalized across objects as simply as rotation & scale. How should a bounce on the Y axis behave across many differently size and shaped objects?

For this a few methods to scale the position curves are provided.
```csharp
// scale the position curves by a flat value
clip.Play(transform, 2f);
// scale the position curves individually
clip.Play(transform, new Vector3(1f, 2f, 1f));
// scale the position curves based on the bounds of a renderer
clip.Play(transform, renderer);
// scale the position curves based on the bounds of all renderers on a gameObject
clip.Play(transform, gameObject);
```

# Performance
- Clips are played on a coroutine.
- It is not super optimized right now. Should eventually pool it nicely inline with how [`https://github.com/Less3Design/L3-tween`](https://github.com/Less3Design/L3-tween) works
