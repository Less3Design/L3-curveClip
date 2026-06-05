using UnityEngine;

namespace Less3.CurveClips
{
    // Example curve player usage
    [RequireComponent(typeof(CurveClipPlayer))]
    public sealed class HoverClipPlayer : MonoBehaviour
    {
        [SerializeField]
        private CurveClipPlayer player;

        private void Reset()
        {
            player = GetComponent<CurveClipPlayer>();
        }

        private void Awake()
        {
            if (player == null)
                player = GetComponent<CurveClipPlayer>();
        }

        private void OnValidate()
        {
            if (player == null)
                player = GetComponent<CurveClipPlayer>();
        }

        private void OnMouseEnter()
        {
            if (player != null)
                player.Play();
        }
    }
}
