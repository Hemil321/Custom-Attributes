using UnityEngine;

namespace ReverseTowerDefense
{
    [CreateAssetMenu()]
    public class EditorDebugModeConfigSO : ScriptableObject
    {
        public float IntFieldWidth = 50;
        public float FloatFieldWidth = 100;
        public float BoolFieldWidth = 20;
        public float ObjectFieldWidth = 100;
        public float DefaultWidth = 100;

        public float ScrollbarWidth = 20;
    }
}
