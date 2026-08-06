-- KEYS[1] = AllocatedId Bitmap
-- KEYS[2] = AllocatedId ByRequester Set (해당 requester)
-- KEYS[3] = AllocatedId ExpiryIndex ZSET
-- ARGV[1] = maxBitInclusive
-- ARGV[2] = count
-- ARGV[3] = startBit (핫스팟 회피용 랜덤 오프셋)
-- ARGV[4] = requester
-- ARGV[5] = nowUnixSeconds (신규 할당이면 CreatedAtUtc, 멱등 경로면 UpdatedAtUtc)
-- ARGV[6] = expiredAtUnixSeconds (두 분기 모두 now + FirstTimeExpiration)
-- ARGV[7] = entryKeyPrefix ("IdKeeper/AllocatedId/{AllocatedId}/")
-- ARGV[8] = description (빈 문자열 허용, 신규 할당에만 사용)
--
-- 반환: { status, ids } — status는 'ALLOCATED'(신규) 또는 'EXISTING'(멱등 재시도).
--   두 분기 모두 정확히 2원소를 반환해야 한다. Lua 테이블은 첫 nil에서 잘려 Redis 응답으로
--   나가므로, 원소가 비면 호출부의 인덱스 접근이 깨진다.
-- 에러: ALREADY_EXISTS(보유 개수 불일치 또는 인덱스-엔트리 불일치), INSUFFICIENT_IDS(여유 ID 부족)
--
-- AuditLog는 {AuditLog} 해시태그가 달라 이 스크립트와 같은 슬롯에 있다는 보장이 없다
-- (Redis Cluster에서 EVAL은 KEYS가 전부 같은 슬롯이어야 함) — 그래서 감사 로그 기록은
-- 호출부(AllocatedIdRepository.AllocAsync)에서 이 스크립트 성공 후 별도로 수행한다.

local bitmapKey, byReqKey, expiryKey = KEYS[1], KEYS[2], KEYS[3]

local maxBit = tonumber(ARGV[1])
local count = tonumber(ARGV[2])
local total = maxBit + 1
local startBit = tonumber(ARGV[3]) % total
local requester = ARGV[4]
local nowAt = ARGV[5]
local expiredAt = ARGV[6]
local entryPrefix = ARGV[7]
local description = ARGV[8]

local existing = redis.call('SMEMBERS', byReqKey)
if #existing > 0 then
	-- 멱등 재시도 경로: Alloc이 서버에서는 성공했는데 응답이 유실되어(타임아웃 또는
	-- resilience handler의 POST 자동 재시도) 같은 requester가 다시 들어온 경우다.
	-- requester는 machineId|pid|프로세스시작시각이라 프로세스 단위로 유일하므로,
	-- 같은 requester = 같은 프로세스이고 기존 노드 ID를 그대로 돌려주는 것이 안전하다.
	-- 보유 개수가 요청 개수와 다르면 의도를 특정할 수 없어 기존 409를 유지한다.
	if #existing ~= count then
		return redis.error_reply('ALREADY_EXISTS')
	end

	-- SMEMBERS는 문자열을 순서 보장 없이 돌려준다. tonumber 후 정렬해 신규 할당 경로와
	-- 반환 타입(정수)·순서(오름차순)를 일치시킨다. 문자열 정렬은 "10" < "9"가 되어 못 쓴다.
	local ids = {}
	for i, v in ipairs(existing) do
		ids[i] = tonumber(v)
	end
	table.sort(ids)

	-- 쓰기 전에 전량 검증한다. Redis는 스크립트 중간에 error_reply를 내도 이미 수행한 쓰기를
	-- 롤백하지 않으므로, 검증과 쓰기를 한 루프에 섞으면 부분 갱신이 남는다.
	for _, id in ipairs(ids) do
		if redis.call('EXISTS', entryPrefix .. id) == 0 then
			-- ByRequester에는 남아 있는데 엔트리 해시가 없다(maxmemory eviction, 백업 부분
			-- 복원 등). 비트맵 비트도 이미 0일 수 있어 HSET으로 되살리면 같은 ID가 다른
			-- 프로세스에도 나갈 수 있고, 비트를 다시 세우는 복구 역시 이미 발급된 ID를
			-- 재점유할 위험이 있다. 그래서 복구하지 않고 안전한 409로 떨어뜨린다.
			return redis.error_reply('ALREADY_EXISTS')
		end
	end

	-- 갱신 필드 집합은 RenewAtomic.lua와 동일한 규칙이다(Lua에 include가 없어 중복).
	-- IgnoreExpire=1인 ID도 UpdatedAtUtc/ExpiredAtUtc는 갱신하고 ExpiryIndex만 건너뛴다.
	-- 해시를 갱신해두지 않으면 관리자가 나중에 IgnoreExpire를 끌 때
	-- ToggleIgnoreExpireAtomic이 과거 만료값으로 ZADD해서 사용 중인 ID가 즉시 회수된다.
	--
	-- CreatedAtUtc / Description / Requester는 의도적으로 건드리지 않는다:
	--   - CreatedAtUtc는 리스가 처음 생긴 시점(관리 화면의 수명 표시 근거)이다.
	--   - Alloc 컨트롤러는 description을 항상 null(→ 빈 문자열)로 넘기므로, 덮어쓰면
	--     관리자가 UpdateDescriptionAsync로 넣어둔 설명이 재시도 한 번에 지워진다.
	for _, id in ipairs(ids) do
		local hkey = entryPrefix .. id
		redis.call('HSET', hkey, 'UpdatedAtUtc', nowAt, 'ExpiredAtUtc', expiredAt)
		if redis.call('HGET', hkey, 'IgnoreExpire') == '0' then
			redis.call('ZADD', expiryKey, expiredAt, id)
		end
	end

	return { 'EXISTING', ids }
end

local found = {}
for i = 0, maxBit do
	if #found >= count then
		break
	end
	local bit = (startBit + i) % total
	if redis.call('GETBIT', bitmapKey, bit) == 0 then
		table.insert(found, bit)
	end
end

if #found < count then
	return redis.error_reply('INSUFFICIENT_IDS')
end

-- 랩어라운드 스캔이라 startBit 이후 순서로 담긴다. 멱등 경로와 동일하게 정렬해
-- API 응답과 감사 로그의 ID 순서를 결정적으로 만든다.
table.sort(found)

for _, id in ipairs(found) do
	redis.call('SETBIT', bitmapKey, id, 1)
	local hkey = entryPrefix .. id
	redis.call('HSET', hkey,
		'Requester', requester,
		'CreatedAtUtc', nowAt,
		'UpdatedAtUtc', '',
		'ExpiredAtUtc', expiredAt,
		'IgnoreExpire', '0',
		'Description', description)
	redis.call('SADD', byReqKey, id)
	redis.call('ZADD', expiryKey, expiredAt, id)
end

return { 'ALLOCATED', found }
