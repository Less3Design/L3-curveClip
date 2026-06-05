

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
