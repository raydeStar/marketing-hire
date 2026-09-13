using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Thaddeus.Infrastructure;

public sealed record OwnedProcessResourceLimits(long CommittedMemoryBytes, int CpuRate, int ActiveProcesses)
{
    public void Validate()
    {
        if (CommittedMemoryBytes is < 16777216 or > 274877906944 || CpuRate is < 1 or > 10000 || ActiveProcesses is < 1 or > 128)
            throw new ArgumentException("Owned process resource limits are outside the supported bounds.");
    }
}

public sealed record OwnedProcessResourceObservation(uint LimitFlags, long CommittedMemoryLimitBytes,
    uint ActiveProcessLimit, uint CpuControlFlags, uint CpuRate, long PeakJobCommittedBytes);

[SupportedOSPlatform("windows10.0")]
public sealed partial class WindowsJobProcess
{
    // These are OS-enforced limits for the trusted adapter, in addition to guest CPU/RAM settings.
    // Query the job we own; a request object is not evidence that Windows applied the boundary.
    private static OwnedProcessResourceObservation ConfigureResources(SafeJobHandle job, OwnedProcessResourceLimits requested)
    {
        var limits = new Native.ExtendedLimit
        {
            Basic = new() { Flags = 0x2000 | 0x200 | 0x8, ActiveProcessLimit = (uint)requested.ActiveProcesses },
            JobMemory = checked((nuint)requested.CommittedMemoryBytes)
        };
        if (!Native.SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<Native.ExtendedLimit>()))
            throw ErrorFor("configure-resource-limits");
        var cpu = new ResourceNative.CpuRateControl { Flags = 0x1 | 0x4, Rate = (uint)requested.CpuRate };
        if (!ResourceNative.SetInformationJobObject(job, 15, ref cpu, (uint)Marshal.SizeOf<ResourceNative.CpuRateControl>()))
            throw ErrorFor("configure-cpu-hard-cap");
        var observed = QueryResources(job);
        if (observed.LimitFlags != limits.Basic.Flags || observed.CommittedMemoryLimitBytes != requested.CommittedMemoryBytes ||
            observed.ActiveProcessLimit != requested.ActiveProcesses || observed.CpuControlFlags != cpu.Flags || observed.CpuRate != requested.CpuRate)
            throw new IOException("Windows job resource limits differ from the requested boundary.");
        return observed;
    }

    public OwnedProcessResourceObservation ObserveResources() => QueryResources(job);

    private static OwnedProcessResourceObservation QueryResources(SafeJobHandle job)
    {
        if (!ResourceNative.QueryInformationJobObject(job, 9, out Native.ExtendedLimit limits, (uint)Marshal.SizeOf<Native.ExtendedLimit>(), out _) ||
            !ResourceNative.QueryInformationJobObject(job, 15, out ResourceNative.CpuRateControl cpu, (uint)Marshal.SizeOf<ResourceNative.CpuRateControl>(), out _))
            throw ErrorFor("query-resource-limits");
        return new(limits.Basic.Flags, checked((long)limits.JobMemory), limits.Basic.ActiveProcessLimit,
            cpu.Flags, cpu.Rate, checked((long)limits.PeakJobMemory));
    }

    private static class ResourceNative
    {
        [StructLayout(LayoutKind.Sequential)] internal struct CpuRateControl { internal uint Flags, Rate; }
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetInformationJobObject(SafeJobHandle job, int informationClass, ref CpuRateControl information, uint length);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool QueryInformationJobObject(SafeJobHandle job, int informationClass, out Native.ExtendedLimit information, uint length, out uint returnedLength);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool QueryInformationJobObject(SafeJobHandle job, int informationClass, out CpuRateControl information, uint length, out uint returnedLength);
    }
}
