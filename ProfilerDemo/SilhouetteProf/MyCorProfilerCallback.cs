using Silhouette;
using System.ComponentModel;

namespace SilhouetteProf;

//[Profiler("2E6E9B94-FCF1-870A-3FA3-A04AB0C914DB")]
public partial class MyCorProfilerCallback //: CorProfilerCallback5Base
{
    //protected override HResult Initialize(int iCorProfilerInfoVersion)
    //{
    //    Console.WriteLine("[SilhouetteProf] Initialize");
    //    if(iCorProfilerInfoVersion < 5)
    //    {
    //        // 我们至少需要 ICorProfilerInfo5，但我们获得的实例数少于 5。
    //        return HResult.E_FAIL;
    //    }

    //    // 调用 SetEventMask 方法告知 .NET 运行时我们关注哪些事件
    //    return ICorProfilerInfo5.SetEventMask(
    //        COR_PRF_MONITOR.COR_PRF_MONITOR_ALL
    //    );
    //}

    //protected override HResult Shutdown()
    //{
    //    Console.WriteLine("[SilhouetteProf] Shutdown");
    //    return HResult.S_OK;
    //}

    //protected override HResult AssemblyLoadFinished(AssemblyId assemblyId, HResult hrStatus)
    //{
    //    // ...
    //    try
    //    {
    //        // Try to get the AssemblyInfoWithName, and if the HResult returns non-success, throw
    //        AssemblyInfoWithName assemblyInfo = ICorProfilerInfo5.GetAssemblyInfo(assemblyId).ThrowIfFailed();

    //        Console.WriteLine($"[SilhouetteProf] AssemblyLoadFinished: {assemblyInfo.AssemblyName}");
    //        return HResult.S_OK;
    //    }
    //    catch (Win32Exception ex)
    //    {
    //        // GetAssemblyInfo() failed for some reason, weird.
    //        Console.WriteLine($"[SilhouetteProf] AssemblyLoadFinished failed: {ex}");
    //        return ex.NativeErrorCode;
    //    }
    //}

    //protected override HResult ClassLoadStarted(ClassId classId)
    //{
    //    try
    //    {
    //        ClassIdInfo classIdInfo = ICorProfilerInfo.GetClassIdInfo(classId).ThrowIfFailed();

    //        using ComPtr<IMetaDataImport>? metaDataImport = ICorProfilerInfo2
    //                                                            .GetModuleMetaDataImport(classIdInfo.ModuleId, CorOpenFlags.ofRead)
    //                                                            .ThrowIfFailed()
    //                                                            .Wrap();
    //        TypeDefPropsWithName classProps = metaDataImport.Value.GetTypeDefProps(classIdInfo.TypeDef).ThrowIfFailed();

    //        Console.WriteLine($"[SilhouetteProf] ClassLoadStarted: {classProps.TypeName}");
    //        return HResult.S_OK;
    //    }
    //    catch (Win32Exception ex)
    //    {
    //        Console.WriteLine($"[SilhouetteProf] ClassLoadStarted failed: {ex}");
    //        return ex.NativeErrorCode;
    //    }
    //}
}
