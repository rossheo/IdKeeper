namespace IdKeeper.Common.Constants;

/// <summary>
/// 현재 프로세스가 사용할 SnowflakeLayout을 보관하는 싱글턴. 기동 시(Program.cs) Redis에서 값을
/// 읽어 한 번 Initialize()하며, 이후 프로세스 생명주기 동안 값이 바뀌지 않는다(재시작해야 새
/// 레이아웃이 반영됨).
/// </summary>
public sealed class SnowflakeLayoutHolder
{
	public SnowflakeLayout Current { get; private set; } = SnowflakeConstant.Default;

	public void Initialize(SnowflakeLayout layout) => Current = layout;
}
