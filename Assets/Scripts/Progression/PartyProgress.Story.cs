using System.Collections.Generic;
using Unity.Netcode;

namespace CheonMyeong.Progression
{
    /// <summary>
    /// 261118 추가 - 스토리 컷신 건너뛰기(기획서 v0.9.9 12장: "Space 길게 눌러 건너뛰기, 둘 다 눌러야 넘어감").
    /// 각자 Space를 길게 누르면 서버에 "누르고 있음"을 알리고, 접속한 사람 전원이 누르고 있으면 서버가 storySkipSerial을 올린다.
    /// 모든 화면의 MapStoryScreen이 이 값이 바뀌는 걸 보고 함께 인게임으로 넘어간다.
    /// </summary>
    public partial class PartyProgress
    {
        private readonly NetworkVariable<byte> storySkipHolders = new NetworkVariable<byte>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> storySkipSerial = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly HashSet<ulong> storyHolders = new HashSet<ulong>();

        /// <summary>지금 Space를 길게 누르고 있는 사람 수.</summary>
        public int StorySkipHolders => storySkipHolders.Value;

        /// <summary>전원이 건너뛰기를 마칠 때마다 1씩 오른다.</summary>
        public int StorySkipSerial => storySkipSerial.Value;

        /// <summary>내 화면 - 건너뛰기 누름 상태를 서버에 알린다.</summary>
        public void SetStorySkipHold(bool holding)
        {
            if (IsSpawned)
            {
                SetStorySkipHoldRpc(holding);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void SetStorySkipHoldRpc(bool holding, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (holding)
            {
                storyHolders.Add(sender);
            }
            else
            {
                storyHolders.Remove(sender);
            }
            ServerCheckStorySkip();
        }

        /// <summary>서버 - 나간 사람은 빼고, 남은 사람 전원이 누르고 있으면 건너뛴다.</summary>
        private void ServerCheckStorySkip()
        {
            if (!IsServer)
            {
                return;
            }
            var connected = NetworkManager.ConnectedClients;
            storyHolders.RemoveWhere(id => !connected.ContainsKey(id));
            int holders = storyHolders.Count;
            if (holders > 0 && holders >= connected.Count)
            {
                storyHolders.Clear();
                holders = 0;
                storySkipSerial.Value++;
            }
            if (storySkipHolders.Value != holders)
            {
                storySkipHolders.Value = (byte)holders;
            }
        }
    }
}
