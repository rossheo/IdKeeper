namespace IdKeeper.Client;

/// <summary>
/// IdKeeper 서버와 공유하는 X-API 키 규약.
///
/// 서버 쪽 IdKeeper.Common에 같은 값이 있지만 의도적으로 복사해 둔다 — IdKeeper.Common은
/// Microsoft.AspNetCore.App FrameworkReference를 갖고 있어, 참조하면 이 패키지를 쓰는 쪽이
/// ASP.NET Core 앱이어야만 한다. 문자열 두 개를 위해 소비자에게 그 제약을 지우지 않는다.
/// </summary>
internal static class XApiKeyConstant
{
	public const string XApiKeyPrefix = "idkeeper-";
	public const string XApiKeyHeaderName = "X-API-Key";
}
