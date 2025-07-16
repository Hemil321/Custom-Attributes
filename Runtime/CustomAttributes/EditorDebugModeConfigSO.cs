using UnityEngine;

namespace ReverseTowerDefense
{
    [CreateAssetMenu()]
    public class EditorDebugModeConfigSO : ScriptableObject
    {
        public float IntFieldWidth;
        public float FloatFieldWidth;
        public float BoolFieldWidth;
        public float ObjectFieldWidth;
        public float DefaultWidth;

        public float ScrollbarWidth;
    }
}
