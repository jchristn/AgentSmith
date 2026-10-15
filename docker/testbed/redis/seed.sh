#!/bin/sh
# Seeds the Redis testbed with keys of every type under the hugo: prefix. Run by the redis-seed service.
# The suites rely on these exact keys and sizes (src/Test.Shared/Connectors/Redis/RedisTestbed.cs).
set -eu

R="redis-cli -h ${REDIS_HOST:-redis} --no-auth-warning"

$R FLUSHALL > /dev/null

# Strings
$R SET hugo:string:greeting "hello from the hugo testbed" > /dev/null
$R SET hugo:string:counter 42 > /dev/null
head -c 200000 /dev/zero | tr '\0' 'x' | $R -x SET hugo:string:big > /dev/null
printf '\000\001\377\376binary' | $R -x SET hugo:string:binary > /dev/null
$R SET hugo:ttl:session "session-data" EX 86400 > /dev/null

# Hashes
$R HSET hugo:hash:user:1 name "Thomas Anderson" email "neo@example.test" plan "red-pill" > /dev/null
i=1
while [ $i -le 300 ]; do
  printf 'HSET hugo:hash:large field-%03d value-%03d\n' "$i" "$i"
  i=$((i + 1))
done | $R > /dev/null

# List (job-1 at the head, job-50 at the tail)
i=1
while [ $i -le 50 ]; do
  printf 'RPUSH hugo:list:queue job-%d\n' "$i"
  i=$((i + 1))
done | $R > /dev/null

# Set
i=1
while [ $i -le 30 ]; do
  printf 'SADD hugo:set:tags tag-%02d\n' "$i"
  i=$((i + 1))
done | $R > /dev/null

# Sorted set (player-01 has score 10, player-25 has score 250)
i=1
while [ $i -le 25 ]; do
  printf 'ZADD hugo:zset:leaderboard %d player-%02d\n' "$((i * 10))" "$i"
  i=$((i + 1))
done | $R > /dev/null

# Stream (explicit IDs 1-1 through 30-1)
i=1
while [ $i -le 30 ]; do
  printf 'XADD hugo:stream:events %d-1 type event-%d n %d\n' "$i" "$i" "$i"
  i=$((i + 1))
done | $R > /dev/null

# Many small keys for scan paging
i=1
while [ $i -le 500 ]; do
  printf 'SET hugo:many:%04d v%d\n' "$i" "$i"
  i=$((i + 1))
done | $R > /dev/null

$R SET hugo:seeded 1 > /dev/null
echo "seeded $($R DBSIZE) keys"
