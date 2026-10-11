using CheonMyeong.Data;
using CheonMyeong.Input;
using UnityEngine;

namespace CheonMyeong.Player
{
    /// <summary>
    /// 탑뷰 8방향 이동. Rigidbody2D 기반으로 FixedUpdate에서만 물리 이동을 처리한다.
    /// 입력은 PlayerInputReader에서만 받아오고, 스탯은 CharacterStatsSO에서만 받아온다 -
    /// 이동 로직 자체는 둘 중 어느 쪽도 직접 알 필요가 없다.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class PlayerMovement : MonoBehaviour
    {
        [SerializeField] private PlayerInputReader inputReader;
        [SerializeField] private CharacterStatsSO stats;

        private Rigidbody2D rb;

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            rb.gravityScale = 0f; // 탑뷰이므로 중력 불필요
            rb.freezeRotation = true;
        }

        private void FixedUpdate()
        {
            if (inputReader == null || stats == null)
            {
                return;
            }

            Vector2 move = inputReader.MoveInput;
            if (move.sqrMagnitude > 1f)
            {
                move.Normalize(); // 대각선 이동 속도 보정
            }

            rb.linearVelocity = move * stats.moveSpeed;
        }
    }
}
