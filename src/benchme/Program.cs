using SimpleW;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;


namespace benchme {

    /// <summary>
    /// Benchmark Program
    /// $ dotnet run -c Release benchme.csproj
    /// </summary>
    internal class Program {

        /// <summary>
        /// EntryPoint
        /// </summary>
        /// <param name="args"></param>
        static void Main(string[] args) {

            var summary = BenchmarkRunner.Run<SimpleWBenchmark>();
            Console.WriteLine("Done!");

        }

    }

    /// <summary>
    /// SimpleW Benchmark
    /// </summary>
    public class SimpleWBenchmark {

        [Benchmark]
        public void Current() {

        }

        [Benchmark]
        public void Optimize() {

        }

    }

}
