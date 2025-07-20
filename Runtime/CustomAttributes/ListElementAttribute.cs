using System;
using UnityEngine;

namespace CustomAttributes
{
    [AttributeUsage(AttributeTargets.Field)]
    public class ListElementAttribute : PropertyAttribute
    {
        public string ElementName;

        public ListElementAttribute()
        {
            ElementName = "Element";
        }

        public ListElementAttribute(string elementName)
        {
            ElementName = elementName;
        }   
    }
}
