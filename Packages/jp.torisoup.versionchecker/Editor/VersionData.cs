using System;
using UnityEngine;

namespace TORISOUP.VersionCheckers.Editor
{
    [CreateAssetMenu(fileName = "VersionData", menuName = "TORISOUP/Version Checker/Create VersionData Asset")]
    [Serializable]
    public class VersionData : ScriptableObject
    {
        [Header("ID")]
        public string Id;
        [Header("このアセットの現在のバージョン情報")]
        public string CurrentVersion;
        [Header("バージョン情報スプレッドシートのURI")]
        public string VersionCsvRepositoryUri;
    }
}