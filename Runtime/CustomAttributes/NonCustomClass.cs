using System;
using UnityEngine;

namespace CustomAttributes
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public class NonCustomClass : PropertyAttribute
    {
    
    }
}
