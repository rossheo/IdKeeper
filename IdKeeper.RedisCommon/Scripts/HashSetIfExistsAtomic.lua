-- KEYS[1] = 갱신 대상 Hash
-- ARGV[1..] = field1, value1, field2, value2, ...
--
-- 반환: 1(갱신함) / 0(Hash가 없어 아무것도 하지 않음)
--
-- EXISTS 확인과 HSET을 따로 호출하면 그 사이에 엔트리가 삭제(정리 잡, 관리자 삭제)될 때
-- HSET이 일부 필드만 가진 고아 Hash를 TTL 없이 되살린다. 단일 키라 Cluster에서도 안전하다.

local hkey = KEYS[1]

if redis.call('EXISTS', hkey) == 0 then
	return 0
end

redis.call('HSET', hkey, unpack(ARGV))
return 1
