using System.Collections.Generic;
using UnityEngine;

namespace Less3.CurveClips
{
    public sealed class SpacebarCurveClipTester : MonoBehaviour
    {
        public Transform target;
        public List<CurveClip> clips = new List<CurveClip>();

        private void Awake()
        {
            if (target == null)
                target = transform;
        }

        private void Update()
        {
            if (!Input.GetKeyDown(KeyCode.Space))
                return;

            CurveClip clip = PickRandomClip();
            if (clip != null)
                clip.Play(target);
        }

        private CurveClip PickRandomClip()
        {
            int validCount = 0;
            for (int i = 0; i < clips.Count; i++)
            {
                if (clips[i] != null)
                    validCount++;
            }

            if (validCount == 0)
                return null;

            int pick = Random.Range(0, validCount);
            for (int i = 0; i < clips.Count; i++)
            {
                CurveClip clip = clips[i];
                if (clip == null)
                    continue;

                if (pick == 0)
                    return clip;

                pick--;
            }

            return null;
        }
    }
}
