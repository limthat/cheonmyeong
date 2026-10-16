using UnityEngine;

namespace CheonMyeong.Data
{
    /// <summary>
    /// 지금 진행 중인 런에 대한 가벼운 로컬 상태(각자 자기 프로세스 안에서만 쓰는 값 -
    /// 네트워크로 동기화되는 NetworkVariable이 아니다). 방을 만든 쪽(ChoiceMapScreen에서
    /// 맵 타일을 고른 사람)이 여기 기록해두면, WaitRoomScreen(START 시 로드)/RewardScreen
    /// (다시 하기·나가기 시 언로드)/EscMenuScreen(나가기 시 언로드)이 "지금 로드/언로드해야
    /// 할 Gameplay 씬이 뭔지"를 이 한 곳만 보고 알 수 있다.
    ///
    /// 참가자(코드로 들어온 쪽)는 맵을 직접 고르지 않으므로 SelectedMap이 비어있을 수
    /// 있다 - 실제 씬 로드/언로드는 어차피 호스트만 수행하므로(GameplaySceneLoader 내부
    /// 서버 권한 체크) 문제가 되지 않는다. 참가자 쪽에서도 맵 정보를 정확히 알아야 하는
    /// 기능이 생기면(예: 대기실에 맵 이름 표시) 호스트→참가자 동기화가 필요하다 -
    /// 그건 "2인 이상 실기 네트워크 테스트" 작업과 함께 다룰 것(확인 필요, 지금은 보류).
    /// </summary>
    public static class CurrentRun
    {
        public static MapCatalogSO.MapEntry SelectedMap;

        /// <summary>
        /// 261014 추가 - "캐릭터도 선택 안 하면 이전에 선택한 걸로"(예섭 요청). 맵은 "선택
        /// 안 하면 START 자체를 막는다"(WaitRoomScreen.OnStartPressed 참고)로 다르게 처리하지만,
        /// 캐릭터는 지금 하영 1명뿐이라 굳이 막을 이유가 없어서 "선택을 건너뛰면 자동으로
        /// 채운다" 쪽으로 처리했다 - 둘이 서로 다른 정책인 이유는 맵은 "안 고르면 로드할 씬
        /// 자체가 없어서 진행 불가"인 반면 캐릭터는 "안 고르면 기본값(마지막 선택 또는
        /// 첫 해금 캐릭터)으로 대체 가능"이기 때문.
        /// </summary>
        public static CharacterCatalogSO.CharacterEntry SelectedCharacter;

        private const string LastCharacterIdPrefsKey = "CheonMyeong.LastSelectedCharacterId";

        /// <summary>
        /// ChoiceMapScreen에서 캐릭터 타일을 클릭했을 때 호출 - 이번 선택을 PlayerPrefs에도
        /// 남겨서, 앱을 재실행해도(세션이 아니라 실제 "이전에") 마지막으로 고른 캐릭터를
        /// 기억하게 한다.
        /// </summary>
        public static void SelectCharacter(CharacterCatalogSO.CharacterEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            SelectedCharacter = entry;
            PlayerPrefs.SetString(LastCharacterIdPrefsKey, entry.characterId);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// ChoiceMapScreen이 뜰 때 호출 - 이번 세션에서 아직 캐릭터를 고르지 않았으면
        /// (1) PlayerPrefs에 저장된 마지막 선택을 찾아 채우고, (2) 그마저 없으면(첫 실행,
        /// 혹은 저장된 캐릭터가 더 이상 해금 상태가 아닌 경우 등) catalog에서 처음 만나는
        /// 해금된 항목으로 대체한다. 이미 이번 세션에 선택한 게 있으면 아무것도 안 건드린다.
        /// </summary>
        public static void EnsureCharacterSelected(CharacterCatalogSO catalog)
        {
            if (SelectedCharacter != null || catalog == null || catalog.characters == null)
            {
                return;
            }

            string lastId = PlayerPrefs.GetString(LastCharacterIdPrefsKey, string.Empty);
            CharacterCatalogSO.CharacterEntry lastEntry = catalog.FindById(lastId);
            if (lastEntry != null && lastEntry.isUnlocked)
            {
                SelectedCharacter = lastEntry;
                return;
            }

            foreach (CharacterCatalogSO.CharacterEntry entry in catalog.characters)
            {
                if (entry != null && entry.isUnlocked)
                {
                    SelectedCharacter = entry;
                    return;
                }
            }
        }
    }
}
