namespace IdKeeper.Web.Jobs;

/// <summary>
/// 예약 백업 잡(RedisBackupJob)과 관리자 화면의 "지금 디스크에 백업 생성" 버튼이
/// 같은 디렉터리에 동시에 쓰거나 동시에 보존 정리를 실행하지 않도록 직렬화한다.
/// Web은 단일 인스턴스로만 배포되므로(replicas: 1 + Recreate) 프로세스 내
/// SemaphoreSlim으로 충분하다 — 분산 락은 불필요하다.
/// </summary>
public sealed class RedisBackupDiskGate
{
	public SemaphoreSlim Semaphore { get; } = new(1, 1);
}
