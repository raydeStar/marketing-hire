namespace Thaddeus.Infrastructure;

/// <summary>Read-only, private evidence collected only after the existing readiness checks fail.</summary>
internal static class GatewayStartupDiagnostics
{
    internal static readonly string[] Command = ["python3", "-c", """
        import glob, json, os, stat
        result = {'schemaVersion': 1, 'processes': []}
        try:
            fd = os.open('/home/agent/.openclaw/gateway-console.log', os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
            with os.fdopen(fd, 'rb', buffering=0) as log:
                info = os.fstat(log.fileno())
                if not stat.S_ISREG(info.st_mode):
                    raise OSError('Gateway log is not a regular file')
                log.seek(max(0, info.st_size - 12000))
                result['logBytes'] = info.st_size
                result['logTail'] = log.read(12000).decode(errors='replace')[-6000:]
        except OSError as error:
            result['logError'] = type(error).__name__
        for path in sorted(glob.glob('/proc/[0-9]*/stat')):
            try:
                with open(path) as source:
                    value = source.read(4096)
                left, right = value.index('('), value.rindex(')')
                name = value[left + 1:right]
                if not (name.startswith('openclaw') or name == 'node'):
                    continue
                fields = value[right + 2:].split()
                result['processes'].append(dict(pid=int(value[:left].strip()), name=name[:32], state=fields[0],
                    parentPid=int(fields[1]), userTicks=int(fields[11]), systemTicks=int(fields[12]),
                    startTicks=int(fields[19]), residentPages=int(fields[21])))
                if len(result['processes']) == 32:
                    break
            except (OSError, ValueError, IndexError):
                continue
        with open('/proc/uptime') as source:
            result['uptime'] = source.read(100).strip()
        print(json.dumps(result, ensure_ascii=True))
        """];
}
