using BenchmarkDotNet.Running;

namespace AustinHarris.JsonRpc.Micro
{
    public static class Program
    {
        // `dotnet run -c Release -- --filter '*'` runs everything; `--filter '*Sync*' --job short` is a quick look.
        // `--disasm` adds the JIT disassembly of each benchmark (with the compiled invokers inlined where the JIT did so).
        public static int Main(string[] args)
        {
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
            return 0;
        }
    }
}
