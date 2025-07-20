using System;
using UnityEngine;

namespace CustomAttributes
{
    [AttributeUsage(AttributeTargets.Field)]
    public class BeginFoldoutAttribute : PropertyAttribute
    {
        public string FoldoutName;

        public BeginFoldoutAttribute(string foldoutName)
        {
            FoldoutName = foldoutName;
        }
    }
}
