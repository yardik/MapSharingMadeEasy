using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine;

namespace MapSharingMadeEasy
{
    [Serializable]
    public class ExtendedPinData
    {
        public string Key { get; }
        public DateTime CreationDate { get; }

        public bool deleted;
        
        public ExtendedPinData(string key, DateTime now, bool isDeleted)
        {
            Key = key;
            CreationDate = now;
            deleted = isDeleted;
        }

        public override string ToString()
        {
            return $"{nameof(Key)}: {Key}, {nameof(deleted)}: {deleted}, {nameof(CreationDate)}: {CreationDate}";
        }
    }
}