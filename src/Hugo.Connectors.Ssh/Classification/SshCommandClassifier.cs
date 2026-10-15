namespace Hugo.Connectors.Ssh.Classification
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Classifies a shell command as Read, Write, or Destructive, or rejects it. Evaluation order (plan section 8.3):
    /// (1) deny patterns reject; (2) known-destructive commands, subcommands, overwriting redirections, and the
    /// resource's destructive patterns produce Destructive; (3) commands on the read-only list, or matching the
    /// resource's read-only patterns, produce Read; (4) everything else is Write. Lists (<c>;</c>, <c>&amp;&amp;</c>,
    /// <c>||</c>), pipelines, subshells, command substitutions, <c>sh -c</c> strings, and wrappers such as
    /// <c>sudo</c>, <c>env</c>, <c>xargs</c>, and <c>timeout</c> are split or unwrapped, and the most severe part wins.
    /// Input that cannot be parsed is Destructive. Pure and thread-safe.
    /// </summary>
    public static class SshCommandClassifier
    {
        #region Private-Members

        private static readonly Regex _AssignmentRegex = new Regex(@"^[A-Za-z_][A-Za-z0-9_]*(\[[^\]]*\])?\+?=", RegexOptions.CultureInvariant);
        private static readonly Regex _SedWriteCommandRegex = new Regex(@"(^|[;{}\n])\s*(\d+|\$|/[^/]*/)?\s*(,\s*(\d+|\$|/[^/]*/))?\s*[wW]\s+\S", RegexOptions.CultureInvariant);
        private static readonly Regex _SedWriteFlagRegex = new Regex(@"s(.).*\1.*\1[gpIiMme0-9]*w\s+\S", RegexOptions.CultureInvariant);
        private static readonly Regex _AwkRedirectRegex = new Regex(@"print[f]?\b[^;{}]*>{1,2}\s*[""A-Za-z_$(]", RegexOptions.CultureInvariant);
        private static readonly Regex _AwkPipeRegex = new Regex(@"\|\s*(getline|"")|""\s*\|", RegexOptions.CultureInvariant);

        private static readonly HashSet<string> _StructuralWords = new HashSet<string>(StringComparer.Ordinal)
        {
            "{", "}", "!", "if", "then", "else", "elif", "do", "while", "until", "fi", "done"
        };

        private static readonly HashSet<string> _SystemctlRead = new HashSet<string>(StringComparer.Ordinal)
        {
            "status", "show", "cat", "list-units", "list-unit-files", "list-sockets", "list-timers", "list-jobs",
            "list-dependencies", "list-machines", "list-automounts", "list-paths", "is-active", "is-enabled", "is-failed",
            "is-system-running", "get-default", "show-environment", "help"
        };

        private static readonly HashSet<string> _SystemctlDestructive = new HashSet<string>(StringComparer.Ordinal)
        {
            "stop", "restart", "try-restart", "reload-or-restart", "try-reload-or-restart", "condrestart", "force-reload",
            "disable", "mask", "kill", "isolate", "poweroff", "reboot", "halt", "kexec", "suspend", "hibernate",
            "hybrid-sleep", "suspend-then-hibernate", "emergency", "rescue", "default", "clean", "freeze", "switch-root",
            "soft-reboot", "exit"
        };

        private static readonly HashSet<string> _DockerRead = new HashSet<string>(StringComparer.Ordinal)
        {
            "ps", "logs", "inspect", "images", "stats", "version", "info", "top", "port", "diff", "history", "events", "search",
            "container ls", "container list", "container ps", "container inspect", "container logs", "container top",
            "container port", "container diff", "container stats", "image ls", "image list", "image inspect",
            "image history", "volume ls", "volume list", "volume inspect", "network ls", "network list", "network inspect",
            "system df", "system info", "system events", "node ls", "node inspect", "node ps", "service ls", "service ps",
            "service inspect", "service logs", "stack ls", "stack ps", "stack services", "secret ls", "secret inspect",
            "config ls", "config inspect", "context ls", "context show", "context inspect", "plugin ls", "plugin inspect",
            "buildx ls", "buildx inspect", "manifest inspect", "builder ls", "trust inspect"
        };

        private static readonly HashSet<string> _DockerDestructive = new HashSet<string>(StringComparer.Ordinal)
        {
            "rm", "rmi", "kill", "stop", "prune", "exec", "attach", "container rm", "container kill", "container stop",
            "container prune", "container exec", "container attach", "image rm", "image prune", "volume rm", "volume prune",
            "network rm", "network prune", "system prune", "builder prune", "buildx prune", "buildx rm", "node rm",
            "service rm", "stack rm", "secret rm", "config rm", "plugin rm", "context rm", "swarm leave", "manifest rm"
        };

        private static readonly HashSet<string> _DockerGroups = new HashSet<string>(StringComparer.Ordinal)
        {
            "container", "image", "volume", "network", "system", "builder", "node", "service", "stack", "secret", "config",
            "plugin", "context", "buildx", "manifest", "swarm", "trust"
        };

        private static readonly HashSet<string> _KubectlRead = new HashSet<string>(StringComparer.Ordinal)
        {
            "get", "describe", "logs", "top", "version", "explain", "api-resources", "api-versions", "cluster-info", "events",
            "diff", "wait", "completion", "kustomize"
        };

        private static readonly HashSet<string> _KubectlDestructive = new HashSet<string>(StringComparer.Ordinal)
        {
            "delete", "drain", "exec", "attach"
        };

        private static readonly HashSet<string> _GitRead = new HashSet<string>(StringComparer.Ordinal)
        {
            "status", "log", "diff", "show", "rev-parse", "describe", "blame", "ls-files", "ls-tree", "ls-remote", "cat-file",
            "shortlog", "grep", "whatchanged", "count-objects", "version", "help", "show-ref", "rev-list", "name-rev",
            "merge-base", "for-each-ref", "check-ignore", "var", "cherry", "range-diff", "fsck", "verify-commit", "verify-tag"
        };

        private static readonly HashSet<string> _PackageQueryVerbs = new HashSet<string>(StringComparer.Ordinal)
        {
            "list", "show", "policy", "search", "depends", "rdepends", "changelog", "madison", "showpkg", "showsrc", "stats",
            "check", "info", "provides", "whatprovides", "repolist", "repoinfo", "check-update", "check-upgrade", "deplist",
            "repoquery", "updateinfo", "se", "if", "repos", "lr", "list-updates", "lu", "packages", "pa", "patches",
            "products", "pd", "patterns", "pt", "what-provides", "wp", "dot", "audit", "version", "verify", "help"
        };

        private static readonly HashSet<string> _PackageRemoveVerbs = new HashSet<string>(StringComparer.Ordinal)
        {
            "remove", "purge", "autoremove", "autopurge", "erase", "rm", "del", "uninstall"
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Classify a command line.
        /// </summary>
        /// <param name="command">Command line. Null or blank classifies as Destructive (nothing to approve).</param>
        /// <param name="options">Resource options. Null uses defaults (no sudo, no extra patterns).</param>
        /// <returns>Classification with an empty approval summary (the tool supplies it). Never null.</returns>
        public static EffectClassification Classify(string? command, SshClassifierOptions? options)
        {
            SshVerdict verdict = Evaluate(command, options);
            EffectClassification classification = new EffectClassification(verdict.Effect, verdict.Reason, String.Empty);
            classification.Rejected = verdict.Rejected;
            return classification;
        }

        /// <summary>
        /// Classify a command line and return the intermediate verdict.
        /// </summary>
        /// <param name="command">Command line.</param>
        /// <param name="options">Resource options. Null uses defaults.</param>
        /// <returns>Verdict. Never null.</returns>
        public static SshVerdict Evaluate(string? command, SshClassifierOptions? options)
        {
            SshClassifierOptions opts = options ?? new SshClassifierOptions();
            if (String.IsNullOrWhiteSpace(command)) return SshVerdict.Destructive("command is empty");

            SshVerdict? denied = CheckDenied(command, opts);
            if (denied != null) return denied;
            return ClassifyText(command, opts, 0);
        }

        #endregion

        #region Private-Methods

        private static SshVerdict ClassifyText(string text, SshClassifierOptions options, int depth)
        {
            if (depth > options.MaxDepth) return SshVerdict.Destructive("command nesting is deeper than " + options.MaxDepth + " levels");
            SshParseResult parsed = SshCommandParser.Parse(text);
            if (!parsed.Success) return SshVerdict.Destructive("command could not be parsed (" + parsed.Error + ")");

            SshVerdict? result = null;
            foreach (SshCommandSegment segment in parsed.Segments)
                result = SshVerdict.Max(result, ClassifySegment(segment, options, depth));
            foreach (string substitution in parsed.Substitutions)
                result = SshVerdict.Max(result, ClassifyText(substitution, options, depth + 1).WithPrefix("command substitution: "));
            return result ?? SshVerdict.Read("no command");
        }

        private static SshVerdict ClassifySegment(SshCommandSegment segment, SshClassifierOptions options, int depth)
        {
            SshVerdict? denied = CheckDenied(segment.ToString(), options);
            if (denied != null) return denied;

            SshVerdict? redirect = null;
            foreach (SshRedirection r in segment.Redirections) redirect = SshVerdict.Max(redirect, ClassifyRedirection(r));

            SshVerdict command = ClassifyWords(segment.Words, 0, options, depth);
            return SshVerdict.Max(command, redirect);
        }

        private static SshVerdict? ClassifyRedirection(SshRedirection r)
        {
            string target = r.Target;
            switch (r.Operator)
            {
                case "<":
                case "<<<":
                case "<&":
                    return null;
                case "<>":
                    return SshVerdict.Write("redirection opens '" + Short(target) + "' for reading and writing");
                case ">&":
                    if (target == "-" || (target.Length > 0 && target.All(Char.IsDigit))) return null;
                    return SshTargetVerdict(target, false);
                case ">>":
                case "&>>":
                    return SshTargetVerdict(target, true);
                default:
                    return SshTargetVerdict(target, false);
            }
        }

        private static SshVerdict? SshTargetVerdict(string target, bool append)
        {
            if (SshCommandTables.HarmlessTargets.Contains(target)) return null;
            if (append) return SshVerdict.Write("redirection appends to '" + Short(target) + "'");
            return SshVerdict.Destructive("redirection overwrites '" + Short(target) + "'");
        }

        private static SshVerdict ClassifyWords(List<string> words, int start, SshClassifierOptions options, int depth)
        {
            if (depth > options.MaxDepth) return SshVerdict.Destructive("command nesting is deeper than " + options.MaxDepth + " levels");

            int i = start;
            bool assignment = false;
            while (i < words.Count)
            {
                string w = words[i];
                if (_AssignmentRegex.IsMatch(w))
                {
                    assignment = true;
                    i++;
                    continue;
                }
                if (_StructuralWords.Contains(w))
                {
                    i++;
                    continue;
                }
                if (w == "for" || w == "select") return SshVerdict.Read("loop header");
                if (w == "case" || w == "esac" || w == "function" || w == "coproc") return SshVerdict.Destructive("shell construct '" + w + "' is not supported");
                break;
            }
            if (i >= words.Count) return SshVerdict.Read(assignment ? "variable assignment only" : "no command");

            string raw = words[i];
            if (raw.Length == 0) return SshVerdict.Destructive("command name is empty");
            if (raw != "[" && raw != "[[" && raw.IndexOfAny(new char[] { '$', '`', '*', '?', '[', '{' }) >= 0)
                return SshVerdict.Destructive("command name '" + Short(raw) + "' is computed at run time");

            string name = BaseName(raw);
            List<string> args = words.GetRange(i + 1, words.Count - i - 1);
            string text = String.Join(" ", words.Skip(i));

            SshVerdict? wrapped = ClassifyWrapper(name, words, i, args, options, depth);
            if (wrapped != null) return wrapped;

            foreach (string pattern in options.DestructivePatterns)
            {
                if (Search(pattern, text, options, true)) return SshVerdict.Destructive("matches destructive pattern '" + Short(pattern) + "'");
            }

            SshVerdict? builtin = ClassifyBuiltin(name, args, options, depth);
            if (builtin != null && builtin.Effect == EffectEnum.Destructive) return builtin;

            foreach (string pattern in options.ReadOnlyPatterns)
            {
                if (FullMatch(pattern, text, options)) return SshVerdict.Read("matches read-only pattern '" + Short(pattern) + "'");
            }

            if (builtin != null) return builtin;
            return SshVerdict.Write("command '" + Short(name) + "' is not on the read-only list");
        }

        private static SshVerdict? ClassifyWrapper(string name, List<string> words, int index, List<string> args, SshClassifierOptions options, int depth)
        {
            if (SshCommandTables.Shells.Contains(name)) return ClassifyShell(name, words, index + 1, options, depth);

            switch (name)
            {
                case "sudo":
                case "doas":
                    return ClassifySudo(name, words, index + 1, options, depth);
                case "sudoedit":
                    if (!options.AllowSudo) return SshVerdict.Reject("sudo is not allowed on this resource (allowSudo is false)");
                    return SshVerdict.Write("sudoedit edits files as root");
                case "su":
                    return ClassifySu(words, index + 1, options, depth);
                case "env":
                    {
                        if (args.Any(a => a == "-S" || a.StartsWith("--split-string", StringComparison.Ordinal))) return SshVerdict.Destructive("env -S is not supported");
                        int j = SkipOptions(words, index + 1, new[] { "-u", "--unset", "-C", "--chdir" });
                        while (j < words.Count && _AssignmentRegex.IsMatch(words[j])) j++;
                        if (j >= words.Count) return SshVerdict.Read("env prints the environment");
                        return ClassifyWords(words, j, options, depth + 1);
                    }
                case "nice":
                case "nohup":
                case "setsid":
                case "chronic":
                case "unbuffer":
                case "builtin":
                case "exec":
                    {
                        int j = SkipOptions(words, index + 1, new[] { "-n", "--adjustment", "-a" });
                        if (j >= words.Count) return SshVerdict.Read(name + " without a command");
                        return ClassifyWords(words, j, options, depth + 1);
                    }
                case "command":
                    {
                        if (args.Any(a => a == "-v" || a == "-V")) return SshVerdict.Read("command -v looks up a command");
                        int j = SkipOptions(words, index + 1, Array.Empty<string>());
                        if (j >= words.Count) return SshVerdict.Read("command without a command");
                        return ClassifyWords(words, j, options, depth + 1);
                    }
                case "time":
                    {
                        bool output = args.Any(a => a == "-o" || a.StartsWith("--output", StringComparison.Ordinal));
                        int j = SkipOptions(words, index + 1, new[] { "-f", "--format", "-o", "--output" });
                        SshVerdict inner = j >= words.Count ? SshVerdict.Read("time without a command") : ClassifyWords(words, j, options, depth + 1);
                        return output ? SshVerdict.Max(inner, SshVerdict.Write("time -o writes a file")) : inner;
                    }
                case "timeout":
                    {
                        int j = SkipOptions(words, index + 1, new[] { "-s", "--signal", "-k", "--kill-after" });
                        j++;
                        if (j >= words.Count) return SshVerdict.Read("timeout without a command");
                        return ClassifyWords(words, j, options, depth + 1);
                    }
                case "stdbuf":
                    {
                        int j = SkipOptions(words, index + 1, new[] { "-i", "-o", "-e", "--input", "--output", "--error" });
                        if (j >= words.Count) return SshVerdict.Read("stdbuf without a command");
                        return ClassifyWords(words, j, options, depth + 1);
                    }
                case "ionice":
                    {
                        if (args.Any(a => a == "-p" || a == "-P" || a == "-u" || a.StartsWith("--pid", StringComparison.Ordinal) || a.StartsWith("--pgid", StringComparison.Ordinal) || a.StartsWith("--uid", StringComparison.Ordinal)))
                            return SshVerdict.Write("ionice changes the priority of running processes");
                        int j = SkipOptions(words, index + 1, new[] { "-c", "--class", "-n", "--classdata" });
                        if (j >= words.Count) return SshVerdict.Read("ionice without a command");
                        return ClassifyWords(words, j, options, depth + 1);
                    }
                case "xargs":
                    {
                        int j = SkipOptions(words, index + 1, new[] { "-I", "-n", "-P", "-L", "-s", "-d", "-E", "-a", "--max-args", "--max-procs", "--delimiter", "--arg-file", "--max-lines", "--max-chars", "--eof", "--replace" });
                        if (j >= words.Count) return SshVerdict.Read("xargs runs echo");
                        return ClassifyWords(words, j, options, depth + 1).WithPrefix("xargs: ");
                    }
                case "watch":
                    {
                        bool exec = args.Any(a => a == "-x" || a == "--exec");
                        int j = SkipOptions(words, index + 1, new[] { "-n", "--interval" });
                        if (j >= words.Count) return SshVerdict.Read("watch without a command");
                        if (exec) return ClassifyWords(words, j, options, depth + 1).WithPrefix("watch: ");
                        return ClassifyText(String.Join(" ", words.Skip(j)), options, depth + 1).WithPrefix("watch: ");
                    }
                case "busybox":
                    if (index + 1 >= words.Count) return SshVerdict.Read("busybox lists applets");
                    return ClassifyWords(words, index + 1, options, depth + 1);
                case "eval":
                    if (args.Count == 0) return SshVerdict.Read("eval without arguments");
                    return ClassifyText(String.Join(" ", args), options, depth + 1).WithPrefix("eval: ");
                case "source":
                case ".":
                    return SshVerdict.Write("'" + name + "' runs a script file whose contents cannot be inspected");
                default:
                    return null;
            }
        }

        private static SshVerdict ClassifySudo(string name, List<string> words, int start, SshClassifierOptions options, int depth)
        {
            if (!options.AllowSudo) return SshVerdict.Reject(name + " is not allowed on this resource (allowSudo is false)");

            bool loginShell = false;
            bool listOnly = false;
            bool edit = false;
            int j = start;
            string[] valueOptions = new[] { "-u", "-g", "-C", "-p", "-h", "-r", "-t", "-D", "-R", "-U", "-T", "--user", "--group", "--close-from", "--prompt", "--host", "--role", "--type", "--chdir", "--chroot", "--other-user", "--command-timeout" };
            while (j < words.Count)
            {
                string w = words[j];
                if (w == "--")
                {
                    j++;
                    break;
                }
                if (!w.StartsWith('-') || w == "-") break;
                if (w == "-i" || w == "-s" || w == "--login" || w == "--shell") loginShell = true;
                if (w == "-l" || w == "--list" || w == "-v" || w == "--validate" || w == "-k" || w == "-K" || w == "-V" || w == "--version" || w == "--reset-timestamp" || w == "--remove-timestamp") listOnly = true;
                if (w == "-e" || w == "--edit") edit = true;
                j += valueOptions.Contains(w) ? 2 : 1;
            }
            if (edit) return SshVerdict.Write(name + " -e edits files as root");
            if (j >= words.Count)
            {
                if (loginShell) return SshVerdict.Destructive(name + " opens an interactive root shell");
                if (listOnly) return SshVerdict.Read(name + " lists privileges or manages its credential cache");
                return SshVerdict.Destructive(name + " without a command");
            }
            return ClassifyWords(words, j, options, depth + 1).WithPrefix(name + ": ");
        }

        private static SshVerdict ClassifySu(List<string> words, int start, SshClassifierOptions options, int depth)
        {
            if (!options.AllowSudo) return SshVerdict.Reject("su is not allowed on this resource (allowSudo is false)");
            for (int j = start; j < words.Count; j++)
            {
                if (words[j] == "-c" || words[j] == "--command")
                {
                    if (j + 1 >= words.Count) return SshVerdict.Destructive("su -c without a command");
                    return ClassifyText(words[j + 1], options, depth + 1).WithPrefix("su: ");
                }
                if (words[j].StartsWith("--command=", StringComparison.Ordinal))
                    return ClassifyText(words[j].Substring("--command=".Length), options, depth + 1).WithPrefix("su: ");
            }
            return SshVerdict.Destructive("su without -c opens an interactive shell");
        }

        private static SshVerdict ClassifyShell(string name, List<string> words, int start, SshClassifierOptions options, int depth)
        {
            bool commandString = false;
            int j = start;
            while (j < words.Count)
            {
                string w = words[j];
                if (w == "--")
                {
                    j++;
                    break;
                }
                if (w == "--rcfile" || w == "--init-file" || w == "-o" || w == "+o" || w == "-O" || w == "+O")
                {
                    j += 2;
                    continue;
                }
                if (w.StartsWith("--", StringComparison.Ordinal))
                {
                    j++;
                    continue;
                }
                if ((w.StartsWith('-') || w.StartsWith('+')) && w.Length > 1)
                {
                    if (w.StartsWith('-') && w.IndexOf('c', 1) > 0) commandString = true;
                    j++;
                    continue;
                }
                break;
            }

            if (commandString)
            {
                if (j >= words.Count) return SshVerdict.Destructive(name + " -c without a command string");
                return ClassifyText(words[j], options, depth + 1).WithPrefix(name + " -c: ");
            }
            if (j < words.Count) return SshVerdict.Write(name + " runs script file '" + Short(words[j]) + "' whose contents cannot be inspected");
            return SshVerdict.Destructive(name + " reads commands from standard input, which cannot be inspected");
        }

        private static SshVerdict? ClassifyBuiltin(string name, List<string> args, SshClassifierOptions options, int depth)
        {
            if (SshCommandTables.DestructiveCommands.Contains(name)
                || name.StartsWith("mkfs", StringComparison.Ordinal)
                || name.StartsWith("fsck.", StringComparison.Ordinal))
                return SshVerdict.Destructive("command '" + name + "' is destructive");

            switch (name)
            {
                case "kill":
                    if (args.Any(a => a == "-l" || a == "-L" || a == "--list" || a == "-0") || HasPair(args, "-s", "0"))
                        return SshVerdict.Read("kill only lists signals or checks that a process exists");
                    return SshVerdict.Destructive("kill sends a signal to processes");
                case "fuser":
                    return args.Any(a => a == "-k" || a == "--kill" || IsShortCluster(a, 'k')) ? SshVerdict.Destructive("fuser -k kills processes") : SshVerdict.Read("fuser lists processes using files");
                case "find":
                    return ClassifyFind(args, options, depth);
                case "sed":
                    return ClassifySed(args);
                case "awk":
                case "gawk":
                case "mawk":
                case "nawk":
                    return ClassifyAwk(name, args);
                case "sort":
                    return args.Any(a => a.StartsWith("-o", StringComparison.Ordinal) || a.StartsWith("--output", StringComparison.Ordinal)) ? SshVerdict.Write("sort -o writes a file") : SshVerdict.Read("sort is read-only");
                case "uniq":
                    return Positionals(args).Count >= 2 ? SshVerdict.Write("uniq writes its output file") : SshVerdict.Read("uniq is read-only");
                case "tee":
                    return ClassifyTee(args);
                case "curl":
                    return ClassifyCurl(args);
                case "wget":
                    return ClassifyWget(args);
                case "hostname":
                    return Positionals(args).Any() || args.Any(a => a == "-F" || a == "--file") ? SshVerdict.Write("hostname sets the host name") : SshVerdict.Read("hostname is read-only");
                case "hostnamectl":
                case "timedatectl":
                case "localectl":
                    {
                        List<string> p = Positionals(args);
                        if (p.Count == 0 || p[0] == "status" || p[0] == "show" || p[0].StartsWith("list-", StringComparison.Ordinal) || p[0] == "timesync-status" || p[0] == "show-timesync")
                            return SshVerdict.Read(name + " " + (p.Count == 0 ? "status" : p[0]) + " is read-only");
                        return SshVerdict.Write(name + " " + p[0] + " changes system settings");
                    }
                case "loginctl":
                    {
                        List<string> p = Positionals(args);
                        if (p.Count == 0 || p[0].StartsWith("list-", StringComparison.Ordinal) || p[0].StartsWith("show-", StringComparison.Ordinal) || p[0].EndsWith("-status", StringComparison.Ordinal))
                            return SshVerdict.Read("loginctl query is read-only");
                        if (p[0].StartsWith("terminate-", StringComparison.Ordinal) || p[0].StartsWith("kill-", StringComparison.Ordinal))
                            return SshVerdict.Destructive("loginctl " + p[0] + " ends sessions");
                        return SshVerdict.Write("loginctl " + p[0] + " changes sessions");
                    }
                case "date":
                    if (args.Any(a => a == "-s" || a.StartsWith("--set", StringComparison.Ordinal)) || Positionals(args).Any(p => !p.StartsWith('+')))
                        return SshVerdict.Write("date sets the system clock");
                    return SshVerdict.Read("date is read-only");
                case "ip":
                    return ClassifyIp(args);
                case "ifconfig":
                    {
                        List<string> p = Positionals(args);
                        if (p.Contains("down")) return SshVerdict.Destructive("ifconfig down takes an interface offline");
                        return p.Count <= 1 ? SshVerdict.Read("ifconfig shows interfaces") : SshVerdict.Write("ifconfig changes interface settings");
                    }
                case "route":
                    {
                        List<string> p = Positionals(args);
                        if (p.Contains("del") || p.Contains("flush")) return SshVerdict.Destructive("route " + (p.Contains("del") ? "del" : "flush") + " removes routes");
                        return p.Count == 0 ? SshVerdict.Read("route shows the routing table") : SshVerdict.Write("route changes the routing table");
                    }
                case "arp":
                    if (args.Any(a => a == "-d" || a == "--delete")) return SshVerdict.Destructive("arp -d deletes entries");
                    if (args.Any(a => a == "-s" || a == "--set" || a == "-f" || a == "--file")) return SshVerdict.Write("arp changes the ARP cache");
                    return SshVerdict.Read("arp shows the ARP cache");
                case "systemctl":
                    return ClassifySystemctl(args);
                case "service":
                case "rc-service":
                    return ClassifyService(name, args);
                case "journalctl":
                    if (args.Any(a => a.StartsWith("--vacuum", StringComparison.Ordinal))) return SshVerdict.Destructive("journalctl --vacuum deletes journal files");
                    if (args.Any(a => a == "--rotate" || a == "--flush" || a == "--sync" || a == "--relinquish-var" || a == "--smart-relinquish-var" || a == "--setup-keys" || a == "--update-catalog"))
                        return SshVerdict.Write("journalctl maintenance changes journal files");
                    return SshVerdict.Read("journalctl reads logs");
                case "docker":
                case "podman":
                case "nerdctl":
                    return ClassifyDocker(name, args);
                case "docker-compose":
                case "podman-compose":
                    return ClassifyCompose(name, args);
                case "crictl":
                    return ClassifyVerb(name, args, Array.Empty<string>(),
                        new[] { "ps", "pods", "images", "img", "inspect", "inspecti", "inspectp", "logs", "stats", "statsp", "info", "version", "help" },
                        new[] { "rm", "rmi", "rmp", "stop", "stopp", "exec", "attach" });
                case "kubectl":
                case "oc":
                    return ClassifyKubectl(name, args);
                case "helm":
                    return ClassifyHelm(args);
                case "crontab":
                    if (args.Contains("-l")) return SshVerdict.Read("crontab -l lists the crontab");
                    if (args.Contains("-r")) return SshVerdict.Destructive("crontab -r removes the crontab");
                    if (args.Contains("-e")) return SshVerdict.Write("crontab -e edits the crontab");
                    return SshVerdict.Destructive("crontab replaces the crontab");
                case "iptables":
                case "ip6tables":
                case "iptables-legacy":
                case "iptables-nft":
                    return ClassifyIptables(name, args);
                case "nft":
                    return ClassifyVerb(name, args, new[] { "-f", "--file", "-I", "--includepath" }, new[] { "list", "describe", "monitor" }, new[] { "flush", "delete", "destroy" });
                case "ufw":
                    {
                        List<string> p = Positionals(args);
                        if (p.Count == 0 || p[0] == "status" || p[0] == "version" || p[0] == "show" || (p[0] == "app" && p.Count > 1 && (p[1] == "list" || p[1] == "info")))
                            return SshVerdict.Read("ufw query is read-only");
                        if (p[0] == "disable" || p[0] == "reset" || p[0] == "delete") return SshVerdict.Destructive("ufw " + p[0] + " removes firewall protection or rules");
                        return SshVerdict.Write("ufw " + p[0] + " changes firewall rules");
                    }
                case "git":
                    return ClassifyGit(args);
                case "apt":
                case "apt-get":
                case "aptitude":
                case "yum":
                case "dnf":
                case "microdnf":
                case "tdnf":
                case "zypper":
                case "apk":
                case "pacman":
                    return ClassifyPackageManager(name, args);
                case "dpkg":
                    return ClassifyDpkg(args);
                case "rpm":
                    return ClassifyRpm(args);
                case "pip":
                case "pip3":
                    return ClassifyVerb(name, args, new[] { "--python", "--log", "--cache-dir" },
                        new[] { "list", "show", "freeze", "check", "search", "index", "inspect", "debug", "help", "hash" },
                        new[] { "uninstall" });
                case "npm":
                case "yarn":
                case "pnpm":
                    {
                        List<string> p = Positionals(args);
                        if (p.Count == 0) return SshVerdict.Read(name + " without a command prints help");
                        string verb = p[0];
                        if (new[] { "ls", "list", "ll", "la", "view", "v", "info", "show", "outdated", "doctor", "root", "prefix", "bin", "why", "explain", "search", "help" }.Contains(verb))
                            return SshVerdict.Read(name + " " + verb + " is read-only");
                        if (verb == "audit") return p.Contains("fix") ? SshVerdict.Write(name + " audit fix changes packages") : SshVerdict.Read(name + " audit is read-only");
                        if (verb == "config") return p.Count > 1 && (p[1] == "get" || p[1] == "list" || p[1] == "ls") ? SshVerdict.Read(name + " config query is read-only") : SshVerdict.Write(name + " config changes settings");
                        if (verb == "version" && p.Count == 1) return SshVerdict.Read(name + " version is read-only");
                        if (new[] { "uninstall", "remove", "rm", "un", "r", "unlink", "prune" }.Contains(verb)) return SshVerdict.Destructive(name + " " + verb + " removes packages");
                        return SshVerdict.Write(name + " " + verb + " changes packages or runs scripts");
                    }
                case "mount":
                    return Positionals(args, new[] { "-t", "--types", "-O", "--test-opts", "-L", "--label" }).Count == 0 ? SshVerdict.Read("mount lists mounted file systems") : SshVerdict.Write("mount mounts a file system");
                case "swapon":
                    return args.Count == 0 || args.Any(a => a == "--show" || a.StartsWith("--show=", StringComparison.Ordinal) || a == "-s" || a == "--summary") ? SshVerdict.Read("swapon shows swap areas") : SshVerdict.Write("swapon enables swap");
                case "sysctl":
                    return args.Any(a => a.Contains('=') || a == "-w" || a == "--write" || a == "-p" || a.StartsWith("--load", StringComparison.Ordinal) || a == "--system") ? SshVerdict.Write("sysctl changes kernel parameters") : SshVerdict.Read("sysctl reads kernel parameters");
                case "dmesg":
                    if (args.Any(a => a == "-c" || a == "-C" || a == "--clear" || a == "--read-clear")) return SshVerdict.Destructive("dmesg clears the kernel ring buffer");
                    if (args.Any(a => a == "-D" || a == "-E" || a == "-n" || a.StartsWith("--console", StringComparison.Ordinal))) return SshVerdict.Write("dmesg changes console logging");
                    return SshVerdict.Read("dmesg reads the kernel ring buffer");
                case "openssl":
                    return args.Any(a => a == "-out" || a == "-keyout") ? SshVerdict.Write("openssl writes an output file") : SshVerdict.Read("openssl without -out writes only to standard output");
                case "tar":
                case "bsdtar":
                    return ClassifyTar(name, args);
                case "gzip":
                case "gunzip":
                case "bzip2":
                case "bunzip2":
                case "xz":
                case "unxz":
                case "zstd":
                case "unzstd":
                case "lz4":
                case "unlz4":
                case "lzma":
                case "unlzma":
                case "pigz":
                case "compress":
                case "uncompress":
                    return args.Any(a => a == "--stdout" || a == "--to-stdout" || a == "--list" || a == "--test" || a == "--help" || a == "--version" || IsShortCluster(a, 'c') || IsShortCluster(a, 'l') || IsShortCluster(a, 't'))
                        ? SshVerdict.Read(name + " writes only to standard output")
                        : SshVerdict.Write(name + " replaces files in place");
                case "unzip":
                    if (args.Any(a => IsShortCluster(a, 'o'))) return SshVerdict.Destructive("unzip -o overwrites files without prompting");
                    return args.Any(a => IsShortCluster(a, 'l') || IsShortCluster(a, 'v') || IsShortCluster(a, 't') || IsShortCluster(a, 'p') || IsShortCluster(a, 'Z') || IsShortCluster(a, 'z'))
                        ? SshVerdict.Read("unzip lists or tests an archive")
                        : SshVerdict.Write("unzip extracts files");
                case "rsync":
                    if (args.Any(a => a.StartsWith("--delete", StringComparison.Ordinal) || a == "--remove-source-files")) return SshVerdict.Destructive("rsync deletes files");
                    return args.Any(a => a == "--dry-run" || IsShortCluster(a, 'n')) ? SshVerdict.Read("rsync dry run") : SshVerdict.Write("rsync copies files");
                case "zfs":
                    return ClassifyVerb(name, args, Array.Empty<string>(), new[] { "list", "get", "holds", "diff", "version", "help" }, new[] { "destroy", "rollback" });
                case "zpool":
                    return ClassifyVerb(name, args, Array.Empty<string>(), new[] { "list", "status", "iostat", "get", "history", "events", "version", "help" }, new[] { "destroy", "labelclear", "detach", "offline", "remove", "export" });
                case "mdadm":
                    if (args.Any(a => a == "--stop" || a == "-S" || a == "--remove" || a == "-r" || a == "--fail" || a == "-f" || a == "--zero-superblock")) return SshVerdict.Destructive("mdadm changes or stops an array");
                    return args.Any(a => a == "--detail" || a == "-D" || a == "--examine" || a == "-E" || a == "--query" || a == "-Q") ? SshVerdict.Read("mdadm query is read-only") : SshVerdict.Write("mdadm manages arrays");
                case "ethtool":
                    return args.Any(a => a == "-s" || a == "--change" || a == "-K" || a == "--features" || a == "--offload" || a == "-G" || a == "--set-ring" || a == "-A" || a == "--pause" || a == "-C" || a == "--coalesce" || a == "-L" || a == "--set-channels" || a == "-E" || a == "--change-eeprom" || a == "-r" || a == "--negotiate" || a == "--reset" || a == "-f" || a == "--flash" || a == "-X" || a == "-N" || a == "-U" || a == "-W")
                        ? SshVerdict.Write("ethtool changes interface settings")
                        : SshVerdict.Read("ethtool shows interface settings");
                case "smartctl":
                    return args.Any(a => a == "-t" || a == "--test" || a == "-s" || a == "--smart" || a == "-o" || a == "--offlineauto" || a == "-S" || a == "--saveauto" || a == "-X" || a == "--abort")
                        ? SshVerdict.Write("smartctl changes drive settings or starts a test")
                        : SshVerdict.Read("smartctl reads drive health");
                case "nvidia-smi":
                    if (args.Any(a => a == "-r" || a == "--gpu-reset")) return SshVerdict.Destructive("nvidia-smi resets a GPU");
                    return args.Any(a => a == "-pm" || a == "-pl" || a == "-c" || a == "-ac" || a == "-rac" || a == "-e" || a.StartsWith("--persistence-mode", StringComparison.Ordinal) || a.StartsWith("--power-limit", StringComparison.Ordinal) || a.StartsWith("--compute-mode", StringComparison.Ordinal))
                        ? SshVerdict.Write("nvidia-smi changes GPU settings")
                        : SshVerdict.Read("nvidia-smi reads GPU state");
                case "iconv":
                    return args.Any(a => a == "-o" || a.StartsWith("--output", StringComparison.Ordinal)) ? SshVerdict.Write("iconv -o writes a file") : SshVerdict.Read("iconv writes to standard output");
                case "ldconfig":
                    return args.Any(a => a == "-p" || a == "--print-cache") ? SshVerdict.Read("ldconfig -p prints the cache") : SshVerdict.Write("ldconfig rebuilds the linker cache");
                case "history":
                    return args.Any(a => a == "-c" || a == "-d" || a == "-w" || a == "-r" || a == "-a" || a == "-n") ? SshVerdict.Write("history changes the history file") : SshVerdict.Read("history is read-only");
                default:
                    if (SshCommandTables.ReadOnlyCommands.Contains(name)) return SshVerdict.Read("'" + name + "' is read-only");
                    return null;
            }
        }

        private static SshVerdict ClassifyFind(List<string> args, SshClassifierOptions options, int depth)
        {
            SshVerdict result = SshVerdict.Read("find searches files");
            for (int j = 0; j < args.Count; j++)
            {
                string a = args[j];
                if (a == "-delete") return SshVerdict.Destructive("find -delete removes files");
                if (a == "-fprint" || a == "-fprint0" || a == "-fprintf" || a == "-fls")
                {
                    result = SshVerdict.Max(result, SshVerdict.Write("find " + a + " writes a file"));
                    continue;
                }
                if (a == "-exec" || a == "-execdir" || a == "-ok" || a == "-okdir")
                {
                    List<string> inner = new List<string>();
                    int k = j + 1;
                    while (k < args.Count && args[k] != ";" && args[k] != "+")
                    {
                        inner.Add(args[k]);
                        k++;
                    }
                    if (inner.Count == 0) return SshVerdict.Destructive("find " + a + " without a command");
                    result = SshVerdict.Max(result, ClassifyWords(inner, 0, options, depth + 1).WithPrefix("find " + a + ": "));
                    j = k;
                }
            }
            return result;
        }

        private static SshVerdict ClassifySed(List<string> args)
        {
            List<string> scripts = new List<string>();
            bool scriptFromOption = false;
            for (int j = 0; j < args.Count; j++)
            {
                string a = args[j];
                if (a == "--in-place" || a.StartsWith("--in-place=", StringComparison.Ordinal)) return SshVerdict.Destructive("sed -i edits files in place");
                if (a == "-e" || a == "--expression")
                {
                    if (j + 1 < args.Count) scripts.Add(args[j + 1]);
                    scriptFromOption = true;
                    j++;
                    continue;
                }
                if (a.StartsWith("--expression=", StringComparison.Ordinal))
                {
                    scripts.Add(a.Substring("--expression=".Length));
                    scriptFromOption = true;
                    continue;
                }
                if (a == "-f" || a == "--file") return SshVerdict.Write("sed -f runs a script file whose contents cannot be inspected");
                if (a.StartsWith('-') && !a.StartsWith("--", StringComparison.Ordinal) && a.Length > 1)
                {
                    for (int k = 1; k < a.Length; k++)
                    {
                        char ch = a[k];
                        if (ch == 'i') return SshVerdict.Destructive("sed -i edits files in place");
                        if (ch == 'e')
                        {
                            string rest = a.Substring(k + 1);
                            if (rest.Length > 0) scripts.Add(rest);
                            else if (j + 1 < args.Count)
                            {
                                scripts.Add(args[j + 1]);
                                j++;
                            }
                            scriptFromOption = true;
                            break;
                        }
                        if (ch == 'f') return SshVerdict.Write("sed -f runs a script file whose contents cannot be inspected");
                        if (ch == 'l') break;
                    }
                    continue;
                }
                if (!a.StartsWith('-') && !scriptFromOption && scripts.Count == 0) scripts.Add(a);
            }
            foreach (string script in scripts)
            {
                if (_SedWriteCommandRegex.IsMatch(script) || _SedWriteFlagRegex.IsMatch(script)) return SshVerdict.Write("sed script writes a file");
            }
            return SshVerdict.Read("sed without -i writes only to standard output");
        }

        private static SshVerdict ClassifyAwk(string name, List<string> args)
        {
            string? program = null;
            for (int j = 0; j < args.Count; j++)
            {
                string a = args[j];
                if (a == "-f" || a.StartsWith("--file", StringComparison.Ordinal)) return SshVerdict.Write(name + " -f runs a program file whose contents cannot be inspected");
                if (a == "-F" || a == "-v" || a == "--assign" || a == "--field-separator")
                {
                    j++;
                    continue;
                }
                if (a == "--")
                {
                    if (j + 1 < args.Count) program = args[j + 1];
                    break;
                }
                if (a.StartsWith('-') && a.Length > 1) continue;
                program = a;
                break;
            }
            if (program == null) return SshVerdict.Read(name + " without a program");
            if (program.Contains("system(", StringComparison.Ordinal) || program.Contains("system (", StringComparison.Ordinal)) return SshVerdict.Write(name + " program runs shell commands");
            if (_AwkPipeRegex.IsMatch(program)) return SshVerdict.Write(name + " program pipes to or from commands");
            if (_AwkRedirectRegex.IsMatch(program)) return SshVerdict.Write(name + " program writes to a file");
            return SshVerdict.Read(name + " program only reads and prints");
        }

        private static SshVerdict ClassifyTee(List<string> args)
        {
            List<string> files = Positionals(args).Where(f => !SshCommandTables.HarmlessTargets.Contains(f)).ToList();
            if (files.Count == 0) return SshVerdict.Read("tee writes only to standard output");
            if (args.Any(a => a == "-a" || a == "--append" || IsShortCluster(a, 'a'))) return SshVerdict.Write("tee appends to '" + Short(files[0]) + "'");
            return SshVerdict.Destructive("tee overwrites '" + Short(files[0]) + "'");
        }

        private static SshVerdict ClassifyCurl(List<string> args)
        {
            for (int j = 0; j < args.Count; j++)
            {
                string a = args[j];
                if (a.StartsWith("--", StringComparison.Ordinal))
                {
                    string opt = a.Contains('=') ? a.Substring(0, a.IndexOf('=')) : a;
                    string? value = a.Contains('=') ? a.Substring(a.IndexOf('=') + 1) : (j + 1 < args.Count ? args[j + 1] : null);
                    if (opt == "--output")
                    {
                        if (value == null || !SshCommandTables.HarmlessTargets.Contains(value)) return SshVerdict.Write("curl writes a file");
                        continue;
                    }
                    if (opt == "--remote-name" || opt == "--remote-name-all" || opt == "--upload-file" || opt == "--form" || opt == "--json"
                        || opt.StartsWith("--data", StringComparison.Ordinal) || opt == "--cookie-jar" || opt == "--dump-header" || opt == "--trace" || opt == "--trace-ascii" || opt == "--config")
                        return SshVerdict.Write("curl " + opt + " sends data or writes a file");
                    if (opt == "--request" && value != null && !IsSafeHttpMethod(value)) return SshVerdict.Write("curl sends an HTTP " + value.ToUpperInvariant() + " request");
                    continue;
                }
                if (a.StartsWith('-') && a.Length > 1)
                {
                    for (int k = 1; k < a.Length; k++)
                    {
                        char ch = a[k];
                        string rest = a.Substring(k + 1);
                        string? value = rest.Length > 0 ? rest : (j + 1 < args.Count ? args[j + 1] : null);
                        if (ch == 'o')
                        {
                            if (value == null || !SshCommandTables.HarmlessTargets.Contains(value)) return SshVerdict.Write("curl -o writes a file");
                            break;
                        }
                        if (ch == 'O' || ch == 'd' || ch == 'F' || ch == 'T' || ch == 'c' || ch == 'D' || ch == 'K') return SshVerdict.Write("curl -" + ch + " sends data or writes a file");
                        if (ch == 'X')
                        {
                            if (value != null && !IsSafeHttpMethod(value)) return SshVerdict.Write("curl sends an HTTP " + value.ToUpperInvariant() + " request");
                            break;
                        }
                        if ("HAeuxmbwrEUQyYzt".IndexOf(ch) >= 0) break;
                    }
                }
            }
            return SshVerdict.Read("curl GET writes only to standard output");
        }

        private static SshVerdict ClassifyWget(List<string> args)
        {
            bool toStdout = false;
            for (int j = 0; j < args.Count; j++)
            {
                string a = args[j];
                if (a.StartsWith("--post", StringComparison.Ordinal) || a.StartsWith("--body", StringComparison.Ordinal) || (a.StartsWith("--method", StringComparison.Ordinal) && !a.EndsWith("GET", StringComparison.OrdinalIgnoreCase) && !a.EndsWith("HEAD", StringComparison.OrdinalIgnoreCase)))
                    return SshVerdict.Write("wget sends data");
                if (a == "--spider")
                {
                    toStdout = true;
                    continue;
                }
                string? target = null;
                if (a == "--output-document" && j + 1 < args.Count) target = args[j + 1];
                else if (a.StartsWith("--output-document=", StringComparison.Ordinal)) target = a.Substring("--output-document=".Length);
                else if (a.StartsWith('-') && !a.StartsWith("--", StringComparison.Ordinal) && a.IndexOf('O') > 0)
                {
                    string rest = a.Substring(a.IndexOf('O') + 1);
                    target = rest.Length > 0 ? rest : (j + 1 < args.Count ? args[j + 1] : null);
                }
                if (target != null) toStdout = SshCommandTables.HarmlessTargets.Contains(target);
            }
            return toStdout ? SshVerdict.Read("wget writes only to standard output") : SshVerdict.Write("wget downloads files");
        }

        private static SshVerdict ClassifyIp(List<string> args)
        {
            int j = 0;
            while (j < args.Count && args[j].StartsWith('-'))
            {
                j += (args[j] == "-n" || args[j] == "-netns" || args[j] == "-f" || args[j] == "-family" || args[j] == "-b" || args[j] == "-batch" || args[j] == "-rc" || args[j] == "-rcvbuf") ? 2 : 1;
            }
            if (j >= args.Count) return SshVerdict.Read("ip without an object prints help");
            string obj = args[j];
            if (obj == "monitor" || obj == "mon") return SshVerdict.Read("ip monitor is read-only");
            string? action = j + 1 < args.Count ? args[j + 1] : null;
            if (action == null || action == "show" || action == "list" || action == "ls" || action == "lst" || action == "sh" || action == "s" || action == "get" || action == "help" || action.StartsWith("dev", StringComparison.Ordinal))
                return SshVerdict.Read("ip " + obj + " " + (action ?? "show") + " is read-only");
            if (action == "del" || action == "delete" || action == "flush" || args.Skip(j).Contains("down"))
                return SshVerdict.Destructive("ip " + obj + " " + action + " removes or disables network configuration");
            return SshVerdict.Write("ip " + obj + " " + action + " changes network configuration");
        }

        private static SshVerdict ClassifySystemctl(List<string> args)
        {
            List<string> p = Positionals(args, new[] { "-t", "--type", "-p", "--property", "-n", "--lines", "-H", "--host", "-M", "--machine", "-o", "--output", "-s", "--signal", "--state", "--kill-whom", "--root" });
            if (p.Count == 0) return SshVerdict.Read("systemctl lists units");
            string verb = p[0];
            if (_SystemctlRead.Contains(verb)) return SshVerdict.Read("systemctl " + verb + " is read-only");
            if (_SystemctlDestructive.Contains(verb)) return SshVerdict.Destructive("systemctl " + verb + " stops or disrupts services");
            return SshVerdict.Write("systemctl " + verb + " changes service state");
        }

        private static SshVerdict ClassifyService(string name, List<string> args)
        {
            if (args.Contains("--status-all") || args.Contains("-l") || args.Contains("--list")) return SshVerdict.Read(name + " lists services");
            List<string> p = Positionals(args);
            if (p.Count < 2) return SshVerdict.Write(name + " without an action");
            string verb = p[1];
            if (verb == "status") return SshVerdict.Read(name + " status is read-only");
            if (verb == "stop" || verb == "restart" || verb == "force-reload" || verb == "try-restart" || verb == "condrestart" || verb == "zap")
                return SshVerdict.Destructive(name + " " + verb + " stops or disrupts a service");
            return SshVerdict.Write(name + " " + verb + " changes service state");
        }

        private static SshVerdict ClassifyDocker(string name, List<string> args)
        {
            List<string> p = Positionals(args, new[] { "-H", "--host", "-c", "--context", "--config", "-l", "--log-level", "--tlscacert", "--tlscert", "--tlskey" });
            if (p.Count == 0) return SshVerdict.Read(name + " without a command prints help");
            string sub = p[0];
            if (sub == "compose")
            {
                int idx = args.IndexOf("compose");
                return ClassifyCompose(name + " compose", args.Skip(idx + 1).ToList());
            }
            string key = sub;
            if (_DockerGroups.Contains(sub))
            {
                int idx = args.IndexOf(sub);
                List<string> rest = Positionals(args.Skip(idx + 1).ToList());
                if (rest.Count == 0) return SshVerdict.Read(name + " " + sub + " without a command prints help");
                key = sub + " " + rest[0];
            }
            if (_DockerDestructive.Contains(key)) return SshVerdict.Destructive(name + " " + key + " removes, stops, or enters containers");
            if (_DockerRead.Contains(key)) return SshVerdict.Read(name + " " + key + " is read-only");
            return SshVerdict.Write(name + " " + key + " changes containers or images");
        }

        private static SshVerdict ClassifyCompose(string name, List<string> args)
        {
            return ClassifyVerb(name, args, new[] { "-f", "--file", "-p", "--project-name", "--profile", "--env-file", "--project-directory", "--ansi", "--parallel", "--progress" },
                new[] { "ps", "logs", "config", "ls", "images", "top", "version", "port", "events" },
                new[] { "down", "rm", "kill", "stop", "exec" });
        }

        private static SshVerdict ClassifyKubectl(string name, List<string> args)
        {
            string[] valueOptions = new[] { "-n", "--namespace", "--context", "--kubeconfig", "--cluster", "--user", "-s", "--server", "--token", "--as", "--as-group", "-l", "--selector", "-o", "--output", "-c", "--container", "-f", "--filename", "--field-selector", "--request-timeout", "--since", "--tail", "--sort-by", "--template" };
            List<string> p = Positionals(args, valueOptions);
            if (p.Count == 0) return SshVerdict.Read(name + " without a command prints help");
            string verb = p[0];
            string? next = p.Count > 1 ? p[1] : null;
            if (_KubectlDestructive.Contains(verb)) return SshVerdict.Destructive(name + " " + verb + " deletes resources or enters containers");
            if (verb == "replace" && args.Contains("--force")) return SshVerdict.Destructive(name + " replace --force deletes and recreates resources");
            if (_KubectlRead.Contains(verb)) return SshVerdict.Read(name + " " + verb + " is read-only");
            if (verb == "auth" && (next == "can-i" || next == "whoami")) return SshVerdict.Read(name + " auth " + next + " is read-only");
            if (verb == "config" && next != null && (next == "view" || next.StartsWith("get-", StringComparison.Ordinal) || next == "current-context")) return SshVerdict.Read(name + " config " + next + " is read-only");
            if (verb == "rollout" && (next == "status" || next == "history")) return SshVerdict.Read(name + " rollout " + next + " is read-only");
            if (verb == "plugin" && next == "list") return SshVerdict.Read(name + " plugin list is read-only");
            return SshVerdict.Write(name + " " + verb + " changes cluster state");
        }

        private static SshVerdict ClassifyHelm(List<string> args)
        {
            List<string> p = Positionals(args, new[] { "-n", "--namespace", "--kube-context", "--kubeconfig", "-o", "--output", "--revision" });
            if (p.Count == 0) return SshVerdict.Read("helm without a command prints help");
            string verb = p[0];
            string? next = p.Count > 1 ? p[1] : null;
            if (new[] { "uninstall", "delete", "del", "un", "rollback" }.Contains(verb)) return SshVerdict.Destructive("helm " + verb + " removes or reverts a release");
            if (new[] { "list", "ls", "status", "get", "history", "hist", "show", "inspect", "search", "template", "version", "env", "lint", "verify", "help" }.Contains(verb))
                return SshVerdict.Read("helm " + verb + " is read-only");
            if ((verb == "dependency" || verb == "dep" || verb == "repo" || verb == "plugin") && (next == "list" || next == "ls"))
                return SshVerdict.Read("helm " + verb + " list is read-only");
            return SshVerdict.Write("helm " + verb + " changes releases or configuration");
        }

        private static SshVerdict ClassifyIptables(string name, List<string> args)
        {
            bool read = false;
            foreach (string a in args)
            {
                if (a == "-F" || a == "--flush" || a == "-X" || a == "--delete-chain" || a == "-D" || a == "--delete" || a == "-Z" || a == "--zero" || a == "-P" || a == "--policy")
                    return SshVerdict.Destructive(name + " " + a + " removes rules or changes policy");
                if (a == "-L" || a == "--list" || a == "-S" || a == "--list-rules" || a == "-C" || a == "--check") read = true;
                if (a.Length > 2 && a.StartsWith('-') && !a.StartsWith("--", StringComparison.Ordinal) && a.Skip(1).All(Char.IsLetter))
                {
                    if (a.IndexOfAny(new char[] { 'F', 'X', 'D', 'Z', 'P' }) > 0) return SshVerdict.Destructive(name + " " + a + " removes rules or changes policy");
                    if (a.IndexOfAny(new char[] { 'L', 'S', 'C' }) > 0) read = true;
                    if (a.IndexOfAny(new char[] { 'A', 'I', 'N', 'R', 'E' }) > 0) return SshVerdict.Write(name + " " + a + " changes rules");
                }
                if (a == "-A" || a == "--append" || a == "-I" || a == "--insert" || a == "-N" || a == "--new-chain" || a == "-R" || a == "--replace" || a == "-E" || a == "--rename-chain")
                    return SshVerdict.Write(name + " " + a + " changes rules");
            }
            return read ? SshVerdict.Read(name + " lists rules") : SshVerdict.Write(name + " changes rules");
        }

        private static SshVerdict ClassifyGit(List<string> args)
        {
            List<string> p = Positionals(args, new[] { "-C", "-c", "--git-dir", "--work-tree", "--namespace" });
            if (p.Count == 0) return SshVerdict.Read("git without a command prints help");
            string sub = p[0];
            int idx = args.IndexOf(sub);
            List<string> rest = args.Skip(idx + 1).ToList();
            List<string> restPositionals = Positionals(rest);
            string? next = restPositionals.FirstOrDefault();

            if (_GitRead.Contains(sub)) return SshVerdict.Read("git " + sub + " is read-only");
            switch (sub)
            {
                case "branch":
                    if (rest.Any(a => a == "-d" || a == "-D" || a == "--delete")) return SshVerdict.Destructive("git branch delete removes a branch");
                    if (restPositionals.Count == 0 && !rest.Any(a => a == "-m" || a == "-M" || a == "-c" || a == "-C" || a == "-u" || a.StartsWith("--set-upstream", StringComparison.Ordinal) || a == "--unset-upstream" || a == "-f" || a == "--force" || a == "--edit-description"))
                        return SshVerdict.Read("git branch lists branches");
                    return SshVerdict.Write("git branch changes branches");
                case "remote":
                    if (next == null || next == "show" || next == "get-url") return SshVerdict.Read("git remote query is read-only");
                    if (next == "remove" || next == "rm" || next == "prune") return SshVerdict.Destructive("git remote " + next + " removes remotes or refs");
                    return SshVerdict.Write("git remote " + next + " changes remotes");
                case "config":
                    if (rest.Any(a => a.StartsWith("--get", StringComparison.Ordinal) || a == "--list" || a == "-l" || a == "get" || a == "list")) return SshVerdict.Read("git config query is read-only");
                    return SshVerdict.Write("git config changes configuration");
                case "tag":
                    if (rest.Any(a => a == "-d" || a == "--delete")) return SshVerdict.Destructive("git tag -d removes a tag");
                    if (restPositionals.Count == 0 || rest.Any(a => a == "-l" || a == "--list" || a == "-v" || a == "--verify")) return SshVerdict.Read("git tag lists tags");
                    return SshVerdict.Write("git tag creates a tag");
                case "stash":
                    if (next == "list" || next == "show") return SshVerdict.Read("git stash " + next + " is read-only");
                    if (next == "drop" || next == "clear") return SshVerdict.Destructive("git stash " + next + " discards stashed changes");
                    return SshVerdict.Write("git stash changes the working tree");
                case "reflog":
                    if (next == "expire" || next == "delete") return SshVerdict.Destructive("git reflog " + next + " removes history");
                    if (next == null || next == "show") return SshVerdict.Read("git reflog is read-only");
                    return SshVerdict.Write("git reflog changes history");
                case "reset":
                    return rest.Contains("--hard") || rest.Contains("--merge") || rest.Contains("--keep") ? SshVerdict.Destructive("git reset --hard discards changes") : SshVerdict.Write("git reset moves HEAD");
                case "clean":
                    return rest.Any(a => a == "-n" || a == "--dry-run" || IsShortCluster(a, 'n')) ? SshVerdict.Read("git clean dry run") : SshVerdict.Destructive("git clean deletes untracked files");
                case "push":
                    if (rest.Any(a => a == "-f" || a.StartsWith("--force", StringComparison.Ordinal) || a == "--delete" || a == "-d" || a == "--mirror" || a == "--prune") || restPositionals.Skip(1).Any(r => r.StartsWith(':') || r.StartsWith('+')))
                        return SshVerdict.Destructive("git push rewrites or deletes remote refs");
                    return SshVerdict.Write("git push updates a remote");
                case "checkout":
                    if (rest.Contains("--") || rest.Contains(".") || rest.Any(a => a == "-f" || a == "--force")) return SshVerdict.Destructive("git checkout discards local changes");
                    return SshVerdict.Write("git checkout switches branches");
                case "restore":
                case "rm":
                case "prune":
                case "filter-branch":
                case "filter-repo":
                    return SshVerdict.Destructive("git " + sub + " discards or deletes data");
                case "gc":
                    return rest.Any(a => a.StartsWith("--prune", StringComparison.Ordinal)) ? SshVerdict.Destructive("git gc --prune deletes objects") : SshVerdict.Write("git gc rewrites the object store");
                case "update-ref":
                    return rest.Contains("-d") ? SshVerdict.Destructive("git update-ref -d deletes a ref") : SshVerdict.Write("git update-ref changes a ref");
                case "worktree":
                    if (next == "list") return SshVerdict.Read("git worktree list is read-only");
                    if (next == "remove" || next == "prune") return SshVerdict.Destructive("git worktree " + next + " deletes a worktree");
                    return SshVerdict.Write("git worktree changes worktrees");
                default:
                    return SshVerdict.Write("git " + sub + " changes the repository");
            }
        }

        private static SshVerdict ClassifyPackageManager(string name, List<string> args)
        {
            if (name == "pacman")
            {
                if (args.Any(a => a.StartsWith("-R", StringComparison.Ordinal) || a == "--remove")) return SshVerdict.Destructive("pacman -R removes packages");
                if (args.Any(a => a.StartsWith("-Q", StringComparison.Ordinal) || a == "--query" || a.StartsWith("-Ss", StringComparison.Ordinal) || a.StartsWith("-Si", StringComparison.Ordinal))) return SshVerdict.Read("pacman query is read-only");
                return SshVerdict.Write("pacman changes packages");
            }
            List<string> p = Positionals(args, new[] { "-o", "-c", "-t", "--option", "--config", "--target-release", "--repo", "--enablerepo", "--disablerepo", "--root", "-X" });
            if (p.Count == 0) return SshVerdict.Read(name + " without a command prints help");
            string verb = p[0];
            if (_PackageRemoveVerbs.Contains(verb)) return SshVerdict.Destructive(name + " " + verb + " removes packages");
            if (_PackageQueryVerbs.Contains(verb)) return SshVerdict.Read(name + " " + verb + " is read-only");
            if (verb == "history" && (p.Count == 1 || p[1] == "list" || p[1] == "info")) return SshVerdict.Read(name + " history is read-only");
            return SshVerdict.Write(name + " " + verb + " changes packages");
        }

        private static SshVerdict ClassifyDpkg(List<string> args)
        {
            if (args.Any(a => a == "-r" || a == "--remove" || a == "-P" || a == "--purge")) return SshVerdict.Destructive("dpkg removes packages");
            if (args.Any(a => a == "-i" || a == "--install" || a == "--configure" || a == "--unpack" || a == "-a" || a == "--set-selections" || a == "--clear-selections" || a == "--add-architecture" || a == "--remove-architecture"))
                return SshVerdict.Write("dpkg changes packages");
            string[] readFlags = new[] { "-l", "--list", "-L", "--listfiles", "-s", "--status", "-S", "--search", "-p", "--print-avail", "--get-selections", "-c", "--contents", "-I", "--info", "--print-architecture", "--print-foreign-architectures", "--audit", "-C", "--verify", "-V", "--compare-versions", "--version", "--help" };
            if (args.Any(a => readFlags.Contains(a))) return SshVerdict.Read("dpkg query is read-only");
            return SshVerdict.Write("dpkg changes packages");
        }

        private static SshVerdict ClassifyRpm(List<string> args)
        {
            if (args.Any(a => a == "--erase" || (a.StartsWith("-e", StringComparison.Ordinal) && !a.StartsWith("--", StringComparison.Ordinal)))) return SshVerdict.Destructive("rpm -e removes packages");
            if (args.Any(a => a == "--install" || a == "--upgrade" || a == "--freshen" || a == "--import" || a == "--rebuilddb" || a == "--initdb" || a == "--setperms" || a == "--setugids" || a == "--restore"
                || (a.StartsWith('-') && !a.StartsWith("--", StringComparison.Ordinal) && a.Length > 1 && (a[1] == 'i' || a[1] == 'U' || a[1] == 'F'))))
                return SshVerdict.Write("rpm changes packages");
            if (args.Any(a => a.StartsWith("-q", StringComparison.Ordinal) || a == "--query" || a == "-V" || a == "--verify" || a == "-K" || a == "--checksig" || a == "--eval" || a == "--querytags" || a == "--showrc" || a == "--version"))
                return SshVerdict.Read("rpm query is read-only");
            return SshVerdict.Write("rpm changes packages");
        }

        private static SshVerdict ClassifyTar(string name, List<string> args)
        {
            if (args.Any(a => a == "--delete")) return SshVerdict.Destructive(name + " --delete removes archive members");
            if (args.Any(a => a == "--extract" || a == "--get")) return SshVerdict.Destructive(name + " extracts files and may overwrite existing files");
            if (args.Any(a => a == "--list")) return SshVerdict.Read(name + " lists an archive");
            if (args.Any(a => a == "--create" || a == "--append" || a == "--update" || a == "--concatenate")) return SshVerdict.Write(name + " writes an archive");

            List<string> clusters = new List<string>();
            if (args.Count > 0 && !args[0].StartsWith('-') && args[0].All(Char.IsLetter)) clusters.Add(args[0]);
            clusters.AddRange(args.Where(a => a.Length > 1 && a.StartsWith('-') && !a.StartsWith("--", StringComparison.Ordinal) && a.Skip(1).All(Char.IsLetter)).Select(a => a.Substring(1)));
            if (clusters.Any(c => c.Contains('x'))) return SshVerdict.Destructive(name + " extracts files and may overwrite existing files");
            if (clusters.Any(c => c.Contains('t'))) return SshVerdict.Read(name + " lists an archive");
            return SshVerdict.Write(name + " writes an archive");
        }

        private static SshVerdict ClassifyVerb(string name, List<string> args, string[] valueOptions, string[] readVerbs, string[] destructiveVerbs)
        {
            List<string> p = Positionals(args, valueOptions);
            if (p.Count == 0) return SshVerdict.Read(name + " without a command");
            string verb = p[0];
            if (destructiveVerbs.Contains(verb)) return SshVerdict.Destructive(name + " " + verb + " removes or stops resources");
            if (readVerbs.Contains(verb)) return SshVerdict.Read(name + " " + verb + " is read-only");
            return SshVerdict.Write(name + " " + verb + " changes state");
        }

        private static SshVerdict? CheckDenied(string text, SshClassifierOptions options)
        {
            foreach (string pattern in options.DeniedPatterns)
            {
                if (Search(pattern, text, options, true)) return SshVerdict.Reject("command matches denied pattern '" + Short(pattern) + "'");
            }
            return null;
        }

        private static bool Search(string pattern, string text, SshClassifierOptions options, bool failResult)
        {
            if (String.IsNullOrEmpty(pattern)) return false;
            try
            {
                return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, options.RegexTimeout);
            }
            catch (RegexMatchTimeoutException)
            {
                return failResult;
            }
            catch (ArgumentException)
            {
                return failResult;
            }
        }

        private static bool FullMatch(string pattern, string text, SshClassifierOptions options)
        {
            if (String.IsNullOrEmpty(pattern)) return false;
            return Search("^(?:" + pattern + ")$", text, options, false);
        }

        private static int SkipOptions(List<string> words, int start, string[] valueOptions)
        {
            int i = start;
            while (i < words.Count)
            {
                string w = words[i];
                if (w == "--") return i + 1;
                if (!w.StartsWith('-') || w == "-") return i;
                i += valueOptions.Contains(w) ? 2 : 1;
            }
            return i;
        }

        private static List<string> Positionals(List<string> args)
        {
            return Positionals(args, Array.Empty<string>());
        }

        private static List<string> Positionals(List<string> args, string[] valueOptions)
        {
            List<string> result = new List<string>();
            bool endOfOptions = false;
            for (int i = 0; i < args.Count; i++)
            {
                string a = args[i];
                if (!endOfOptions && a == "--")
                {
                    endOfOptions = true;
                    continue;
                }
                if (!endOfOptions && a.StartsWith('-') && a != "-")
                {
                    if (valueOptions.Contains(a)) i++;
                    continue;
                }
                result.Add(a);
            }
            return result;
        }

        private static bool HasPair(List<string> args, string option, string value)
        {
            for (int i = 0; i + 1 < args.Count; i++)
            {
                if (args[i] == option && args[i + 1] == value) return true;
            }
            return false;
        }

        private static bool IsShortCluster(string arg, char flag)
        {
            return arg.Length > 1 && arg[0] == '-' && arg[1] != '-' && arg.IndexOf(flag, 1) > 0;
        }

        private static bool IsSafeHttpMethod(string method)
        {
            string m = method.Trim().ToUpperInvariant();
            return m == "GET" || m == "HEAD" || m == "OPTIONS";
        }

        private static string BaseName(string command)
        {
            int slash = command.LastIndexOf('/');
            return slash >= 0 && slash < command.Length - 1 ? command.Substring(slash + 1) : command;
        }

        private static string Short(string value)
        {
            if (value == null) return String.Empty;
            return value.Length > 120 ? value.Substring(0, 117) + "..." : value;
        }

        #endregion
    }
}
