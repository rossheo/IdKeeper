namespace IdKeeper.Client;

/// <summary>
/// 아직 노드 Id 임대를 받지 못했거나 임대가 만료되어 발급이 차단된 상태에서 ID를 요청했을 때
/// 던진다.
///
/// 빈 목록을 돌려주지 않는 이유: 소비자가 반환값 검사를 빠뜨리면 조용히 0개를 받게 되어,
/// 문제가 훨씬 나중에 엉뚱한 곳에서 드러난다. 임대 만료 시에는 호스트가 종료되므로 이 상태는
/// 사실상 기동 직후에만 존재한다.
/// </summary>
public sealed class SnowflakeNotReadyException : InvalidOperationException
{
	/// <summary>지정한 메시지로 예외를 만든다.</summary>
	public SnowflakeNotReadyException(string message)
		: base(message)
	{
	}
}
