using Aspire.Hosting.Docker.Resources.ComposeNodes;
using Aspire.Hosting.Docker.Resources.ServiceNodes;

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

// 로컬 개발(F5) 오케스트레이션 + Docker Compose 퍼블리시(배포용) 겸용.
// Docker Compose 산출물(docker-compose.yaml)은 `aspire publish`로 생성해 저장소에 정적으로
// 커밋하며, AppHost.cs를 바꾸면 재생성해야 한다(드리프트 방지).
var composeEnv = builder.AddDockerComposeEnvironment("idkeeper")
	// Aspire Dashboard는 로컬 개발 관측용이라 Seq를 이미 쓰는 배포 산출물에는 불필요하고,
	// 인증 없이 텔레메트리 UI가 노출되는 것도 바람직하지 않아 끈다.
	.WithDashboard(enabled: false)
	// web에 붙이는 redis-backup-data 볼륨은 Service 노드에만 추가하면 최상위 volumes:
	// 섹션에 선언되지 않아 `docker compose up`이 "undefined volume"으로 실패한다.
	.ConfigureComposeFile(file =>
	{
		file.AddVolume(new Volume
		{
			Name = "redis-backup-data",
			Driver = "local",
		});

		// Docker named volume은 처음 생성될 때 root:root 755로 초기화된다. web 컨테이너는
		// mcr.microsoft.com/dotnet/aspnet 이미지의 비루트 사용자(uid=1654)로 실행되므로
		// 그대로면 백업 디렉터리에 쓰기 권한이 없어 "Access to the path ... is denied"가
		// 난다. Compose엔 볼륨을 자동으로 chown해주는 기능이 없어, web이 뜨기 전에 한 번
		// chown하는 초기화 컨테이너를 둔다.
		Service backupVolumeInit = new()
		{
			Name = "redis-backup-data-init",
			Image = "busybox:latest",
			Command = ["chown", "-R", "1654:1654", "/data/redis-backup"],
			Restart = "no",
		};
		backupVolumeInit.AddVolume(new Volume
		{
			Name = "redis-backup-data",
			Type = "volume",
			Source = "redis-backup-data",
			Target = "/data/redis-backup",
		});
		file.AddService(backupVolumeInit);
	});

var seq = builder.AddSeq("seq")
	.WithLifetime(ContainerLifetime.Persistent)
	.WithEnvironment("ACCEPT_EULA", "Y")
	.WithDataVolume("seq-data")
	.WithComputeEnvironment(composeEnv);

var redis = builder.AddRedis("redis")
	.WithImageTag("7-alpine")
	.WithArgs("--appendonly", "yes", "--appendfsync", "everysec")
	.WithDataVolume("redis-data")
	.WithLifetime(ContainerLifetime.Persistent)
	.WithComputeEnvironment(composeEnv);

var apiService = builder.AddProject<Projects.IdKeeper_ApiService>("apiservice")
	.WithHttpHealthCheck("/health", endpointName: "http")
	.WithEnvironment("TZ", "Asia/Seoul")
	.WithReference(redis)
	.WaitFor(redis)
	.WithReference(seq)
	.WaitFor(seq)
	.WithComputeEnvironment(composeEnv);

var snowflakeApiServiceXApiKey =
	builder.AddParameter("snowflakeapiservice-xapikey")
		.WithCustomInput(input => new()
		{
			InputType = InputType.SecretText,
			Name = input.Name,
			Placeholder = "Enter the X-API-KEY for snowflakeApiService",
		});

builder.AddProject<Projects.IdKeeper_SnowflakeApiService>("snowflakeapiservice")
	.WithHttpHealthCheck("/health", endpointName: "http")
	.WithEnvironment("IDKEEPER_APIKEY", snowflakeApiServiceXApiKey)
	.WithReference(apiService)
	.WaitFor(apiService)
	.WithReference(seq)
	.WaitFor(seq)
	.WithComputeEnvironment(composeEnv);

builder.AddProject<Projects.IdKeeper_Web>("web")
	.WithExternalHttpEndpoints()
	.WithHttpHealthCheck("/health", endpointName: "http")
	.WithEnvironment("TZ", "Asia/Seoul")
	.WithEnvironment("RedisBackupSetting__RedisBackupDirectory", "/data/redis-backup")
	.WithReference(redis)
	.WaitFor(redis)
	.WithReference(apiService)
	.WaitFor(apiService)
	.WithReference(seq)
	.WaitFor(seq)
	.WithComputeEnvironment(composeEnv)
	// Web 프로젝트 리소스는 ContainerResource가 아니라 WithVolume()을 쓸 수 없어, compose
	// Service 노드에 직접 볼륨을 추가한다.
	.PublishAsDockerComposeService((_, service) =>
	{
		service.AddVolume(new Volume
		{
			Name = "redis-backup-data",
			Type = "volume",
			Source = "redis-backup-data",
			Target = "/data/redis-backup",
			ReadOnly = false,
		});
		service.DependsOn["redis-backup-data-init"] = new ServiceDependency
		{
			Condition = "service_completed_successfully",
		};
	});

builder.Build().Run();
