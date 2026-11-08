#if UNITY_5_3_OR_NEWER
using UnityEngine;

namespace Jamkkaebi.Npc
{
    /// <summary>
    /// Data/ 의 JSON(default_lines · personas · actions)을 읽는다. Unity 에서만 컴파일된다.
    /// JsonUtility 는 키 이름이 필드 이름과 정확히 같아야 읽고, 모르는 키는 무시한다.
    /// </summary>
    public static class NpcContentLoader
    {
        public static T FromJson<T>(string json)
        {
            return JsonUtility.FromJson<T>(json);
        }

        public static T FromTextAsset<T>(TextAsset asset)
        {
            return FromJson<T>(asset.text);
        }

        /// <summary>Resources 아래 파일을 읽는다. 경로에 확장자를 붙이지 않는다 (예: "Npc/default_lines").</summary>
        public static T FromResources<T>(string path) where T : class
        {
            TextAsset asset = Resources.Load<TextAsset>(path);
            return asset != null ? FromTextAsset<T>(asset) : null;
        }
    }
}
#endif
