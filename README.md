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
        - 최초 만료 시간(FirstTimeExpiration) 설정 (고정값: 10분)
  - `IdKeeper.SnowflakeApiService`
    - `IdKeeper.ApiService`에서 노드 Id를 임대받아 SnowflakeId를 발급하는 API
      - GetSnowFlakeId (필요한 개수)
      - Version (버전 정보)
  - `IdKeeper.Web`
    - MudBlazor 기반 관리자 페이지
      - X-API Key / 화이트리스트 관리
      - Id 목록 보기, 사용자/역할 관리, 감사 로그
      - Redis 백업(예약/수동) 관리
  - `IdKeeper.RedisCommon`, `IdKeeper.Common`, `IdKeeper.ServiceDefaults`
    - 서비스 간 공유 Redis 접근/도메인 모델, OpenTelemetry 등 공통 설정

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
      - MachineId + PID (각 OS 혹은 Docker container마다 MachineId를 가져올 수 있다.)
      - 필요한 Id 개수
    - 서버 처리
      - X-API 키가 일치하는지 확인한다.
      - 필요한 Id 개수만큼 할당 가능한지 확인한다.
      - 할당한 Id마다 Requester에 MachineId + PID를 등록한다.
      - 최초 임대 기간(Lease duration)은 10분으로 설정한다. (Renew시 임대 기간은 기본값으로 설정)
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
      - json array로 Id, 임대 기간(Lease duration)을 받는다.
      - 업데이트 요청 스케쥴러에 등록한다. (임대 기간 * 1/2 ~ 만료시간까지 10분 단위로 업데이트)
      - ExpiredAtUtc에 맞춰 프로그램 종료 시간도 업데이트 한다.
  - Step 3)
    - Application 종료시 제거 요청 (Remove)
      - X-API 키
      - 서버 종료 과정에서 MachineId + PID를 포함하여 Remove 요청을 보낸다.
    - 서버 처리
      - X-API 키가 일치하는지 확인한다.
      - Requester(MachineId + PID) 정보와 일치하는 Id 목록을 제거한다.
    - 서버 응답 기다리지 않고 Application 종료

## 임대 기간 만료시 삭제 정책
- RestAPI서버에서 10분마다 주기적으로 만료된 Id를 삭제한다.

## SnowflakeId 발급 정책 (IdKeeper.SnowflakeApiService)
- 초기화 (Alloc 응답 검증)
  - BitCount는 각 항목이 양수이고 합이 63이어야 한다. 위반 시 애플리케이션을 종료한다. (fail-fast)
  - 할당받은 노드 Id가 NodeId 비트 수 범위(0 ~ 2^N-1)를 벗어나면 애플리케이션을 종료한다. (fail-fast)
- 발급 (Alloc API)
  - 반환되는 Id 목록은 오름차순 정렬을 보장한다.
  - 대량 요청은 여러 Generator에 청크로 분산 발급한다.
    - 일부 청크가 실패하면 요청 전체를 실패 처리하고, 이미 소비된 Id는 재사용하지 않고 gap으로 버린다.
- 임대 갱신/만료
  - 초기화 직후 첫 갱신을 즉시 1회 수행하여 갱신 경로가 정상인지 조기에 검증한다.
  - 갱신 실패 시 별도 백오프 없이 RenewLoopDuration 간격(기본값: 10분)으로 만료 전까지 재시도한다.
  - 임대가 만료되면 만료 감지를 기다리지 않고 즉시 Id 발급을 차단(503)하고 애플리케이션을 종료한다.
    - 만료된 노드 Id는 서버가 다른 프로세스에 재할당할 수 있으므로 Id 중복을 방지하기 위함이다.

## X-API 관련 정책
- MudBlazor로 관리 페이지에서 관리한다.
- X-API 마다 Description과 만료일을 지정할 수 있다.