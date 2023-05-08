using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace TORISOUP.VersionCheckers.Editor
{
    [CreateAssetMenu(fileName = "VersionData", menuName = "TORISOUP/Version Checker/Create VersionData Asset")]
    [Serializable]
    public class VersionData : ScriptableObject
    {
        [Header("プロダクト名")]
        public string Name;
        [Header("現在のバージョン情報")]
        public string CurrentVersion;
        [Header("バージョン情報取得先URI")]
        public string VersionCsvRepositoryUri;
    }
}