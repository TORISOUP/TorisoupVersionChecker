using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace TORISOUP.VersionCheckers.Editor
{
    [InitializeOnLoad]
    public class ToriVersionChecker : EditorWindow
    {
        #region static

        static readonly string TempFilePath = "Temp/TorisoupVersionCheckerTemp";


        private static readonly Dictionary<string, ServerVersionInfo> _updatableDict =
            new Dictionary<string, ServerVersionInfo>();

        private static VersionData[] _loadedVersionData;

        private static int _loadingCount;
        private static int _fetchedCount;
        private static int _failedCount;

        private static bool IsLoading { get; set; }

        static ToriVersionChecker()
        {
            if (File.Exists(TempFilePath) == false)
            {
                File.Create(TempFilePath);
                _ = FetchUpdateAsync();
            }
        }

        private static async Task FetchUpdateAsync()
        {
            IsLoading = true;
            var httpClient = new HttpClient();
            try
            {
                // リセット
                _updatableDict.Clear();

                _loadingCount = 0;
                _fetchedCount = 0;
                _failedCount = 0;

                // VersionDataをまとめて取得
                var targetVersionData = FindAllVersionData();

                _loadedVersionData = targetVersionData.SelectMany(x => x.Value).ToArray();
                _failedCount = _loadingCount;

                // 対象URIごとにチェック開始
                foreach (var kv in targetVersionData)
                {
                    var uri = ConvertToUri(kv.Key);
                    var data = kv.Value;

                    var result = await FetchCsvAsync(httpClient, uri);
                    var dic = ParseCsv(result);

                    foreach (var target in data)
                    {
                        // リストに含まれていなかったら無視
                        if (!dic.TryGetValue(target.Id, out var latestVersionInfo)) continue;

                        _fetchedCount++;
                        _failedCount--;
                        // 差分があったらリストに追加する
                        if (target.CurrentVersion != latestVersionInfo.Version)
                        {
                            _updatableDict[target.Id] = latestVersionInfo;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning(ex);
            }
            finally
            {
                IsLoading = false;
                httpClient.Dispose();
            }

            // 差分が1個以上発見されたらWindowを開く
            if (_updatableDict.Count > 0)
            {
                OpenWindow();
            }
        }


        private static Uri ConvertToUri(string origin)
        {
            var url = new Uri(origin);

            // スプレッドシートならcsvで読み取れるようにURLを加工
            if (!url.GetLeftPart(UriPartial.Authority).Contains("google.com") ||
                !url.PathAndQuery.Contains("spreadsheets") ||
                url.Segments.Last() != "edit") return url;

            var ub = new UriBuilder(url);
            ub.Path = ub.Path.Replace("edit", "export");
            ub.Query = "format=csv&gid=0";
            return ub.Uri;
        }

        private static async Task<string> FetchCsvAsync(HttpClient client, Uri uri)
        {
            try
            {
                var response = await client.GetAsync(uri);
                return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync() : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// VersionDataファイルを全部みつける
        /// </summary>
        private static IReadOnlyDictionary<string, IEnumerable<VersionData>> FindAllVersionData()
        {
            var guids = AssetDatabase.FindAssets("t:VersionData");
            var paths = guids.Select(guid => AssetDatabase.GUIDToAssetPath(guid)).ToArray();
            var list = new List<VersionData>();
            foreach (var path in paths)
            {
                try
                {
                    var vd = AssetDatabase.LoadAssetAtPath<VersionData>(path);
                    list.Add(vd);
                    _loadingCount++;
                }
                catch
                {
                    // nice catch
                }
            }

            return list.GroupBy(x => x.VersionCsvRepositoryUri).ToDictionary(x => x.Key, x => x.AsEnumerable());
        }


        // csvをパースしてDictionaryにして返す
        private static IReadOnlyDictionary<string, ServerVersionInfo> ParseCsv(string csv)
        {
            if (string.IsNullOrEmpty(csv)) return null;

            var result = new Dictionary<string, ServerVersionInfo>();
            var lines = LoadCsv(csv);

            foreach (var values in lines)
            {
                // 1行目相当は無視
                if (values.Length > 0 && values[0] == "!ID!") continue;
                if (values.Length < 4) continue;
                result[values[0]] =
                    new ServerVersionInfo(id: values[0], version: values[1], displayName: values[2],
                        downloadUri: values[3], releaseNote: values.Length > 4 ? values[4] : null);
            }


            return result;
        }

        #endregion

        #region Utilities

        // CSVのParse
        // ref. https://resanaplaza.com/2020/09/28/%E3%80%90c%E3%80%91csv%E3%81%AE%E8%AA%AD%E3%81%BF%E8%BE%BC%E3%81%BF%E3%83%AD%E3%82%B8%E3%83%83%E3%82%AF%E3%82%92%E7%B0%A1%E5%8D%98%E8%A7%A3%E8%AA%AC%EF%BC%88%E9%A0%85%E7%9B%AE%E4%B8%AD%E3%81%AE/
        private static List<string[]> LoadCsv(string csv)
        {
            //結果を格納するリスト
            var result = new List<string[]>();

            //カンマで分割した1行分を格納するリスト
            var line = new List<string>();

            //１カラム分の値を格納する変数
            var value = new StringBuilder();

            //ダブルクォーテーションの中であることを現わすフラグ
            var dqFlg = false;

            //ファイルをオープンする
            using (var rs = new StringReader(csv))
            {
                while (rs.Peek() > -1)
                {
                    //1文字読み込む
                    var ch = (char)rs.Read();

                    //ダブルクオーテーションが見つかるとフラグを反転する
                    dqFlg = (ch == '\"') ? !dqFlg : dqFlg;

                    //ダブルクォーテーション中ではないキャリッジリターンは破棄する
                    if (ch == '\r' && dqFlg == false)
                    {
                        continue;
                    }

                    //ダブルクォーテーション中ではない時にカンマが見つかったら、
                    //それまでに読み取った文字列を１つのかたまりとしてline に追加する
                    if (ch == ',' && dqFlg == false)
                    {
                        line.Add(to_str(value));
                        value.Clear();
                        continue;
                    }

                    //ダブルクォーテーション中ではない時にラインフィードが見つかったら
                    //line（1行分） を result に追加する
                    if (ch == '\n' && dqFlg == false)
                    {
                        line.Add(to_str(value));
                        result.Add(line.ToArray());
                        line.Clear();
                        value.Clear();
                        continue;
                    }

                    value.Append(ch);
                }
            }

            //ファイル末尾が改行コードでない場合、ループを抜けてしまうので、
            //未処理の項目がある場合は、ここでline に追加
            if (value.Length > 0)
            {
                line.Add(to_str(value));
                result.Add(line.ToArray());
            }

            return result;

            //前後のダブルクォーテーションを削除し、2個連続するダブルクォーテーションを1個に置換する
            string to_str(StringBuilder p_str)
            {
                string l_val = p_str.ToString().Replace("\"\"", "\"");
                int l_start = (l_val.StartsWith("\"")) ? 1 : 0;
                int l_end = l_val.EndsWith("\"") ? 1 : 0;
                return l_val.Substring(l_start, l_val.Length - l_start - l_end);
            }
        }

        #endregion

        #region Window

        private bool _openDebugPane;

        [MenuItem("TORISOUP/Version Checker/Open Window")]
        public static ToriVersionChecker OpenWindow()
        {
            return GetWindow<ToriVersionChecker>("Version Checker");
        }

        private void OnGUI()
        {
            if (IsLoading)
            {
                EditorGUILayout.LabelField($"Loading... {_fetchedCount}/{_loadingCount}");
                return;
            }

            EditorGUILayout.BeginHorizontal();
            {
                if (GUILayout.Button("Check Update"))
                {
                    _ = FetchUpdateAsync();
                }
            }
            EditorGUILayout.EndHorizontal();

            if (_updatableDict.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    $"アップデートはありません\nThere are no updates available.\nFetched count: {_fetchedCount}, Failed count: {_failedCount}",
                    MessageType.Info);
            }

            EditorGUILayout.Space();

            var style = new GUIStyle(GUI.skin.label)
            {
                wordWrap = true,
                fontStyle = FontStyle.Bold
            };

            if (_updatableDict.Count != 0)
            {
                EditorGUILayout.HelpBox($"{_updatableDict.Count}件のアップデートが見つかりました", MessageType.Warning);

                foreach (var kv in _updatableDict)
                {
                    var old = GUI.backgroundColor;
                    GUI.backgroundColor = Color.cyan;
                    EditorGUILayout.BeginVertical(GUI.skin.box);
                    {
                        GUI.backgroundColor = old;
                        EditorGUILayout.BeginHorizontal();
                        {
                            EditorGUILayout.LabelField(kv.Value.DisplayName, style);
                            if (GUILayout.Button($"Update to {kv.Value.Version}"))
                            {
                                Application.OpenURL(kv.Value.DownloadUri);
                            }
                        }
                        EditorGUILayout.EndHorizontal();
                        if (!string.IsNullOrEmpty(kv.Value.ReleaseNote))
                        {
                            EditorGUILayout.LabelField(kv.Value.ReleaseNote, EditorStyles.helpBox);
                        }
                    }
                    EditorGUILayout.EndVertical();
                }
            }

            EditorGUILayout.Space(10);

            _openDebugPane = EditorGUILayout.Foldout(_openDebugPane, "Details...");
            if (_openDebugPane)
            {
                EditorGUILayout.LabelField($"Fetched count: {_fetchedCount}, Failed count: {_failedCount}");

                EditorGUILayout.BeginVertical(GUI.skin.box);
                {
                    if (_loadedVersionData.Length != 0)
                    {
                        EditorGUILayout.LabelField($"VersionData files:{_loadedVersionData.Length}");

                        foreach (var v in _loadedVersionData)
                        {
                            EditorGUILayout.LabelField($" - {v.Id}\t{v.CurrentVersion}");
                        }
                    }
                }
                EditorGUILayout.EndVertical();
            }
        }

        #endregion
    }

    internal readonly struct ServerVersionInfo : IEquatable<ServerVersionInfo>
    {
        public string Id { get; }
        public string DisplayName { get; }
        public string Version { get; }

        public string DownloadUri { get; }

        public string ReleaseNote { get; }

        public ServerVersionInfo(string id, string displayName, string version, string downloadUri, string releaseNote)
        {
            Id = id;
            DisplayName = displayName;
            Version = version;
            DownloadUri = downloadUri;
            ReleaseNote = releaseNote;
        }

        public bool Equals(ServerVersionInfo other)
        {
            return Id == other.Id && DisplayName == other.DisplayName && Version == other.Version &&
                   DownloadUri == other.DownloadUri && ReleaseNote == other.ReleaseNote;
        }

        public override bool Equals(object obj)
        {
            return obj is ServerVersionInfo other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (Id != null ? Id.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (DisplayName != null ? DisplayName.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (Version != null ? Version.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (DownloadUri != null ? DownloadUri.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (ReleaseNote != null ? ReleaseNote.GetHashCode() : 0);
                return hashCode;
            }
        }
    }
}