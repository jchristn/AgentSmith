namespace Hugo.Connectors.Ssh.Classification
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Built-in command tables used by <see cref="SshCommandClassifier"/>. Resources extend them with the
    /// <c>readOnlyCommandPatterns</c>, <c>destructiveCommandPatterns</c>, and <c>deniedCommandPatterns</c> settings.
    /// Thread-safe (read-only after type initialization).
    /// </summary>
    public static class SshCommandTables
    {
        #region Public-Members

        /// <summary>
        /// Commands that only observe state, whatever their arguments. Output redirection is classified separately.
        /// Commands with argument-dependent effects (for example <c>sed</c>, <c>find</c>, <c>systemctl</c>) are not
        /// listed here; the classifier has a dedicated rule for each.
        /// </summary>
        public static IReadOnlySet<string> ReadOnlyCommands { get; } = new HashSet<string>(StringComparer.Ordinal)
        {
            // files and text
            "cat", "tac", "ls", "ll", "dir", "vdir", "head", "tail", "more", "less", "most", "grep", "egrep", "fgrep",
            "rgrep", "zgrep", "zegrep", "zfgrep", "rg", "ag", "ack", "zcat", "bzcat", "xzcat", "zstdcat", "lzcat",
            "zless", "zmore", "wc", "cut", "tr", "nl", "fold", "fmt", "paste", "join", "expand", "unexpand", "column",
            "comm", "cmp", "diff", "diff3", "sdiff", "colordiff", "rev", "strings", "od", "hexdump", "xxd", "base64",
            "base32", "md5sum", "sha1sum", "sha224sum", "sha256sum", "sha384sum", "sha512sum", "b2sum", "cksum", "sum",
            "file", "stat", "realpath", "readlink", "basename", "dirname", "namei", "tree", "locate", "mlocate",
            "plocate", "jq", "yq", "lsattr", "getfacl", "echo", "printf", "pwd", "which", "whereis", "type", "look",
            // system state
            "du", "df", "free", "uptime", "w", "who", "whoami", "id", "groups", "users", "last", "lastb", "lastlog",
            "finger", "ps", "pgrep", "pidof", "pstree", "top", "htop", "atop", "iotop", "vmstat", "iostat", "mpstat",
            "pidstat", "sar", "nproc", "lscpu", "lsmem", "lsblk", "blkid", "lspci", "lsusb", "lsmod", "lsof", "lsns",
            "lsipc", "ipcs", "lslocks", "lslogins", "findmnt", "mountpoint", "uname", "arch", "getconf", "printenv",
            "hostid", "lsb_release", "systemd-analyze", "systemd-cgls", "systemd-cgtop", "systemd-detect-virt",
            "virt-what", "dmidecode", "lshw", "hwinfo", "inxi", "sensors", "lvs", "vgs", "pvs", "lvdisplay", "vgdisplay",
            "pvdisplay", "quota", "repquota", "rc-status", "tty", "locale", "logname", "cal", "ncal", "ldd", "nm",
            "objdump", "readelf", "size", "whatis", "apropos", "man", "info", "help",
            // network observation
            "ss", "netstat", "nstat", "ping", "ping6", "traceroute", "traceroute6", "tracepath", "mtr", "dig",
            "nslookup", "host", "drill", "whois", "getent",
            // package queries
            "apt-cache", "dpkg-query", "iptables-save", "ip6tables-save",
            // shell builtins with local effects only
            "true", "false", "test", "[", "[[", "sleep", "yes", "seq", "expr", "bc", "factor", "numfmt", "cd", "pushd",
            "popd", "dirs", "export", "set", "unset", "shopt", "umask", "ulimit", "local", "declare", "typeset",
            "readonly", "wait", "jobs", "hash", "read", "shift", "exit", "return", ":", "trap", "getopts", "alias",
            "unalias", "tput", "clear"
        };

        /// <summary>
        /// Commands that are Destructive whatever their arguments (deleting data, stopping the host, killing processes,
        /// changing disks). Names beginning with <c>mkfs</c> or <c>fsck.</c> are also Destructive.
        /// </summary>
        public static IReadOnlySet<string> DestructiveCommands { get; } = new HashSet<string>(StringComparer.Ordinal)
        {
            "rm", "rmdir", "unlink", "shred", "srm", "wipe", "wipefs", "mkswap", "mke2fs", "dd", "fdisk", "sfdisk",
            "cfdisk", "gdisk", "sgdisk", "parted", "blkdiscard", "shutdown", "reboot", "poweroff", "halt", "init",
            "telinit", "kexec", "killall", "killall5", "pkill", "skill", "slay", "truncate", "userdel", "groupdel",
            "deluser", "delgroup", "swapoff", "umount", "lvremove", "vgremove", "pvremove", "lvreduce", "resize2fs",
            "e2fsck", "fsck", "xfs_repair", "iptables-restore", "ip6tables-restore", "atrm"
        };

        /// <summary>
        /// Commands that run other commands and are unwrapped before classification.
        /// </summary>
        public static IReadOnlySet<string> WrapperCommands { get; } = new HashSet<string>(StringComparer.Ordinal)
        {
            "sudo", "doas", "su", "env", "nice", "nohup", "setsid", "chronic", "unbuffer", "builtin", "exec", "command",
            "time", "timeout", "stdbuf", "ionice", "xargs", "watch", "busybox", "eval"
        };

        /// <summary>
        /// Shell interpreters. With <c>-c</c> their command string is classified recursively; reading commands from
        /// standard input (for example <c>curl ... | sh</c>) is Destructive.
        /// </summary>
        public static IReadOnlySet<string> Shells { get; } = new HashSet<string>(StringComparer.Ordinal)
        {
            "sh", "bash", "zsh", "dash", "ksh", "ash", "mksh", "fish", "tcsh", "csh", "rbash"
        };

        /// <summary>
        /// Redirection targets that do not change files.
        /// </summary>
        public static IReadOnlySet<string> HarmlessTargets { get; } = new HashSet<string>(StringComparer.Ordinal)
        {
            "/dev/null", "/dev/stdout", "/dev/stderr", "/dev/tty", "/dev/fd/1", "/dev/fd/2", "-"
        };

        #endregion
    }
}
