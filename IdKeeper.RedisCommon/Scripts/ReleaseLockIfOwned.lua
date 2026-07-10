-- KEYS[1] = lock key
-- ARGV[1] = lock token
--
-- 자신이 획득한 락만 해제하도록 토큰이 일치할 때만 DEL한다.
-- (TTL 만료 후 다른 인스턴스가 같은 키로 새 락을 잡은 경우 실수로 지우지 않기 위함)

if redis.call('GET', KEYS[1]) == ARGV[1] then
	return redis.call('DEL', KEYS[1])
end
return 0
