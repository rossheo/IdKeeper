using System.Runtime.CompilerServices;

// 임대 생명주기 구현(SnowflakeHostedService, IdKeeperApiClient)은 패키지 공개 표면이 아니라
// 내부 배관이다. 소비자는 ISnowflakeIdGenerator만 쓰면 된다.
// 테스트는 이 내부 타입에 직접 접근해야 하므로 예외적으로 열어 준다 — 리플렉션 대신 타입을
// 그대로 쓰면 이름이 바뀔 때 런타임 실패가 아니라 컴파일 오류로 드러난다.
[assembly: InternalsVisibleTo("IdKeeper.Client.Tests")]
