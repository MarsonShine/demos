using Silhouette;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;

namespace SilhouetteProf
{
    [Profiler("090154FB-B2B3-9AEB-F725-F2DE0CEFBDFF")]
    public partial class MyCorProfilerCallback2 : CorProfilerCallback5Base
    {
        private readonly Stopwatch _sw = Stopwatch.StartNew();

        // 你可以改成从环境变量读取，比如 "TestApp;MyCompany."
        private readonly string[] _typePrefixFilters = ["TestApp", "MyCompany."];

        // ModuleId -> AssemblyName
        private readonly ConcurrentDictionary<ModuleId, string> _moduleToAssembly = new();

        // AssemblyName -> typeCount
        private readonly ConcurrentDictionary<string, int> _typeCountsByAssembly = new();

        // 记录前 N 个类型名用于抽样
        private readonly ConcurrentDictionary<string, int> _sampleTypeNames = new(); // typeName -> times seen

        protected override HResult Initialize(int iCorProfilerInfoVersion)
        {
            Console.WriteLine($"[{_sw.ElapsedMilliseconds,6}ms] [SilhouetteProf] Initialize (ICorProfilerInfoVersion={iCorProfilerInfoVersion})");

            if (iCorProfilerInfoVersion < 5) return HResult.E_FAIL;

            // 注意：开启全部事件会很吵。这里给你一个“够用”的起点：
            // - 要 AssemblyLoadFinished / ClassLoadStarted 相关事件，需要相应 mask
            // 为了简单先 ALL（你已经这样做过），后续可以再收敛 mask。
            return ICorProfilerInfo5.SetEventMask(COR_PRF_MONITOR.COR_PRF_MONITOR_ALL);
        }

        protected override HResult Shutdown()
        {
            Console.WriteLine($"[{_sw.ElapsedMilliseconds,6}ms] [SilhouetteProf] Shutdown");
            DumpSummary();
            return HResult.S_OK;
        }

        protected override HResult AssemblyLoadFinished(AssemblyId assemblyId, HResult hrStatus)
        {
            try
            {
                var info = ICorProfilerInfo5.GetAssemblyInfo(assemblyId).ThrowIfFailed();
                Console.WriteLine($"[{_sw.ElapsedMilliseconds,6}ms] [SilhouetteProf] AssemblyLoadFinished: {info.AssemblyName}");
                // 这里拿不到 moduleId（GetAssemblyInfo 里其实也有 ModuleId/AppDomainId，但 Silhouette 的封装类型不同版本略有差异）
                // 所以我们在 ClassLoadStarted 里用 GetClassIdInfo 拿 ModuleId，再反查 AssemblyName（通过 GetModuleInfo 等方式）。
                return HResult.S_OK;
            }
            catch (Win32Exception ex)
            {
                return ex.NativeErrorCode;
            }
        }

        protected override HResult ClassLoadStarted(ClassId classId)
        {
            try
            {
                // 1) 先拿到 class 所属 module/typeDef
                ClassIdInfo classInfo = ICorProfilerInfo.GetClassIdInfo(classId).ThrowIfFailed();

                // 2) 通过 metadata 读取类型名
                using ComPtr<IMetaDataImport>? meta = ICorProfilerInfo2
                    .GetModuleMetaDataImport(classInfo.ModuleId, CorOpenFlags.ofRead)
                    .ThrowIfFailed()
                    .Wrap();

                TypeDefPropsWithName typeProps = meta.Value.GetTypeDefProps(classInfo.TypeDef).ThrowIfFailed();
                string typeName = typeProps.TypeName;

                // 3) 过滤：只处理你关心的命名空间/前缀
                if (!MatchesPrefix(typeName)) return HResult.S_OK;

                // 4) 取该 module 对应的 assemblyName（缓存）
                string assemblyName = _moduleToAssembly.GetOrAdd(classInfo.ModuleId, mid => ResolveAssemblyName(mid));

                // 5) 聚合计数（避免每次 Console.WriteLine）
                _typeCountsByAssembly.AddOrUpdate(assemblyName, 1, (_, old) => old + 1);

                // （可选）抽样：记录少量类型名，避免无限增长
                if (_sampleTypeNames.Count < 200)
                    _sampleTypeNames.AddOrUpdate(typeName, 1, (_, old) => old + 1);

                return HResult.S_OK;
            }
            catch (Win32Exception ex)
            {
                // 不要在这里打印 ex（会刷屏），直接吞掉或计数更好
                return ex.NativeErrorCode;
            }
        }

        private bool MatchesPrefix(string typeName)
        {
            for (int i = 0; i < _typePrefixFilters.Length; i++)
            {
                if (typeName.StartsWith(_typePrefixFilters[i], StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private string ResolveAssemblyName(ModuleId moduleId)
        {
            // Silhouette 不同版本的 GetModuleInfo 封装类型名可能略有差异；
            // 下面这种写法表达的是：通过 ICorProfilerInfo* 去拿 ModuleInfo，再得到 AssemblyId，再反查 AssemblyName。
            // 如果你的版本没有 GetModuleInfo/返回类型不同，你把 IDE 提示的实际方法名替换一下即可。
            try
            {
                var moduleInfo = ICorProfilerInfo.GetModuleInfo(moduleId).ThrowIfFailed();
                var assemblyInfo = ICorProfilerInfo5.GetAssemblyInfo(moduleInfo.AssemblyId).ThrowIfFailed();
                return assemblyInfo.AssemblyName;
            }
            catch
            {
                return "<unknown-assembly>";
            }
        }

        private void DumpSummary()
        {
            Console.WriteLine("========== [SilhouetteProf] Type Load Summary (filtered) ==========");

            foreach (var kv in _typeCountsByAssembly.OrderByDescending(kv => kv.Value).Take(30))
            {
                Console.WriteLine($"{kv.Value,6} types  |  {kv.Key}");
            }

            if (_sampleTypeNames.Count > 0)
            {
                Console.WriteLine("---------- sample type names ----------");
                foreach (var kv in _sampleTypeNames.OrderByDescending(kv => kv.Value).Take(30))
                {
                    Console.WriteLine($"{kv.Value,3}x  {kv.Key}");
                }
            }

            Console.WriteLine("===================================================================");
        }
    }
}
