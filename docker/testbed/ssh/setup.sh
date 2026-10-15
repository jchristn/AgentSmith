#!/bin/bash
# Prepares a Hugo SSH testbed container, then hands off to the image's s6 init.
# Runs as the container entrypoint (see compose.yaml), before the image creates its config.
set -euo pipefail

# Allow TCP forwarding so the bastion can tunnel to the target. The image copies
# /etc/ssh/sshd_config to /config/sshd/sshd_config on first start.
sed -i 's/^AllowTcpForwarding no/AllowTcpForwarding yes/' /etc/ssh/sshd_config

# Every test connection arrives from the Docker gateway address. OpenSSH 9.8+ penalizes a source
# after failed logins (PerSourcePenalties), and the suites fail logins on purpose, so turn the
# penalties off here and allow more concurrent unauthenticated connections.
if ! grep -q '^PerSourcePenalties' /etc/ssh/sshd_config; then
  printf '\nPerSourcePenalties no\nMaxStartups 100:30:200\n' >> /etc/ssh/sshd_config
fi

# Key-only user. "*" unlocks the account for public key login without enabling a password.
if ! id hugokey >/dev/null 2>&1; then
  useradd -m -s /bin/bash hugokey
fi
usermod -p '*' hugokey
mkdir -p /home/hugokey/.ssh
cat /testbed/keys/*.pub > /home/hugokey/.ssh/authorized_keys
chown -R hugokey:hugokey /home/hugokey/.ssh
chmod 700 /home/hugokey/.ssh
chmod 600 /home/hugokey/.ssh/authorized_keys

# Fixtures for the read, tail, and list tools.
mkdir -p /srv/hugo/dir/sub
: > /srv/hugo/app.log
for i in $(seq 1 500); do
  if [ $((i % 10)) -eq 0 ]; then
    echo "2026-01-01T00:00:00Z ERROR request failed line $i" >> /srv/hugo/app.log
  else
    echo "2026-01-01T00:00:00Z INFO request ok line $i" >> /srv/hugo/app.log
  fi
done
printf 'alpha\nbeta\ngamma\ndelta\n' > /srv/hugo/data.txt
printf 'a\n' > /srv/hugo/dir/a.txt
printf 'bb\n' > /srv/hugo/dir/b.txt
printf 'ccc\n' > /srv/hugo/dir/c.txt
printf 'hidden\n' > /srv/hugo/dir/.hidden
head -c 2048 /dev/urandom > /srv/hugo/binary.bin
chmod -R a+rX /srv/hugo
printf 'not for you\n' > /srv/hugo/private.txt
chmod 600 /srv/hugo/private.txt
echo "$(hostname)" > /srv/hugo/whoami.txt
chmod 644 /srv/hugo/whoami.txt

exec /init
