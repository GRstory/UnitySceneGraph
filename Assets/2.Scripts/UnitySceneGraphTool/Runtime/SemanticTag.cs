using UnityEngine;

namespace SceneGraph
{
    /// <summary>
    /// 수동 수정용 컴포넌트
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SemanticTag : MonoBehaviour
    {
        [Tooltip("이름 토큰 대신 쓸 명사. 같은 큐브를 상자/책상으로 다르게 쓸 때")]
        public string[] OverridesNameTokens = new string[0];

        [Tooltip("이 오브젝트와 자식을 검색 대상에서 뺀다")]
        public bool ExcludeFromSearch;

        [Tooltip("여기가 그룹이다. 아래쪽 신호는 무시하고 위쪽 신호는 그대로. 절대 우선")]
        public bool GroupRoot;

        [Tooltip("신호로 안 친다. 휴리스틱도 여기서 멈춰 자식들이 따로 묶인다")]
        public bool SplitChildren;

        [Tooltip("디버그용")]
        public Hash128 CachedFingerprintHash;
    }
}
