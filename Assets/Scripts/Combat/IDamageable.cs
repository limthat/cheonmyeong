namespace CheonMyeong.Combat
{
    /// <summary>
    /// 피해를 받을 수 있는 대상이 구현하는 인터페이스.
    /// 적, 파괴 가능한 오브젝트 등에서 공통으로 사용한다.
    /// </summary>
    public interface IDamageable
    {
        void TakeDamage(float amount);
    }
}
