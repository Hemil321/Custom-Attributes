using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CustomAttributes;
using ReverseTowerDefense.Utils;
using UnityEditor;
using UnityEngine;

namespace ReverseTowerDefense
{
    [CustomEditor(typeof(MonoBehaviour), true)]
    public partial class MonoBehaviorEditor_Foldouts : Editor
    {
        private Dictionary<Pair<int, int>, bool> fieldIndexToFoldoutMap;

        private void HandleFoldouts()
        {

        }

        private void PopulateFoldouts()
        {
            fieldIndexToFoldoutMap = new Dictionary<Pair<int, int>, bool>();

            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

            Type type = target.GetType();
            FieldInfo[] fields = type.GetFields(flags);
            List<FieldInfo> serializeFields = fields.Where(field => field.GetCustomAttribute<SerializeField>() != null).ToList();

            int currentFoldoutStartIndex = -1;

            for(int i = 0; i < serializeFields.Count; i++)
            {
                FieldInfo field = fields[i];

                if(field.GetCustomAttribute<BeginFoldoutAttribute>() != null)
                {
                    if(currentFoldoutStartIndex != -1)
                    {
                        fieldIndexToFoldoutMap.Add(new Pair<int, int>(currentFoldoutStartIndex, i), false);
                    }
                    currentFoldoutStartIndex = i + 1;
                }
            }

            if(currentFoldoutStartIndex != -1)
            {
                fieldIndexToFoldoutMap.Add(new Pair<int, int>(currentFoldoutStartIndex, serializeFields.Count), false);
            }

            foreach(var mapPair in  fieldIndexToFoldoutMap)
            {
                this.Log(mapPair.Key);
                this.Log(mapPair.Value);
            }
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            if(fieldIndexToFoldoutMap == null)
            {
                PopulateFoldouts();
            }
        }
    }
}
