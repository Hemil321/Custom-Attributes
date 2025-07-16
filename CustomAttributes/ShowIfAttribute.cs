using System;
using UnityEngine;

namespace ReverseTowerDefense
{
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public class ShowIfAttribute : PropertyAttribute
    {
        public string ConditionFieldName;
        public object ConditionFieldValue;

        public ShowIfAttribute(string fieldName)
        {
            ConditionFieldName = fieldName;
            ConditionFieldValue = true;
        }

        public ShowIfAttribute(string fieldName, object value)
        {
            ConditionFieldName = fieldName;
            ConditionFieldValue = value;
        }
    }
}
