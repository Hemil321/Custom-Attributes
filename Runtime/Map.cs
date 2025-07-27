using System.Collections.Generic;
using System;
using UnityEngine;

namespace CustomAttributes
{
    [Serializable, NonCustomClass]
    public class Map<TKey, TValue>
    {
        [SerializeField] private List<TKey> keys = new();
        [SerializeField] private List<TValue> values = new();

        public Dictionary<TKey, TValue> ToDictionary()
        {
            Dictionary<TKey, TValue> dictionary = new();
            for (int i = 0; i < Math.Min(keys.Count, values.Count); i++)
            {
                if (!dictionary.ContainsKey(keys[i]))
                {
                    dictionary[keys[i]] = values[i];
                }
            }
            return dictionary;
        }

        public void FromDictionary(Dictionary<TKey, TValue> dictionary)
        {
            keys.Clear();
            values.Clear();
            foreach (var keyValuePair in dictionary)
            {
                keys.Add(keyValuePair.Key);
                values.Add(keyValuePair.Value);
            }
        }

        public List<TKey> Keys => keys;
        public List<TValue> Values => values;
    }
}
