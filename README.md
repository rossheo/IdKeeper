# IdKeeper 개요

- IdKeeper는 snowflakeId에서 DataCenterId + WorkderId 혹은 NodeId라고 하는 Id를 발급하고 임대 기간을 주기적으로 업데이트하여 UniqueId를 할당하고 관리하는 RestAPI 서비스이다.

## 실행 방법 (Docker Compose)

`docker-compose.yaml`은 `IdKeeper.AppHost`(Aspire)에서 `aspire publish`로 생성한 정적
산출물이다. AppHost.cs를 바꾸면 아래 명령으로 재생성해야 한다.

```bash
aspire publish --apphost IdKeeper.AppHost/IdKeeper.AppHost.csproj -o aspire-output
cp aspire-output/docker-compose.yaml ./docker-compose.yaml
```

실행 전에 `.env.example`을 `.env`로 복사해 값을 채운다(이미지 태그, 포트, Redis
비밀번호, SnowflakeApiService용 X-API 키). `.env`는 `.gitignore`로 커밋되지 않는다.

```bash
cp .env.example .env
# .env 값을 채운 뒤
docker compose up -d
```

## 저장소 안내

이 GitHub 저장소가 공개(canonical) 소스이며, 커밋 히스토리는 공개 시점에 새로
시작했다. 배포는 여기 커밋된 `docker-compose.yaml` +
`.github/workflows/docker-publish.yml`(GHCR 이미지 빌드/푸시)을 기준으로 한다.

## 서비스 구성

### C# Aspire 기반, Redis 단일 스택

데이터 저장소는 Redis 하나만 사용한다. 로컬 개발은 `IdKeeper.AppHost`(.NET Aspire)로
오케스트레이션하며, 배포용 Docker Compose 산출물은 `docker-compose.yaml`을 참고한다.

- 프로젝트
  - `IdKeeper.ApiService`
    - RestAPI
      - Id 할당 관련 API
        - Alloc (최초 할당)
        - Renew (임대 기간 갱신)
        - Remove (제거)
        - CountOfRemainId (남은 Id 개수 조회)
        - Version (버전 정보)
      - 설정 (IdKeeperSetting)
        - 임대 기간(Lease duration) 설정 (기본값: 48시간)
        - 최초 만료 시간(FirstTimeExpiration) 설정 (기본값: 10분, 1~20분 범위 설정 가능)
        - 회수 유예 시간(CleanupGracePeriod) 설정 (기본값: 10분, 0~1시간 범위 설정 가능. 0이면 유예 없음)
  - `IdKeeper.SnowflakeApiService`
    - `IdKeeper.ApiService`에서 노드 Id를 임대받아 SnowflakeId를 발급하는 API
      - Alloc (SnowflakeId 발급, 필요한 개수)
      - Version (버전 정보)
  - `IdKeeper.Web`
    - MudBlazor 기반 관리자 페이지
      - X-API Key / 화이트리스트 관리
      - Id 목록 보기, 사용자/역할 관리, 감사 로그
      - Redis 백업(예약/수동) 관리
  - `IdKeeper.RedisCommon`, `IdKeeper.Common`, `IdKeeper.ServiceDefaults`
    - 서비스 간 공유 Redis 접근/도메인 모델, OpenTelemetry 등 공통 설정
  - `IdKeeper.AppHost`
    - .NET Aspire 오케스트레이션(로컬 개발) + Docker Compose 산출물 생성
  - `IdKeeper.RedisBackupTool`
    - Redis 전체 키를 파일로 export/import하는 커맨드라인 도구(`export`/`import`)

## Id 할당에 대한 기본 정책 (SnowflakeId의 비트할당과 다르다)
| 비트 수    | 설명                                   |
| --------- | ------------------------------------------- |
| **1비트**  | 부호 비트, 항상 0 |
| **41비트** | 기준 시점(2026-01-01)부터의 밀리초 단위 타임스탬프 |
| **12비트** | DataCenterId + WorkerId 혹은 NodeId (4096개 지원 가능) |
| **10비트** | 같은 밀리초 내 생성된 Id를 구분하기 위한 시퀀스 번호. 초당 102만개 발급 가능 (1024 * 1000)|

- 다음과 같이 Process의 Id 소비량에 따라 각각 다르게 할당 받아서 사용한다.
  - Process마다 1~N개의 Id를 요청할 수 있다.
    - Process에서 1초에 소모되는 Id 갯수가 102만개를 넘는 경우 N개를 할당 받아서 Application 특성에 맞게 할당할 수 있다.
  - Process 내부에서는 N개를 할당 받아서 다음과 같은 방식으로 구현 가능하다.
    - RoundRobin 방식으로 순차적으로 할당
    - ThreadId를 % N으로 나누어서 할당
    - WorkerThread 개수만큼 할당 받고 Thread마다 1개씩 고정 할당(1:1 매칭)

## 임대 관련 용어
- 임대 기간: Lease duration (second 단위, 기본값: 48시간)

## 임대 기간 갱신 정책
- Application은 초기화 구간에 예외로 종료되는 경우가 있으므로 다음과 같이 단계별로 다르게 처리한다.
  - Step 1)
    - Application 최초 요청 (Alloc)
      - X-API 키
      - MachineId + PID + 프로세스 시작 시각 (각 OS 혹은 Docker container마다 MachineId를 가져올 수 있다. 시작 시각까지 포함해야 컨테이너에서 PID가 1로 고정되어도 재시작한 프로세스가 서로 구분된다.)
      - 필요한 Id 개수
    - 서버 처리
      - X-API 키가 일치하는지 확인한다.
      - 필요한 Id 개수만큼 할당 가능한지 확인한다.
      - 할당한 Id마다 Requester에 MachineId + PID + 프로세스 시작 시각을 등록한다.
      - 최초 임대 기간(Lease duration)은 10분으로 설정한다. (Renew시 임대 기간은 기본값으로 설정)
      - 같은 Requester가 이미 Id를 보유 중이면 멱등 처리한다. 요청 개수가 보유 개수와 같으면 기존 Id 목록을 그대로 반환하고 임대만 최초 만료 시간으로 갱신하며, 개수가 다르면 실패로 처리한다.
        - 응답이 유실되어(타임아웃 등) 클라이언트가 재시도한 경우를 복구하기 위함이다. Requester가 MachineId + PID + 프로세스 시작 시각이라 프로세스 단위로 유일하므로, 같은 Requester는 같은 프로세스임이 보장된다.
    - 서버 응답 (Success)
      - json array로 Id, ExpiredAtUtc를 받는다.
      - ExpiredAtUtc에 맞춰 프로그램 종료 시간을 등록한다.
    - 서버 응답 (Failure)
      - 서버 초기화 실패 및 프로그램 종료
  - Step 2)
    - Application 갱신 요청 (Renew)
      - X-API 키
      - 서버 초기화 과정 완료 후 MachineId + PID를 포함하여 Renew 요청을 보낸다.
    - 서버 처리
      - X-API 키가 일치하는지 확인한다.
      - Requester(MachineId + PID) 정보와 일치하는 Id 목록을 찾는다.
      - 임대 기간(Lease duration)을 기본값으로 ExpiredAtUtc를 업데이트 한다.
    - 서버 응답
      - json array로 Id, ExpiredAtUtc를 받는다.
      - 업데이트 요청 스케쥴러에 등록한다. (임대 기간 * 1/2 시점에 갱신하고, 갱신에 실패하면 만료 전까지 재시도한다)
      - ExpiredAtUtc에 맞춰 프로그램 종료 시간도 업데이트 한다.
  - Step 3)
    - Application 종료시 제거 요청 (Remove)
      - X-API 키
      - 서버 종료 과정에서 MachineId + PID를 포함하여 Remove 요청을 보낸다.
      - 요청 전에 신규 Id 발급을 먼저 차단하고, 진행 중인 발급이 모두 끝날 때까지 기다린다(드레인).
        - 노드 Id를 반납한 뒤에도 발급이 진행 중이면 서버가 재할당한 노드 Id와 겹칠 수 있으므로, 드레인 완료 후에만 반납한다.
      - 임대가 이미 무효화된 상태(만료 감지 등으로 발급이 중단된 경우)에는 반납할 노드 Id가 없으므로 요청을 보내지 않는다.
    - 서버 처리
      - X-API 키가 일치하는지 확인한다.
      - Requester(MachineId + PID) 정보와 일치하는 Id 목록을 제거한다.
    - 서버 응답을 기다린 뒤 Application 종료
      - 종료 경로이므로 반납에 실패해도 fail-fast 하지 않고 오류를 로깅한 뒤 정상 종료한다. 반납되지 않은 노드 Id는 임대 만료 후 서버의 주기적 삭제로 회수된다.

## 임대 기간 만료시 삭제 정책
- RestAPI서버에서 10분마다 주기적으로 만료된 Id를 삭제한다.
- 단, 만료 즉시 삭제하지 않고 회수 유예 시간(CleanupGracePeriod, 기본값 10분)이 지난 것만 삭제한다.
  - 클라이언트는 자기 로컬 시계로 만료를 판단해 발급을 멈추지만, 노드 Id가 실제로 다른 프로세스에게 넘어가는 시점은 이 삭제 작업이 실행될 때다. 클라이언트 시계가 서버보다 뒤처져 있으면 서버가 이미 회수·재할당한 노드 Id를 클라이언트가 계속 쓰게 되어 SnowflakeId가 중복된다. 유예를 두면 안전 조건이 "시계 오차 < 유예 시간"이라는 유한값이 되며, 시계 오차뿐 아니라 GC 스톨·컨테이너 프리즈·서버 레플리카 간 시계 차이도 함께 흡수한다.
  - 삭제 작업이 10분 주기이므로 **실효 회수 지연은 `유예 시간 + [0, 10분]`**이다. 유예 시간은 하한이지 정확값이 아니다.
  - 정상 종료(Remove)는 유예를 타지 않고 즉시 회수된다. 유예가 실제로 적용되는 건 비정상 종료(크래시, SIGKILL, 노드 유실)뿐이다.
  - 유예 기간 동안 해당 Id는 관리 화면에서 "만료됨"으로 표시되면서도 잔여 Id 개수에는 계속 사용 중으로 집계된다. 실제 할당 가능 여부와 일치하는 정상 동작이다.

## 클라이언트 시계 오차(clock skew) 탐지
- 위 유예는 "시계 오차 < 유예 시간"일 때만 안전하다. 그 조건이 깨졌는지 서버가 직접 판정한다.
  - 클라이언트는 이 방향(자기 시계가 뒤처짐)을 스스로 검출할 수 없다. 서버의 현재 시각이 필요하기 때문이다.
- Alloc/Renew 요청에 `ClientUtcNow`(선택 필드)를 실어 보내면 서버가 `서버시각 - ClientUtcNow`를 계산한다. 양수면 클라이언트가 뒤처진 것이고, 이 방향만 위험하다(앞선 경우 클라이언트가 더 일찍 멈추므로 안전하다).
  - **필드를 보내지 않으면 검사 자체를 건너뛴다.** 따라서 서버·클라이언트 배포 순서는 양방향 모두 안전하다.
- 임계값은 `CleanupGracePeriod` 하나에서 파생한다. 별도 설정을 두면 유예 기간과 조용히 어긋나 유예가 덮으려던 중복 창이 되살아난다.
  - 유예의 절반 초과: 경고 로그 (앞선 방향도 시계 고장 신호이므로 경고한다)
  - 유예 초과: **Alloc을 409로 거부한다.** 응답 본문에 오차·임계값·조치(NTP 동기화)가 담긴다.
  - `CleanupGracePeriod`가 0이면 강제도 비활성화되고 로그만 남는다.
- **Renew는 어떤 경우에도 거부하지 않고 경고만 한다.**
  - 시계 오차는 단독으로 위험하지 않고 갱신 실패와 결합해야 위험하다. 갱신이 성공하는 동안에는 만료 시각이 계속 밀려 회수 시점에 도달하지 않는다. 여기서 거부하면 지금 안전한 프로세스를 죽이게 되고, NTP 장애는 보통 클러스터 단위라 전면 장애로 번진다.
- Alloc이 거부되면 클라이언트는 기동하지 못하고 재시도한다(3초에서 시작해 두 배씩 늘리며 60초를 상한으로 하는 지수 백오프, ±20% 지터). 헬스체크가 계속 Unhealthy이므로 Id를 발급하지 않으며, NTP가 교정되면 최대 60초 안에 재시작 없이 자동으로 정상 기동한다. 거부 사유는 클라이언트 로그에 응답 본문 그대로 남는다.
- requester별 마지막 관측값은 Redis에 기록되고(임대 기간 + 유예 시간 후 자동 만료) 관리 화면 `Allocated Ids`의 **Clock Skew** 컬럼에서 볼 수 있다.
  - 최초 Alloc이 거부된 클라이언트는 할당된 Id가 없어 이 화면에는 나타나지 않는다. 대신 아래 Discord 알림과 서버 경고 로그(requester·오차·임계값·remoteIp 포함)로 확인한다.
- 매시 정각에 오차가 임계값을 넘은 클라이언트가 있으면 Discord 웹훅을 설정한 모든 사용자에게 알린다(`ClockSkewAlertJob`).
  - 거부(유예 초과)와 경고(유예의 절반 초과)를 함께 알리며, 거부 건을 앞에 나열한다. 조건이 해소될 때까지 매시간 반복된다(`CapacityAlertJob`과 동일한 상태 기반 알림).
  - 등급은 서버가 판정해 저장한 값을 그대로 쓴다 — 임계값이 `IdKeeper.ApiService` 설정이라 Web에서는 알 수 없고, 재계산하면 임계값을 두 곳에서 맞춰야 한다.
  - 관측값은 requester 인덱스로 조회하므로 **노드 Id를 받지 못한(= 거부된) 클라이언트도 포함**된다. 만료된 항목은 조회 시점에 인덱스에서 함께 정리된다.

## SnowflakeId 발급 정책 (IdKeeper.SnowflakeApiService)
- 초기화 (Alloc 응답 검증)
  - BitCount는 각 항목이 양수이고 합이 63이어야 한다. 위반 시 애플리케이션을 종료한다. (fail-fast)
  - 할당받은 노드 Id가 NodeId 비트 수 범위(0 ~ 2^N-1)를 벗어나면 애플리케이션을 종료한다. (fail-fast)
  - 할당받은 임대가 로컬 시계 기준으로 이미 만료 상태이면 애플리케이션을 종료한다. (fail-fast)
    - 서버와 로컬의 시계 괴리가 임대 기간을 넘어선 경우이다. 다만 이 검사로 잡히는 것은 로컬 시계가 앞선 방향뿐이며, Id 중복 위험이 있는 뒤처진 방향은 서버의 현재 시각 없이는 검출할 수 없어 임대 기간 절반 시점 갱신이 제공하는 마진에 의존한다.
- 발급 (Alloc API)
  - 반환되는 Id 목록은 오름차순 정렬을 보장한다.
  - 대량 요청은 여러 Generator에 청크로 분산 발급한다.
    - 일부 청크가 실패하면 요청 전체를 실패 처리하고, 이미 소비된 Id는 재사용하지 않고 gap으로 버린다.
  - 헬스체크(`/health`)는 노드 Id 할당 여부와 임대 유효성을 함께 확인한다.
    - 발급 차단 조건과 동일하게 판정하므로, 모든 발급이 503인데 헬스체크만 정상으로 보고되는 구간이 생기지 않는다.
- 임대 갱신/만료
  - 초기화 직후 첫 갱신을 즉시 1회 수행하여 갱신 경로가 정상인지 조기에 검증한다.
  - 갱신 응답을 아래와 같이 구분하여 처리한다.
    - 전송 실패(네트워크 오류·타임아웃·5xx)이거나 응답 형식이 비정상인 경우: 임대 소멸의 근거가 아니므로 재시도한다.
    - 서버가 200 + 빈 Id 목록으로 응답한 경우: 임대가 서버에 존재하지 않는다는 확정 신호이므로 즉시 Id 발급을 차단하고 애플리케이션을 종료한다. (fail-fast)
    - 갱신된 Id 개수가 보유한 Generator 개수와 다른 경우: 일부 노드 Id가 서버에서 사라진 것이며 어느 쪽이 유효한지 특정할 수 없으므로 즉시 Id 발급을 차단하고 애플리케이션을 종료한다. (fail-fast)
    - 갱신된 임대가 로컬 시계 기준으로 이미 만료 상태인 경우: 초기화와 동일하게 애플리케이션을 종료한다. (fail-fast)
  - 갱신 재시도 간격은 RenewLoopDuration(기본값: 10분)을 상한으로 하되, 갱신이 밀린 상태에서는 만료까지 남은 시간의 1/4로 좁혀 만료 전 최소 4회의 재시도를 확보한다. (하한 5초)
  - 임대가 만료되면 만료 감지를 기다리지 않고 즉시 Id 발급을 차단(503)하고 애플리케이션을 종료한다.
    - 만료된 노드 Id는 서버가 다른 프로세스에 재할당할 수 있으므로 Id 중복을 방지하기 위함이다.

## X-API 관련 정책
- MudBlazor로 관리 페이지에서 관리한다.
- X-API 마다 Description과 만료일을 지정할 수 있다.