namespace IdKeeper.Client;

/// <summary>
/// 임대 갱신 중 복구 불가능한 오류가 발생했음을 알린다. 호스트 종료(fail-fast)로 이어진다 —
/// 만료된 노드 Id로 계속 발급하면 서버가 재할당한 다른 프로세스와 ID가 겹치기 때문이다.
/// </summary>
public sealed class SnowflakeRuntimeException : Exception
{
	/// <summary>지정한 메시지로 예외를 만든다.</summary>
	public SnowflakeRuntimeException(string message)
		: base(message)
	{
	}

	/// <summary>지정한 메시지와 내부 예외로 예외를 만든다.</summary>
	public SnowflakeRuntimeException(string message, Exception? innerException)
		: base(message, innerException)
	{
	}
}
