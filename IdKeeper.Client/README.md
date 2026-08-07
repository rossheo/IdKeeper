# IdKeeper.Client

IdKeeper 서버에서 노드 Id를 임대받아 **프로세스 안에서 직접** SnowflakeId를 발급하는 클라이언트다.
발급마다 네트워크 호출이 일어나지 않는다. 임대 획득·주기적 갱신·정상 종료 시 반납은 백그라운드에서
자동으로 처리된다.

## 설치

GitHub Packages에 배포되므로 소스 등록과 인증이 필요하다. **GitHub Packages의 NuGet은 공개
패키지도 복원에 인증을 요구한다** — `read:packages` 스코프의 PAT가 있어야 한다.

프로젝트 루트에 `nuget.config`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="idkeeper" value="https://nuget.pkg.github.com/rossheo/index.json" />
  </packageSources>
  <packageSourceCredentials>
    <idkeeper>
      <add key="Username" value="%GITHUB_USER%" />
      <add key="ClearTextPassword" value="%GITHUB_TOKEN%" />
    </idkeeper>
  </packageSourceCredentials>
</configuration>
```

```bash
dotnet add package IdKeeper.Client
```

## 사용

```csharp
builder.Services.AddIdKeeperSnowflake(options =>
{
    options.BaseAddress = new Uri("https://idkeeper.internal");
    options.ApiKey = builder.Configuration["IDKEEPER_APIKEY"];
});

// 선택: 임대 상태를 헬스체크로 노출
builder.Services.AddHealthChecks().AddIdKeeperSnowflake();
```

```csharp
public class OrderService(ISnowflakeIdGenerator idGenerator)
{
    public Order Create()
    {
        Int64 id = idGenerator.NextId();
        ...
    }
}
```

여러 개가 필요하면 `NextIds(count)` 또는 `NextIdsAsync(count)`를 쓴다. 반환 목록은 오름차순
정렬을 보장한다.

## 알아둘 것

**등록은 한 번만.** `AddIdKeeperSnowflake()`를 여러 번 불러도 한 번만 등록된다. 한 프로세스에
발급기가 둘 이상 있으면 안 되기 때문이다 — 서버의 Alloc은 같은 요청자에게 **같은 노드 Id**를
돌려주는 멱등 동작을 하므로, 제너레이터 세트가 둘이면 각자의 시퀀스 카운터가 독립적으로 돌아
**완전히 동일한 ID**가 생성된다. 한 프로세스에 호스트를 둘 띄우는 경우는 기동 시 예외로 막는다.

**기동 직후에는 아직 준비되지 않았다.** 임대를 받기 전에 `NextId()`를 부르면
`SnowflakeNotReadyException`이 발생한다. `IsReady`로 확인하거나, 헬스체크를 등록해 준비될 때까지
트래픽을 받지 않게 하는 편이 낫다.

**임대가 만료되면 호스트가 종료된다.** 만료된 노드 Id를 계속 쓰면 서버가 그 Id를 다른 프로세스에
재할당해 ID가 중복된다. 갱신은 임대 기간의 절반 시점에 자동으로 이뤄지므로 정상 상황에서는
만료에 도달하지 않는다.

**`Requester`는 기본값을 쓰는 것이 안전하다.** 비워 두면 머신 Id + PID + 프로세스 시작 시각으로
자동 산출된다. 호스트명이나 서비스명처럼 여러 프로세스가 공유하는 값을 직접 지정하면 서로 다른
프로세스가 같은 노드 Id를 받아 **ID가 중복된다.**

**노드 Id는 유한하다.** 임베드한 앱 인스턴스마다 노드 Id를 하나 이상 소비한다. 기본 레이아웃에서
전체 4,096개이며, `GeneratorCount` 기본값은 1이다. 노드당 1ms 발급 상한(기본 레이아웃 기준
1,024개, 초당 약 102만 개)을 넘겨야 할 때만 늘린다.

## 옵션

| 옵션 | 기본값 | 설명 |
|---|---|---|
| `BaseAddress` | (필수) | IdKeeper 서버 주소 |
| `ApiKey` | (필수) | X-API 키 (`idkeeper-`로 시작) |
| `GeneratorCount` | `1` | 임대받을 노드 Id 개수 |
| `RenewLoopDuration` | `10분` | 갱신 확인 주기 |
| `Requester` | 자동 | 요청자 식별자 — 지정하지 않는 것을 권장 |

잘못된 값은 기동 시점에 검증되어 애플리케이션이 즉시 실패한다.
